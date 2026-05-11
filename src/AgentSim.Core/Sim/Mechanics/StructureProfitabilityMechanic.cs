using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// End-of-month profitability check. Per `structures.md`:
///   - 2 consecutive unprofitable months → structure goes inactive (lays off employees)
///   - Inactive structures auto-reactivate after 1 month of being dormant
///   - Treasury-funded structures (civic / healthcare / education / utility) and residential
///     are NOT subject to this check; only commercial and industrial operate on profit/loss.
///
/// Fires on day 30 AFTER all settlements (so utilities, taxes, COL revenue are all in
/// MonthlyRevenue / MonthlyExpenses) but BEFORE the monthly reset.
/// </summary>
public static class StructureProfitabilityMechanic
{
    public static void EndOfMonthCheck(SimState state)
    {
        foreach (var structure in state.City.Structures.Values)
        {
            if (!IsProfitabilityTracked(structure)) continue;
            if (structure.UnderConstruction) continue;

            if (structure.Inactive)
            {
                // Dormant — count months and auto-reactivate after 1 month.
                structure.InactiveMonths++;
                if (structure.InactiveMonths >= 1)
                {
                    Reactivate(structure);
                }
                continue;
            }

            // Operational — evaluate this month's profit.
            var profit = structure.MonthlyRevenue - structure.MonthlyExpenses;
            if (profit < 0)
            {
                if (structure.UnprofitableWarning)
                {
                    // Two consecutive unprofitable months → go inactive.
                    GoInactive(state, structure);
                }
                else
                {
                    structure.UnprofitableWarning = true;
                }
            }
            else
            {
                // Profitable — clear any prior warning.
                structure.UnprofitableWarning = false;
            }
        }
    }

    private static bool IsProfitabilityTracked(Structure s) =>
        s.Category == StructureCategory.Commercial
        || s.Category == StructureCategory.IndustrialExtractor
        || s.Category == StructureCategory.IndustrialProcessor
        || s.Category == StructureCategory.IndustrialManufacturer
        || s.Category == StructureCategory.IndustrialStorage;

    private static void GoInactive(SimState state, Structure structure)
    {
        structure.Inactive = true;
        structure.UnprofitableWarning = false;
        structure.InactiveMonths = 0;

        // Lay off all employees: clear their employment, leave them wageless (they enter the
        // standard monthly emigration check next month).
        foreach (var employeeId in structure.EmployeeIds.ToList())
        {
            if (state.City.Agents.TryGetValue(employeeId, out var agent))
            {
                agent.EmployerStructureId = null;
                agent.CurrentWage = 0;
            }
        }
        structure.EmployeeIds.Clear();
        structure.FilledSlots.Clear();
    }

    private static void Reactivate(Structure structure)
    {
        structure.Inactive = false;
        structure.InactiveMonths = 0;
        structure.UnprofitableWarning = false;
        // FilledSlots was cleared on inactivation. Hiring picks up next tick via
        // CommercialOperationMechanic.HireForNewlyOperationalStructures.
    }
}
