#nullable enable
using System.Collections.Generic;
using AgentSim.Core.Types;
using UnityEngine;

namespace AgentSimUnity
{
    /// <summary>
    /// Spawns a 3D model for each placed structure. Models live in
    /// <c>Assets/Resources/Models/</c> as GLB files (Kenney "City Builder Kit", CC0). The map
    /// from <see cref="StructureType"/> to model name is in <see cref="MeshForType"/>.
    ///
    /// Loading strategy: <c>Resources.Load&lt;GameObject&gt;("Models/&lt;name&gt;")</c> returns
    /// the imported model as a prefab; we Instantiate it as a child of the SimGrid container.
    /// If a structure type has no mapping, the procedural house mesh is used as a fallback
    /// so the city isn't full of invisible blanks.
    /// </summary>
    [RequireComponent(typeof(SimBootstrap))]
    [RequireComponent(typeof(SimVisualizer))]
    public class Structure3DRenderer : MonoBehaviour
    {
        private SimBootstrap _bootstrap = null!;
        private SimVisualizer _visualizer = null!;
        private Transform _container = null!;

        // Cached prefab refs (Resources.Load is fast but we re-use anyway).
        private readonly Dictionary<string, GameObject?> _prefabCache = new();

        // Fallback for any structure type without a model mapping.
        private GameObject _fallbackPrefab = null!;

        // structureId → spawned GameObject
        private readonly Dictionary<long, GameObject> _spawned = new();
        private int _lastStructureCount = -1;

        void Awake()
        {
            _bootstrap = GetComponent<SimBootstrap>();
            _visualizer = GetComponent<SimVisualizer>();
            BuildFallbackPrefab();
        }

        void Start()
        {
            var grid = _visualizer.Grid;
            if (grid == null) { Debug.LogWarning("[Structure3DRenderer] No SimGrid."); return; }
            var go = new GameObject("StructureMeshes");
            go.transform.SetParent(grid.transform, worldPositionStays: false);
            _container = go.transform;
        }

        void LateUpdate()
        {
            if (_bootstrap.Sim is null || _container == null) return;
            var state = _bootstrap.Sim.State;
            if (state.City.Structures.Count == _lastStructureCount) return;
            SyncSpawned(state);
            _lastStructureCount = state.City.Structures.Count;
        }

        private void SyncSpawned(SimState state)
        {
            // Remove spawned GOs whose backing structure is gone.
            var stale = new List<long>();
            foreach (var kv in _spawned)
                if (!state.City.Structures.ContainsKey(kv.Key)) stale.Add(kv.Key);
            foreach (var id in stale)
            {
                if (_spawned[id] != null) Destroy(_spawned[id]);
                _spawned.Remove(id);
            }

            // Spawn for any structure that hasn't been rendered yet.
            foreach (var s in state.City.Structures.Values)
            {
                if (s.X < 0 || s.Y < 0) continue;
                if (_spawned.ContainsKey(s.Id)) continue;

                var prefab = ResolvePrefab(s.Type);
                if (prefab == null) continue;

                var (w, h) = AgentSim.Core.Defaults.Footprint.For(s.Type);
                var instance = Instantiate(prefab, _container);
                instance.name = $"{s.Type}#{s.Id}";

                // The SimGrid parent is rotated +90° around X (XY tilemap → XZ ground). The
                // GLB models from Kenney are authored Y-up. To get their Y back to world +Y
                // after the parent's +90° rotation, the child needs a -90° X counter-rotation.
                instance.transform.localPosition = new Vector3(s.X + w * 0.5f, s.Y + h * 0.5f, 0f);
                instance.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                // Scale to match the structure's footprint. Kenney models are authored at ~1m
                // native; the procedural-house fallback at ~2m. Apply per-source scale so each
                // ends up filling a footprint × footprint area.
                bool isFallback = prefab == _fallbackPrefab;
                float scale = isFallback ? w * 0.5f : w * 1.0f;
                instance.transform.localScale = Vector3.one * scale;

                ApplyShadowsToHierarchy(instance);
                _spawned[s.Id] = instance;
            }
        }

        private GameObject? ResolvePrefab(StructureType type)
        {
            var name = MeshForType(type);
            if (name == null) return _fallbackPrefab;

            if (_prefabCache.TryGetValue(name, out var cached)) return cached ?? _fallbackPrefab;

            var loaded = Resources.Load<GameObject>($"Models/{name}");
            _prefabCache[name] = loaded;
            if (loaded == null)
            {
                Debug.LogWarning($"[Structure3DRenderer] Missing model 'Models/{name}'; using fallback.");
                return _fallbackPrefab;
            }
            return loaded;
        }

        /// <summary>Returns the Resources/Models name (without extension) for a structure type,
        /// or null to use the procedural fallback. The Kenney pack only has 5 building shapes
        /// + roads + foliage; we reuse the same models for related types until we get more.</summary>
        private static string? MeshForType(StructureType t) => t switch
        {
            StructureType.House                  => "building-small-a",
            StructureType.AffordableHousing      => "building-small-a",
            StructureType.Apartment              => "building-small-b",
            StructureType.Townhouse              => "building-small-c",
            StructureType.Condo                  => "building-small-d",

            StructureType.Shop                   => "building-small-b",
            StructureType.Marketplace            => "building-small-c",
            StructureType.Restaurant             => "building-small-d",
            StructureType.Theater                => "building-garage",
            StructureType.CorporateHq            => "building-garage",

            StructureType.PoliceStation          => "building-small-a",
            StructureType.FireStation            => "building-small-b",
            StructureType.TownHall               => "building-garage",
            StructureType.Clinic                 => "building-small-c",
            StructureType.Hospital               => "building-garage",
            StructureType.PrimarySchool          => "building-small-d",
            StructureType.SecondarySchool        => "building-garage",
            StructureType.College                => "building-garage",

            StructureType.Generator              => "building-garage",
            StructureType.Well                   => "pavement-fountain",
            StructureType.ElectricityDistribution => "road-straight-lightposts",
            StructureType.WaterDistribution      => "pavement",

            StructureType.Park                   => "grass-trees",
            StructureType.ReforestationSite      => "grass-trees-tall",
            StructureType.WetlandRestoration     => "grass",

            _ => null,  // industrial + anything else → procedural fallback
        };

        private void BuildFallbackPrefab()
        {
            // Wrap the procedural house in a deactivated prefab-style GameObject we can
            // Instantiate from. Material + mesh shared across all fallback instances.
            var mesh = ProceduralMesh.BuildHouse();
            var mat = SceneLighting.MakeLitMaterial(new Color(0.75f, 0.55f, 0.40f));

            _fallbackPrefab = new GameObject("FallbackHousePrefab");
            _fallbackPrefab.SetActive(false);  // keep the "prefab" inert; only clones render
            _fallbackPrefab.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = _fallbackPrefab.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;

            // Hide it from the scene hierarchy (DontSave isn't strictly necessary, but keeps
            // it from polluting the saved scene).
            _fallbackPrefab.hideFlags = HideFlags.HideAndDontSave;
        }

        // Walk a model hierarchy and enable shadow cast/receive on every MeshRenderer.
        private static void ApplyShadowsToHierarchy(GameObject root)
        {
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                mr.receiveShadows = true;
            }
        }
    }
}
