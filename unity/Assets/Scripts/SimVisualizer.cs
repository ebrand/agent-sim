#nullable enable
using System.Collections.Generic;
using AgentSim.Core.Defaults;
using AgentSim.Core.Types;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using UnityTilemap = UnityEngine.Tilemaps.Tilemap;
using SimTilemap = AgentSim.Core.Types.Tilemap;

namespace AgentSimUnity
{
    /// <summary>
    /// Phase C: renders the sim's spatial state as a Unity Tilemap. Two tilemaps stack:
    /// the zone layer paints zones as faint background tiles; the structure layer paints each
    /// structure's footprint in its category color.
    ///
    /// Programmatic setup — no scene wiring required. Drop on a GameObject with a SimBootstrap
    /// component, press Play.
    /// </summary>
    [RequireComponent(typeof(SimBootstrap))]
    public class SimVisualizer : MonoBehaviour
    {
        private SimBootstrap _bootstrap = null!;
        private UnityTilemap _zoneTilemap = null!;
        private UnityTilemap _structureTilemap = null!;
        private Grid _grid = null!;

        /// <summary>Parent Grid for the zone + structure tilemaps. Used by placement UX
        /// to add overlay layers (ghost preview, drag selection).</summary>
        public Grid Grid => _grid;
        private readonly Dictionary<Color, Tile> _tileCache = new();
        private Texture2D _whiteTex = null!;
        private Sprite _whiteSprite = null!;

        // Last-seen counts so we can detect when re-paint is needed.
        private int _lastZoneCount = -1;
        private int _lastStructureCount = -1;

        // Tile coords currently painted by the structure layer (for cheap diffing).
        private readonly HashSet<Vector3Int> _paintedStructureCells = new();

        // Currently selected structure (clicked).
        private long? _selectedStructureId;

        void Awake()
        {
            _bootstrap = GetComponent<SimBootstrap>();
            BuildSpriteAsset();
            BuildTilemaps();
            SetupCamera();
        }

        void Update()
        {
            HandleCameraControl();
        }

        void LateUpdate()
        {
            if (_bootstrap.Sim is null) return;

            var state = _bootstrap.Sim.State;

            // Repaint zones when zone count changes (zones don't move once created).
            if (state.City.Zones.Count != _lastZoneCount)
            {
                PaintZones();
                _lastZoneCount = state.City.Zones.Count;
            }

            // Repaint structures when count changes. Simple heuristic — structures rarely move
            // (only on placement / demolition). Could subscribe to events for finer control.
            if (state.City.Structures.Count != _lastStructureCount)
            {
                PaintStructures();
                _lastStructureCount = state.City.Structures.Count;
            }

            HandleClick();
        }

        void OnGUI()
        {
            if (!_selectedStructureId.HasValue) return;
            if (_bootstrap.Sim is null) return;
            if (!_bootstrap.Sim.State.City.Structures.TryGetValue(_selectedStructureId.Value, out var s)) return;

            var rect = new Rect(Screen.width - 320, 10, 300, 180);
            GUI.Box(rect, $"Structure #{s.Id}");
            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 20, rect.width - 20, rect.height - 30));
            GUILayout.Label($"Type: {s.Type}");
            if (s.Sector is CommercialSector sec) GUILayout.Label($"Sector: {sec}");
            if (s.Industry is IndustryType ind) GUILayout.Label($"Industry: {ind}");
            GUILayout.Label($"Position: ({s.X}, {s.Y})");
            var (w, h) = Footprint.For(s.Type);
            GUILayout.Label($"Footprint: {w}×{h}");
            GUILayout.Label(s.Operational ? "Status: active" : s.UnderConstruction ? "Status: building" : s.Inactive ? "Status: INACTIVE" : "Status: unknown");
            GUILayout.Label($"Cash: ${s.CashBalance:N0}");
            GUILayout.Label($"Jobs: {s.EmployeeIds.Count}/{s.JobSlotsTotal()}");
            if (GUILayout.Button("Close")) _selectedStructureId = null;
            GUILayout.EndArea();
        }

        // ===== Setup =====

        private void BuildSpriteAsset()
        {
            _whiteTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _whiteTex.SetPixel(0, 0, Color.white);
            _whiteTex.filterMode = FilterMode.Point;
            _whiteTex.Apply();
            _whiteSprite = Sprite.Create(_whiteTex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            _whiteSprite.name = "WhiteTile";
        }

        private void BuildTilemaps()
        {
            // Parent grid.
            var gridGo = new GameObject("SimGrid");
            gridGo.transform.SetParent(transform, worldPositionStays: false);
            _grid = gridGo.AddComponent<Grid>();
            _grid.cellSize = new Vector3(1f, 1f, 0f);

            _zoneTilemap = MakeTilemap(gridGo.transform, "Zones", sortingOrder: 0);
            _structureTilemap = MakeTilemap(gridGo.transform, "Structures", sortingOrder: 1);
        }

        private static UnityTilemap MakeTilemap(Transform parent, string name, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            var tm = go.AddComponent<UnityTilemap>();
            var rend = go.AddComponent<TilemapRenderer>();
            rend.sortingOrder = sortingOrder;
            return tm;
        }

        private void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("MainCamera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
            }
            cam.orthographic = true;
            cam.orthographicSize = 16f;  // ~32 tiles tall — fits the default bootstrap zone
            cam.transform.position = new Vector3(16f, 16f, -10f);
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
            cam.clearFlags = CameraClearFlags.SolidColor;
        }

        // WASD/arrows pan, scroll zoom. Speed scales with zoom so it stays usable at any scale.
        private void HandleCameraControl()
        {
            var cam = Camera.main;
            if (cam == null) return;

            if (Keyboard.current is not null)
            {
                Vector3 move = Vector3.zero;
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) move.x -= 1f;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) move.x += 1f;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) move.y -= 1f;
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) move.y += 1f;
                if (move != Vector3.zero)
                {
                    float panSpeed = cam.orthographicSize * 2f;
                    cam.transform.position += move.normalized * Time.deltaTime * panSpeed;
                }
            }

            if (Mouse.current is not null)
            {
                float scroll = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    float factor = 1f - scroll * 0.001f;  // Input System scroll is ~120 per notch
                    cam.orthographicSize = Mathf.Clamp(cam.orthographicSize * factor, 6f, 128f);
                }
            }
        }

        private Tile TileFor(Color color)
        {
            if (_tileCache.TryGetValue(color, out var existing)) return existing;
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = _whiteSprite;
            tile.color = color;
            _tileCache[color] = tile;
            return tile;
        }

        // ===== Paint =====

        private void PaintZones()
        {
            if (_bootstrap.Sim is null) return;
            _zoneTilemap.ClearAllTiles();

            foreach (var zone in _bootstrap.Sim.State.City.Zones.Values)
            {
                if (zone.Bounds is not ZoneBounds zb) continue;
                var color = ZoneColor(zone);
                var tile = TileFor(color);
                for (int dy = 0; dy < zb.Height; dy++)
                {
                    for (int dx = 0; dx < zb.Width; dx++)
                    {
                        _zoneTilemap.SetTile(new Vector3Int(zb.X + dx, zb.Y + dy, 0), tile);
                    }
                }
            }
        }

        private void PaintStructures()
        {
            if (_bootstrap.Sim is null) return;

            // Clear previously painted structure cells.
            foreach (var cell in _paintedStructureCells)
                _structureTilemap.SetTile(cell, null);
            _paintedStructureCells.Clear();

            foreach (var s in _bootstrap.Sim.State.City.Structures.Values)
            {
                if (s.X < 0 || s.Y < 0) continue;
                var color = StructureColor(s);
                var tile = TileFor(color);
                var (w, h) = Footprint.For(s.Type);
                for (int dy = 0; dy < h; dy++)
                {
                    for (int dx = 0; dx < w; dx++)
                    {
                        var cell = new Vector3Int(s.X + dx, s.Y + dy, 0);
                        _structureTilemap.SetTile(cell, tile);
                        _paintedStructureCells.Add(cell);
                    }
                }
            }
        }

        // ===== Interaction =====

        private void HandleClick()
        {
            if (Mouse.current is null) return;
            if (!Mouse.current.leftButton.wasPressedThisFrame) return;

            // Yield to PlacementController if it's in an active mode.
            var placement = GetComponent<PlacementController>();
            if (placement != null && placement.IsActive) return;

            var mousePos = Mouse.current.position.ReadValue();
            // Skip clicks inside the placement sidebar's screen area.
            if (placement != null && mousePos.x < 220) return;

            var cam = Camera.main;
            if (cam == null) return;
            var world = cam.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, 0f));
            int tx = Mathf.FloorToInt(world.x);
            int ty = Mathf.FloorToInt(world.y);
            _selectedStructureId = _bootstrap.Sim?.State.Region.Tilemap.StructureAt(tx, ty);
        }

        // ===== Color tables =====

        private static Color ZoneColor(Zone zone) => zone.Type switch
        {
            ZoneType.Residential => new Color(0.55f, 0.40f, 0.30f, 0.35f),  // muted tan
            ZoneType.Commercial => zone.Sector switch
            {
                CommercialSector.Food => new Color(0.85f, 0.55f, 0.30f, 0.35f),  // orange
                CommercialSector.Retail => new Color(0.85f, 0.80f, 0.30f, 0.35f),  // yellow
                CommercialSector.Entertainment => new Color(0.65f, 0.40f, 0.75f, 0.35f),  // purple
                CommercialSector.Construction => new Color(0.55f, 0.55f, 0.55f, 0.35f),  // gray
                _ => new Color(0.50f, 0.70f, 0.85f, 0.35f),  // generic
            },
            _ => new Color(0.4f, 0.4f, 0.4f, 0.3f),
        };

        private static Color StructureColor(Structure s)
        {
            if (s.Inactive) return new Color(0.30f, 0.30f, 0.30f, 1f);
            if (s.UnderConstruction) return new Color(0.55f, 0.55f, 0.65f, 0.7f);

            return s.Category switch
            {
                StructureCategory.Residential => new Color(0.65f, 0.45f, 0.30f, 1f),
                StructureCategory.Commercial when s.Type == StructureType.CorporateHq => new Color(0.10f, 0.30f, 0.60f, 1f),
                StructureCategory.Commercial => new Color(0.50f, 0.75f, 0.85f, 1f),
                StructureCategory.IndustrialExtractor => new Color(0.80f, 0.50f, 0.30f, 1f),
                StructureCategory.IndustrialProcessor => new Color(0.75f, 0.35f, 0.35f, 1f),
                StructureCategory.IndustrialManufacturer => new Color(0.55f, 0.75f, 0.50f, 1f),
                StructureCategory.Civic => new Color(0.90f, 0.80f, 0.40f, 1f),
                StructureCategory.Healthcare => new Color(0.85f, 0.50f, 0.65f, 1f),
                StructureCategory.Education => new Color(0.40f, 0.55f, 0.85f, 1f),
                StructureCategory.Utility => new Color(0.55f, 0.70f, 0.85f, 1f),
                StructureCategory.Restoration => new Color(0.45f, 0.75f, 0.45f, 1f),
                _ => Color.gray,
            };
        }
    }

    internal static class StructureExtensions
    {
        public static int JobSlotsTotal(this Structure s)
        {
            int total = 0;
            foreach (var v in s.JobSlots.Values) total += v;
            return total;
        }
    }
}
