namespace AgentSim.Core.Types;

/// <summary>
/// 2D tile grid representing the city map. Each tile records which structure occupies it (if any)
/// and which zone contains it (if any). Used for spatial placement validation, footprint
/// rendering, and adjacency / land-value computations.
/// </summary>
public sealed class Tilemap
{
    public const int MapSize = 256;

    private readonly long?[,] _structureAt = new long?[MapSize, MapSize];
    private readonly long?[,] _zoneAt = new long?[MapSize, MapSize];
    private readonly double[,] _landValue = new double[MapSize, MapSize];

    public int Size => MapSize;

    /// <summary>Highest land-value value across all tiles. Updated by LandValueMechanic each month.
    /// Used by the heatmap renderer to normalize colors.</summary>
    public double MaxLandValue { get; internal set; }

    public double LandValueAt(int x, int y) =>
        InBounds(x, y) ? _landValue[x, y] : 0.0;

    internal void SetLandValueAt(int x, int y, double v)
    {
        if (InBounds(x, y)) _landValue[x, y] = v;
    }

    internal void AccumulateLandValue(int x, int y, double delta)
    {
        if (InBounds(x, y)) _landValue[x, y] += delta;
    }

    internal void ResetLandValue()
    {
        Array.Clear(_landValue, 0, _landValue.Length);
        MaxLandValue = 0;
    }

    public long? StructureAt(int x, int y) =>
        InBounds(x, y) ? _structureAt[x, y] : null;

    public long? ZoneAt(int x, int y) =>
        InBounds(x, y) ? _zoneAt[x, y] : null;

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < MapSize && y < MapSize;

    public bool InBounds(int x, int y, int w, int h) =>
        x >= 0 && y >= 0 && x + w <= MapSize && y + h <= MapSize;

    /// <summary>True when no structure occupies any tile in the given rectangle.</summary>
    public bool IsAreaFree(int x, int y, int w, int h)
    {
        if (!InBounds(x, y, w, h)) return false;
        for (int dy = 0; dy < h; dy++)
        for (int dx = 0; dx < w; dx++)
            if (_structureAt[x + dx, y + dy].HasValue) return false;
        return true;
    }

    /// <summary>True when every tile in the rectangle belongs to the given zone.</summary>
    public bool AreaInZone(int x, int y, int w, int h, long zoneId)
    {
        if (!InBounds(x, y, w, h)) return false;
        for (int dy = 0; dy < h; dy++)
        for (int dx = 0; dx < w; dx++)
            if (_zoneAt[x + dx, y + dy] != zoneId) return false;
        return true;
    }

    /// <summary>Mark every tile in the rectangle as occupied by `structureId`.</summary>
    public void SetStructureFootprint(long structureId, int x, int y, int w, int h)
    {
        for (int dy = 0; dy < h; dy++)
        for (int dx = 0; dx < w; dx++)
            _structureAt[x + dx, y + dy] = structureId;
    }

    public void ClearStructureFootprint(int x, int y, int w, int h)
    {
        for (int dy = 0; dy < h; dy++)
        for (int dx = 0; dx < w; dx++)
            _structureAt[x + dx, y + dy] = null;
    }

    /// <summary>Mark every tile in the rectangle as part of `zoneId`.</summary>
    public void SetZoneArea(long zoneId, int x, int y, int w, int h)
    {
        for (int dy = 0; dy < h; dy++)
        for (int dx = 0; dx < w; dx++)
            _zoneAt[x + dx, y + dy] = zoneId;
    }

    /// <summary>Find the lowest (x, y) inside `bounds` where a w×h footprint fits.
    /// Returns null if no spot is free.</summary>
    public (int X, int Y)? FindFreeSpotInZone(long zoneId, ZoneBounds bounds, int w, int h)
    {
        for (int y = bounds.Y; y <= bounds.Y + bounds.Height - h; y++)
        for (int x = bounds.X; x <= bounds.X + bounds.Width - w; x++)
        {
            if (AreaInZone(x, y, w, h, zoneId) && IsAreaFree(x, y, w, h))
                return (x, y);
        }
        return null;
    }

    /// <summary>Find a free spot anywhere on the map for a w×h footprint (used for non-zoned
    /// structures: civic, industrial outside HQ, restoration).</summary>
    public (int X, int Y)? FindFreeSpotAnywhere(int w, int h)
    {
        for (int y = 0; y <= MapSize - h; y++)
        for (int x = 0; x <= MapSize - w; x++)
        {
            if (IsAreaFree(x, y, w, h)) return (x, y);
        }
        return null;
    }
}

/// <summary>Rectangular area in tile coordinates. (X, Y) is the bottom-left corner.</summary>
public readonly record struct ZoneBounds(int X, int Y, int Width, int Height)
{
    public bool Contains(int px, int py) =>
        px >= X && py >= Y && px < X + Width && py < Y + Height;
}
