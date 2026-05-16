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
            Demolish,
            Connect,
            PlaceRoad,
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
        private long? _connectSourceId;  // first click in Connect mode

        // Road tool settings — exposed for toggle UI; defaults on.
        [Header("Road tool")]
        public bool GridSnapEnabled = true;
        public bool AlignmentGuidesEnabled = true;
        public bool AngleConstraintEnabled = true;
        public float AngleConstraintIncrementDeg = 15f;
        /// <summary>Grid-snap step in meters. 1m = every tile, 10m = every 10 tiles, etc.</summary>
        public float GridSnapStep = 10f;

        // Anchored-start flag: true once drag starts from an existing node OR on an existing
        // edge; while true (and constraint enabled), the cursor angle is snapped to 15°.
        private bool _startIsAnchored;

        private Vector3Int? _zoneDragStart;
        private Vector2 _sidebarScroll;

        // Road drawing: float-precision world-XZ points (matches Sim's Point2 coord space).
        private Vector2? _roadDragStart;
        private Vector2? _roadDragCurrent;

        // Sidebar geometry.
        private const int SidebarWidth = 220;
        private const int SidebarPad = 12;

        public bool IsActive => _mode != Mode.Inspect;
        public Mode CurrentMode => _mode;
        public long? ConnectSourceId => _connectSourceId;

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

        private Vector3Int? MouseToTile() => SimVisualizer.MouseToTile(Camera.main);

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

            // PlaceRoad uses float-precision points, not integer tiles, so it has its own
            // input path that bypasses MouseToTile.
            if (_mode == Mode.PlaceRoad)
            {
                HandleRoadDrag();
                return;
            }

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
            else if (_mode == Mode.Demolish)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame)
                    TryDemolish(tile.Value);
            }
            else if (_mode == Mode.Connect)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame)
                    HandleConnectClick(tile.Value);
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
            else if (_mode == Mode.Demolish)
            {
                // Paint the structure's whole footprint in red so the player sees what
                // they're about to remove.
                var sim = _bootstrap.Sim;
                if (sim == null) return;
                var sid = sim.State.Region.Tilemap.StructureAt(tile.Value.x, tile.Value.y);
                if (sid is null) return;
                if (!sim.State.City.Structures.TryGetValue(sid.Value, out var s)) return;
                if (s.X < 0 || s.Y < 0) return;
                var (w, h) = Footprint.For(s.Type);
                PaintGhostRect(s.X, s.Y, w, h, BadColor);
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

        /// <summary>Drag-to-draw with multi-layer snapping. Precedence (highest first):
        ///   1. Existing-node snap (always on)
        ///   2. 15° angle constraint when starting from an anchored point (on existing node
        ///      or existing edge) and AngleConstraintEnabled
        ///   3. Grid snap (default-on; Alt inverts)
        ///   4. Per-axis alignment to existing nodes (AlignmentGuidesEnabled)</summary>
        private void HandleRoadDrag()
        {
            var world = MouseGroundPoint();
            if (world is null) return;
            bool altHeld = Keyboard.current is not null && Keyboard.current.altKey.isPressed;
            // Alt INVERTS the default grid-snap setting.
            bool effectiveGridSnap = GridSnapEnabled ^ altHeld;

            // Snap the cursor itself (start of a new drag uses this directly).
            var snappedCursor = SnapEndpoint(world.Value, effectiveGridSnap);

            Vector2 endValue = snappedCursor;
            if (_roadDragStart.HasValue && AngleConstraintEnabled)
            {
                // Angle constraint applies to every drag (no longer requires anchored start).
                // Angles are measured from world +X axis so snapped angles align with the
                // world grid regardless of where the road starts.
                endValue = ApplyAngleConstraint(_roadDragStart.Value, world.Value, effectiveGridSnap);
            }
            _roadDragCurrent = endValue;

            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                _roadDragStart = snappedCursor;
                _startIsAnchored = IsAnchored(snappedCursor);
            }
            else if (Mouse.current.leftButton.wasReleasedThisFrame && _roadDragStart.HasValue)
            {
                TryPlaceRoad(_roadDragStart.Value, endValue);
                _roadDragStart = null;
                _roadDragCurrent = null;
                _startIsAnchored = false;
            }
        }

        /// <summary>True when the position is on (or snapped to) an existing road node, or
        /// lies on an existing edge segment within a small tolerance.</summary>
        private bool IsAnchored(Vector2 p)
        {
            var sim = _bootstrap.Sim;
            if (sim is null) return false;
            // On-existing-node test (exact, since we already snapped).
            foreach (var n in sim.State.RoadNodes.Values)
            {
                if (Mathf.Approximately(n.Position.X, p.x) && Mathf.Approximately(n.Position.Y, p.y))
                    return true;
            }
            // On-existing-edge test (within 0.5 tile of any segment).
            const float edgeTolerance = 0.5f;
            foreach (var edge in sim.State.RoadEdges.Values)
            {
                if (!sim.State.RoadNodes.TryGetValue(edge.FromNodeId, out var a)) continue;
                if (!sim.State.RoadNodes.TryGetValue(edge.ToNodeId, out var b)) continue;
                if (PointSegmentDistance(p, a.Position, b.Position) <= edgeTolerance) return true;
            }
            return false;
        }

        private static float PointSegmentDistance(Vector2 p, Point2 a, Point2 b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-6f) return Vector2.Distance(p, new Vector2(a.X, a.Y));
            float t = ((p.x - a.X) * dx + (p.y - a.Y) * dy) / lenSq;
            t = Mathf.Clamp01(t);
            float px = a.X + t * dx, py = a.Y + t * dy;
            return Mathf.Sqrt((p.x - px) * (p.x - px) + (p.y - py) * (p.y - py));
        }

        private Vector2 ApplyAngleConstraint(Vector2 start, Vector2 rawCursor, bool gridSnap)
        {
            float dx = rawCursor.x - start.x;
            float dy = rawCursor.y - start.y;
            float distance = Mathf.Sqrt(dx * dx + dy * dy);
            if (distance < 0.5f) return start;
            float angleDeg = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            float snappedAngleDeg = Mathf.Round(angleDeg / AngleConstraintIncrementDeg)
                                  * AngleConstraintIncrementDeg;
            float r = snappedAngleDeg * Mathf.Deg2Rad;
            // Optionally also snap distance to the grid step when grid snap is in play —
            // keeps the end on a regular grid step from the start.
            float effectiveDist = distance;
            if (gridSnap)
            {
                float s = Mathf.Max(1f, GridSnapStep);
                effectiveDist = Mathf.Max(s, Mathf.Round(distance / s) * s);
            }
            float ex = start.x + effectiveDist * Mathf.Cos(r);
            float ey = start.y + effectiveDist * Mathf.Sin(r);
            // Still allow existing-node snap on the resulting end (highest priority).
            var snappedEnd = SnapEndpointNodeOnly(new Vector2(ex, ey));
            return snappedEnd;
        }

        /// <summary>Node-only snap (no grid, no alignment). Used by ApplyAngleConstraint so
        /// the angle-constrained end still snaps to an existing node if one is right there.</summary>
        private Vector2 SnapEndpointNodeOnly(Vector2 p)
        {
            var sim = _bootstrap.Sim;
            if (sim is null) return p;
            float bestSq = Sim.NodeSnapRadiusTiles * Sim.NodeSnapRadiusTiles;
            Point2? best = null;
            foreach (var n in sim.State.RoadNodes.Values)
            {
                float dx = n.Position.X - p.x;
                float dy = n.Position.Y - p.y;
                float dsq = dx * dx + dy * dy;
                if (dsq <= bestSq) { bestSq = dsq; best = n.Position; }
            }
            return best.HasValue ? new Vector2(best.Value.X, best.Value.Y) : p;
        }

        /// <summary>Snap a float cursor position to the nearest existing road node if one is
        /// within the sim's snap radius (always on). If no node is close and gridSnap is true
        /// (e.g. Option/Alt is held), snap to the nearest integer tile. Otherwise apply
        /// per-axis alignment snap so the visual alignment guides and the actual placement
        /// agree.</summary>
        public Vector2 SnapEndpoint(Vector2 p, bool gridSnap)
        {
            var sim = _bootstrap.Sim;
            if (sim is not null)
            {
                float bestSq = Sim.NodeSnapRadiusTiles * Sim.NodeSnapRadiusTiles;
                Point2? best = null;
                foreach (var node in sim.State.RoadNodes.Values)
                {
                    float dx = node.Position.X - p.x;
                    float dy = node.Position.Y - p.y;
                    float dsq = dx * dx + dy * dy;
                    if (dsq <= bestSq) { bestSq = dsq; best = node.Position; }
                }
                if (best.HasValue) return new Vector2(best.Value.X, best.Value.Y);
            }
            if (gridSnap) return SnapToStep(p, GridSnapStep);
            if (AlignmentGuidesEnabled) return AlignToExistingAxes(p);
            return p;
        }

        /// <summary>Round to the nearest multiple of <paramref name="step"/>. Step is clamped
        /// to a minimum of 1m so 0 (or a negative value) doesn't break the math.</summary>
        public static Vector2 SnapToStep(Vector2 p, float step)
        {
            float s = Mathf.Max(1f, step);
            return new Vector2(Mathf.Round(p.x / s) * s, Mathf.Round(p.y / s) * s);
        }

        /// <summary>Per-axis alignment snap: independently snap X and Y to a matching
        /// existing node's coordinate if either is within tolerance. Symmetric with the
        /// guide-line rendering in SimVisualizer.</summary>
        private Vector2 AlignToExistingAxes(Vector2 p)
        {
            var sim = _bootstrap.Sim;
            if (sim is null) return p;
            float bestXErr = SimVisualizer.AlignToleranceTiles;
            float bestYErr = SimVisualizer.AlignToleranceTiles;
            float snappedX = p.x, snappedY = p.y;
            foreach (var node in sim.State.RoadNodes.Values)
            {
                float dx = Mathf.Abs(node.Position.X - p.x);
                float dy = Mathf.Abs(node.Position.Y - p.y);
                if (dx < bestXErr) { bestXErr = dx; snappedX = node.Position.X; }
                if (dy < bestYErr) { bestYErr = dy; snappedY = node.Position.Y; }
            }
            return new Vector2(snappedX, snappedY);
        }

        private void TryPlaceRoad(Vector2 a, Vector2 b)
        {
            var sim = _bootstrap.Sim;
            if (sim == null) return;
            try
            {
                sim.PlaceRoad(new Point2(a.x, a.y), new Point2(b.x, b.y));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Road] Failed: {e.Message}");
            }
        }

        /// <summary>Mouse cursor projected onto the Y=0 ground plane, returned in tile coords
        /// (which after the SimGrid's +90° rotation == world XZ).</summary>
        private Vector2? MouseGroundPoint()
        {
            var cam = Camera.main;
            if (cam == null || Mouse.current is null) return null;
            var screen = Mouse.current.position.ReadValue();
            var ray = cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0));
            if (Mathf.Abs(ray.direction.y) < 1e-5f) return null;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0) return null;
            var hit = ray.origin + ray.direction * t;
            // World X → tile X, world Z → tile Y (Sim coords use Y as the second axis).
            return new Vector2(hit.x, hit.z);
        }

        public Vector2? RoadPreviewStart => _roadDragStart;
        public Vector2? RoadPreviewEnd => _roadDragCurrent;

        private void HandleConnectClick(Vector3Int tile)
        {
            var sim = _bootstrap.Sim;
            if (sim == null) return;
            var sid = sim.State.Region.Tilemap.StructureAt(tile.x, tile.y);
            if (sid is null) return;
            if (!sim.State.City.Structures.TryGetValue(sid.Value, out var s)) return;

            // Both endpoints must be distributors of the same kind. The whole connected
            // component is "energized" once any of its distributors touches a producer.
            bool isDistributor = s.Type == StructureType.ElectricityDistribution
                              || s.Type == StructureType.WaterDistribution;
            if (!isDistributor)
            {
                Debug.LogWarning("[Connect] Click a distributor (ElectricityDistribution or WaterDistribution).");
                return;
            }

            if (_connectSourceId is null)
            {
                _connectSourceId = sid.Value;
                return;
            }

            if (!sim.State.City.Structures.TryGetValue(_connectSourceId.Value, out var srcDist))
            {
                _connectSourceId = null;
                return;
            }
            if (srcDist.Type != s.Type)
            {
                Debug.LogWarning("[Connect] Both endpoints must be the same distributor type (power↔power or water↔water).");
                _connectSourceId = null;
                return;
            }
            var kind = s.Type == StructureType.ElectricityDistribution
                ? NetworkKind.Power
                : NetworkKind.Water;

            try
            {
                sim.ConnectEdge(_connectSourceId.Value, sid.Value, kind);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Connect] Failed: {e.Message}");
            }
            _connectSourceId = null;
        }

        private void TryDemolish(Vector3Int tile)
        {
            var sim = _bootstrap.Sim;
            if (sim == null) return;
            var sid = sim.State.Region.Tilemap.StructureAt(tile.x, tile.y);
            if (sid is null) return;
            try
            {
                sim.RemoveStructure(sid.Value);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Placement] Demolition of #{sid.Value} failed: {e.Message}");
            }
            // Stay in demolish mode for chain-removal. Esc to exit.
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
            RoadButton();
            ConnectButton();
            DemolishButton();

            GUILayout.Space(8);
            GUILayout.Label("ROAD SETTINGS");
            GridSnapEnabled        = GUILayout.Toggle(GridSnapEnabled,        " Grid snap (Alt inverts)");
            AlignmentGuidesEnabled = GUILayout.Toggle(AlignmentGuidesEnabled, " Alignment guides");
            AngleConstraintEnabled = GUILayout.Toggle(AngleConstraintEnabled, " 15° angle constraint");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Snap step: {GridSnapStep:F0} m", GUILayout.Width(110));
            GridSnapStep = Mathf.Round(GUILayout.HorizontalSlider(GridSnapStep, 1f, 25f));
            GUILayout.EndHorizontal();

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

        private void RoadButton()
        {
            bool active = _mode == Mode.PlaceRoad;
            var bg = GUI.backgroundColor;
            GUI.backgroundColor = active ? Color.green : new Color(0.20f, 0.55f, 0.30f, 1f);
            if (GUILayout.Button("ROAD"))
            {
                _mode = Mode.PlaceRoad;
                _roadDragStart = null;
                _roadDragCurrent = null;
            }
            GUI.backgroundColor = bg;
        }

        private void ConnectButton()
        {
            bool active = _mode == Mode.Connect;
            var bg = GUI.backgroundColor;
            GUI.backgroundColor = active ? Color.cyan : new Color(0.25f, 0.45f, 0.55f, 1f);
            if (GUILayout.Button("CONNECT"))
            {
                _mode = Mode.Connect;
                _zoneDragStart = null;
                _connectSourceId = null;
            }
            GUI.backgroundColor = bg;
        }

        private void DemolishButton()
        {
            bool active = _mode == Mode.Demolish;
            var bg = GUI.backgroundColor;
            GUI.backgroundColor = active ? Color.red : new Color(0.55f, 0.20f, 0.20f, 1f);
            if (GUILayout.Button("DEMOLISH"))
            {
                _mode = Mode.Demolish;
                _zoneDragStart = null;
            }
            GUI.backgroundColor = bg;
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
                Mode.Demolish => "DEMOLISH: click a structure to remove it  (Esc to cancel)",
                Mode.Connect => _connectSourceId.HasValue
                    ? "CONNECT: click another distributor to link  (Esc to cancel)"
                    : "CONNECT: click a distributor (power or water)  (Esc to cancel)",
                Mode.PlaceRoad => _roadDragStart.HasValue
                    ? "ROAD: release left mouse to commit segment  (Esc to cancel)"
                    : "ROAD: click + drag to draw a road segment  (Esc to cancel)",
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
            _connectSourceId = null;
            _roadDragStart = null;
            _roadDragCurrent = null;
            ClearGhost();
        }

        // === Colors ===

        private static readonly Color GoodColor = new(0.30f, 0.85f, 0.35f, 0.55f);
        private static readonly Color BadColor = new(0.85f, 0.30f, 0.30f, 0.55f);
    }
}
