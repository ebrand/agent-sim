namespace AgentSim.Core.Types;

/// <summary>
/// Player-drawn link between a producer (Generator / Well) and a distributor
/// (ElectricityDistribution / WaterDistribution). The edge carries power or water across the
/// map without requiring a physical chain of touching structures. Distribution still spreads
/// locally via 8-neighbor adjacency from the target distributor's coverage radius.
/// </summary>
public sealed class NetworkEdge
{
    public required long Id { get; init; }
    public required long SourceStructureId { get; init; }
    public required long TargetStructureId { get; init; }
    public required NetworkKind Kind { get; init; }
}

public enum NetworkKind
{
    Power,
    Water,
}
