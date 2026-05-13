using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Residential structure defaults — capacity, rent, build duration. M16: construction recipes
/// removed; M17 will reintroduce them as a single Construction-sector cost (treasury pays
/// home-builder commercial, which pulls from construction-sector manufacturers).
/// </summary>
public static class Residential
{
    /// <summary>Construction duration in ticks (90 = 3 months) for all residential types.</summary>
    public const int BuildDurationTicks = 90;

    public static int Capacity(StructureType type) => type switch
    {
        StructureType.House => 4,
        StructureType.Apartment => 40,
        StructureType.Townhouse => 12,
        StructureType.Condo => 25,
        StructureType.AffordableHousing => 40,
        _ => throw new ArgumentOutOfRangeException(nameof(type), $"{type} is not a residential structure"),
    };

}
