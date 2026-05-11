using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// M12/M13: monthly corporate-profit tax + externality tax. After M13's consolidation, the HQ's
/// `MonthlyRevenue` and `MonthlyExpenses` already reflect the entire chain's books — storage
/// sales go directly to HQ, and every sub's utility / property tax / wage charge flowed to HQ
/// via the ChargeExpenseToHqOrSelf helper. So this mechanic doesn't sweep anymore; it just taxes
/// the HQ's net profit.
///
/// Net profit = HQ.MonthlyRevenue - HQ.MonthlyExpenses. Only positive net is taxed. Both taxes
/// go to the city treasury:
///   - Corporate profit tax: flat 25% (TaxRates.CorporateProfit)
///   - Externality tax: per industry (Oil highest, Agriculture lowest)
///
/// Runs on day 30, AFTER all monthly settlements have populated R/E and BEFORE the monthly reset.
/// </summary>
public static class CorporateProfitMechanic
{
    public static void SweepAndTax(SimState state)
    {
        var hqs = state.City.Structures.Values
            .Where(s => s.Type == StructureType.CorporateHq)
            .ToList();

        foreach (var hq in hqs)
        {
            if (!hq.Operational || hq.Inactive) continue;

            var netProfit = hq.MonthlyRevenue - hq.MonthlyExpenses;
            if (netProfit <= 0) continue;  // loss — no tax this month

            var corpTax = (int)(netProfit * TaxRates.CorporateProfit);
            var externalityTax = (int)(netProfit * TaxRates.Externality(hq.Industry!.Value));
            var totalTax = corpTax + externalityTax;
            if (totalTax > 0)
            {
                hq.CashBalance -= totalTax;
                hq.MonthlyExpenses += totalTax;
                state.City.TreasuryBalance += totalTax;
            }
        }
    }
}
