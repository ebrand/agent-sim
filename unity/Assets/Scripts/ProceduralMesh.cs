#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace AgentSimUnity
{
    /// <summary>
    /// Procedural mesh builders. Each function returns a fresh Mesh with vertices, triangles,
    /// and recomputed normals. Meshes are deliberately blocky / low-poly: matches the flat-
    /// colored tile aesthetic and is cheap to generate per structure type.
    ///
    /// Coordinate convention: the mesh is centered at (0, 0, 0) on its XZ footprint, sits
    /// on Y=0, and extends upward in +Y. Callers translate to the structure's tile coords
    /// before assigning to a MeshFilter.
    /// </summary>
    public static class ProceduralMesh
    {
        /// <summary>House: rectangular box with a pitched roof running along the X axis.
        /// width/depth are the XZ footprint; wallHeight + roofHeight are the vertical sizes.</summary>
        public static Mesh BuildHouse(float width = 2f, float depth = 2f,
                                      float wallHeight = 1.2f, float roofHeight = 0.8f)
        {
            float hx = width * 0.5f;
            float hz = depth * 0.5f;
            float wt = wallHeight;
            float rt = wallHeight + roofHeight;

            // Indices:
            //  B0..B3 = bottom 4 corners
            //  T0..T3 = wall-top 4 corners
            //  R0, R1 = ridge endpoints (along X axis, front to back at z=-hz, z=+hz)
            var verts = new List<Vector3>
            {
                new(-hx, 0,   -hz),  // B0 (front-left)
                new( hx, 0,   -hz),  // B1 (front-right)
                new( hx, 0,    hz),  // B2 (back-right)
                new(-hx, 0,    hz),  // B3 (back-left)
                new(-hx, wt,  -hz),  // T0
                new( hx, wt,  -hz),  // T1
                new( hx, wt,   hz),  // T2
                new(-hx, wt,   hz),  // T3
                new(  0, rt,  -hz),  // R0 (front ridge endpoint)
                new(  0, rt,   hz),  // R1 (back ridge endpoint)
            };

            var tris = new List<int>();

            // Walls (outward-facing, CCW when viewed from outside).
            // Front wall (-z): B0, T0, T1, B1
            AddQuad(tris, 0, 4, 5, 1);
            // Right wall (+x): B1, T1, T2, B2
            AddQuad(tris, 1, 5, 6, 2);
            // Back wall (+z): B2, T2, T3, B3
            AddQuad(tris, 2, 6, 7, 3);
            // Left wall (-x): B3, T3, T0, B0
            AddQuad(tris, 3, 7, 4, 0);

            // Roof gable triangles (front + back).
            // Front gable: T0, R0, T1 (CCW from outside, i.e. -z facing)
            tris.AddRange(new[] { 4, 8, 5 });
            // Back gable: T2, R1, T3
            tris.AddRange(new[] { 6, 9, 7 });

            // Roof slopes (rectangles).
            // Right slope (+x facing-ish): T1, R0, R1, T2
            AddQuad(tris, 5, 8, 9, 6);
            // Left slope (-x facing-ish): T3, R1, R0, T0
            AddQuad(tris, 7, 9, 8, 4);

            var mesh = new Mesh { name = "ProceduralHouse" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Thin road slab as a cuboid (top + 4 sides; bottom omitted since it isn't
        /// visible). Local +Z is "up" in the SimGrid's pre-rotation frame; the parent's +90°
        /// X rotation maps local +Z → world +Y, so verts at z = +height end up above the ground.
        /// Per-vertex normals are set explicitly so URP/Lit can do directional shading.
        /// Material should use Cull = Off so winding direction isn't load-bearing.</summary>
        public static Mesh BuildRoadStrip(float sx, float sy, float ex, float ey,
                                          float width = 1.6f, float height = 0.15f)
        {
            float dx = ex - sx;
            float dy = ey - sy;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            var mesh = new Mesh { name = "RoadStrip" };
            if (len < 1e-4f) return mesh;

            float halfW = width * 0.5f;
            float nx = -dy / len * halfW;
            float ny = dx / len * halfW;
            float zTop = +height;  // positive → above ground after rotation
            float zBot = 0f;

            Vector3 BSL = new(sx + nx, sy + ny, zBot);
            Vector3 BSR = new(sx - nx, sy - ny, zBot);
            Vector3 BER = new(ex - nx, ey - ny, zBot);
            Vector3 BEL = new(ex + nx, ey + ny, zBot);
            Vector3 TSL = new(sx + nx, sy + ny, zTop);
            Vector3 TSR = new(sx - nx, sy - ny, zTop);
            Vector3 TER = new(ex - nx, ey - ny, zTop);
            Vector3 TEL = new(ex + nx, ey + ny, zTop);

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            void AddFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color color)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                for (int k = 0; k < 4; k++) { normals.Add(normal); colors.Add(color); }
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }

            var top    = new Color(1.00f, 1.00f, 1.00f, 1f);
            var longSL = new Color(0.78f, 0.78f, 0.78f, 1f);
            var longSR = new Color(0.62f, 0.62f, 0.62f, 1f);

            AddFace(TSL, TEL, TER, TSR, new Vector3(0, 0, 1), top);     // top: local +Z
            AddFace(BSL, BEL, TEL, TSL, new Vector3(0, 1, 0), longSL);  // left side: local +Y
            AddFace(BSR, TSR, TER, BER, new Vector3(0, -1, 0), longSR); // right side: local -Y
            // End caps omitted; intersection patches handle the X / T cases. Standalone road
            // endpoints look open but it's barely noticeable at the iso angle.

            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Road-aligned grid of cells (each <paramref name="stepSize"/> × <paramref name="stepSize"/>)
        /// forming the buildable corridor around an edge. Cells whose center is closer to ANY
        /// other road edge in <paramref name="otherEdges"/> than to this edge are culled out
        /// of the mesh — gives a Voronoi-like partition between adjacent corridors.
        /// Outline only (thin line quads), no fill. Sits slightly below the road slab.</summary>
        public static Mesh BuildCorridorGrid(float sx, float sy, float ex, float ey,
                                              float halfDepth, float stepSize, float lineWidth,
                                              IReadOnlyList<(float fx, float fy, float tx, float ty)> otherEdges,
                                              Color color,
                                              float setback = 0f,
                                              float elevation = 0.01f,
                                              float backGuideDistance = 0f,
                                              Mesh? reuseMesh = null)
        {
            // Reuse the caller's mesh if provided — otherwise allocating a new Mesh every
            // frame burns Unity's resource-id pool (overflows ~2^20 after ~30 min at 60 fps).
            var mesh = reuseMesh ?? new Mesh { name = "RoadCorridorGrid" };
            mesh.Clear();
            float dx = ex - sx, dy = ey - sy;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) return mesh;

            // Road-local axes.
            float ddx = dx / len, ddy = dy / len;    // along
            float pdx = -ddy, pdy = ddx;             // perpendicular

            int cellsAlong = Mathf.CeilToInt(len / stepSize);
            float minPerpAbs = Mathf.Max(0f, setback);  // inner boundary of the corridor
            float ourMaxDist = halfDepth;

            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();

            // Generate cell bands separately on each side of the road. Bands start exactly at
            // the setback boundary and step outward by stepSize, so changing stepSize doesn't
            // shift where the inner edge of the corridor sits.
            var bands = new List<(float pMin, float pMax)>();
            for (float p = minPerpAbs; p < ourMaxDist; p += stepSize)
                bands.Add((p, Mathf.Min(p + stepSize, ourMaxDist)));
            for (float p = -minPerpAbs; p > -ourMaxDist; p -= stepSize)
                bands.Add((Mathf.Max(p - stepSize, -ourMaxDist), p));

            for (int i = 0; i < cellsAlong; i++)
            {
                float alongMin = i * stepSize;
                float alongMax = Mathf.Min((i + 1) * stepSize, len);
                foreach (var (perpMin, perpMax) in bands)
                {
                    float perpCenter = (perpMin + perpMax) * 0.5f;
                    float alongCenter = (alongMin + alongMax) * 0.5f;

                    if (Mathf.Abs(perpCenter) > ourMaxDist) continue;

                    // 4 corners of the cell, in world-local frame.
                    Vector3 c00 = ToLocal(sx, sy, ddx, ddy, pdx, pdy, alongMin, perpMin, elevation);
                    Vector3 c10 = ToLocal(sx, sy, ddx, ddy, pdx, pdy, alongMax, perpMin, elevation);
                    Vector3 c11 = ToLocal(sx, sy, ddx, ddy, pdx, pdy, alongMax, perpMax, elevation);
                    Vector3 c01 = ToLocal(sx, sy, ddx, ddy, pdx, pdy, alongMin, perpMax, elevation);

                    // Corner-based Voronoi cull: cull if 2+ corners are on a competitor's
                    // side. 90° corner cells (typically 1 of 4 corners cross) survive; cells
                    // straddling a diagonal-vs-diagonal boundary (typically 2+) get pruned.
                    int losses = 0;
                    if (CornerLosesVoronoi(c00, sx, sy, ex, ey, otherEdges)) losses++;
                    if (CornerLosesVoronoi(c10, sx, sy, ex, ey, otherEdges)) losses++;
                    if (CornerLosesVoronoi(c11, sx, sy, ex, ey, otherEdges)) losses++;
                    if (CornerLosesVoronoi(c01, sx, sy, ex, ey, otherEdges)) losses++;
                    if (losses >= 1) continue;

                    // 4 sides as thin line quads.
                    AddLineQuad(verts, colors, tris, c00, c10, pdx, pdy, lineWidth, color);
                    AddLineQuad(verts, colors, tris, c10, c11, ddx, ddy, lineWidth, color);
                    AddLineQuad(verts, colors, tris, c11, c01, -pdx, -pdy, lineWidth, color);
                    AddLineQuad(verts, colors, tris, c01, c00, -ddx, -ddy, lineWidth, color);
                }
            }

            // Dashed back-guide line: shows where a deep structure's body would reach if it
            // extended <backGuideDistance> off the centerline. One dashed line per side.
            // Dash period is tied to stepSize so it visually beats with the front-cell grid.
            if (backGuideDistance > 0f)
            {
                float dashLen = stepSize * 0.55f;
                float gapLen  = stepSize * 0.45f;
                float period  = dashLen + gapLen;
                Color guideCol = new(color.r, color.g, color.b, color.a * 0.55f);

                for (int sign = -1; sign <= 1; sign += 2)
                {
                    float perp = sign * backGuideDistance;
                    for (float a = 0f; a < len - 0.05f; a += period)
                    {
                        float dStart = a;
                        float dEnd = Mathf.Min(a + dashLen, len);
                        if (dEnd - dStart < 0.1f) continue;
                        float aMid = (dStart + dEnd) * 0.5f;
                        Vector3 mid = ToLocal(sx, sy, ddx, ddy, pdx, pdy, aMid, perp, elevation);
                        // Cull dashes that fall inside a competing corridor (Voronoi).
                        if (CornerLosesVoronoi(mid, sx, sy, ex, ey, otherEdges)) continue;
                        Vector3 ps = ToLocal(sx, sy, ddx, ddy, pdx, pdy, dStart, perp, elevation);
                        Vector3 pe = ToLocal(sx, sy, ddx, ddy, pdx, pdy, dEnd,   perp, elevation);
                        AddLineQuad(verts, colors, tris, ps, pe, pdx * sign, pdy * sign, lineWidth, guideCol);
                    }
                }
            }

            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 ToLocal(float sx, float sy, float ddx, float ddy, float pdx, float pdy,
                                       float along, float perp, float elevation)
        {
            return new Vector3(sx + along * ddx + perp * pdx,
                               sy + along * ddy + perp * pdy,
                               -elevation);
        }

        // Thin rectangle along the segment a→b, perpendicular = (px, py), width = w.
        private static void AddLineQuad(List<Vector3> verts, List<Color> colors, List<int> tris,
                                        Vector3 a, Vector3 b, float px, float py, float w, Color col)
        {
            float halfW = w * 0.5f;
            int i = verts.Count;
            verts.Add(new Vector3(a.x + px * halfW, a.y + py * halfW, a.z));
            verts.Add(new Vector3(a.x - px * halfW, a.y - py * halfW, a.z));
            verts.Add(new Vector3(b.x - px * halfW, b.y - py * halfW, b.z));
            verts.Add(new Vector3(b.x + px * halfW, b.y + py * halfW, b.z));
            colors.Add(col); colors.Add(col); colors.Add(col); colors.Add(col);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
        }

        /// <summary>True when <paramref name="c"/> is closer to ANY edge in <paramref name="others"/>
        /// than to our own edge — meaning the cell containing this corner extends into a
        /// competing corridor and should be culled.</summary>
        private static bool CornerLosesVoronoi(Vector3 c, float sx, float sy, float ex, float ey,
                                               IReadOnlyList<(float fx, float fy, float tx, float ty)> others)
        {
            float ourDist = PointSegmentDistance(c.x, c.y, sx, sy, ex, ey);
            for (int k = 0; k < others.Count; k++)
            {
                var (afx, afy, atx, aty) = others[k];
                if (PointSegmentDistance(c.x, c.y, afx, afy, atx, aty) < ourDist) return true;
            }
            return false;
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

        /// <summary>Small square slab centered at (cx, cy) in the SimGrid's local XY plane.
        /// Used as an intersection / junction patch where two road center lines cross.
        /// All 5 visible faces are included (top + 4 sides) with the same baked vertex shading
        /// as the road strips so they blend cleanly.</summary>
        public static Mesh BuildSquareSlab(float cx, float cy, float size = 2.0f, float height = 0.15f)
        {
            float h = size * 0.5f;
            float zTop = +height;
            float zBot = 0f;

            Vector3 BSW = new(cx - h, cy - h, zBot);
            Vector3 BSE = new(cx + h, cy - h, zBot);
            Vector3 BNE = new(cx + h, cy + h, zBot);
            Vector3 BNW = new(cx - h, cy + h, zBot);
            Vector3 TSW = new(cx - h, cy - h, zTop);
            Vector3 TSE = new(cx + h, cy - h, zTop);
            Vector3 TNE = new(cx + h, cy + h, zTop);
            Vector3 TNW = new(cx - h, cy + h, zTop);

            var top  = new Color(1.00f, 1.00f, 1.00f, 1f);
            var side = new Color(0.70f, 0.70f, 0.70f, 1f);

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            void AddFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color color)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                for (int k = 0; k < 4; k++) { normals.Add(normal); colors.Add(color); }
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }
            AddFace(TSW, TSE, TNE, TNW, new Vector3(0, 0, 1),  top);   // top: local +Z
            AddFace(BNW, BNE, TNE, TNW, new Vector3(0, 1, 0),  side);  // north (+Y)
            AddFace(BSE, BNE, TNE, TSE, new Vector3(1, 0, 0),  side);  // east  (+X)
            AddFace(BSW, BSE, TSE, TSW, new Vector3(0, -1, 0), side);  // south (-Y)
            AddFace(BSW, TSW, TNW, BNW, new Vector3(-1, 0, 0), side);  // west  (-X)

            var mesh = new Mesh { name = "JunctionSlab" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Two triangles for a quad with corners a-b-c-d (CCW order).
        private static void AddQuad(List<int> tris, int a, int b, int c, int d)
        {
            tris.Add(a); tris.Add(b); tris.Add(c);
            tris.Add(a); tris.Add(c); tris.Add(d);
        }

        /// <summary>Overlay mesh for corridor cells across MULTIPLE road edges — one filled
        /// quad per cell. Used for short-lived previews (selection highlight) where cells
        /// can come from different edges and the set changes every frame; one mesh + one
        /// GameObject is cheaper than the per-edge GO churn of the permanent overlay.</summary>
        public static Mesh BuildMultiEdgeCellsOverlay(
            System.Collections.Generic.IReadOnlyList<(long edgeId, int alongCell, int side)> cells,
            System.Func<long, (float fx, float fy, float tx, float ty, float setback)?> edgeLookup,
            float stepSize,
            Color color,
            float elevation = 0.018f,
            Mesh? reuseMesh = null)
        {
            var mesh = reuseMesh ?? new Mesh { name = "MultiEdgeCellsOverlay" };
            mesh.Clear();
            if (cells.Count == 0) return mesh;

            var verts  = new List<Vector3>(cells.Count * 4);
            var colors = new List<Color>(cells.Count * 4);
            var tris   = new List<int>(cells.Count * 6);

            foreach (var (edgeId, alongCell, side) in cells)
            {
                var info = edgeLookup(edgeId);
                if (info is null) continue;
                var (sx, sy, ex, ey, setback) = info.Value;
                float dx = ex - sx, dy = ey - sy;
                float len = Mathf.Sqrt(dx * dx + dy * dy);
                if (len < 1e-4f) continue;
                float ddx = dx / len, ddy = dy / len;
                float pdx = -ddy, pdy = ddx;

                if (alongCell < 0) continue;
                float alongMin = alongCell * stepSize;
                float alongMax = Mathf.Min(alongMin + stepSize, len);
                if (alongMin >= len) continue;
                float perpMin = setback;
                float perpMax = setback + stepSize;
                if (side < 0) { float t = perpMin; perpMin = -perpMax; perpMax = -t; }

                Vector3 c00 = ToLocal(sx, sy, ddx, ddy, pdx, pdy, alongMin, perpMin, elevation);
                Vector3 c10 = ToLocal(sx, sy, ddx, ddy, pdx, pdy, alongMax, perpMin, elevation);
                Vector3 c11 = ToLocal(sx, sy, ddx, ddy, pdx, pdy, alongMax, perpMax, elevation);
                Vector3 c01 = ToLocal(sx, sy, ddx, ddy, pdx, pdy, alongMin, perpMax, elevation);

                int i = verts.Count;
                verts.Add(c00); verts.Add(c10); verts.Add(c11); verts.Add(c01);
                for (int k = 0; k < 4; k++) colors.Add(color);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
            }

            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Overlay mesh for zoned corridor cells on a single road edge — filled
        /// quads at each (alongCell, side) entry, sitting slightly above the corridor
        /// outline mesh and below the structures. perpCell is always 0 (front row).</summary>
        public static Mesh BuildZonedCellsOverlay(float sx, float sy, float ex, float ey,
                                                   float stepSize, float setback,
                                                   IReadOnlyList<(int alongCell, int side)> cells,
                                                   Color color,
                                                   float elevation = 0.015f,
                                                   Mesh? reuseMesh = null)
        {
            var mesh = reuseMesh ?? new Mesh { name = "ZonedCellsOverlay" };
            mesh.Clear();
            float dx = ex - sx, dy = ey - sy;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f || cells.Count == 0) return mesh;
            float ddx = dx / len, ddy = dy / len;
            float pdx = -ddy, pdy = ddx;

            var verts  = new List<Vector3>(cells.Count * 4);
            var colors = new List<Color>(cells.Count * 4);
            var tris   = new List<int>(cells.Count * 6);

            foreach (var (alongCell, side) in cells)
            {
                if (alongCell < 0) continue;
                float alongMin = alongCell * stepSize;
                float alongMax = Mathf.Min(alongMin + stepSize, len);
                if (alongMin >= len) continue;
                float perpMin = setback;
                float perpMax = setback + stepSize;
                if (side < 0) { float t = perpMin; perpMin = -perpMax; perpMax = -t; }

                Vector3 c00 = ToLocal(sx, sy, ddx, ddy, pdx, pdy, alongMin, perpMin, elevation);
                Vector3 c10 = ToLocal(sx, sy, ddx, ddy, pdx, pdy, alongMax, perpMin, elevation);
                Vector3 c11 = ToLocal(sx, sy, ddx, ddy, pdx, pdy, alongMax, perpMax, elevation);
                Vector3 c01 = ToLocal(sx, sy, ddx, ddy, pdx, pdy, alongMin, perpMax, elevation);

                int i = verts.Count;
                verts.Add(c00); verts.Add(c10); verts.Add(c11); verts.Add(c01);
                for (int k = 0; k < 4; k++) colors.Add(color);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
            }

            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A single building lot: a flat ground quad (the "yard") with a 3D box
        /// (the "building") centered on it, plus a small front-edge marker triangle.
        ///
        /// Mesh is centered at local (0, 0, 0) with the lot in the XY plane. Local +Y is the
        /// "front" of the lot — the caller is expected to rotate the GameObject around its
        /// vertical axis so +Y points toward the road. Local +Z is up.
        ///
        /// Sizes are passed in METERS (so the mesh is independent of the unit-size convention).
        /// Vertex colors carry per-face shading (top brightest, sides slightly darker) so the
        /// mesh reads as 3D even under an unlit shader.</summary>
        public static Mesh BuildStructureLot(
            float lotWidthM, float lotDepthM,
            float bldgWidthM, float bldgDepthM, float bldgHeightM,
            Color lotColor, Color bldgColor, Color markerColor,
            float elevation = 0.02f,
            Mesh? reuseMesh = null)
        {
            var mesh = reuseMesh ?? new Mesh { name = "StructureLot" };
            mesh.Clear();

            float hLW = lotWidthM * 0.5f;
            float hLD = lotDepthM * 0.5f;
            float hBW = bldgWidthM * 0.5f;
            float hBD = bldgDepthM * 0.5f;
            float lotZ = elevation;                  // lot quad
            float bz0 = elevation + 0.01f;           // building bottom (above lot, avoids z-fight)
            float bz1 = bz0 + bldgHeightM;           // building top
            float mkZ = elevation + 0.005f;          // marker (above lot, below building)

            // Vertex shading: top = full color, front/back = 95%, sides = 80%, lot = as given.
            Color Side(Color c, float m) => new(c.r * m, c.g * m, c.b * m, c.a);
            var topCol   = Side(bldgColor, 1.10f);
            var frontCol = bldgColor;
            var sideCol  = Side(bldgColor, 0.78f);

            var verts = new List<Vector3>();
            var cols  = new List<Color>();
            var tris  = new List<int>();
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                cols.Add(col); cols.Add(col); cols.Add(col); cols.Add(col);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }
            void Tri(Vector3 a, Vector3 b, Vector3 c, Color col)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                cols.Add(col); cols.Add(col); cols.Add(col);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            }

            // 1) Lot ground quad.
            Face(new Vector3(-hLW, -hLD, lotZ),
                 new Vector3( hLW, -hLD, lotZ),
                 new Vector3( hLW,  hLD, lotZ),
                 new Vector3(-hLW,  hLD, lotZ), lotColor);

            // 2) Building box. Vertices match BuildSquareSlab's compass labelling (S = -Y, N = +Y).
            Vector3 BSW = new(-hBW, -hBD, bz0), BSE = new( hBW, -hBD, bz0),
                    BNE = new( hBW,  hBD, bz0), BNW = new(-hBW,  hBD, bz0);
            Vector3 TSW = new(-hBW, -hBD, bz1), TSE = new( hBW, -hBD, bz1),
                    TNE = new( hBW,  hBD, bz1), TNW = new(-hBW,  hBD, bz1);
            Face(TSW, TSE, TNE, TNW, topCol);                       // top
            Face(BNW, BNE, TNE, TNW, frontCol);                     // north (+Y) — "front" side
            Face(BSE, BNE, TNE, TSE, sideCol);                      // east  (+X)
            Face(BSW, BSE, TSE, TSW, Side(bldgColor, 0.85f));       // south (-Y) — "back"
            Face(BSW, TSW, TNW, BNW, sideCol);                      // west  (-X)

            // 3) Front-edge marker: small triangle on the lot, apex pointing OUT past the +Y edge.
            //    Inset from the edge so it sits cleanly on the lot quad.
            float mkrSpan = Mathf.Min(lotWidthM, lotDepthM) * 0.25f;
            Tri(new Vector3(0,                  hLD - mkrSpan * 0.05f, mkZ),  // apex (near +Y edge)
                new Vector3(-mkrSpan * 0.5f,    hLD - mkrSpan * 0.95f, mkZ),
                new Vector3( mkrSpan * 0.5f,    hLD - mkrSpan * 0.95f, mkZ), markerColor);

            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
