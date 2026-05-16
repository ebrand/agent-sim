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
    }
}
