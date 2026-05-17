using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Tile footprint (in 1m × 1m tiles) per structure type. All structures are axis-aligned
/// rectangles. Currently all structures are 2×2 to compress space during early playtesting —
/// makes it easy to fit a whole city in view. Variable footprints (1×1..6×6) can be restored
/// later once the gameplay loop is settled.
/// </summary>
public static class Footprint
{
    /// <summary>(Width, Height) in tiles. Uniform 20×20 for now — 4u × 4u when the placement
    /// grid is on its default 5m step. Variable rectangular footprints (4u × 5u, 4u × 6u, …)
    /// will come later once structure types are visually differentiated.</summary>
    public static (int W, int H) For(StructureType type) => (20, 20);

    /// <summary>True when the structure occupies tiles in a zone (residential/commercial).
    /// Non-zoned structures (civic/industrial outside HQ/restoration) place anywhere on the map.</summary>
    public static bool IsZoned(StructureType type) =>
        type.Category() == StructureCategory.Residential
        || type.Category() == StructureCategory.Commercial;
}
