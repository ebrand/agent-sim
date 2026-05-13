using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Recomputes per-tile land value from scratch each month: every operational structure stamps
/// its (radius, weight) influence onto nearby tiles with linear falloff; residential and zoned-
/// but-non-amenity types contribute nothing. Industrial stamps are negative (pollution).
///
/// Negative LV is clamped to zero — disamenity can cancel amenities, but a tile far from any
/// amenity and near industry shouldn't dip below baseline. Downstream tax/rent/revenue
/// modulation can assume LV >= 0.
/// </summary>
public static class LandValueMechanic
{
    public static void RunMonthly(SimState state)
    {
        var tm = state.Region.Tilemap;
        tm.ResetLandValue();

        foreach (var structure in state.City.Structures.Values)
        {
            // Include under-construction structures: a building site announces the future
            // amenity, so land value can ramp up before the doors open. Inactive (demolished
            // / decommissioned) structures don't contribute.
            if (structure.Inactive) continue;
            if (structure.X < 0 || structure.Y < 0) continue;

            var stamp = LandValue.StampFor(structure.Type);
            if (stamp.Radius <= 0 || stamp.Weight == 0) continue;

            var (fw, fh) = Footprint.For(structure.Type);
            // Stamp radially from each tile of the footprint (sum of point-stamps). Cheaper to
            // approximate from a single center for large footprints; for the 1×1..6×6 range
            // we have, looping the footprint is fine and gives a smoother field.
            for (int sy = 0; sy < fh; sy++)
            for (int sx = 0; sx < fw; sx++)
            {
                int ox = structure.X + sx;
                int oy = structure.Y + sy;
                StampPoint(tm, ox, oy, stamp.Radius, stamp.Weight / (fw * fh));
            }
        }

        // Clamp negatives + find max.
        double max = 0;
        for (int y = 0; y < Tilemap.MapSize; y++)
        for (int x = 0; x < Tilemap.MapSize; x++)
        {
            double v = tm.LandValueAt(x, y);
            if (v < 0)
            {
                tm.SetLandValueAt(x, y, 0);
                v = 0;
            }
            if (v > max) max = v;
        }
        tm.MaxLandValue = max;
    }

    private static void StampPoint(Tilemap tm, int cx, int cy, int radius, double weight)
    {
        int rSq = radius * radius;
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            int distSq = dx * dx + dy * dy;
            if (distSq > rSq) continue;
            int tx = cx + dx;
            int ty = cy + dy;
            if (!tm.InBounds(tx, ty)) continue;
            double dist = System.Math.Sqrt(distSq);
            double falloff = 1.0 - dist / radius;
            tm.AccumulateLandValue(tx, ty, weight * falloff);
        }
    }

    /// <summary>Average land value across the structure's footprint. Used by rent/revenue
    /// modulation to translate "this structure sits on tiles worth X" into a multiplier.</summary>
    public static double AverageOverFootprint(Tilemap tm, Structure structure)
    {
        if (structure.X < 0 || structure.Y < 0) return 0;
        var (w, h) = Footprint.For(structure.Type);
        double sum = 0;
        int n = 0;
        for (int dy = 0; dy < h; dy++)
        for (int dx = 0; dx < w; dx++)
        {
            sum += tm.LandValueAt(structure.X + dx, structure.Y + dy);
            n++;
        }
        return n > 0 ? sum / n : 0;
    }
}
