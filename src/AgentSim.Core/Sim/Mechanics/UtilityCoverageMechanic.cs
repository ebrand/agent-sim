using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Computes per-structure power / water coverage by flood-filling outward from operational
/// producer structures. Coverage spreads via 8-neighbor adjacency between any structure
/// footprints (touching buildings share utility). Distribution structures extend reach by
/// projecting coverage to all structure tiles within a fixed radius — they bridge gaps
/// between disconnected neighborhood blobs.
///
/// Cadence: runs on every spatial placement (so the UI reflects connections immediately)
/// and at the start of the monthly settlement so downstream service satisfaction sees a
/// fresh snapshot.
/// </summary>
public static class UtilityCoverageMechanic
{
    /// <summary>Radius in tiles that a distribution structure projects coverage to other
    /// structure tiles. Lets player bridge ~one footprint width plus margin.</summary>
    public const int DistributionRadius = 6;

    public static void Compute(SimState state)
    {
        ComputeForResource(state, isProducer: IsPowerProducer,
                                  isDistributor: IsPowerDistributor,
                                  setFlag: (s, v) => s.IsPowered = v,
                                  kind: NetworkKind.Power);
        ComputeForResource(state, isProducer: IsWaterProducer,
                                  isDistributor: IsWaterDistributor,
                                  setFlag: (s, v) => s.IsWatered = v,
                                  kind: NetworkKind.Water);
    }

    private static bool IsPowerProducer(Structure s) =>
        s.Type == StructureType.Generator;

    private static bool IsPowerDistributor(Structure s) =>
        s.Type == StructureType.ElectricityDistribution;

    private static bool IsWaterProducer(Structure s) =>
        s.Type == StructureType.Well;

    private static bool IsWaterDistributor(Structure s) =>
        s.Type == StructureType.WaterDistribution;

    private static void ComputeForResource(SimState state,
                                           System.Func<Structure, bool> isProducer,
                                           System.Func<Structure, bool> isDistributor,
                                           System.Action<Structure, bool> setFlag,
                                           NetworkKind kind)
    {
        var tm = state.Region.Tilemap;
        int n = Tilemap.MapSize;

        // Reset all flags first.
        foreach (var s in state.City.Structures.Values) setFlag(s, false);

        // Per-tile bitmap of "this tile is in the coverage set." We work in tile space rather
        // than structure space so adjacency between distinct structures Just Works.
        var covered = new bool[n, n];
        var queue = new System.Collections.Generic.Queue<(int X, int Y)>();

        // Energize the distributor network. A distributor is energized when its footprint
        // contains a tile that's 8-neighbor adjacent to an operational producer's footprint
        // tile. The energized set then spreads through player-drawn edges (undirected) to
        // every distributor in the same connected component. Energized distributors are
        // then used to seed coverage just like producers would.
        var producers = new List<Structure>();
        var distributors = new List<Structure>();
        foreach (var s in state.City.Structures.Values)
        {
            if (s.Inactive || s.X < 0 || s.Y < 0) continue;
            if (isProducer(s) && s.Operational) producers.Add(s);
            else if (isDistributor(s)) distributors.Add(s);
        }

        // Step 1: directly-adjacent distributors get energized by producers.
        var energized = new HashSet<long>();
        foreach (var d in distributors)
        {
            if (TouchesAnyProducer(d, producers)) energized.Add(d.Id);
        }

        // Step 2: BFS through the undirected edge graph to spread energization.
        var adj = new Dictionary<long, List<long>>();
        foreach (var e in state.NetworkEdges.Values)
        {
            if (e.Kind != kind) continue;
            if (!adj.TryGetValue(e.SourceStructureId, out var la))
                adj[e.SourceStructureId] = la = new List<long>();
            if (!adj.TryGetValue(e.TargetStructureId, out var lb))
                adj[e.TargetStructureId] = lb = new List<long>();
            la.Add(e.TargetStructureId);
            lb.Add(e.SourceStructureId);
        }
        var bfs = new System.Collections.Generic.Queue<long>(energized);
        while (bfs.Count > 0)
        {
            var id = bfs.Dequeue();
            if (!adj.TryGetValue(id, out var nbrs)) continue;
            foreach (var nb in nbrs)
            {
                if (energized.Add(nb)) bfs.Enqueue(nb);
            }
        }

        // Step 3: seed coverage from each operational producer's footprint AND from every
        // energized, operational distributor's footprint. Local spread (8-neighbor + radius)
        // then handles the rest.
        foreach (var p in producers) SeedFootprint(tm, p, covered, queue);
        foreach (var d in distributors)
        {
            if (!energized.Contains(d.Id)) continue;
            if (!d.Operational) continue;
            SeedFootprint(tm, d, covered, queue);
        }

        // BFS: expand to 8-neighbor tiles that contain any structure. Distribution structures
        // additionally seed a radius-stamp on first visit.
        var distRadius = DistributionRadius;
        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            var sidHere = tm.StructureAt(cx, cy);

            // Radius jump: producers AND distributors both project coverage to every
            // structure-bearing tile within DistributionRadius. Producers self-amplify so
            // a lone Generator near houses powers them directly (no chain required for the
            // simple case). Distributors do the same to extend reach across gaps.
            if (sidHere is long sid
                && state.City.Structures.TryGetValue(sid, out var s)
                && (isDistributor(s) || isProducer(s))
                && !s.Inactive && s.Operational)
            {
                for (int dy = -distRadius; dy <= distRadius; dy++)
                for (int dx = -distRadius; dx <= distRadius; dx++)
                {
                    if (dx * dx + dy * dy > distRadius * distRadius) continue;
                    int rx = cx + dx, ry = cy + dy;
                    if (!tm.InBounds(rx, ry) || covered[rx, ry]) continue;
                    if (tm.StructureAt(rx, ry) is null) continue;
                    covered[rx, ry] = true;
                    queue.Enqueue((rx, ry));
                }
            }

            // 8-neighbor adjacency: cover an adjacent tile if it has a structure.
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int nx = cx + dx, ny = cy + dy;
                if (!tm.InBounds(nx, ny) || covered[nx, ny]) continue;
                if (tm.StructureAt(nx, ny) is null) continue;
                covered[nx, ny] = true;
                queue.Enqueue((nx, ny));
            }
        }

        // Resolve per-structure flag: covered if any footprint tile is in the set.
        foreach (var s in state.City.Structures.Values)
        {
            if (s.X < 0 || s.Y < 0) continue;
            var (w, h) = Defaults.Footprint.For(s.Type);
            bool found = false;
            for (int dy = 0; dy < h && !found; dy++)
            for (int dx = 0; dx < w; dx++)
            {
                if (covered[s.X + dx, s.Y + dy]) { found = true; break; }
            }
            if (found) setFlag(s, true);
        }
    }

    /// <summary>True when any tile in <paramref name="distributor"/>'s footprint is 8-neighbor
    /// adjacent to (or directly inside) any tile of any producer's footprint.</summary>
    private static bool TouchesAnyProducer(Structure distributor, System.Collections.Generic.List<Structure> producers)
    {
        var (dw, dh) = Defaults.Footprint.For(distributor.Type);
        // Expand the distributor's footprint by 1 in every direction; check overlap with any
        // producer's footprint rect.
        int dxMin = distributor.X - 1, dxMax = distributor.X + dw;       // inclusive bounds
        int dyMin = distributor.Y - 1, dyMax = distributor.Y + dh;
        foreach (var p in producers)
        {
            var (pw, ph) = Defaults.Footprint.For(p.Type);
            int pxMin = p.X, pxMax = p.X + pw - 1;
            int pyMin = p.Y, pyMax = p.Y + ph - 1;
            bool overlapsX = pxMin <= dxMax && pxMax >= dxMin;
            bool overlapsY = pyMin <= dyMax && pyMax >= dyMin;
            if (overlapsX && overlapsY) return true;
        }
        return false;
    }

    private static void SeedFootprint(Tilemap tm, Structure s, bool[,] covered,
                                      System.Collections.Generic.Queue<(int X, int Y)> queue)
    {
        var (w, h) = Defaults.Footprint.For(s.Type);
        for (int dy = 0; dy < h; dy++)
        for (int dx = 0; dx < w; dx++)
        {
            int tx = s.X + dx, ty = s.Y + dy;
            if (!tm.InBounds(tx, ty) || covered[tx, ty]) continue;
            covered[tx, ty] = true;
            queue.Enqueue((tx, ty));
        }
    }
}
