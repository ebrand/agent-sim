namespace AgentSim.Core.Types;

/// <summary>
/// A constructed (or under-construction) structure in the city.
/// </summary>
public sealed class Structure
{
    public required long Id { get; init; }
    public required StructureType Type { get; init; }
    public required long ZoneId { get; init; }

    /// <summary>
    /// Construction progress in ticks. When equal to required build duration, the structure is operational.
    /// </summary>
    public int ConstructionTicks { get; set; }

    /// <summary>Total ticks required to complete construction (default 90 = 3 months).</summary>
    public int RequiredConstructionTicks { get; init; } = 90;

    public bool Operational => ConstructionTicks >= RequiredConstructionTicks;
    public bool UnderConstruction => !Operational;

    /// <summary>Whether the structure has gone inactive due to unprofitability (only relevant for commercial / industrial).</summary>
    public bool Inactive { get; set; }

    /// <summary>Whether the previous month was unprofitable (warning state before going inactive).</summary>
    public bool UnprofitableWarning { get; set; }

    /// <summary>Capacity in agent count (residential structures only). 0 for non-residential.</summary>
    public required int ResidentialCapacity { get; init; }

    /// <summary>Current resident IDs, for residential structures. Empty for non-residential.</summary>
    public List<long> ResidentIds { get; } = new();

    public StructureCategory Category => Type.Category();
}
