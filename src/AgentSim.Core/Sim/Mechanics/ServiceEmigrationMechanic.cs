using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// End-of-month worst-of service emigration roll. Per `feedback-loops.md`:
///   per_agent_monthly_emigration_chance = max(0, (threshold - worst_satisfaction%) / 100) × scale
///
/// Default threshold 60, scale 0.02 → max 1.2%/month at 0% worst satisfaction.
///
/// Independent from the M6 insolvency emigration (EmigrationMechanic). An agent who emigrates
/// from insolvency this month is already gone before this check fires.
/// </summary>
public static class ServiceEmigrationMechanic
{
    public static void EndOfMonthCheck(SimState state)
    {
        if (!state.Config.ServiceEmigrationEnabled) return;
        if (state.City.Agents.Count == 0) return;

        var snapshot = ServiceSatisfactionMechanic.Compute(state);

        // Iterate in stable agent-id order so seeded sims produce identical results.
        var agents = state.City.Agents.Values.OrderBy(a => a.Id).ToList();
        foreach (var agent in agents)
        {
            var worst = ServiceSatisfactionMechanic.WorstOfForAgent(agent, snapshot);
            if (worst >= Services.EmigrationThresholdPercent) continue;

            var chance = (Services.EmigrationThresholdPercent - worst) / 100.0 * Services.EmigrationScale;
            if (chance <= 0) continue;

            var roll = state.Prng.NextDouble();
            if (roll < chance)
            {
                Emigrate(state, agent);
            }
        }
    }

    /// <summary>
    /// Service-pressure emigration. The agent is removed from city and returned to the reservoir
    /// at their CURRENT education tier (matches the insolvency-emigration path).
    /// </summary>
    private static void Emigrate(SimState state, Agent agent)
    {
        // Vacate residence.
        if (agent.ResidenceStructureId is long resId
            && state.City.Structures.TryGetValue(resId, out var residence))
        {
            residence.ResidentIds.Remove(agent.Id);
        }

        // Vacate employment slot.
        if (agent.EmployerStructureId is long empId
            && state.City.Structures.TryGetValue(empId, out var employer)
            && agent.CurrentJobTier is EducationTier jobTier)
        {
            employer.EmployeeIds.Remove(agent.Id);
            if (employer.FilledSlots.TryGetValue(jobTier, out var count) && count > 0)
            {
                employer.FilledSlots[jobTier] = count - 1;
            }
        }

        // Vacate school seat.
        if (agent.EnrolledStructureId is long schoolId
            && state.City.Structures.TryGetValue(schoolId, out var school))
        {
            school.EnrolledStudentIds.Remove(agent.Id);
        }

        state.City.Agents.Remove(agent.Id);
        state.Region.AgentReservoir.Increment(agent.EducationTier);
    }
}
