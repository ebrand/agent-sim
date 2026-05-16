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
    /// <summary>(Width, Height) in tiles. Uniform 10×10 for now (realistic city-block scale).</summary>
    public static (int W, int H) For(StructureType type) => (10, 10);

    /// <summary>True when the structure occupies tiles in a zone (residential/commercial).
    /// Non-zoned structures (civic/industrial outside HQ/restoration) place anywhere on the map.</summary>
    public static bool IsZoned(StructureType type) =>
        type.Category() == StructureCategory.Residential
        || type.Category() == StructureCategory.Commercial;
}
