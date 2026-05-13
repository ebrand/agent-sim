using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Variable workforce for industrial structures. Each month, compute target staffing from recent
/// demand (units sold/pulled) and lay off excess workers. Underutilization happens when downstream
/// demand is smaller than production capacity (which is JobSlots × MaxOutputPerDay × 30/mo).
///
/// Rule of thumb:
///   target_staff = ceil(daily_demand / MaxOutputPerDay)
///   where daily_demand = MonthlySalesUnits / 30
///
/// Workers laid off return to the unemployed pool. CommercialOperationMechanic will rehire them
/// next tick if any operational structure has open slots (including in another industry).
/// Note: existing job-slot caps on the structure still apply — staffing never exceeds JobSlots.
/// </summary>
public static class ProductionStaffingMechanic
{
    /// <summary>Run monthly after settlement. Resets MonthlySalesUnits after using it.</summary>
    public static void RunMonthly(SimState state)
    {
        foreach (var s in state.City.Structures.Values)
        {
            if (!Industrial.IsIndustrial(s.Type)) continue;
            if (!s.Operational) continue;

            var currentStaff = s.EmployeeIds.Count;
            var maxStaff = s.JobSlots.Values.Sum();

            // Target: enough staff to produce last month's actual sales rate (capped at maxStaff).
            // Always keep at least 1 worker so the structure isn't permanently dormant.
            int dailyDemand = (s.MonthlySalesUnits + 29) / 30;
            int targetStaff;
            if (dailyDemand <= 0)
            {
                // No sales last month — keep minimum staffing of 1 to allow restart on demand.
                targetStaff = 1;
            }
            else
            {
                targetStaff = Math.Max(1, (dailyDemand + Industrial.MaxOutputPerDay - 1) / Industrial.MaxOutputPerDay);
            }
            targetStaff = Math.Min(targetStaff, maxStaff);

            if (currentStaff > targetStaff)
            {
                LayOff(state, s, currentStaff - targetStaff);
            }

            s.MonthlySalesUnits = 0;
        }
    }

    private static void LayOff(SimState state, Structure s, int count)
    {
        if (count <= 0) return;

        // FIFO layoff: last hired first to leave. EmployeeIds is in insertion order.
        var toRemove = s.EmployeeIds.TakeLast(count).ToList();
        foreach (var id in toRemove)
        {
            s.EmployeeIds.Remove(id);
            if (state.City.Agents.TryGetValue(id, out var agent))
            {
                if (agent.CurrentJobTier is EducationTier tier)
                {
                    s.FilledSlots[tier] = Math.Max(0, s.FilledSlots.GetValueOrDefault(tier) - 1);
                }
                agent.EmployerStructureId = null;
                agent.CurrentJobTier = null;
                agent.CurrentWage = 0;
            }
        }
    }
}
