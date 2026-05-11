using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// M12: monthly sweep of industrial profits into their parent CorporateHq, and corporate-profit
/// tax to the city treasury. Fires on day 30, after the profitability check (which uses raw
/// MonthlyRevenue / MonthlyExpenses) and BEFORE the monthly accumulator reset.
///
/// Sweep rule: for each owned industrial structure, compute profit = MonthlyRevenue - MonthlyExpenses.
/// Only positive profit is swept (a loss-making subsidiary keeps its cash; it's the HQ's
/// responsibility to recapitalize if it wants to keep that subsidiary alive). The swept amount
/// moves from subsidiary.CashBalance into HQ.CashBalance and is recorded as HQ.MonthlyRevenue.
///
/// Corporate profit tax: a fixed fraction (TaxRates.CorporateProfit) of the total swept amount
/// goes to the city treasury. This replaces sales tax for HQs (they don't sell to consumers).
/// Per design discussion: tax is on profits, not gross revenue — a barely-solvent chain pays
/// little tax, a thriving one pays much more.
/// </summary>
public static class CorporateProfitMechanic
{
    public static void SweepAndTax(SimState state)
    {
        // Snapshot HQs to avoid mutating while iterating.
        var hqs = state.City.Structures.Values
            .Where(s => s.Type == StructureType.CorporateHq)
            .ToList();

        foreach (var hq in hqs)
        {
            if (!hq.Operational || hq.Inactive) continue;

            int totalSwept = 0;
            foreach (var ownedId in hq.OwnedStructureIds)
            {
                if (!state.City.Structures.TryGetValue(ownedId, out var owned)) continue;
                if (!owned.Operational || owned.Inactive) continue;

                var profit = owned.MonthlyRevenue - owned.MonthlyExpenses;
                if (profit <= 0) continue;  // only positive profit gets swept

                // Transfer profit from subsidiary to HQ.
                owned.CashBalance -= profit;
                hq.CashBalance += profit;
                totalSwept += profit;
            }

            if (totalSwept <= 0) continue;

            hq.MonthlyRevenue += totalSwept;

            // Two taxes on swept profit, both feeding the city treasury:
            //   1. Corporate-profit tax — flat 25% (TaxRates.CorporateProfit).
            //   2. Externality tax — per-industry rate reflecting ecological cost (Oil highest,
            //      Agriculture lowest). Funds the city's ecological mitigation conceptually.
            var corpTax = (int)(totalSwept * TaxRates.CorporateProfit);
            var externalityTax = (int)(totalSwept * TaxRates.Externality(hq.Industry!.Value));
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
