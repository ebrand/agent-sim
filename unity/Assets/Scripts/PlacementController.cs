#nullable enable
using System;
using System.Collections.Generic;
using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Types;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using UnityTilemap = UnityEngine.Tilemaps.Tilemap;
using SimTilemap = AgentSim.Core.Types.Tilemap;

namespace AgentSimUnity
{
    /// <summary>
    /// Phase D: placement UX. Left-side sidebar exposes zone-painting tools (Residential,
    /// Food / Retail / Entertainment / Construction commercial) and structure-placement tools
    /// (Residential / Commercial / Civic / Utility / Education / Restoration / HQ). A ghost
    /// overlay shows where the action would land — green = valid, red = invalid.
    ///
    /// Click-and-drag a rectangle to paint a zone. Click a tile to place a single structure.
    /// Escape (or "Cancel" button) returns to inspection mode.
    /// </summary>
    [RequireComponent(typeof(SimBootstrap))]
    [RequireComponent(typeof(SimVisualizer))]
    public class PlacementController : MonoBehaviour
    {
        public enum Mode
        {
            Inspect,
            PlaceStructure,
            PaintZone,
        }

        private SimBootstrap _bootstrap = null!;
        private SimVisualizer _visualizer = null!;
        private UnityTilemap _ghostTilemap = null!;
        private Texture2D _whiteTex = null!;
        private Sprite _whiteSprite = null!;
        private readonly Dictionary<Color, Tile> _tileCache = new();
        private readonly HashSet<Vector3Int> _ghostCells = new();

        private Mode _mode = Mode.Inspect;
        private StructureType _pendingStructureType;
        private ZoneType _pendingZoneType;
        private CommercialSector? _pendingZoneSector;

        private Vector3Int? _zoneDragStart;
        private Vector2 _sidebarScroll;

        // Sidebar geometry.
        private const int SidebarWidth = 220;
        private const int SidebarPad = 12;

        public bool IsActive => _mode != Mode.Inspect;

        void Awake()
        {
            _bootstrap = GetComponent<SimBootstrap>();
            _visualizer = GetComponent<SimVisualizer>();
            BuildSpriteAsset();
        }

        void Start()
        {
            BuildGhostTilemap();
        }

        void Update()
        {
            HandleEscape();
            UpdateGhost();
            HandleMouse();
        }

        void OnGUI()
        {
            DrawSidebar();
            if (_mode != Mode.Inspect) DrawModeStrip();
        }

        // === Setup ===

        private void BuildSpriteAsset()
        {
            _whiteTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _whiteTex.SetPixel(0, 0, Color.white);
            _whiteTex.filterMode = FilterMode.Point;
            _whiteTex.Apply();
            _whiteSprite = Sprite.Create(_whiteTex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            _whiteSprite.name = "PlacementGhostTile";
        }

        private void BuildGhostTilemap()
        {
            var grid = _visualizer.Grid;
            if (grid == null) return;

            var go = new GameObject("Ghost");
            go.transform.SetParent(grid.transform, worldPositionStays: false);
            _ghostTilemap = go.AddComponent<UnityTilemap>();
            var rend = go.AddComponent<TilemapRenderer>();
            rend.sortingOrder = 10;  // above structures
        }

        private Tile TileFor(Color color)
        {
            if (_tileCache.TryGetValue(color, out var existing)) return existing;
            var t = ScriptableObject.CreateInstance<Tile>();
            t.sprite = _whiteSprite;
            t.color = color;
            _tileCache[color] = t;
            return t;
        }

        // === Mouse handling ===

        private bool MouseInSidebar()
        {
            if (Mouse.current is null) return false;
            var pos = Mouse.current.position.ReadValue();
            // Input System y=0 is screen bottom; top bar covers screen top, so y > height - TopBar.
            if (pos.x < SidebarWidth) return true;
            if (pos.y > Screen.height - TopBarHeightPx) return true;
            return false;
        }

        private const float TopBarHeightPx = 56f;

        private Vector3Int? MouseToTile()
        {
            if (Mouse.current is null) return null;
            var cam = Camera.main;
            if (cam == null) return null;
            var screen = Mouse.current.position.ReadValue();
            var world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
            int tx = Mathf.FloorToInt(world.x);
            int ty = Mathf.FloorToInt(world.y);
            if (tx < 0 || ty < 0 || tx >= SimTilemap.MapSize || ty >= SimTilemap.MapSize) return null;
            return new Vector3Int(tx, ty, 0);
        }

        private void HandleEscape()
        {
            if (Keyboard.current is null) return;
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CancelMode();
            }
        }

        private void HandleMouse()
        {
            if (_mode == Mode.Inspect) return;  // SimVisualizer handles inspect clicks
            if (Mouse.current is null) return;
            if (MouseInSidebar()) return;

            var tile = MouseToTile();
            if (!tile.HasValue) return;

            if (_mode == Mode.PlaceStructure)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame)
                    TryPlaceStructure(tile.Value);
            }
            else if (_mode == Mode.PaintZone)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame)
                    _zoneDragStart = tile.Value;
                else if (Mouse.current.leftButton.wasReleasedThisFrame && _zoneDragStart.HasValue)
                {
                    TryCreateZone(_zoneDragStart.Value, tile.Value);
                    _zoneDragStart = null;
                }
            }
        }

        // === Ghost overlay ===

        private void UpdateGhost()
        {
            ClearGhost();
            if (_mode == Mode.Inspect) return;
            if (MouseInSidebar()) return;
            var tile = MouseToTile();
            if (!tile.HasValue) return;

            if (_mode == Mode.PlaceStructure)
            {
                var (w, h) = Footprint.For(_pendingStructureType);
                bool valid = IsPlacementValid(_pendingStructureType, tile.Value.x, tile.Value.y);
                PaintGhostRect(tile.Value.x, tile.Value.y, w, h, valid ? GoodColor : BadColor);
            }
            else if (_mode == Mode.PaintZone)
            {
                var start = _zoneDragStart ?? tile.Value;
                var rect = NormalizeRect(start, tile.Value);
                bool valid = IsZoneValid(rect);
                PaintGhostRect(rect.x, rect.y, rect.width, rect.height, valid ? GoodColor : BadColor);
            }
        }

        private void ClearGhost()
        {
            foreach (var c in _ghostCells) _ghostTilemap.SetTile(c, null);
            _ghostCells.Clear();
        }

        private void PaintGhostRect(int x, int y, int w, int h, Color color)
        {
            var tile = TileFor(color);
            for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
            {
                var c = new Vector3Int(x + dx, y + dy, 0);
                if (c.x < 0 || c.y < 0 || c.x >= SimTilemap.MapSize || c.y >= SimTilemap.MapSize) continue;
                _ghostTilemap.SetTile(c, tile);
                _ghostCells.Add(c);
            }
        }

        // === Validation ===

        private bool IsPlacementValid(StructureType type, int x, int y)
        {
            var sim = _bootstrap.Sim;
            if (sim == null) return false;
            var (w, h) = Footprint.For(type);
            var tm = sim.State.Region.Tilemap;
            if (!tm.InBounds(x, y, w, h)) return false;
            if (!tm.IsAreaFree(x, y, w, h)) return false;

            // Zoned structures must sit entirely in a matching zone.
            if (Footprint.IsZoned(type))
            {
                var zoneId = tm.ZoneAt(x, y);
                if (zoneId is null) return false;
                if (!sim.State.City.Zones.TryGetValue(zoneId.Value, out var zone)) return false;
                if (!tm.AreaInZone(x, y, w, h, zone.Id)) return false;
                if (type.Category() == StructureCategory.Residential && zone.Type != ZoneType.Residential) return false;
                if (type.Category() == StructureCategory.Commercial && zone.Type != ZoneType.Commercial) return false;
            }
            return true;
        }

        private bool IsZoneValid(RectInt rect)
        {
            if (rect.width < 4 || rect.height < 4) return false;
            if (rect.x < 0 || rect.y < 0
                || rect.x + rect.width > SimTilemap.MapSize
                || rect.y + rect.height > SimTilemap.MapSize) return false;
            var sim = _bootstrap.Sim;
            if (sim == null) return false;
            // No overlap with existing zones or structures.
            var tm = sim.State.Region.Tilemap;
            for (int dy = 0; dy < rect.height; dy++)
            for (int dx = 0; dx < rect.width; dx++)
            {
                if (tm.ZoneAt(rect.x + dx, rect.y + dy) is not null) return false;
                if (tm.StructureAt(rect.x + dx, rect.y + dy) is not null) return false;
            }
            return true;
        }

        private static RectInt NormalizeRect(Vector3Int a, Vector3Int b)
        {
            int xMin = Mathf.Min(a.x, b.x);
            int yMin = Mathf.Min(a.y, b.y);
            int w = Mathf.Abs(a.x - b.x) + 1;
            int h = Mathf.Abs(a.y - b.y) + 1;
            return new RectInt(xMin, yMin, w, h);
        }

        // === Actions ===

        private void TryPlaceStructure(Vector3Int tile)
        {
            if (!IsPlacementValid(_pendingStructureType, tile.x, tile.y)) return;
            var sim = _bootstrap.Sim;
            if (sim == null) return;

            try
            {
                PlaceStructureViaApi(sim, _pendingStructureType, tile.x, tile.y);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Placement] {_pendingStructureType} failed: {e.Message}");
                return;
            }
            // Stay in mode for chain-placement. Escape to exit.
        }

        private void TryCreateZone(Vector3Int start, Vector3Int end)
        {
            var rect = NormalizeRect(start, end);
            if (!IsZoneValid(rect)) return;
            var sim = _bootstrap.Sim;
            if (sim == null) return;

            var bounds = new ZoneBounds(rect.x, rect.y, rect.width, rect.height);
            try
            {
                if (_pendingZoneType == ZoneType.Residential)
                    sim.CreateResidentialZone(bounds);
                else if (_pendingZoneType == ZoneType.Commercial && _pendingZoneSector.HasValue)
                    sim.CreateCommercialZone(_pendingZoneSector.Value, bounds);
                else
                    throw new InvalidOperationException("Sectorless commercial zone not supported via UI.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Placement] Zone creation failed: {e.Message}");
            }
            CancelMode();
        }

        /// <summary>Dispatch to the right Sim.Place* method based on category, passing the clicked
        /// tile coordinates so the structure lands exactly where the ghost preview showed.</summary>
        private static void PlaceStructureViaApi(Sim sim, StructureType type, int x, int y)
        {
            var category = type.Category();
            switch (category)
            {
                case StructureCategory.Residential:
                    {
                        var zoneId = sim.State.Region.Tilemap.ZoneAt(x, y)
                            ?? throw new InvalidOperationException("No zone at target tile.");
                        sim.PlaceResidentialStructure(zoneId, type, x, y);
                        break;
                    }
                case StructureCategory.Commercial when type == StructureType.CorporateHq:
                    {
                        var zoneId = sim.State.Region.Tilemap.ZoneAt(x, y)
                            ?? throw new InvalidOperationException("No zone at target tile.");
                        // TODO: HQ needs an Industry choice. v1 defaults to Forestry.
                        sim.PlaceCorporateHq(zoneId, IndustryType.Forestry, $"Corp-{sim.State.CurrentTick}", x, y);
                        break;
                    }
                case StructureCategory.Commercial:
                    {
                        var zoneId = sim.State.Region.Tilemap.ZoneAt(x, y)
                            ?? throw new InvalidOperationException("No zone at target tile.");
                        if (!sim.State.City.Zones.TryGetValue(zoneId, out var zone))
                            throw new InvalidOperationException("Zone not found.");
                        var sector = zone.Sector ?? CommercialSector.Food;
                        sim.PlaceCommercialStructure(zoneId, type, sector, x, y);
                        break;
                    }
                case StructureCategory.Civic:
                case StructureCategory.Healthcare:
                case StructureCategory.Utility:
                    sim.PlaceServiceStructure(type, x, y);
                    break;
                case StructureCategory.Education:
                    sim.PlaceEducationStructure(type, x, y);
                    break;
                case StructureCategory.Restoration:
                    sim.PlaceRestorationStructure(type, x, y);
                    break;
                default:
                    throw new NotSupportedException($"Placement of {type} not supported yet (e.g., industrial).");
            }
        }

        // === Sidebar ===

        private void DrawSidebar()
        {
            GUI.Box(new Rect(0, 0, SidebarWidth, Screen.height), "");
            GUILayout.BeginArea(new Rect(SidebarPad, SidebarPad, SidebarWidth - SidebarPad * 2, Screen.height - SidebarPad * 2));
            _sidebarScroll = GUILayout.BeginScrollView(_sidebarScroll);

            GUILayout.Label("ZONES");
            ZoneButton("Residential", ZoneType.Residential, null);
            ZoneButton("Food", ZoneType.Commercial, CommercialSector.Food);
            ZoneButton("Retail", ZoneType.Commercial, CommercialSector.Retail);
            ZoneButton("Entertainment", ZoneType.Commercial, CommercialSector.Entertainment);
            ZoneButton("Construction", ZoneType.Commercial, CommercialSector.Construction);
            GUILayout.Space(8);

            CategorySection("RESIDENTIAL", new[] {
                StructureType.House, StructureType.Apartment, StructureType.Townhouse,
                StructureType.Condo, StructureType.AffordableHousing,
            });
            CategorySection("COMMERCIAL", new[] {
                StructureType.Shop, StructureType.Marketplace,
                StructureType.Restaurant, StructureType.Theater,
                StructureType.CorporateHq,
            });
            CategorySection("CIVIC", new[] {
                StructureType.PoliceStation, StructureType.FireStation, StructureType.TownHall,
            });
            CategorySection("HEALTHCARE", new[] {
                StructureType.Clinic, StructureType.Hospital,
            });
            CategorySection("EDUCATION", new[] {
                StructureType.PrimarySchool, StructureType.SecondarySchool, StructureType.College,
            });
            CategorySection("UTILITY", new[] {
                StructureType.Generator, StructureType.Well,
                StructureType.ElectricityDistribution, StructureType.WaterDistribution,
            });
            CategorySection("RESTORATION", new[] {
                StructureType.Park, StructureType.ReforestationSite, StructureType.WetlandRestoration,
            });

            GUILayout.Space(10);
            if (_mode != Mode.Inspect && GUILayout.Button("Cancel (Esc)"))
                CancelMode();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void ZoneButton(string label, ZoneType type, CommercialSector? sector)
        {
            bool active = _mode == Mode.PaintZone
                && _pendingZoneType == type
                && _pendingZoneSector == sector;
            var bg = GUI.backgroundColor;
            if (active) GUI.backgroundColor = Color.cyan;
            if (GUILayout.Button(label))
            {
                _mode = Mode.PaintZone;
                _pendingZoneType = type;
                _pendingZoneSector = sector;
                _zoneDragStart = null;
            }
            GUI.backgroundColor = bg;
        }

        private void CategorySection(string title, StructureType[] types)
        {
            GUILayout.Label(title);
            foreach (var t in types)
            {
                StructureButton(t);
            }
            GUILayout.Space(6);
        }

        private void StructureButton(StructureType type)
        {
            bool active = _mode == Mode.PlaceStructure && _pendingStructureType == type;
            var bg = GUI.backgroundColor;
            if (active) GUI.backgroundColor = Color.cyan;
            if (GUILayout.Button(type.ToString()))
            {
                _mode = Mode.PlaceStructure;
                _pendingStructureType = type;
            }
            GUI.backgroundColor = bg;
        }

        private void DrawModeStrip()
        {
            string label = _mode switch
            {
                Mode.PlaceStructure => $"Placing: {_pendingStructureType}  (click tile, Esc to cancel)",
                Mode.PaintZone when _pendingZoneSector.HasValue =>
                    $"Painting: {_pendingZoneType} [{_pendingZoneSector}]  (drag rectangle, Esc to cancel)",
                Mode.PaintZone => $"Painting: {_pendingZoneType}  (drag rectangle, Esc to cancel)",
                _ => "",
            };
            // Sit just below the UI Toolkit top bar (height 56).
            var rect = new Rect(SidebarWidth + 10, 66, 600, 24);
            GUI.Box(rect, label);
        }

        private void CancelMode()
        {
            _mode = Mode.Inspect;
            _zoneDragStart = null;
            ClearGhost();
        }

        // === Colors ===

        private static readonly Color GoodColor = new(0.30f, 0.85f, 0.35f, 0.55f);
        private static readonly Color BadColor = new(0.85f, 0.30f, 0.30f, 0.55f);
    }
}
