namespace AgentSim.Core.Types;

/// <summary>
/// A player-defined zone (residential or commercial) within which auto-spawn places matching structures.
/// Spatial geometry is deferred to the spatial layer; for alpha-1 we just track type and a structure-count capacity.
/// </summary>
public sealed class Zone
{
    public required long Id { get; init; }
    public required ZoneType Type { get; init; }

    /// <summary>Maximum number of structures this zone can hold. Placeholder until spatial layer.</summary>
    public required int StructureCapacity { get; init; }

    /// <summary>IDs of structures currently in this zone (built or under construction).</summary>
    public List<long> StructureIds { get; } = new();
}
