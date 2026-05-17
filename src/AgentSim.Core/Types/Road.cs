namespace AgentSim.Core.Types;

/// <summary>Float-precision 2D point used for road node positions. The 256-tile grid is still
/// the coordinate space (X, Y in tile units); nodes are not constrained to integer tiles.</summary>
public readonly record struct Point2(float X, float Y);

/// <summary>A node in the transportation graph: either a player-placed endpoint (start/end of
/// a drawn road) or an automatically-created intersection where two edges crossed.</summary>
public sealed class RoadNode
{
    public required long Id { get; init; }
    // Settable so the player can drag the node in the editor and the connected edges
    // automatically reflect the new position (they reference by id, not by coordinate).
    public Point2 Position { get; set; }
}

/// <summary>An edge in the transportation graph — one straight segment between two nodes.
/// Player-drawn "roads" may produce several edges if the segment crosses existing edges
/// (the sim auto-splits at intersection points). Lane configuration is carried per edge.</summary>
public sealed class RoadEdge
{
    public required long Id { get; init; }
    public required long FromNodeId { get; init; }
    public required long ToNodeId { get; init; }

    /// <summary>Lanes carrying traffic from FromNode → ToNode. Default 1.</summary>
    public int LanesForward { get; init; } = 1;
    /// <summary>Lanes carrying traffic from ToNode → FromNode. Default 1. Set 0 for one-way.</summary>
    public int LanesBackward { get; init; } = 1;

    public int TotalLanes => LanesForward + LanesBackward;

    /// <summary>Render width in tiles: <c>lanes × 4m + 2m</c> (1m shoulder each side).
    /// Sim treats 1 tile = 1m so this is also tile units. Default 1+1 → 10 tiles.</summary>
    public float WidthTiles => TotalLanes * 4f + 2f;
}
