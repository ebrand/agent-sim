using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// When commercial or industrial structures become operational, they fill their job slots from the
/// city's unemployed agent pool via FIFO assignment.
/// </summary>
public static class CommercialOperationMechanic
{
    /// <summary>
    /// Called each tick to detect structures that just completed construction and hire workers for them.
    /// Idempotent: only hires for structures that don't yet have all their slots filled.
    /// Covers commercial AND industrial categories.
    /// </summary>
    public static void HireForNewlyOperationalStructures(SimState state)
    {
        foreach (var structure in state.City.Structures.Values)
        {
            if (!structure.Operational || structure.Inactive) continue;
            var cat = structure.Category;
            var employsWorkers = cat == StructureCategory.Commercial
                || cat == StructureCategory.IndustrialExtractor
                || cat == StructureCategory.IndustrialProcessor
                || cat == StructureCategory.IndustrialManufacturer
                || cat == StructureCategory.Civic
                || cat == StructureCategory.Healthcare
                || cat == StructureCategory.Education
                || cat == StructureCategory.Utility;
            if (!employsWorkers) continue;
            if (!AllSlotsFilled(structure))
            {
                JobAssignmentMechanic.FillJobSlots(state, structure);
            }
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
