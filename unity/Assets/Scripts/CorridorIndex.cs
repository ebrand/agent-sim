#nullable enable
using AgentSim.Core.Types;
using UnityEngine;

namespace AgentSimUnity
{
    /// <summary>Geometric queries over the road-graph corridor grid. Pure functions, no
    /// state. Used by PlacementController for the structure-snap UX and by SimVisualizer
    /// to recover rotation from an anchored edge. Cell indices match BuildCorridorGrid's
    /// layout: alongCell from 0 at FromNode → ceil(len/stepSize)-1 at ToNode; perpCell
    /// 0 = closest to road; side +1 = "left" of FromNode→ToNode direction, -1 = right.</summary>
    public static class CorridorIndex
    {
        public readonly struct CellInfo
        {
            public CellInfo(long edgeId, int alongCell, int perpCell, int side,
                            float setback, float alongInRoadMeters,
                            float dirX, float dirY)
            {
                EdgeId = edgeId;
                AlongCell = alongCell;
                PerpCell = perpCell;
                Side = side;
                Setback = setback;
                AlongInRoadMeters = alongInRoadMeters;
                DirX = dirX;
                DirY = dirY;
            }
            public long EdgeId { get; }
            public int AlongCell { get; }
            public int PerpCell { get; }
            public int Side { get; }
            public float Setback { get; }
            /// <summary>Float along-coord of the query point in road-local meters
            /// (used by snap math to position the lot's front-center on the cursor).</summary>
            public float AlongInRoadMeters { get; }
            public float DirX { get; }
            public float DirY { get; }
        }

        /// <summary>Find the corridor cell containing world (wx, wy), if any. Picks the
        /// closest edge (smallest perpendicular distance) when the point is in multiple
        /// corridor bands. Does NOT enforce Voronoi-validity on the returned cell — the
        /// caller should call <see cref="IsCellRendered"/> if they need that.</summary>
        public static CellInfo? LocateCellAt(SimState state, float wx, float wy,
                                             float stepSize, int corridorDepthCells)
        {
            float bestDist = float.MaxValue;
            CellInfo? best = null;
            foreach (var e in state.RoadEdges.Values)
            {
                if (e.ControlPoint.HasValue) continue;  // curved edges don't have corridor cells in v1
                if (!state.RoadNodes.TryGetValue(e.FromNodeId, out var fn)) continue;
                if (!state.RoadNodes.TryGetValue(e.ToNodeId, out var tn)) continue;
                float fx = fn.Position.X, fy = fn.Position.Y;
                float dx = tn.Position.X - fx, dy = tn.Position.Y - fy;
                float len = Mathf.Sqrt(dx * dx + dy * dy);
                if (len < 1e-4f) continue;
                float ddx = dx / len, ddy = dy / len;
                float pdx = -ddy, pdy = ddx;
                float ox = wx - fx, oy = wy - fy;
                float along = ox * ddx + oy * ddy;
                float perp  = ox * pdx + oy * pdy;
                if (along < 0 || along > len) continue;

                float setback = e.WidthTiles * 0.5f;
                float halfDepth = setback + corridorDepthCells * stepSize;
                float absPerp = Mathf.Abs(perp);
                if (absPerp < setback || absPerp > halfDepth) continue;

                if (absPerp < bestDist)
                {
                    bestDist = absPerp;
                    int side = perp >= 0 ? +1 : -1;
                    int alongCell = Mathf.FloorToInt(along / stepSize);
                    int perpCell  = Mathf.FloorToInt((absPerp - setback) / stepSize);
                    best = new CellInfo(e.Id, alongCell, perpCell, side, setback,
                                        along, ddx, ddy);
                }
            }
            return best;
        }

        /// <summary>True if the cell at (alongCell, perpCell, side) on edge <paramref name="edgeId"/>
        /// would be rendered by BuildCorridorGrid. Uses the same corner-based Voronoi cull:
        /// any corner closer to a competing edge kills the cell.</summary>
        public static bool IsCellRendered(SimState state, long edgeId, int alongCell,
                                          int perpCell, int side, float stepSize)
        {
            if (!state.RoadEdges.TryGetValue(edgeId, out var e)) return false;
            if (!state.RoadNodes.TryGetValue(e.FromNodeId, out var fn)) return false;
            if (!state.RoadNodes.TryGetValue(e.ToNodeId, out var tn)) return false;
            float fx = fn.Position.X, fy = fn.Position.Y;
            float tx = tn.Position.X, ty = tn.Position.Y;
            float dx = tx - fx, dy = ty - fy;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) return false;
            float ddx = dx / len, ddy = dy / len;
            float pdx = -ddy, pdy = ddx;
            float setback = e.WidthTiles * 0.5f;

            if (alongCell < 0) return false;
            float alongMin = alongCell * stepSize;
            float alongMax = Mathf.Min((alongCell + 1) * stepSize, len);
            if (alongMin >= len) return false;

            float perpMin = setback + perpCell * stepSize;
            float perpMax = perpMin + stepSize;
            if (side < 0) { float t = perpMin; perpMin = -perpMax; perpMax = -t; }

            // 4 corners in world coords.
            var corners = new (float x, float y)[]
            {
                (fx + alongMin * ddx + perpMin * pdx, fy + alongMin * ddy + perpMin * pdy),
                (fx + alongMax * ddx + perpMin * pdx, fy + alongMax * ddy + perpMin * pdy),
                (fx + alongMax * ddx + perpMax * pdx, fy + alongMax * ddy + perpMax * pdy),
                (fx + alongMin * ddx + perpMax * pdx, fy + alongMin * ddy + perpMax * pdy),
            };
            foreach (var (cx, cy) in corners)
            {
                float ourDist = PointSegmentDistance(cx, cy, fx, fy, tx, ty);
                foreach (var o in state.RoadEdges.Values)
                {
                    if (o.Id == edgeId) continue;
                    if (o.ControlPoint.HasValue) continue;  // curves don't participate in Voronoi cull yet
                    if (!state.RoadNodes.TryGetValue(o.FromNodeId, out var ofn)) continue;
                    if (!state.RoadNodes.TryGetValue(o.ToNodeId, out var otn)) continue;
                    if (PointSegmentDistance(cx, cy, ofn.Position.X, ofn.Position.Y,
                                             otn.Position.X, otn.Position.Y) < ourDist)
                        return false;
                }
            }
            return true;
        }

        /// <summary>World-space center of the corridor cell, including the road-perp offset
        /// that puts the cell on the correct side of the road.</summary>
        public static (float x, float y) CellCenterWorld(SimState state, long edgeId,
                                                          int alongCell, int perpCell, int side,
                                                          float stepSize)
        {
            if (!state.RoadEdges.TryGetValue(edgeId, out var e))
                return (0, 0);
            if (!state.RoadNodes.TryGetValue(e.FromNodeId, out var fn))
                return (0, 0);
            if (!state.RoadNodes.TryGetValue(e.ToNodeId, out var tn))
                return (0, 0);
            float fx = fn.Position.X, fy = fn.Position.Y;
            float dx = tn.Position.X - fx, dy = tn.Position.Y - fy;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) return (fx, fy);
            float ddx = dx / len, ddy = dy / len;
            float pdx = -ddy, pdy = ddx;
            float setback = e.WidthTiles * 0.5f;
            float alongMid = (alongCell + 0.5f) * stepSize;
            float perpMid  = (setback + (perpCell + 0.5f) * stepSize) * side;
            return (fx + alongMid * ddx + perpMid * pdx,
                    fy + alongMid * ddy + perpMid * pdy);
        }

        private static float PointSegmentDistance(float px, float py,
                                                  float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-6f)
            {
                float ex = px - ax, ey = py - ay;
                return Mathf.Sqrt(ex * ex + ey * ey);
            }
            float t = ((px - ax) * dx + (py - ay) * dy) / lenSq;
            t = Mathf.Clamp01(t);
            float qx = ax + t * dx, qy = ay + t * dy;
            float ex2 = px - qx, ey2 = py - qy;
            return Mathf.Sqrt(ex2 * ex2 + ey2 * ey2);
        }
    }
}
