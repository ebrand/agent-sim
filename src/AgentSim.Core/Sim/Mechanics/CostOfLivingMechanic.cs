using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// M16 sector-based cost-of-living. On day 30:
///
///   1. Each agent's wage is split into Food / Retail / Entertainment / Disposable buckets.
///   2. Disposable is left in agent savings.
///   3. Food/Retail/Entertainment dollars are deducted from agent savings and routed to the
///      sector's commercial structures (pro-rata across structures in that sector).
///   4. Each commercial structure spends `CommercialGoodsCostFraction` of its received revenue
///      buying units (FIFO) from manufacturers that service its sector — at each mfg's MfgUnitPrice.
///      Unspent revenue is retail margin.
///   5. If no commercial exists for a sector, those dollars evaporate (stay nowhere — agent
///      still pays). If commercial exists but no mfg can supply, commercial keeps the margin and
///      the goods-cost portion sits unused (no imports in M16).
/// </summary>
public static class CostOfLivingMechanic
{
    /// <summary>Fraction of commercial-sector revenue spent on actual goods. Calibration: lowered
    /// from 0.70 to 0.50 so commercials keep a 50% margin sufficient to cover small-shop overhead.</summary>
    public const double CommercialGoodsCostFraction = 0.50;

    public static void RunMonthlyCol(SimState state)
    {
        var operational = state.City.Structures.Values
            .Where(s => s.Category == StructureCategory.Commercial
                        && s.Type != StructureType.CorporateHq
                        && s.Operational
                        && !s.Inactive)
            .ToList();

        var bySector = new Dictionary<CommercialSector, List<Structure>>();
        foreach (var s in operational)
        {
            if (s.Sector is not CommercialSector sec) continue;
            if (!bySector.TryGetValue(sec, out var list))
            {
                list = new List<Structure>();
                bySector[sec] = list;
            }
            list.Add(s);
        }

        // Per the historic "no commercial → COL not paid" invariant: agents only spend on sectors
        // where commercial exists. Without commercial, the dollars stay in agent savings.
        var foodTotal = 0;
        var retailTotal = 0;
        var entertainmentTotal = 0;

        var foodAvailable = bySector.ContainsKey(CommercialSector.Food);
        var retailAvailable = bySector.ContainsKey(CommercialSector.Retail);
        var entAvailable = bySector.ContainsKey(CommercialSector.Entertainment);

        foreach (var agent in state.City.Agents.Values)
        {
            var spend = 0;
            if (foodAvailable)
            {
                var food = CostOfLiving.SectorAmount(agent.EducationTier, CommercialSector.Food);
                spend += food;
                foodTotal += food;
            }
            if (retailAvailable)
            {
                var retail = CostOfLiving.SectorAmount(agent.EducationTier, CommercialSector.Retail);
                spend += retail;
                retailTotal += retail;
            }
            if (entAvailable)
            {
                var ent = CostOfLiving.SectorAmount(agent.EducationTier, CommercialSector.Entertainment);
                spend += ent;
                entertainmentTotal += ent;
            }
            agent.Savings -= spend;
        }

        DistributeAndBuy(state, bySector, CommercialSector.Food, foodTotal);
        DistributeAndBuy(state, bySector, CommercialSector.Retail, retailTotal);
        DistributeAndBuy(state, bySector, CommercialSector.Entertainment, entertainmentTotal);
    }

    private static void DistributeAndBuy(
        SimState state,
        Dictionary<CommercialSector, List<Structure>> bySector,
        CommercialSector sector,
        int sectorDollars)
    {
        if (sectorDollars <= 0) return;
        if (!bySector.TryGetValue(sector, out var commercials) || commercials.Count == 0)
        {
            // No commercial in this sector — dollars evaporate (agents already paid).
            return;
        }

        // Pro-rata distribute revenue to commercials in sector.
        var perStructure = sectorDollars / commercials.Count;
        var remainder = sectorDollars % commercials.Count;
        foreach (var c in commercials)
        {
            var share = perStructure + (remainder > 0 ? 1 : 0);
            if (remainder > 0) remainder--;
            c.CashBalance += share;
            c.MonthlyRevenue += share;

            var goodsBudget = (int)(share * CommercialGoodsCostFraction);
            if (goodsBudget > 0)
            {
                BuyFromMfgsServicingSector(state, c, sector, goodsBudget);
            }
        }
    }

    /// <summary>
    /// Spend `dollarBudget` buying units (FIFO across manufacturers) from any mfg servicing this
    /// sector. Cash leaves the commercial structure; revenue accrues to the mfg. Any budget that
    /// can't be filled locally goes to imports at TaxRates.ImportUpcharge upcharge — commercial
    /// pays MORE than the budget (cash deficit) and the dollars leave the local economy.
    /// </summary>
    private static void BuyFromMfgsServicingSector(
        SimState state,
        Structure commercial,
        CommercialSector sector,
        int dollarBudget)
    {
        foreach (var mfg in state.City.Structures.Values)
        {
            if (dollarBudget <= 0) break;
            if (!Industrial.IsManufacturer(mfg.Type)) continue;
            if (!mfg.Operational || mfg.Inactive) continue;
            if (!mfg.ManufacturerSectors.Contains(sector)) continue;
            if (mfg.MfgOutputStock <= 0) continue;
            if (mfg.MfgUnitPrice <= 0) continue;

            var affordable = dollarBudget / mfg.MfgUnitPrice;
            if (affordable <= 0) continue;

            var qty = Math.Min(affordable, mfg.MfgOutputStock);
            if (qty <= 0) continue;

            var cost = qty * mfg.MfgUnitPrice;

            commercial.CashBalance -= cost;
            commercial.MonthlyExpenses += cost;
            mfg.CashBalance += cost;
            mfg.MonthlyRevenue += cost;
            mfg.MfgOutputStock -= qty;
            mfg.MonthlySalesUnits += qty;

            dollarBudget -= cost;
        }

        // Imports fallback: unmet budget goes off-region at base + upcharge. Commercial loses the
        // full upcharged amount (worse than local sourcing); the dollars leak from the economy.
        // Founding phase waives the upcharge so shops can survive on imports while pop grows.
        if (dollarBudget > 0)
        {
            var upcharge = FoundingPhase.EffectiveImportUpcharge(state);
            var importCost = dollarBudget + (int)(dollarBudget * upcharge);
            commercial.CashBalance -= importCost;
            commercial.MonthlyExpenses += importCost;
        }
    }
}
