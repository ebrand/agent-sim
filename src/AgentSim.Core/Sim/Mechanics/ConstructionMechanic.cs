using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Advances construction progress on all under-construction structures by 1 tick.
/// </summary>
public static class ConstructionMechanic
{
    public static void AdvanceConstruction(SimState state)
    {
        foreach (var structure in state.City.Structures.Values)
        {
            if (structure.UnderConstruction)
            {
                structure.ConstructionTicks++;
                if (!structure.UnderConstruction)
                {
                    state.LogEvent(SimEventSeverity.Info, "Construction",
                        $"{structure.Type} #{structure.Id} construction complete");
                }
            }
        }
    }
}
