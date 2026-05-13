using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// M17 construction-goods routing. When a structure is placed, the cost is deducted from the
/// payer (treasury or HQ) as before. This mechanic then routes those dollars through any
/// operational Construction-sector commercial(s), which spend a fraction of the received revenue
/// buying generic units from Construction-sector manufacturers (FIFO at each mfg's UnitPrice).
///
/// "Imports fallback" is implicit:
///   - If no Construction-sector commercial exists, the dollars leak abroad — treasury already
///     paid; no local recipient. The build still happens (all goods imported).
///   - If a commercial receives revenue but no mfg supply is available, the commercial keeps the
///     unspent goods budget as cash (effectively the commercial "imports" without explicit price
///     upcharge — the build cost was already collected from the payer).
/// </summary>
public static class ConstructionGoodsMechanic
{
    /// <summary>Fraction of routed cost the commercial spends buying construction-sector mfg units.
    /// Matched to CostOfLivingMechanic.CommercialGoodsCostFraction.</summary>
    public const double GoodsCostFraction = 0.50;

    /// <summary>Route a construction-cost outflow through the construction commercial → mfg chain.</summary>
    public static void Route(SimState state, int totalCost)
    {
        if (totalCost <= 0) return;

        var commercials = state.City.Structures.Values
            .Where(s => s.Category == StructureCategory.Commercial
                        && s.Type != StructureType.CorporateHq
                        && s.Operational
                        && !s.Inactive
                        && s.Sector == CommercialSector.Construction)
            .ToList();

        if (commercials.Count == 0)
        {
            // No construction-sector commercial — dollars leak abroad (full import).
            return;
        }

        var perStructure = totalCost / commercials.Count;
        var remainder = totalCost % commercials.Count;
        foreach (var c in commercials)
        {
            var share = perStructure + (remainder > 0 ? 1 : 0);
            if (remainder > 0) remainder--;
            c.CashBalance += share;
            c.MonthlyRevenue += share;

            var goodsBudget = (int)(share * GoodsCostFraction);
            if (goodsBudget > 0)
            {
                BuyFromConstructionMfgs(state, c, goodsBudget);
            }
        }
    }

    private static void BuyFromConstructionMfgs(SimState state, Structure commercial, int dollarBudget)
    {
        foreach (var mfg in state.City.Structures.Values)
        {
            if (dollarBudget <= 0) break;
            if (!Industrial.IsManufacturer(mfg.Type)) continue;
            if (!mfg.Operational || mfg.Inactive) continue;
            if (!mfg.ManufacturerSectors.Contains(CommercialSector.Construction)) continue;
            if (mfg.MfgOutputStock <= 0 || mfg.MfgUnitPrice <= 0) continue;

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

        // Imports fallback: same as COL — founding phase waives upcharge so construction can
        // proceed without crippling early-game commercial.
        if (dollarBudget > 0)
        {
            var upcharge = FoundingPhase.EffectiveImportUpcharge(state);
            var importCost = dollarBudget + (int)(dollarBudget * upcharge);
            commercial.CashBalance -= importCost;
            commercial.MonthlyExpenses += importCost;
        }
    }
}
