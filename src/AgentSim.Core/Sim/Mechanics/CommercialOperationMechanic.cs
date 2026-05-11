using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// When a commercial structure becomes operational (construction completes), it fills its job slots
/// from the city's unemployed agent pool via FIFO assignment.
/// </summary>
public static class CommercialOperationMechanic
{
    /// <summary>
    /// Called each tick to detect structures that just completed construction and hire workers for them.
    /// Idempotent: only hires for structures that don't yet have all their slots filled.
    /// </summary>
    public static void HireForNewlyOperationalStructures(SimState state)
    {
        foreach (var structure in state.City.Structures.Values)
        {
            if (!structure.Operational || structure.Inactive) continue;
            if (structure.Category != StructureCategory.Commercial) continue;
            if (AllSlotsFilled(structure)) continue;

            JobAssignmentMechanic.FillJobSlots(state, structure);
        }
    }

    private static bool AllSlotsFilled(Structure s)
    {
        foreach (var (tier, count) in s.JobSlots)
        {
            if (s.FilledSlots.GetValueOrDefault(tier) < count) return false;
        }
        return true;
    }
}
