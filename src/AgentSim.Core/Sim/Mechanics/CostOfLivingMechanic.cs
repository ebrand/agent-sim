using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Cost-of-living spending: agents pay COL (food, clothing, household, entertainment) to
/// commercial structures monthly. Commercial then pays storage for goods to fulfill that demand,
/// falling through storage → regional goods reservoir → imports (at 25% upcharge) in priority order.
///
/// Per `economy.md`: if no commercial structure exists, COL spending fails silently — money stays
/// in the agent's savings. If commercial exists but cannot get goods from any source (e.g., empty
/// storage, empty region, no imports), the import fallback handles it (off-region world is always
/// available, just expensive).
///
/// Goods-backed COL (food, clothing, household) consumes physical units; entertainment (service-only)
/// produces no goods consumption.
///
/// Commercial spends a fixed fraction of goods-backed COL revenue on actual goods. The default 0.70
/// implies a 30% commercial retail margin — the per-agent retail price is implicitly 1.43× the
/// manufactured (wholesale) price, encoding the markup without an explicit retail-price table.
/// </summary>
public static class CostOfLivingMechanic
{
    /// <summary>Fraction of goods-backed COL revenue that commercial spends on actual goods (rest is margin).</summary>
    public const double CommercialGoodsCostFraction = 0.70;

    /// <summary>Fires on day 30 (before the end-of-month emigration check).</summary>
    public static void RunMonthlyCol(SimState state)
    {
        // M12: CorporateHq is in the Commercial category but is NOT consumer-facing — it's a
        // holding company for industrial subsidiaries, not a retail outlet. Exclude it from
        // COL distribution (agents can't buy household goods from Big Oil HQ).
        var commercials = state.City.Structures.Values
            .Where(s => s.Category == StructureCategory.Commercial
                        && s.Type != StructureType.CorporateHq
                        && s.Operational
                        && !s.Inactive)
            .ToList();

        if (commercials.Count == 0)
        {
            // No commercial → all COL spending fails silently. Money stays in agent savings.
            return;
        }

        // Aggregate COL revenue from agents AND per-good dollar demand
        var totalRevenue = 0;
        var foodDollars = 0;
        var clothingDollars = 0;
        var householdDollars = 0;

        foreach (var agent in state.City.Agents.Values)
        {
            var col = CostOfLiving.MonthlyCol(agent.EducationTier);
            agent.Savings -= col;
            totalRevenue += col;

            var wage = Wages.MonthlyWage(agent.EducationTier);
            foodDollars += (int)(wage * CostOfLiving.FoodFraction);
            clothingDollars += (int)(wage * CostOfLiving.ClothingFraction);
            householdDollars += (int)(wage * CostOfLiving.HouseholdFraction);
        }

        // Distribute total COL revenue evenly across operational commercial structures.
        DistributeProRata(commercials, totalRevenue, isInflow: true);

        // Commercial pays for goods. 70% of goods-backed COL goes to storage / region / imports;
        // commercial keeps the remaining 30% as its retail margin.
        var foodGoodsCost = (int)(foodDollars * CommercialGoodsCostFraction);
        var clothingGoodsCost = (int)(clothingDollars * CommercialGoodsCostFraction);
        var householdGoodsCost = (int)(householdDollars * CommercialGoodsCostFraction);

        FulfillGoodsDemand(state, commercials, ManufacturedGood.Food, foodGoodsCost);
        FulfillGoodsDemand(state, commercials, ManufacturedGood.Clothing, clothingGoodsCost);
        FulfillGoodsDemand(state, commercials, ManufacturedGood.Household, householdGoodsCost);
    }

    /// <summary>
    /// Commercial structures collectively buy goods worth `dollarAmount` of `good`.
    /// Fulfill from storage → regional reservoir → imports in priority order.
    /// Money flows out from commercial structures (deducted pro-rata).
    /// </summary>
    private static void FulfillGoodsDemand(
        SimState state,
        List<Structure> commercials,
        ManufacturedGood good,
        int dollarAmount)
    {
        if (dollarAmount <= 0) return;

        var unitPrice = Industrial.ManufacturedGoodPrice(good);
        var unitsDemanded = dollarAmount / unitPrice;
        if (unitsDemanded <= 0) return;

        var actualCost = 0;
        var unitsRemaining = unitsDemanded;

        // 1. Try local storage
        foreach (var storage in state.City.Structures.Values
                     .Where(s => s.Type == StructureType.Storage
                                 && s.Operational && !s.Inactive))
        {
            if (unitsRemaining <= 0) break;
            var available = storage.ManufacturedStorage.GetValueOrDefault(good);
            var pull = Math.Min(available, unitsRemaining);
            if (pull <= 0) continue;

            var cost = pull * unitPrice;
            storage.ManufacturedStorage[good] = available - pull;
            storage.CashBalance += cost;
            storage.MonthlyRevenue += cost;
            actualCost += cost;
            unitsRemaining -= pull;
        }

        // 2. Try regional reservoir (regional treasury is functionally infinite — no balance tracked)
        if (unitsRemaining > 0)
        {
            var regionAvailable = state.Region.GoodsReservoir.GetValueOrDefault(good);
            var pull = Math.Min(regionAvailable, unitsRemaining);
            if (pull > 0)
            {
                var cost = pull * unitPrice;
                state.Region.GoodsReservoir[good] = regionAvailable - pull;
                // Money to regional treasury (no balance tracked)
                actualCost += cost;
                unitsRemaining -= pull;
            }
        }

        // 3. Imports at 25% upcharge — off-region world is always available
        if (unitsRemaining > 0)
        {
            var importCost = (int)(unitsRemaining * unitPrice * (1 + TaxRates.ImportUpcharge));
            actualCost += importCost;
            unitsRemaining = 0;
            // Money leaves the city economy (paid to off-region rights-holders)
        }

        // Deduct from commercial structures pro-rata
        DistributeProRata(commercials, actualCost, isInflow: false);
    }

    /// <summary>Distribute an amount across a list of structures pro-rata (handles remainder).</summary>
    private static void DistributeProRata(List<Structure> structures, int amount, bool isInflow)
    {
        if (structures.Count == 0 || amount == 0) return;
        var per = amount / structures.Count;
        var remainder = amount % structures.Count;
        foreach (var s in structures)
        {
            var share = per + (remainder > 0 ? 1 : 0);
            if (remainder > 0) remainder--;
            if (isInflow)
            {
                s.CashBalance += share;
                s.MonthlyRevenue += share;
            }
            else
            {
                s.CashBalance -= share;
                s.MonthlyExpenses += share;
            }
        }
    }
}
