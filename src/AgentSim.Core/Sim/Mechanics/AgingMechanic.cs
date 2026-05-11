using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Daily aging: every agent ages by 1 day per tick. When an agent reaches the lifespan
/// (default 21,600 days = 60 game-years), they die and are removed from the city.
/// Per `region.md`: deaths do NOT return the agent to the reservoir (death is true removal
/// from the simulation; the 60k total agent cap is freed up to allow births / new immigration).
/// </summary>
public static class AgingMechanic
{
    public static void RunDaily(SimState state)
    {
        // Snapshot to avoid mutating the dict while iterating
        var agents = state.City.Agents.Values.ToList();
        foreach (var agent in agents)
        {
            agent.AgeDays++;
            if (agent.AgeDays >= Demographics.LifespanDays)
            {
                Die(state, agent);
            }
        }
    }

    private static void Die(SimState state, Agent agent)
    {
        // Vacate residence (if any)
        if (agent.ResidenceStructureId is long resId
            && state.City.Structures.TryGetValue(resId, out var residence))
        {
            residence.ResidentIds.Remove(agent.Id);
        }

        // Vacate employment (if any). Decrement FilledSlots for the employer.
        // Note: we don't know which tier slot the agent was in without storing it on Agent.
        // For M7 simplicity: just decrement the lowest-tier filled slot, which is conservative
        // (under-filling at higher tiers triggers re-hire for those tiers next tick).
        // A future refinement is to store SlotTier on the agent.
        if (agent.EmployerStructureId is long empId
            && state.City.Structures.TryGetValue(empId, out var employer))
        {
            employer.EmployeeIds.Remove(agent.Id);
            // Decrement filled count for the agent's education tier (best approximation)
            var tier = agent.EducationTier;
            if (employer.FilledSlots.TryGetValue(tier, out var count) && count > 0)
            {
                employer.FilledSlots[tier] = count - 1;
            }
        }

        // Savings are lost (per `agents.md`: agent records removed on death; savings leave the economy).
        state.City.Agents.Remove(agent.Id);
    }
}
