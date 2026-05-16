#nullable enable
using UnityEngine;

namespace AgentSimUnity
{
    /// <summary>
    /// Sets up lighting + a lit ground plane at runtime so the procedural 3D meshes have a
    /// surface to cast shadows onto. Idempotent: skips if a directional light already exists.
    /// </summary>
    [RequireComponent(typeof(SimBootstrap))]
    [RequireComponent(typeof(SimVisualizer))]
    public class SceneLighting : MonoBehaviour
    {
        [Header("Sun")]
        public Vector3 SunRotationEuler = new(50f, -30f, 0f);
        [Range(0f, 2f)] public float SunIntensity = 1.1f;
        public Color SunColor = new(1f, 0.96f, 0.88f);

        [Header("Ambient")]
        public Color AmbientColor = new(0.30f, 0.32f, 0.40f);

        [Header("Ground")]
        public Color GroundColor = new(0.10f, 0.12f, 0.16f);
        public bool CreateGroundPlane = true;

        void Awake()
        {
            ConfigureAmbient();
            EnsureDirectionalLight();
        }

        void Start()
        {
            if (CreateGroundPlane) EnsureGroundPlane();
        }

        private void ConfigureAmbient()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = AmbientColor;
        }

        private void EnsureDirectionalLight()
        {
            // Reuse an existing sun if the scene has one; otherwise spawn one.
            Light? existing = null;
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional) { existing = light; break; }
            }
            if (existing == null)
            {
                var go = new GameObject("Sun");
                existing = go.AddComponent<Light>();
                existing.type = LightType.Directional;
            }
            existing.color = SunColor;
            existing.intensity = SunIntensity;
            existing.shadows = LightShadows.Soft;
            existing.transform.rotation = Quaternion.Euler(SunRotationEuler);
        }

        private void EnsureGroundPlane()
        {
            // Big flat lit quad sitting at Y = -0.02 so it's below the sprite tilemaps but
            // close enough to read shadows from buildings at Y = 0+. Side = 2× map size so the
            // edges aren't visible when the camera pans.
            int side = AgentSim.Core.Types.Tilemap.MapSize * 2;
            float half = side * 0.5f;
            float center = AgentSim.Core.Types.Tilemap.MapSize * 0.5f;

            var go = new GameObject("GroundPlane");
            go.transform.position = new Vector3(center, -0.02f, center);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();

            var mesh = new Mesh { name = "Ground" };
            mesh.SetVertices(new[]
            {
                new Vector3(-half, 0, -half),
                new Vector3( half, 0, -half),
                new Vector3( half, 0,  half),
                new Vector3(-half, 0,  half),
            });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            mesh.RecalculateBounds();
            mf.sharedMesh = mesh;

            mr.sharedMaterial = MakeLitMaterial(GroundColor);
            mr.receiveShadows = true;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Try the URP Lit shader first; fall back to Standard if URP isn't present.
        /// Public so other renderers can share the material factory.</summary>
        public static Material MakeLitMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader);
            // Both URP/Lit and Standard expose _BaseColor / _Color respectively; set both.
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }
    }
}
