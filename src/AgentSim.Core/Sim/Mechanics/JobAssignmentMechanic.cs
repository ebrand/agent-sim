using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Job assignment: agents are matched to job slots via a FIFO queue per education tier.
/// Higher-tier agents can take lower-tier work (college can do uneducated jobs), preferring
/// the highest tier they qualify for. Lower-tier agents cannot take higher-tier slots.
/// </summary>
public static class JobAssignmentMechanic
{
    /// <summary>
    /// Fills all open job slots in the given structure from the city's unemployed agent pool.
    /// FIFO: oldest unemployed agent (by ID order, which is insertion order) gets first crack.
    /// Higher-tier agents try matching slots first, then drop to lower tiers if no match.
    /// </summary>
    public static void FillJobSlots(SimState state, Structure structure)
    {
        if (!structure.Operational || structure.Inactive) return;

        // Iterate slots from highest tier to lowest (so higher-paying slots fill first with eligible workers).
        var tiersHighToLow = new[]
        {
            EducationTier.College,
            EducationTier.Secondary,
            EducationTier.Primary,
            EducationTier.Uneducated,
        };

        foreach (var slotTier in tiersHighToLow)
        {
            var slotCount = structure.JobSlots.GetValueOrDefault(slotTier);
            var filled = structure.FilledSlots.GetValueOrDefault(slotTier);
            var openSlots = slotCount - filled;
            if (openSlots <= 0) continue;

            // Find unemployed agents qualifying for this tier (their tier >= slotTier).
            // FIFO by agent ID (insertion order).
            var candidates = state.City.Agents.Values
                .Where(a => a.EmployerStructureId == null
                            && (int)a.EducationTier >= (int)slotTier)
                .OrderBy(a => a.Id)
                .Take(openSlots)
                .ToList();

            foreach (var agent in candidates)
            {
                HireAgent(structure, agent, slotTier);
            }
        }
    }

    /// <summary>
    /// Fills slots in the highest-tier-affordable manner: a college agent takes a college slot first
    /// if one is open. Called by `FillJobSlots` but exposed for use when an agent becomes available
    /// later (e.g., after schooling completes).
    /// </summary>
    public static bool TryHireAgent(SimState state, Agent agent)
    {
        if (agent.EmployerStructureId != null) return false;

        // Try highest-tier matching slots first.
        var tiers = new[]
        {
            EducationTier.College,
            EducationTier.Secondary,
            EducationTier.Primary,
            EducationTier.Uneducated,
        };

        foreach (var slotTier in tiers)
        {
            if ((int)agent.EducationTier < (int)slotTier) continue;  // agent under-qualified for this tier

            var structure = state.City.Structures.Values
                .FirstOrDefault(s => s.Operational
                                     && !s.Inactive
                                     && s.JobSlots.GetValueOrDefault(slotTier) > s.FilledSlots.GetValueOrDefault(slotTier));
            if (structure != null)
            {
                HireAgent(structure, agent, slotTier);
                return true;
            }
        }

        return false;
    }

    private static void HireAgent(Structure structure, Agent agent, EducationTier slotTier)
    {
        structure.EmployeeIds.Add(agent.Id);
        structure.FilledSlots[slotTier] = structure.FilledSlots.GetValueOrDefault(slotTier) + 1;
        agent.EmployerStructureId = structure.Id;
        agent.CurrentWage = Defaults.Wages.MonthlyWage(slotTier);
    }
}
