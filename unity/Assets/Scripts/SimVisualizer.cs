#nullable enable
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
        private UnityTilemap _backgroundTilemap = null!;
        private UnityTilemap _zoneTilemap = null!;
        private UnityTilemap _structureTilemap = null!;
        private UnityTilemap _landValueTilemap = null!;
        private UnityTilemap _utilityTilemap = null!;
        private UnityTilemap _hoverTilemap = null!;
        private Tile _gridTile = null!;

        // Network-edge line rendering.
        private GameObject _edgeContainer = null!;
        private readonly Dictionary<long, LineRenderer> _edgeRenderers = new();
        private Material _edgeMaterial = null!;
        private int _lastEdgeCount = -1;

        // Road rendering — graph mode. Each edge is a thin line between its two nodes; each
        // node is a small disc. No 3D extrusion yet — just enough visual to confirm the graph
        // structure is right.
        private GameObject _roadContainer = null!;
        private readonly Dictionary<long, LineRenderer> _roadEdgeRenderers = new();
        private readonly Dictionary<long, GameObject> _nodeObjects = new();
        private LineRenderer? _roadPreview;
        // Guide circle that follows the cursor while in PlaceRoad mode. Radius matches
        // setback + corridor depth — i.e., how far the corridor would extend if a road were
        // placed through this point at the default lane count. Visual aid only.
        private LineRenderer? _roadNodeGuideCircle;
        /// <summary>Half-width of the default-config road (1+1 lanes → 10m total → 5m setback).
        /// Used to size the guide circle; if road config becomes selectable, plumb the chosen
        /// lane counts through here.</summary>
        private const float DefaultRoadHalfWidthMeters = 5f;
        private Material _roadLineMaterial = null!;
        private Material _nodeMaterial = null!;
        private Material _roadPreviewMaterial = null!;
        private int _lastRoadEdgeCount = -1;
        private int _lastRoadNodeCount = -1;
        private Mesh _nodeMesh = null!;
        private static readonly Color RoadEdgeColor = new(0.65f, 0.65f, 0.70f, 1f);
        private static readonly Color RoadNodeColor = new(0.95f, 0.55f, 0.30f, 1f);
        private static readonly Color RoadNodeHoverColor = new(1.00f, 0.85f, 0.40f, 1f);
        private static readonly Color RoadNodeDragColor = new(0.40f, 0.95f, 1.00f, 1f);
        private static readonly Color RoadPreviewColor = new(0.95f, 0.90f, 0.45f, 1f);

        // Node hover + drag state.
        private long? _hoveredNodeId;
        private long? _draggingNodeId;
        private const float NodeHitRadiusTiles = 1.0f;

        // Ghost-node preview shown at would-be endpoints while drawing a road.
        private GameObject? _ghostStartNode;
        private GameObject? _ghostEndNode;
        private Material _ghostNodeMaterial = null!;
        private static readonly Color RoadGhostNodeColor = new(0.95f, 0.90f, 0.45f, 0.7f);

        // Road-edge corridors: faint translucent rectangles showing the buildable strip
        // around each edge. Visualization-only for now (no placement constraint).
        private readonly Dictionary<long, GameObject> _corridorObjects = new();
        private Material _corridorMaterial = null!;
        /// <summary>Corridor visualizes only the front row of buildable cells (1 step deep
        /// beyond the road setback). Structures snap their FRONT EDGE to these cells; the
        /// rest of the lot is allowed to extend past the corridor strip.</summary>
        /// <summary>Number of corridor cells deep on each side of the road. Only the FIRST
        /// row is load-bearing for placement (a structure's front edge snaps to row 0); deeper
        /// rows are shown purely as a visual hint for how far back lots can extend.</summary>
        public const int CorridorDepthCells = 10;
        /// <summary>How deep a structure's lot is allowed to extend behind the corridor strip
        /// (visualized as a dashed back-guide line). 10u at the standard 5m unit = 50m.
        /// Currently disabled — set to a positive value to re-enable the back-guide line.</summary>
        public const float StructureMaxDepthMeters = 0f;
        private static readonly Color CorridorColor = new(0.10f, 0.10f, 0.10f, 0.10f);

        // Per-edge zoning overlay: one GameObject per road edge that has zoned corridor
        // cells. Rendered as filled translucent quads over the corridor outline.
        private readonly Dictionary<long, GameObject> _zoneOverlayObjects = new();
        private static readonly Color ResidentialZoneColor = new(0.45f, 0.85f, 0.50f, 0.25f);

        // 3D structure meshes: one GameObject per Structure (keyed by structure id).
        // Each one holds a lot quad + a centered building box, oriented so its +Y face points
        // toward the nearest road. Material is shared (vertex colors carry per-face shading).
        private readonly Dictionary<long, GameObject> _structureObjects = new();
        private GameObject? _structureContainer;
        private Material? _structureMaterial;
        private static readonly Color LotColor    = new(0.42f, 0.58f, 0.30f, 1f);  // grass green
        private static readonly Color MarkerColor = new(0.95f, 0.30f, 0.20f, 1f);  // front-edge marker

        // Cursor-following grid: a small procedural mesh of line quads at integer tile
        // positions within a 20-tile radius of the cursor. Per-vertex alpha fades radially
        // so it dissolves at the edge.
        private GameObject? _cursorGridGo;
        private MeshFilter? _cursorGridMF;
        private Mesh? _cursorGridMesh;
        private Material _cursorGridMaterial = null!;
        private const int CursorGridRadius = 20;
        private const float CursorGridLineWidth = 0.06f;
        private static readonly Color CursorGridColor = new(0.55f, 0.60f, 0.75f, 1f);

        // Alignment guides: thin lines shown when the cursor / dragged-node aligns with
        // another existing node's X or Y (drawio-style smart guides).
        private GameObject? _guideVertical;
        private GameObject? _guideHorizontal;
        private Material _guideMaterial = null!;
        private static readonly Color GuideColor = new(0.95f, 0.85f, 0.30f, 0.8f);
        public const float AlignToleranceTiles = 0.6f;

        // Distance-label state — populated in UpdateAlignmentGuides, consumed in OnGUI.
        private Vector2? _guideProbePos;
        private Point2? _guideAlignedNodeForX;  // node whose X matched
        private Point2? _guideAlignedNodeForY;  // node whose Y matched

        /// <summary>Toggle for the utility-coverage overlay (highlights unpowered / unwatered).</summary>
        public bool ShowUtilityCoverage { get; set; }
        private bool _lastShowUtility;
        private int _lastUtilitySignature = -1;
        private readonly HashSet<Vector3Int> _paintedUtilityCells = new();

        // Hover highlight tracking.
        private long? _lastHoveredStructureId;
        private readonly HashSet<Vector3Int> _paintedHoverCells = new();
        private Grid _grid = null!;

        /// <summary>Toggle for the land-value heatmap overlay. HUD writes this.</summary>
        public bool ShowLandValue { get; set; }

        // Re-paint LV when the sim's MaxLandValue changes (i.e., after a monthly recompute).
        private double _lastMaxLv = -1;
        private bool _lastShowLv;
        private readonly HashSet<Vector3Int> _paintedLvCells = new();

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
            BuildGridTile();
            BuildTilemaps();
            // PaintGridBackground();  // disabled — replaced by cursor-following grid (UpdateCursorGrid)
            SetupCamera();
        }

        void Update()
        {
            HandleCameraControl();
            UpdateHoverHighlight();
            UpdateCursorGrid();
        }

        // Warm-yellow (or cyan in Connect mode) translucent overlay on the footprint of whichever
        // structure the mouse is over. Suppressed only in modes that paint their own ghost
        // (PlaceStructure / PaintZone) — Connect and Demolish leave hover available.
        private void UpdateHoverHighlight()
        {
            if (_bootstrap.Sim is null) return;

            var placement = GetComponent<PlacementController>();
            bool suppress = placement != null
                && (placement.CurrentMode == PlacementController.Mode.PlaceStructure
                    || placement.CurrentMode == PlacementController.Mode.PaintZone);
            if (suppress)
            {
                if (_lastHoveredStructureId.HasValue) ClearHoverPaint();
                _lastHoveredStructureId = null;
                return;
            }

            var hoveredId = ResolveHoveredStructureId();
            if (hoveredId == _lastHoveredStructureId) return;

            ClearHoverPaint();
            if (hoveredId is long id) PaintHoverFor(id, placement);
            _lastHoveredStructureId = hoveredId;
        }

        private long? ResolveHoveredStructureId()
        {
            if (Mouse.current is null) return null;
            var pos = Mouse.current.position.ReadValue();
            if (pos.x < SidebarWidthPx) return null;
            if (pos.y > Screen.height - TopBarHeightPx) return null;
            var tile = MouseToTile(Camera.main);
            if (tile is null) return null;
            return _bootstrap.Sim!.State.Region.Tilemap.StructureAt(tile.Value.x, tile.Value.y);
        }

        private void ClearHoverPaint()
        {
            foreach (var c in _paintedHoverCells) _hoverTilemap.SetTile(c, null);
            _paintedHoverCells.Clear();
        }

        private void PaintHoverFor(long structureId, PlacementController? placement)
        {
            if (!_bootstrap.Sim!.State.City.Structures.TryGetValue(structureId, out var s)) return;
            if (s.X < 0 || s.Y < 0) return;
            var (w, h) = Footprint.For(s.Type);
            // Cyan when Connect mode is active and this is a valid distributor; red when it's
            // a non-distributor (so the player sees "can't click this"); warm yellow otherwise.
            Color color;
            if (placement != null && placement.CurrentMode == PlacementController.Mode.Connect)
            {
                bool isDist = s.Type == StructureType.ElectricityDistribution
                              || s.Type == StructureType.WaterDistribution;
                color = isDist ? new Color(0.40f, 0.90f, 0.95f, 0.55f)
                               : new Color(0.95f, 0.35f, 0.35f, 0.40f);
            }
            else
            {
                color = new Color(1f, 0.92f, 0.45f, 0.38f);
            }
            var tile = TileFor(color);
            for (int dy = 0; dy < h; dy++)
            for (int dx = 0; dx < w; dx++)
            {
                var cell = new Vector3Int(s.X + dx, s.Y + dy, 0);
                _hoverTilemap.SetTile(cell, tile);
                _paintedHoverCells.Add(cell);
            }
        }

        // Hide the full-map grid background when each tile becomes too small to render its
        // 1-pixel border cleanly — that's when the borders alias against screen pixels and
        // produce a moiré pattern. Threshold: ~12 screen pixels per tile.
        private void UpdateGridVisibility()
        {
            var cam = Camera.main;
            if (cam == null || _backgroundTilemap == null) return;
            float visibleHeightAtGround = cam.orthographic
                ? cam.orthographicSize * 2f
                : 2f * GroundDistance(cam) * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float pxPerTile = Screen.height / visibleHeightAtGround;
            bool shouldShow = pxPerTile >= 12f;
            if (_backgroundTilemap.gameObject.activeSelf != shouldShow)
                _backgroundTilemap.gameObject.SetActive(shouldShow);
        }

        // UI overlap constants — used to guard clicks/hovers that fall on HUD chrome.
        private const float SidebarWidthPx = 220f;
        private const float TopBarHeightPx = 56f;
        private const float CameraPitchDeg = 32f;
        private const float CameraYawDeg = 35f;
        private const float CameraFov = 50f;
        private const float CameraInitialDistance = 80f;
        private const float CameraMinDistanceToGround = 12f;
        private const float CameraMaxDistanceToGround = 400f;
        private const float PanDragMultiplier = 1.5f;  // mouse-drag amplification beyond pixel-exact

        /// <summary>Raycast the mouse cursor onto the Y=0 ground plane (where the rotated
        /// tilemap lives) and return the integer tile coords. Tile X = world X, tile Y = world Z
        /// because the SimGrid is rotated +90° around X. Returns null if the ray misses the
        /// plane or the result is off the map.</summary>
        public static Vector3Int? MouseToTile(Camera cam)
        {
            if (cam == null) return null;
            if (Mouse.current is null) return null;
            var screen = Mouse.current.position.ReadValue();
            var ray = cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0));
            if (Mathf.Abs(ray.direction.y) < 1e-5f) return null;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0) return null;
            var world = ray.origin + ray.direction * t;
            int tx = Mathf.FloorToInt(world.x);
            int ty = Mathf.FloorToInt(world.z);
            if (tx < 0 || ty < 0 || tx >= SimTilemap.MapSize || ty >= SimTilemap.MapSize) return null;
            return new Vector3Int(tx, ty, 0);
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

            // Repaint LV when toggle changes or when monthly recompute pushed a new MaxLandValue.
            var tm = state.Region.Tilemap;
            if (ShowLandValue != _lastShowLv || tm.MaxLandValue != _lastMaxLv)
            {
                PaintLandValue(tm);
                _lastShowLv = ShowLandValue;
                _lastMaxLv = tm.MaxLandValue;
            }

            // Repaint utility overlay when toggle changes or coverage state shifts.
            int utilitySig = ComputeUtilitySignature(state);
            if (ShowUtilityCoverage != _lastShowUtility || utilitySig != _lastUtilitySignature)
            {
                PaintUtilityCoverage(state);
                _lastShowUtility = ShowUtilityCoverage;
                _lastUtilitySignature = utilitySig;
            }

            // Sync network-edge LineRenderers when the edge set changes.
            if (state.NetworkEdges.Count != _lastEdgeCount)
            {
                SyncEdgeRenderers(state);
                _lastEdgeCount = state.NetworkEdges.Count;
            }

            // Sync structural changes (add/destroy GameObjects when counts change).
            if (state.RoadEdges.Count != _lastRoadEdgeCount
                || state.RoadNodes.Count != _lastRoadNodeCount)
            {
                SyncRoadGraphRenderers(state);
                _lastRoadEdgeCount = state.RoadEdges.Count;
                _lastRoadNodeCount = state.RoadNodes.Count;
            }
            // Refresh positions every frame so dragging a node visibly updates connected edges.
            UpdateRoadGraphPositions(state);
            UpdateCorridorMeshes(state);
            UpdateZoneOverlays(state);
            UpdateStructureMeshes(state);

            // Live preview line during drag.
            UpdateRoadPreview();
            // 10u radius guide circle around the cursor while in PlaceRoad mode.
            UpdateRoadNodeGuideCircle();
            // Hover + drag on nodes (only in inspect mode).
            HandleNodeInteraction();
            // Alignment guides when the cursor / dragged node aligns with another node.
            UpdateAlignmentGuides();

            HandleClick();
        }

        void OnGUI()
        {
            DrawAlignmentDistanceLabels();
            DrawRoadLengthLabel();
            if (_bootstrap.Sim is null) return;

            // Hover tooltip near cursor — only shown when we're hovering over something and
            // nothing is selected (so it doesn't compete with the detail panel).
            DrawHoverTooltip();

            if (!_selectedStructureId.HasValue) return;
            if (!_bootstrap.Sim.State.City.Structures.TryGetValue(_selectedStructureId.Value, out var s)) return;

            // Below top bar (56) + mode strip slot (24), right of sidebar (220).
            var rect = new Rect(SidebarWidthPx + 10, 96, 320, 200);
            GUI.Box(rect, $"Structure #{s.Id}");
            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 20, rect.width - 20, rect.height - 30));
            GUILayout.Label($"Type: {s.Type}");
            if (s.Sector is CommercialSector sec) GUILayout.Label($"Sector: {sec}");
            if (s.Industry is IndustryType ind) GUILayout.Label($"Industry: {ind}");
            GUILayout.Label($"Position: ({s.X}, {s.Y})");
            var (w, h) = Footprint.For(s.Type);
            GUILayout.Label($"Footprint: {w}×{h}");
            GUILayout.Label(s.Operational ? "Status: active" : s.UnderConstruction ? "Status: building" : s.Inactive ? "Status: INACTIVE" : "Status: unknown");
            GUILayout.Label($"Power: {(s.IsPowered ? "yes" : "NO")}   Water: {(s.IsWatered ? "yes" : "NO")}");
            GUILayout.Label($"Cash: ${s.CashBalance:N0}");
            GUILayout.Label($"Jobs: {s.EmployeeIds.Count}/{s.JobSlotsTotal()}");
            if (GUILayout.Button("Close")) _selectedStructureId = null;
            GUILayout.EndArea();
        }

        // Lightweight tooltip floating next to the cursor — just type + status + jobs.
        // Full detail comes from clicking (the detail panel above). In Connect mode, the
        // tooltip becomes a hint about what clicking will do.
        /// <summary>Draw a small "Xm" label between the probe (cursor/dragged node) and the
        /// node it's aligned with — one for each active axis. Distance in tiles == meters
        /// since the sim treats 1 tile = 1m.</summary>
        private void DrawAlignmentDistanceLabels()
        {
            if (_guideProbePos is null) return;
            var cam = Camera.main;
            if (cam == null) return;
            var probe = _guideProbePos.Value;

            // Lazy-build a small style once.
            if (_alignLabelStyle == null)
            {
                _alignLabelStyle = new GUIStyle
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.95f, 0.85f, 0.30f) },
                };
            }
            var bg = new GUIStyle(GUI.skin.box) { fontSize = 12, alignment = TextAnchor.MiddleCenter };
            bg.normal.textColor = new Color(0.95f, 0.85f, 0.30f);

            float stepSize = GetComponent<PlacementController>()?.GridSnapStep ?? 5f;
            if (_guideAlignedNodeForX is Point2 ax)
            {
                // Vertical guide → distance is along Y axis between probe and aligned node.
                float meters = Mathf.Abs(probe.y - ax.Y);
                int units = Mathf.RoundToInt(meters / stepSize);
                var midTile = new Vector2(ax.X, (probe.y + ax.Y) * 0.5f);
                DrawGuideLabel(cam, midTile, $"{units}u", bg);
            }
            if (_guideAlignedNodeForY is Point2 ay)
            {
                float meters = Mathf.Abs(probe.x - ay.X);
                int units = Mathf.RoundToInt(meters / stepSize);
                var midTile = new Vector2((probe.x + ay.X) * 0.5f, ay.Y);
                DrawGuideLabel(cam, midTile, $"{units}u", bg);
            }
        }
        private GUIStyle? _alignLabelStyle;

        /// <summary>While drawing a road, show the current length (in meters) at the midpoint
        /// of the preview line.</summary>
        private void DrawRoadLengthLabel()
        {
            var placement = GetComponent<PlacementController>();
            if (placement == null || placement.CurrentMode != PlacementController.Mode.PlaceRoad) return;
            if (!placement.RoadPreviewStart.HasValue || !placement.RoadPreviewEnd.HasValue) return;
            var cam = Camera.main;
            if (cam == null) return;

            var s = placement.RoadPreviewStart.Value;
            var e = placement.RoadPreviewEnd.Value;
            float meters = Vector2.Distance(s, e);
            if (meters < 0.5f) return;
            var mid = (s + e) * 0.5f;

            // atan2 returns the math angle (CCW from +X). Convert to a compass bearing
            // (CW from +Z which is "north" in our world after the +90° X grid rotation):
            //   bearing = (90 - mathAngle) mod 360.
            float mathAngleDeg = Mathf.Atan2(e.y - s.y, e.x - s.x) * Mathf.Rad2Deg;
            float bearing = (90f - mathAngleDeg) % 360f;
            if (bearing < 0) bearing += 360f;

            if (_roadLenStyle == null)
            {
                _roadLenStyle = new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
                _roadLenStyle.normal.textColor = new Color(0.95f, 0.90f, 0.45f);
            }
            int units = Mathf.RoundToInt(meters / placement.GridSnapStep);
            DrawGuideLabel(cam, mid, $"{units}u  bearing {bearing:F0}°", _roadLenStyle, wider: true);
        }
        private GUIStyle? _roadLenStyle;

        private static void DrawGuideLabel(Camera cam, Vector2 tilePos, string text, GUIStyle style,
                                           bool wider = false)
        {
            // Tilemap is on world XZ ground plane (Y=0). Tile (X, Y) → world (X, 0, Y).
            var screen = cam.WorldToScreenPoint(new Vector3(tilePos.x, 0f, tilePos.y));
            if (screen.z < 0) return;  // behind the camera
            float w = wider ? 170f : 56f, h = 22f;
            // IMGUI Y is top-down; flip from screen-space Y (bottom-up).
            var rect = new Rect(screen.x - w * 0.5f, Screen.height - screen.y - h * 0.5f, w, h);
            GUI.Box(rect, text, style);
        }

        private void DrawHoverTooltip()
        {
            if (_selectedStructureId.HasValue) return;  // detail panel is showing
            if (!_lastHoveredStructureId.HasValue) return;
            if (!_bootstrap.Sim!.State.City.Structures.TryGetValue(_lastHoveredStructureId.Value, out var s)) return;
            if (Mouse.current is null) return;

            var mp = Mouse.current.position.ReadValue();
            float guiX = mp.x + 16f;
            float guiY = Screen.height - mp.y + 16f;
            var rect = new Rect(guiX, guiY, 240f, 78f);
            if (rect.x + rect.width > Screen.width) rect.x = Screen.width - rect.width - 4;
            if (rect.y + rect.height > Screen.height) rect.y = Screen.height - rect.height - 4;

            var placement = GetComponent<PlacementController>();
            bool inConnect = placement != null
                          && placement.CurrentMode == PlacementController.Mode.Connect;

            GUI.Box(rect, $"{s.Type} #{s.Id}");
            GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 20, rect.width - 16, rect.height - 24));

            if (inConnect)
            {
                DrawConnectHint(s, placement!);
            }
            else
            {
                string status = s.Inactive ? "INACTIVE"
                              : s.UnderConstruction ? "building"
                              : s.Operational ? "active" : "?";
                GUILayout.Label($"Status: {status}");
                if (s.Category == StructureCategory.Residential)
                {
                    int residents = CountResidents(_bootstrap.Sim!.State, s.Id);
                    GUILayout.Label($"Residents: {residents}/{s.ResidentialCapacity}");
                }
                else
                {
                    GUILayout.Label($"Jobs: {s.EmployeeIds.Count}/{s.JobSlotsTotal()}");
                }
            }
            GUILayout.EndArea();
        }

        private void DrawConnectHint(Structure hovered, PlacementController placement)
        {
            bool isDistributor = hovered.Type == StructureType.ElectricityDistribution
                              || hovered.Type == StructureType.WaterDistribution;
            if (!isDistributor)
            {
                GUILayout.Label("Not a distributor.");
                GUILayout.Label("Click an ElectricityDistribution or WaterDistribution.");
                return;
            }

            // No source picked yet → this would be the source.
            if (placement.ConnectSourceId is null)
            {
                GUILayout.Label("Click to start connection");
                return;
            }

            // Source picked → check kind/type compatibility.
            if (!_bootstrap.Sim!.State.City.Structures.TryGetValue(
                    placement.ConnectSourceId.Value, out var src))
            {
                GUILayout.Label("Click to start connection");
                return;
            }
            if (src.Id == hovered.Id)
            {
                GUILayout.Label("Same as source — pick a different distributor.");
                return;
            }
            if (src.Type != hovered.Type)
            {
                GUILayout.Label($"Type mismatch: source is {src.Type}");
                GUILayout.Label("Cancel (Esc) or hover a matching distributor.");
                return;
            }
            GUILayout.Label($"Click to connect to #{src.Id}");
        }

        private static int CountResidents(SimState state, long structureId)
        {
            int n = 0;
            foreach (var a in state.City.Agents.Values)
                if (a.ResidenceStructureId == structureId) n++;
            return n;
        }

        // ===== Setup =====

        private void BuildSpriteAsset()
        {
            // 16×16 sprite with a 1-pixel darker border so each tile has a visible edge.
            // Tinting via tile.color still works — the border becomes a darker shade of
            // whatever color the tile is set to.
            const int size = 16;
            _whiteTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            _whiteTex.filterMode = FilterMode.Point;
            var pixels = new Color32[size * size];
            var inner = new Color32(255, 255, 255, 255);
            var border = new Color32(70, 70, 70, 255);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool isBorder = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                pixels[y * size + x] = isBorder ? border : inner;
            }
            _whiteTex.SetPixels32(pixels);
            _whiteTex.Apply();
            // pixelsPerUnit = size so the 16×16 sprite spans exactly 1 world unit (1 tile).
            _whiteSprite = Sprite.Create(_whiteTex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            _whiteSprite.name = "WhiteTile";
        }

        private void BuildTilemaps()
        {
            // Parent grid, rotated so the tilemap's XY plane becomes the world XZ ground plane.
            // After rotation: tile (cx, cy) renders at world (cx, 0, cy). Sprites face +Y (up),
            // so the camera looks down at them.
            var gridGo = new GameObject("SimGrid");
            gridGo.transform.SetParent(transform, worldPositionStays: false);
            gridGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _grid = gridGo.AddComponent<Grid>();
            _grid.cellSize = new Vector3(1f, 1f, 0f);

            // Container for network-edge LineRenderers. Parented to the rotated grid so the
            // lines lay flat on the ground plane along with the rest of the visuals.
            _edgeContainer = new GameObject("NetworkEdges");
            _edgeContainer.transform.SetParent(gridGo.transform, worldPositionStays: false);
            _edgeMaterial = new Material(Shader.Find("Sprites/Default"));

            _roadContainer = new GameObject("Roads");
            _roadContainer.transform.SetParent(gridGo.transform, worldPositionStays: false);
            // Graph-mode rendering: unlit lines + circle discs. No reliance on lighting.
            _roadLineMaterial = MakeVertexColoredMaterial(RoadEdgeColor);
            _nodeMaterial = MakeVertexColoredMaterial(RoadNodeColor);
            _ghostNodeMaterial = MakeVertexColoredMaterial(RoadGhostNodeColor);
            _roadPreviewMaterial = MakeVertexColoredMaterial(RoadPreviewColor);
            _guideMaterial = MakeVertexColoredMaterial(GuideColor);
            _cursorGridMaterial = MakeVertexColoredMaterial(Color.white);
            // Corridors use vertex colors per edge (white-tinted material so vertex color wins).
            _corridorMaterial = MakeVertexColoredMaterial(Color.white);
            _structureMaterial = MakeVertexColoredMaterial(Color.white);
            _structureContainer = new GameObject("Structures3D");
            _structureContainer.transform.SetParent(gridGo.transform, worldPositionStays: false);
            _nodeMesh = BuildCircleMesh(radius: 1.0f, sides: 24);

            _backgroundTilemap = MakeTilemap(gridGo.transform, "Background", sortingOrder: -10);
            _zoneTilemap = MakeTilemap(gridGo.transform, "Zones", sortingOrder: 0);
            _structureTilemap = MakeTilemap(gridGo.transform, "Structures", sortingOrder: 1);
            _landValueTilemap = MakeTilemap(gridGo.transform, "LandValue", sortingOrder: 2);
            _utilityTilemap = MakeTilemap(gridGo.transform, "Utility", sortingOrder: 3);
            _hoverTilemap = MakeTilemap(gridGo.transform, "Hover", sortingOrder: 4);
        }

        private void BuildGridTile()
        {
            // 32×32 texture with a 1-pixel border: at typical zooms the border is sub-pixel
            // and renders as a thin hairline. Low alpha keeps it from dominating.
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            var pixels = new Color32[size * size];
            var border = new Color32(60, 65, 80, 90);
            var transparent = new Color32(0, 0, 0, 0);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool isBorder = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                pixels[y * size + x] = isBorder ? border : transparent;
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            // pixelsPerUnit = size so the sprite still spans one world unit.
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            sprite.name = "GridTile";
            _gridTile = ScriptableObject.CreateInstance<Tile>();
            _gridTile.sprite = sprite;
            _gridTile.color = Color.white;
        }

        private void PaintGridBackground()
        {
            int n = SimTilemap.MapSize;
            var bounds = new BoundsInt(0, 0, 0, n, n, 1);
            var tiles = new TileBase[n * n];
            for (int i = 0; i < tiles.Length; i++) tiles[i] = _gridTile;
            _backgroundTilemap.SetTilesBlock(bounds, tiles);
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
            // Perspective camera for real 3D feel: distant tiles get smaller (vanishing point).
            cam.orthographic = false;
            cam.fieldOfView = CameraFov;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 600f;

            // Isometric-ish: pitch down + yaw 45°, looking down at the ground (Y=0).
            cam.transform.rotation = Quaternion.Euler(CameraPitchDeg, CameraYawDeg, 0f);
            // Aim at the middle of the 256-tile map; closer distance so the grid is visible.
            float mid = SimTilemap.MapSize / 2f;
            var target = new Vector3(mid, 0f, mid);
            cam.transform.position = target - cam.transform.forward * CameraInitialDistance;
            Debug.Log($"[SimVisualizer] Camera (perspective) pitch={CameraPitchDeg}° yaw={CameraYawDeg}° fov={CameraFov}, position={cam.transform.position}, rotation={cam.transform.rotation.eulerAngles}");
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
            cam.clearFlags = CameraClearFlags.SolidColor;
        }

        // WASD/arrows pan, scroll zoom. Speed scales with zoom so it stays usable at any scale.
        private void HandleCameraControl()
        {
            var cam = Camera.main;
            if (cam == null) return;

            // Build pan axes parallel to the ground plane: right is purely horizontal already
            // (no pitch in the right vector); forward needs its Y zeroed out so panning doesn't
            // change camera altitude.
            Vector3 panRight = cam.transform.right;
            Vector3 panForward = cam.transform.forward;
            panForward.y = 0;
            if (panForward.sqrMagnitude > 1e-6f) panForward.Normalize();

            // World units per screen pixel at the ground plane — used to size both keyboard
            // pan and mouse-drag pan so feel is consistent under any zoom.
            float groundDistance = GroundDistance(cam);
            float worldPerPixel = (2f * groundDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad))
                                  / Screen.height;

            if (Keyboard.current is not null)
            {
                Vector2 input = Vector2.zero;
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) input.x -= 1f;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) input.x += 1f;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) input.y -= 1f;
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) input.y += 1f;
                if (input != Vector2.zero)
                {
                    Vector3 worldMove = input.x * panRight + input.y * panForward;
                    float panSpeed = groundDistance * 2.5f;  // larger view → faster pan, feels stable
                    cam.transform.position += worldMove.normalized * Time.deltaTime * panSpeed;
                }

                // Orbit: Q/E yaw, R/F pitch. Camera revolves around the ground point it's
                // currently looking at, so the user's focus stays anchored.
                float yawDelta = 0f, pitchDelta = 0f;
                if (Keyboard.current.qKey.isPressed) yawDelta -= 1f;
                if (Keyboard.current.eKey.isPressed) yawDelta += 1f;
                if (Keyboard.current.rKey.isPressed) pitchDelta -= 1f;
                if (Keyboard.current.fKey.isPressed) pitchDelta += 1f;
                if (yawDelta != 0f || pitchDelta != 0f)
                {
                    float orbitSpeed = 60f;  // deg/sec
                    OrbitCamera(cam, yawDelta * orbitSpeed * Time.deltaTime,
                                     pitchDelta * orbitSpeed * Time.deltaTime);
                }
            }

            if (Mouse.current is not null)
            {
                float scroll = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    // Dolly along forward, capped per-frame so a wheel burst doesn't fly the
                    // camera past min/max distance. Magnitude is fraction-of-distance — 40%
                    // closer/further per cap, gives strong zoom without being uncontrollable.
                    float dollyDelta = Mathf.Clamp(scroll * 0.12f, -0.4f, 0.4f);
                    var newPos = cam.transform.position + cam.transform.forward * dollyDelta * groundDistance;
                    if (Mathf.Abs(cam.transform.forward.y) > 1e-5f)
                    {
                        float newDistance = -newPos.y / cam.transform.forward.y;
                        if (newDistance >= CameraMinDistanceToGround && newDistance <= CameraMaxDistanceToGround)
                            cam.transform.position = newPos;
                    }
                }

                bool shiftHeld = Keyboard.current is not null && Keyboard.current.shiftKey.isPressed;
                bool panDragging = Mouse.current.rightButton.isPressed
                                   || (Mouse.current.middleButton.isPressed && shiftHeld);
                bool orbitDragging = Mouse.current.middleButton.isPressed && !shiftHeld;

                if (panDragging)
                {
                    // Base motion is pixel-exact ground-raycast tracking. PanDragMultiplier
                    // amplifies it slightly so short drags cover more world.
                    var delta = Mouse.current.delta.ReadValue();
                    if (delta.sqrMagnitude > 0)
                    {
                        var now = Mouse.current.position.ReadValue();
                        var before = now - delta;
                        if (TryRaycastGround(cam, before, out var wOld)
                            && TryRaycastGround(cam, now, out var wNew))
                        {
                            var move = (wOld - wNew) * PanDragMultiplier;
                            move.y = 0;
                            cam.transform.position += move;
                        }
                    }
                }
                else if (orbitDragging)
                {
                    var delta = Mouse.current.delta.ReadValue();
                    const float orbitSensitivity = 0.25f;
                    float yawDelta = delta.x * orbitSensitivity;
                    float pitchDelta = -delta.y * orbitSensitivity;
                    if (yawDelta != 0f || pitchDelta != 0f)
                        OrbitCamera(cam, yawDelta, pitchDelta);
                }
            }
        }

        // Ray-cast a screen point onto the Y=0 ground plane. Returns true and the hit world
        // point, or false if the ray misses (parallel to plane or pointing wrong direction).
        private static bool TryRaycastGround(Camera cam, Vector2 screen, out Vector3 hit)
        {
            hit = default;
            var ray = cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0));
            if (Mathf.Abs(ray.direction.y) < 1e-5f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0) return false;
            hit = ray.origin + ray.direction * t;
            return true;
        }

        // Distance from the camera to the ground plane (Y=0) measured along its forward axis.
        // Returns CameraInitialDistance when forward is parallel to the ground (shouldn't happen).
        private static float GroundDistance(Camera cam)
        {
            float fy = cam.transform.forward.y;
            if (Mathf.Abs(fy) < 1e-5f) return CameraInitialDistance;
            return Mathf.Max(1f, -cam.transform.position.y / fy);
        }

        // Rotate the camera around the point on the ground it's currently looking at. Yaw is
        // applied around world Y, pitch around the camera's local right axis. Pitch is clamped
        // so the camera never goes below horizontal or fully overhead.
        private const float MinPitchDeg = 15f;
        private const float MaxPitchDeg = 80f;
        private static void OrbitCamera(Camera cam, float yawDeltaDeg, float pitchDeltaDeg)
        {
            // Find current ground target along forward axis.
            float fy = cam.transform.forward.y;
            if (Mathf.Abs(fy) < 1e-5f) return;
            float t = -cam.transform.position.y / fy;
            if (t < 0) return;
            Vector3 target = cam.transform.position + cam.transform.forward * t;

            // Clamp pitch delta so the resulting pitch stays in [MinPitchDeg, MaxPitchDeg].
            // forward.y = -sin(pitch), so pitch = asin(-forward.y).
            if (pitchDeltaDeg != 0f)
            {
                float currentPitch = Mathf.Asin(Mathf.Clamp(-fy, -1f, 1f)) * Mathf.Rad2Deg;
                float newPitch = Mathf.Clamp(currentPitch + pitchDeltaDeg, MinPitchDeg, MaxPitchDeg);
                pitchDeltaDeg = newPitch - currentPitch;
            }

            Vector3 offset = cam.transform.position - target;

            // Yaw: rotate around world up.
            if (yawDeltaDeg != 0f)
            {
                var yawRot = Quaternion.AngleAxis(yawDeltaDeg, Vector3.up);
                offset = yawRot * offset;
                cam.transform.rotation = yawRot * cam.transform.rotation;
            }

            // Pitch: rotate around the camera's local right axis (now updated by yaw).
            if (pitchDeltaDeg != 0f)
            {
                var pitchRot = Quaternion.AngleAxis(pitchDeltaDeg, cam.transform.right);
                offset = pitchRot * offset;
                cam.transform.rotation = pitchRot * cam.transform.rotation;
            }

            cam.transform.position = target + offset;
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

        private void PaintLandValue(SimTilemap tm)
        {
            // Clear previous paint.
            foreach (var cell in _paintedLvCells)
                _landValueTilemap.SetTile(cell, null);
            _paintedLvCells.Clear();

            if (!ShowLandValue) return;
            if (tm.MaxLandValue <= 0) return;

            int n = SimTilemap.MapSize;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                double lv = tm.LandValueAt(x, y);
                if (lv <= 0) continue;
                float t = (float)(lv / tm.MaxLandValue);  // 0..1
                var color = HeatmapColor(t);
                var tile = TileFor(color);
                var cell = new Vector3Int(x, y, 0);
                _landValueTilemap.SetTile(cell, tile);
                _paintedLvCells.Add(cell);
            }
        }

        // Cool-to-hot gradient: low LV blue → green → yellow → red high LV. Alpha is semi-
        // transparent so the underlying zone/structure colors stay visible.
        private static Color HeatmapColor(float t)
        {
            t = Mathf.Clamp01(t);
            Color c;
            if (t < 0.5f)
            {
                float k = t * 2f;
                c = Color.Lerp(new Color(0.20f, 0.40f, 0.85f, 1f),  // blue
                               new Color(0.30f, 0.85f, 0.40f, 1f),  // green
                               k);
            }
            else
            {
                float k = (t - 0.5f) * 2f;
                c = Color.Lerp(new Color(0.30f, 0.85f, 0.40f, 1f),  // green
                               new Color(0.95f, 0.30f, 0.30f, 1f),  // red
                               k);
            }
            c.a = 0.55f;
            return c;
        }

        // Hash that changes whenever a structure's served-state flips. Cheap diffing trigger.
        private static int ComputeUtilitySignature(SimState state)
        {
            int sig = 17;
            foreach (var s in state.City.Structures.Values)
            {
                int bits = (s.IsPowered ? 1 : 0) | (s.IsWatered ? 2 : 0);
                sig = unchecked(sig * 31 + (int)s.Id * 4 + bits);
            }
            return sig;
        }

        private void PaintUtilityCoverage(SimState state)
        {
            foreach (var c in _paintedUtilityCells) _utilityTilemap.SetTile(c, null);
            _paintedUtilityCells.Clear();

            if (!ShowUtilityCoverage) return;

            // Color code: red = unpowered + unwatered, orange = unwatered only,
            // purple = unpowered only. Served structures get no overlay (clean).
            var noPowerNoWater = new Color(0.95f, 0.30f, 0.30f, 0.55f);
            var noPower = new Color(0.65f, 0.35f, 0.90f, 0.55f);
            var noWater = new Color(0.95f, 0.65f, 0.30f, 0.55f);

            foreach (var s in state.City.Structures.Values)
            {
                if (s.X < 0 || s.Y < 0) continue;
                // Producers (Generator/Well) self-serve — skip them. Distributors are
                // included so the player can see if a power-line / water-pipe structure is
                // unpowered (a clear hint that the network isn't connected upstream).
                if (s.Type == StructureType.Generator || s.Type == StructureType.Well) continue;
                bool needPower = !s.IsPowered;
                bool needWater = !s.IsWatered;
                if (!needPower && !needWater) continue;

                Color color = (needPower && needWater) ? noPowerNoWater
                            : needPower ? noPower
                            : noWater;
                var tile = TileFor(color);
                var (w, h) = Footprint.For(s.Type);
                for (int dy = 0; dy < h; dy++)
                for (int dx = 0; dx < w; dx++)
                {
                    var cell = new Vector3Int(s.X + dx, s.Y + dy, 0);
                    _utilityTilemap.SetTile(cell, tile);
                    _paintedUtilityCells.Add(cell);
                }
            }
        }

        private static bool IsUtilitySource(StructureType t) =>
            t == StructureType.Generator || t == StructureType.Well
            || t == StructureType.ElectricityDistribution || t == StructureType.WaterDistribution;

        private static Material MakeVertexColoredMaterial(Color tint)
        {
            var mat = new Material(Shader.Find("Sprites/Default"));
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
            return mat;
        }

        /// <summary>Structural sync only — creates/destroys edge LineRenderers and node
        /// disc GameObjects when the graph's entity counts change. Positions are refreshed
        /// every frame in UpdateRoadGraphPositions.</summary>
        private void SyncRoadGraphRenderers(SimState state)
        {
            // Edges
            var staleEdges = new List<long>();
            foreach (var kv in _roadEdgeRenderers)
                if (!state.RoadEdges.ContainsKey(kv.Key)) staleEdges.Add(kv.Key);
            foreach (var id in staleEdges)
            {
                if (_roadEdgeRenderers[id] != null) Destroy(_roadEdgeRenderers[id].gameObject);
                _roadEdgeRenderers.Remove(id);
            }
            foreach (var edge in state.RoadEdges.Values)
            {
                if (_roadEdgeRenderers.ContainsKey(edge.Id)) continue;
                var go = new GameObject($"RoadEdge#{edge.Id}");
                go.transform.SetParent(_roadContainer.transform, worldPositionStays: false);
                var lr = go.AddComponent<LineRenderer>();
                lr.material = _roadLineMaterial;
                lr.positionCount = 2;
                lr.startWidth = 0.4f;
                lr.endWidth = 0.4f;
                lr.useWorldSpace = false;
                lr.numCapVertices = 2;
                lr.startColor = RoadEdgeColor;
                lr.endColor = RoadEdgeColor;
                _roadEdgeRenderers[edge.Id] = lr;
            }

            // Nodes
            var staleNodes = new List<long>();
            foreach (var kv in _nodeObjects)
                if (!state.RoadNodes.ContainsKey(kv.Key)) staleNodes.Add(kv.Key);
            foreach (var id in staleNodes)
            {
                if (_nodeObjects[id] != null) Destroy(_nodeObjects[id]);
                _nodeObjects.Remove(id);
            }
            foreach (var node in state.RoadNodes.Values)
            {
                if (_nodeObjects.ContainsKey(node.Id)) continue;
                var go = new GameObject($"RoadNode#{node.Id}");
                go.transform.SetParent(_roadContainer.transform, worldPositionStays: false);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = _nodeMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.material = new Material(_nodeMaterial);  // per-node material for tint changes
                mr.receiveShadows = false;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _nodeObjects[node.Id] = go;
            }
        }

        /// <summary>Refresh node + edge positions from the sim every frame. Cheap (O(N+E))
        /// and necessary so dragging a node visibly drags its connected edges with it.</summary>
        private void UpdateRoadGraphPositions(SimState state)
        {
            foreach (var node in state.RoadNodes.Values)
            {
                if (!_nodeObjects.TryGetValue(node.Id, out var go)) continue;
                go.transform.localPosition = new Vector3(node.Position.X, node.Position.Y, -0.1f);

                // Tint per node based on hover/drag state.
                var mr = go.GetComponent<MeshRenderer>();
                var col = node.Id == _draggingNodeId ? RoadNodeDragColor
                        : node.Id == _hoveredNodeId  ? RoadNodeHoverColor
                        : RoadNodeColor;
                if (mr.material.HasProperty("_Color")) mr.material.SetColor("_Color", col);
            }
            foreach (var edge in state.RoadEdges.Values)
            {
                if (!_roadEdgeRenderers.TryGetValue(edge.Id, out var lr)) continue;
                if (!state.RoadNodes.TryGetValue(edge.FromNodeId, out var fromN)) continue;
                if (!state.RoadNodes.TryGetValue(edge.ToNodeId, out var toN)) continue;
                lr.SetPosition(0, new Vector3(fromN.Position.X, fromN.Position.Y, -0.05f));
                lr.SetPosition(1, new Vector3(toN.Position.X, toN.Position.Y, -0.05f));
            }
        }

        /// <summary>Build / refresh / tear down corridor GameObjects so every road edge has
        /// a faint translucent rectangle painted on the ground showing its buildable strip
        /// (within CorridorHalfDepthTiles tiles perpendicular to the edge on each side).</summary>
        private void UpdateCorridorMeshes(SimState state)
        {
            var stale = new List<long>();
            foreach (var kv in _corridorObjects)
                if (!state.RoadEdges.ContainsKey(kv.Key)) stale.Add(kv.Key);
            foreach (var id in stale)
            {
                if (_corridorObjects[id] != null) Destroy(_corridorObjects[id]);
                _corridorObjects.Remove(id);
            }
            if (state.RoadEdges.Count == 0) return;

            // Precompute the endpoint tuples for cull queries — each edge gets the list of
            // all OTHER edges as its competitors.
            var allTuples = new List<(float fx, float fy, float tx, float ty)>(state.RoadEdges.Count);
            var edgeIds = new List<long>(state.RoadEdges.Count);
            foreach (var e in state.RoadEdges.Values)
            {
                if (!state.RoadNodes.TryGetValue(e.FromNodeId, out var f)) continue;
                if (!state.RoadNodes.TryGetValue(e.ToNodeId, out var t)) continue;
                allTuples.Add((f.Position.X, f.Position.Y, t.Position.X, t.Position.Y));
                edgeIds.Add(e.Id);
            }

            // Cell size = the player's current snap step (so corridor cells line up with the
            // placement grid). Falls back to 5m when no PlacementController is present.
            var placement = GetComponent<PlacementController>();
            float stepSize = Mathf.Max(1f, placement?.GridSnapStep ?? 5f);
            float lineWidth = Mathf.Max(0.05f, stepSize * 0.04f);

            for (int idx = 0; idx < edgeIds.Count; idx++)
            {
                var edgeId = edgeIds[idx];
                if (!state.RoadEdges.TryGetValue(edgeId, out var edge)) continue;
                if (!state.RoadNodes.TryGetValue(edge.FromNodeId, out var fromN)) continue;
                if (!state.RoadNodes.TryGetValue(edge.ToNodeId, out var toN)) continue;

                if (!_corridorObjects.TryGetValue(edge.Id, out var go))
                {
                    go = new GameObject($"Corridor#{edge.Id}");
                    go.transform.SetParent(_roadContainer.transform, worldPositionStays: false);
                    go.AddComponent<MeshFilter>();
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = _corridorMaterial;
                    mr.receiveShadows = false;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _corridorObjects[edge.Id] = go;
                }

                // Build per-edge "others" list (exclude self).
                var others = new List<(float, float, float, float)>(allTuples.Count - 1);
                for (int k = 0; k < allTuples.Count; k++)
                    if (k != idx) others.Add(allTuples[k]);

                var mf = go.GetComponent<MeshFilter>();
                // Reuse the same Mesh across frames so we don't leak GPU resource ids.
                float setback = edge.WidthTiles * 0.5f;
                float halfDepth = setback + CorridorDepthCells * stepSize;
                // Dashed back-guide marks where a 10u-deep structure's body would reach.
                float backGuide = setback + StructureMaxDepthMeters;
                mf.sharedMesh = ProceduralMesh.BuildCorridorGrid(
                    fromN.Position.X, fromN.Position.Y, toN.Position.X, toN.Position.Y,
                    halfDepth: halfDepth,
                    stepSize: stepSize,
                    lineWidth: lineWidth,
                    otherEdges: others,
                    color: CorridorColor,
                    setback: setback,
                    backGuideDistance: backGuide,
                    reuseMesh: mf.sharedMesh);
            }
        }

        /// <summary>One 3D lot mesh per placed structure. Adds new objects, removes stale ones,
        /// refreshes the rotation each frame (cheap) so structures re-orient when roads change.</summary>
        /// <summary>Build / refresh / tear down per-edge zone overlay meshes. Group zoned
        /// cells by edge id, then ensure each edge with cells has a GameObject whose mesh
        /// fills those cells with the residential zone color.</summary>
        private void UpdateZoneOverlays(SimState state)
        {
            var placement = GetComponent<PlacementController>();
            float stepSize = Mathf.Max(1f, placement?.GridSnapStep ?? 5f);

            // Group zoned cells by edge id.
            var byEdge = new Dictionary<long, List<(int alongCell, int side)>>();
            foreach (var (edgeId, alongCell, side) in state.City.ZonedResidentialCells)
            {
                if (!byEdge.TryGetValue(edgeId, out var list))
                {
                    list = new List<(int, int)>();
                    byEdge[edgeId] = list;
                }
                list.Add((alongCell, side));
            }

            // Drop overlay objects for edges that no longer have zoned cells (or whose
            // edge was deleted).
            var stale = new List<long>();
            foreach (var kv in _zoneOverlayObjects)
                if (!byEdge.ContainsKey(kv.Key) || !state.RoadEdges.ContainsKey(kv.Key))
                    stale.Add(kv.Key);
            foreach (var id in stale)
            {
                if (_zoneOverlayObjects[id] != null) Destroy(_zoneOverlayObjects[id]);
                _zoneOverlayObjects.Remove(id);
            }

            // Build / refresh per-edge overlay meshes.
            foreach (var (edgeId, cells) in byEdge)
            {
                if (!state.RoadEdges.TryGetValue(edgeId, out var edge)) continue;
                if (!state.RoadNodes.TryGetValue(edge.FromNodeId, out var fn)) continue;
                if (!state.RoadNodes.TryGetValue(edge.ToNodeId, out var tn)) continue;

                if (!_zoneOverlayObjects.TryGetValue(edgeId, out var go))
                {
                    go = new GameObject($"ZoneOverlay#{edgeId}");
                    go.transform.SetParent(_roadContainer.transform, worldPositionStays: false);
                    go.AddComponent<MeshFilter>();
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = _corridorMaterial;  // same vertex-colored material
                    mr.receiveShadows = false;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _zoneOverlayObjects[edgeId] = go;
                }

                var mf = go.GetComponent<MeshFilter>();
                float setback = edge.WidthTiles * 0.5f;
                mf.sharedMesh = ProceduralMesh.BuildZonedCellsOverlay(
                    fn.Position.X, fn.Position.Y, tn.Position.X, tn.Position.Y,
                    stepSize, setback, cells, ResidentialZoneColor,
                    reuseMesh: mf.sharedMesh);
            }
        }

        private void UpdateStructureMeshes(SimState state)
        {
            if (_structureContainer == null || _structureMaterial == null) return;

            // Cull stale (structure removed).
            var stale = new List<long>();
            foreach (var kv in _structureObjects)
                if (!state.City.Structures.ContainsKey(kv.Key)) stale.Add(kv.Key);
            foreach (var id in stale)
            {
                if (_structureObjects[id] != null) Destroy(_structureObjects[id]);
                _structureObjects.Remove(id);
            }

            foreach (var s in state.City.Structures.Values)
            {
                if (s.X < 0 || s.Y < 0) continue;
                var (w, h) = Footprint.For(s.Type);

                if (!_structureObjects.TryGetValue(s.Id, out var go))
                {
                    go = new GameObject($"Structure#{s.Id}");
                    go.transform.SetParent(_structureContainer.transform, worldPositionStays: false);
                    var mf = go.AddComponent<MeshFilter>();
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = _structureMaterial;
                    mr.receiveShadows = false;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                    // Lot is 4u × 4u; building is 2u × 2u × ~1u tall, centered. Sizes in meters
                    // since 1 tile = 1 meter in the sim.
                    float lotM = w;            // tiles → m (1:1)
                    float bldgM = lotM * 0.5f; // half the lot width
                    float bldgH = bldgM * 0.5f;
                    mf.sharedMesh = ProceduralMesh.BuildStructureLot(
                        lotWidthM: lotM, lotDepthM: h,
                        bldgWidthM: bldgM, bldgDepthM: bldgM, bldgHeightM: bldgH,
                        lotColor: LotColor,
                        bldgColor: StructureColor(s),
                        markerColor: MarkerColor);

                    _structureObjects[s.Id] = go;
                }

                // Position: center of the footprint in sim-tile coords.
                float cx = s.X + w * 0.5f;
                float cy = s.Y + h * 0.5f;
                go.transform.localPosition = new Vector3(cx, cy, 0f);

                // Orient: prefer the stored RotationDegrees (set during corridor-snap
                // placement). Fall back to nearest-road for legacy / sim-auto-spawned
                // structures that don't have rotation baked in yet.
                float angleDeg;
                if (s.PlacementEdgeId.HasValue
                    && state.RoadEdges.ContainsKey(s.PlacementEdgeId.Value))
                {
                    angleDeg = s.RotationDegrees;
                }
                else
                {
                    var dir = NearestRoadDirection(state, cx, cy);
                    angleDeg = dir.HasValue
                        ? Mathf.Atan2(dir.Value.dx, dir.Value.dy) * Mathf.Rad2Deg
                        : 0f;
                }
                go.transform.localRotation = Quaternion.Euler(0f, 0f, angleDeg);
            }
        }

        /// <summary>Direction (in tile-space dx, dy) from (sx, sy) toward the closest point on
        /// any road edge. Returns null if there are no edges.</summary>
        private static (float dx, float dy)? NearestRoadDirection(SimState state, float sx, float sy)
        {
            float bestD2 = float.MaxValue;
            (float dx, float dy) best = (0, 0);
            bool any = false;
            foreach (var e in state.RoadEdges.Values)
            {
                if (!state.RoadNodes.TryGetValue(e.FromNodeId, out var f)) continue;
                if (!state.RoadNodes.TryGetValue(e.ToNodeId, out var t)) continue;
                var (cx, cy) = ClosestPointOnSegment(
                    f.Position.X, f.Position.Y, t.Position.X, t.Position.Y, sx, sy);
                float dx = cx - sx, dy = cy - sy;
                float d2 = dx * dx + dy * dy;
                if (d2 < bestD2) { bestD2 = d2; best = (dx, dy); any = true; }
            }
            if (!any || bestD2 < 1e-6f) return null;
            return best;
        }

        private static (float, float) ClosestPointOnSegment(float ax, float ay, float bx, float by,
                                                            float px, float py)
        {
            float dx = bx - ax, dy = by - ay;
            float len2 = dx * dx + dy * dy;
            if (len2 < 1e-6f) return (ax, ay);
            float t = ((px - ax) * dx + (py - ay) * dy) / len2;
            t = Mathf.Clamp01(t);
            return (ax + dx * t, ay + dy * t);
        }


        /// <summary>Hover + click+drag handling for road graph nodes. Hover highlight is
        /// always active so the player sees snap targets when drawing roads. Drag is only
        /// allowed in inspect mode so it doesn't fight any active placement tool.</summary>
        private void HandleNodeInteraction()
        {
            if (_bootstrap.Sim is null) return;
            if (Mouse.current is null) return;

            var placement = GetComponent<PlacementController>();
            // Ignore mouse over the UI sidebar / top bar.
            var mp = Mouse.current.position.ReadValue();
            if (mp.x < SidebarWidthPx || mp.y > Screen.height - TopBarHeightPx)
            {
                _hoveredNodeId = null;
                _draggingNodeId = null;
                return;
            }

            var groundPt = MouseGroundPoint();
            if (groundPt is null) { _hoveredNodeId = null; return; }
            var gp = groundPt.Value;
            var state = _bootstrap.Sim.State;

            // Drag only valid in inspect mode (otherwise PlaceRoad / other tools would fight).
            bool dragAllowed = placement == null || !placement.IsActive;

            if (dragAllowed && _draggingNodeId.HasValue)
            {
                if (Mouse.current.leftButton.isPressed)
                {
                    bool altHeld = Keyboard.current is not null && Keyboard.current.altKey.isPressed;
                    bool gridSnap = (placement?.GridSnapEnabled ?? true) ^ altHeld;
                    bool alignSnap = placement?.AlignmentGuidesEnabled ?? true;
                    float x = gp.x, y = gp.y;
                    if (gridSnap)
                    {
                        float step = Mathf.Max(1f, placement?.GridSnapStep ?? 1f);
                        x = Mathf.Round(x / step) * step;
                        y = Mathf.Round(y / step) * step;
                    }
                    else if (alignSnap)
                    {
                        long me = _draggingNodeId.Value;
                        float bestXErr = AlignToleranceTiles, bestYErr = AlignToleranceTiles;
                        foreach (var n in state.RoadNodes.Values)
                        {
                            if (n.Id == me) continue;
                            float dx = Mathf.Abs(n.Position.X - x);
                            float dy = Mathf.Abs(n.Position.Y - y);
                            if (dx < bestXErr) { bestXErr = dx; x = n.Position.X; }
                            if (dy < bestYErr) { bestYErr = dy; y = n.Position.Y; }
                        }
                    }
                    try { _bootstrap.Sim.MoveRoadNode(_draggingNodeId.Value, new Point2(x, y)); }
                    catch { /* swallow: out-of-bounds etc. */ }
                }
                else
                {
                    _draggingNodeId = null;
                }
                return;
            }
            if (!dragAllowed) _draggingNodeId = null;

            // Find nearest node within hit radius for hover (active in ALL modes — players
            // need to see snap targets when drawing roads, not just in inspect mode).
            float bestSq = NodeHitRadiusTiles * NodeHitRadiusTiles;
            long? best = null;
            foreach (var n in state.RoadNodes.Values)
            {
                float dx = n.Position.X - gp.x;
                float dy = n.Position.Y - gp.y;
                float dsq = dx * dx + dy * dy;
                if (dsq <= bestSq) { bestSq = dsq; best = n.Id; }
            }
            _hoveredNodeId = best;

            // Start drag only when dragging is allowed (inspect mode).
            if (dragAllowed && best.HasValue && Mouse.current.leftButton.wasPressedThisFrame)
            {
                _draggingNodeId = best;
            }
        }

        /// <summary>Mouse cursor projected onto the Y=0 ground plane, returned in tile coords
        /// (world X → tile X, world Z → tile Y because SimGrid is +90° X rotated).</summary>
        private static Vector2? MouseGroundPoint()
        {
            var cam = Camera.main;
            if (cam == null || Mouse.current is null) return null;
            var screen = Mouse.current.position.ReadValue();
            var ray = cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0));
            if (Mathf.Abs(ray.direction.y) < 1e-5f) return null;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0) return null;
            var hit = ray.origin + ray.direction * t;
            return new Vector2(hit.x, hit.z);
        }

        private void UpdateRoadPreview()
        {
            var placement = GetComponent<PlacementController>();
            bool inMode = placement != null
                       && placement.CurrentMode == PlacementController.Mode.PlaceRoad;
            bool dragInProgress = inMode
                       && placement!.RoadPreviewStart.HasValue
                       && placement.RoadPreviewEnd.HasValue;

            // Preview line: only during an active drag.
            if (!dragInProgress)
            {
                if (_roadPreview != null) { Destroy(_roadPreview.gameObject); _roadPreview = null; }
            }
            else
            {
                if (_roadPreview == null)
                {
                    var go = new GameObject("RoadPreview");
                    go.transform.SetParent(_roadContainer.transform, worldPositionStays: false);
                    _roadPreview = go.AddComponent<LineRenderer>();
                    _roadPreview.material = _roadPreviewMaterial;
                    _roadPreview.positionCount = 2;
                    _roadPreview.startWidth = 0.4f;
                    _roadPreview.endWidth = 0.4f;
                    _roadPreview.useWorldSpace = false;
                    _roadPreview.numCapVertices = 2;
                    _roadPreview.startColor = RoadPreviewColor;
                    _roadPreview.endColor = RoadPreviewColor;
                }
                var s = placement!.RoadPreviewStart!.Value;
                var e = placement.RoadPreviewEnd!.Value;
                _roadPreview.SetPosition(0, new Vector3(s.x, s.y, -0.08f));
                _roadPreview.SetPosition(1, new Vector3(e.x, e.y, -0.08f));
            }

            // Ghost nodes: show a translucent circle wherever a NEW node would be created.
            // Hide the ghost when the position is already covered by an existing node (which
            // will get the hover-highlight color instead).
            UpdateGhostNode(ref _ghostStartNode, "RoadGhostStart",
                            inMode && placement!.RoadPreviewStart.HasValue ? placement.RoadPreviewStart.Value : (Vector2?)null);
            UpdateGhostNode(ref _ghostEndNode, "RoadGhostEnd",
                            inMode && placement!.RoadPreviewEnd.HasValue ? placement.RoadPreviewEnd.Value : (Vector2?)null);
        }

        /// <summary>Draw a 10u-radius ring around the cursor / dragged road endpoint while in
        /// PlaceRoad mode. Pure visual aid — no constraint is enforced from this.</summary>
        private void UpdateRoadNodeGuideCircle()
        {
            var placement = GetComponent<PlacementController>();
            bool inMode = placement != null
                       && placement.CurrentMode == PlacementController.Mode.PlaceRoad;

            // Center: the snapped cursor position if we have it (during drag), otherwise the
            // raw ground hit (pre-drag hover).
            Vector2? center = null;
            if (inMode)
            {
                if (placement!.RoadPreviewEnd.HasValue) center = placement.RoadPreviewEnd;
                else
                {
                    var hit = MouseToGroundFloat(Camera.main);
                    if (hit.HasValue) center = hit;
                }
            }

            if (center is null)
            {
                if (_roadNodeGuideCircle != null)
                {
                    Destroy(_roadNodeGuideCircle.gameObject);
                    _roadNodeGuideCircle = null;
                }
                return;
            }

            if (_roadNodeGuideCircle == null)
            {
                var go = new GameObject("RoadNodeGuideCircle");
                go.transform.SetParent(_roadContainer.transform, worldPositionStays: false);
                _roadNodeGuideCircle = go.AddComponent<LineRenderer>();
                _roadNodeGuideCircle.material = _guideMaterial;
                _roadNodeGuideCircle.startWidth = 0.25f;
                _roadNodeGuideCircle.endWidth = 0.25f;
                _roadNodeGuideCircle.useWorldSpace = false;
                _roadNodeGuideCircle.loop = true;
                _roadNodeGuideCircle.numCapVertices = 0;
                var dimGuide = new Color(GuideColor.r, GuideColor.g, GuideColor.b, 0.30f);
                _roadNodeGuideCircle.startColor = dimGuide;
                _roadNodeGuideCircle.endColor = dimGuide;
                const int segments = 64;
                _roadNodeGuideCircle.positionCount = segments;
            }

            // Radius = setback + corridor depth, so the ring sits exactly at the back edge of
            // the corridor that would be drawn if a default-config road went through here.
            float stepSize = Mathf.Max(1f, placement?.GridSnapStep ?? 5f);
            float radius = DefaultRoadHalfWidthMeters + CorridorDepthCells * stepSize;
            int n = _roadNodeGuideCircle.positionCount;
            float cx = center.Value.x, cy = center.Value.y;
            for (int i = 0; i < n; i++)
            {
                float a = (i / (float)n) * Mathf.PI * 2f;
                _roadNodeGuideCircle.SetPosition(i,
                    new Vector3(cx + Mathf.Cos(a) * radius, cy + Mathf.Sin(a) * radius, -0.07f));
            }
        }

        /// <summary>Raycast cursor onto Y=0 ground plane, return float (tileX, tileY) — same
        /// mapping as MouseToTile but without flooring to integer cells.</summary>
        private static Vector2? MouseToGroundFloat(Camera cam)
        {
            if (cam == null || Mouse.current is null) return null;
            var screen = Mouse.current.position.ReadValue();
            var ray = cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0));
            if (Mathf.Abs(ray.direction.y) < 1e-5f) return null;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0) return null;
            var hit = ray.origin + ray.direction * t;
            return new Vector2(hit.x, hit.z);
        }

        private void UpdateGhostNode(ref GameObject? go, string name, Vector2? worldPos)
        {
            if (worldPos is null || _bootstrap.Sim is null)
            {
                if (go != null) { Destroy(go); go = null; }
                return;
            }
            // Hide ghost if the snapped position coincides with an existing node — the user
            // already sees the hover-highlight on that node and a ghost on top is redundant.
            var p = worldPos.Value;
            float sq = (float)(Sim.NodeSnapRadiusTiles * Sim.NodeSnapRadiusTiles * 0.5f);
            foreach (var n in _bootstrap.Sim.State.RoadNodes.Values)
            {
                float dx = n.Position.X - p.x;
                float dy = n.Position.Y - p.y;
                if (dx * dx + dy * dy <= sq)
                {
                    if (go != null) { Destroy(go); go = null; }
                    return;
                }
            }
            if (go == null)
            {
                go = new GameObject(name);
                go.transform.SetParent(_roadContainer.transform, worldPositionStays: false);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = _nodeMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _ghostNodeMaterial;
                mr.receiveShadows = false;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            go.transform.localPosition = new Vector3(p.x, p.y, -0.12f);
        }

        /// <summary>drawio-style smart alignment guides. When the current "probe" point
        /// (cursor-during-road-draw, or position-of-dragged-node) shares an X or Y with any
        /// other existing node within <see cref="AlignToleranceTiles"/>, draw a thin line
        /// spanning the whole map at the matching axis.</summary>
        private void UpdateAlignmentGuides()
        {
            if (_bootstrap.Sim is null)
            {
                ClearGuides();
                return;
            }
            var placement = GetComponent<PlacementController>();
            if (placement != null && !placement.AlignmentGuidesEnabled)
            {
                ClearGuides();
                return;
            }
            bool inRoadDraw = placement != null
                            && placement.CurrentMode == PlacementController.Mode.PlaceRoad;

            Vector2? probe = null;
            long? excludeNodeId = null;
            if (inRoadDraw && placement!.RoadPreviewEnd.HasValue)
            {
                probe = placement.RoadPreviewEnd.Value;
            }
            else if (_draggingNodeId.HasValue
                  && _bootstrap.Sim.State.RoadNodes.TryGetValue(_draggingNodeId.Value, out var dn))
            {
                probe = new Vector2(dn.Position.X, dn.Position.Y);
                excludeNodeId = dn.Id;  // don't self-align
            }
            if (probe is null) { ClearGuides(); return; }
            _guideProbePos = probe;

            // Two-pass selection per axis: first collect all nodes within alignment tolerance,
            // then pick the one CLOSEST to the cursor along the perpendicular axis. That way
            // a third node placed near the cursor measures off the just-placed neighbor, not
            // the first-inserted node on that axis.
            float? alignedX = null;
            float? alignedY = null;
            Point2? alignedNodeX = null, alignedNodeY = null;
            float closestPerpY = float.MaxValue;  // for vertical guide, perpendicular = Y
            float closestPerpX = float.MaxValue;
            foreach (var node in _bootstrap.Sim.State.RoadNodes.Values)
            {
                if (excludeNodeId.HasValue && node.Id == excludeNodeId.Value) continue;
                float dx = Mathf.Abs(node.Position.X - probe.Value.x);
                float dy = Mathf.Abs(node.Position.Y - probe.Value.y);

                if (dx < AlignToleranceTiles)
                {
                    float perpY = Mathf.Abs(node.Position.Y - probe.Value.y);
                    if (perpY < closestPerpY)
                    {
                        closestPerpY = perpY;
                        alignedX = node.Position.X;
                        alignedNodeX = node.Position;
                    }
                }
                if (dy < AlignToleranceTiles)
                {
                    float perpX = Mathf.Abs(node.Position.X - probe.Value.x);
                    if (perpX < closestPerpX)
                    {
                        closestPerpX = perpX;
                        alignedY = node.Position.Y;
                        alignedNodeY = node.Position;
                    }
                }
            }
            _guideAlignedNodeForX = alignedNodeX;
            _guideAlignedNodeForY = alignedNodeY;

            int n = SimTilemap.MapSize;
            DrawOrHideGuide(ref _guideVertical, "GuideV",
                alignedX, new Vector3(0, 0, -0.15f), new Vector3(0, n, -0.15f), axisVertical: true);
            DrawOrHideGuide(ref _guideHorizontal, "GuideH",
                alignedY, new Vector3(0, 0, -0.15f), new Vector3(n, 0, -0.15f), axisVertical: false);
        }

        private void DrawOrHideGuide(ref GameObject? go, string name, float? axis,
                                     Vector3 fromTemplate, Vector3 toTemplate, bool axisVertical)
        {
            if (axis is null)
            {
                if (go != null) { Destroy(go); go = null; }
                return;
            }
            if (go == null)
            {
                go = new GameObject(name);
                go.transform.SetParent(_roadContainer.transform, worldPositionStays: false);
                var lr = go.AddComponent<LineRenderer>();
                lr.material = _guideMaterial;
                lr.positionCount = 2;
                lr.startWidth = 0.15f;
                lr.endWidth = 0.15f;
                lr.useWorldSpace = false;
                lr.numCapVertices = 0;
                lr.startColor = GuideColor;
                lr.endColor = GuideColor;
            }
            var line = go.GetComponent<LineRenderer>();
            float v = axis.Value;
            Vector3 a = axisVertical ? new Vector3(v, fromTemplate.y, fromTemplate.z)
                                     : new Vector3(fromTemplate.x, v, fromTemplate.z);
            Vector3 b = axisVertical ? new Vector3(v, toTemplate.y, toTemplate.z)
                                     : new Vector3(toTemplate.x, v, toTemplate.z);
            line.SetPosition(0, a);
            line.SetPosition(1, b);
        }

        private void ClearGuides()
        {
            if (_guideVertical != null) { Destroy(_guideVertical); _guideVertical = null; }
            if (_guideHorizontal != null) { Destroy(_guideHorizontal); _guideHorizontal = null; }
            _guideProbePos = null;
            _guideAlignedNodeForX = null;
            _guideAlignedNodeForY = null;
        }

        /// <summary>Rebuild the cursor-following grid mesh every frame. Generates vertical
        /// + horizontal line quads at integer tile positions within CursorGridRadius of the
        /// cursor, with per-vertex alpha that fades to zero at the radius boundary so the
        /// grid dissolves smoothly into nothing at the edges.</summary>
        private void UpdateCursorGrid()
        {
            // Lazy-create the GameObject + mesh container.
            if (_cursorGridGo == null)
            {
                _cursorGridGo = new GameObject("CursorGrid");
                if (_grid != null)
                    _cursorGridGo.transform.SetParent(_grid.transform, worldPositionStays: false);
                _cursorGridMF = _cursorGridGo.AddComponent<MeshFilter>();
                var mr = _cursorGridGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _cursorGridMaterial;
                mr.receiveShadows = false;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _cursorGridMesh = new Mesh { name = "CursorGrid" };
                _cursorGridMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                _cursorGridMF.sharedMesh = _cursorGridMesh;
            }
            if (_cursorGridGo == null || _cursorGridMesh == null) return;

            // Hide when no useful cursor position (off-screen, over UI, etc.).
            var pt = MouseGroundPoint();
            if (pt is null
                || (Mouse.current is not null
                    && (Mouse.current.position.ReadValue().x < SidebarWidthPx
                        || Mouse.current.position.ReadValue().y > Screen.height - TopBarHeightPx)))
            {
                if (_cursorGridGo.activeSelf) _cursorGridGo.SetActive(false);
                return;
            }
            // Hide during residential corridor zoning — the cell-highlight outline + the
            // corridor grid are the relevant visuals there; the world grid is just noise.
            var pc = GetComponent<PlacementController>();
            if (pc != null
                && pc.CurrentMode == PlacementController.Mode.PaintZone
                && pc.CurrentZoneType == ZoneType.Residential)
            {
                if (_cursorGridGo.activeSelf) _cursorGridGo.SetActive(false);
                return;
            }
            if (!_cursorGridGo.activeSelf) _cursorGridGo.SetActive(true);

            var cursor = pt.Value;
            int R = CursorGridRadius;
            var placement = GetComponent<PlacementController>();
            int step = Mathf.Max(1, Mathf.RoundToInt(placement?.GridSnapStep ?? 1f));

            // Snap the grid origin (where lines pass through) to the same step the placement
            // snap uses, so the visual grid lines line up with what placement actually snaps to.
            int cx = Mathf.RoundToInt(cursor.x / step) * step;
            int cy = Mathf.RoundToInt(cursor.y / step) * step;

            var verts = new List<Vector3>(8 * (2 * R / step + 1) * 2);
            var colors = new List<Color>(verts.Capacity);
            var tris = new List<int>(12 * (2 * R / step + 1) * 2);

            // Vertical grid lines at multiples of `step`.
            for (int x = cx - R; x <= cx + R; x += step)
                AddGridLineV(verts, colors, tris, x, cy - R, cy + R, cursor, R, step);
            // Horizontal grid lines.
            for (int y = cy - R; y <= cy + R; y += step)
                AddGridLineH(verts, colors, tris, cx - R, cx + R, y, cursor, R, step);

            _cursorGridMesh.Clear();
            _cursorGridMesh.SetVertices(verts);
            _cursorGridMesh.SetColors(colors);
            _cursorGridMesh.SetTriangles(tris, 0);
            _cursorGridMesh.RecalculateBounds();
        }

        private static void AddGridLineV(List<Vector3> verts, List<Color> colors, List<int> tris,
                                         int x, int yMin, int yMax, Vector2 cursor, int radius, int step)
        {
            float halfW = CursorGridLineWidth * 0.5f;
            int firstIdx = verts.Count;
            // Subdivide every `step` so per-vertex alpha still fades smoothly along long lines.
            for (int y = yMin; y <= yMax; y += step)
            {
                float dist = Mathf.Sqrt((x - cursor.x) * (x - cursor.x) + (y - cursor.y) * (y - cursor.y));
                float alpha = Mathf.Clamp01(1f - dist / radius);
                var col = new Color(CursorGridColor.r, CursorGridColor.g, CursorGridColor.b, alpha);
                verts.Add(new Vector3(x - halfW, y, -0.04f));
                verts.Add(new Vector3(x + halfW, y, -0.04f));
                colors.Add(col);
                colors.Add(col);
            }
            int segments = (yMax - yMin) / step;
            for (int i = 0; i < segments; i++)
            {
                int a = firstIdx + i * 2;
                tris.Add(a); tris.Add(a + 1); tris.Add(a + 3);
                tris.Add(a); tris.Add(a + 3); tris.Add(a + 2);
            }
        }

        private static void AddGridLineH(List<Vector3> verts, List<Color> colors, List<int> tris,
                                         int xMin, int xMax, int y, Vector2 cursor, int radius, int step)
        {
            float halfW = CursorGridLineWidth * 0.5f;
            int firstIdx = verts.Count;
            for (int x = xMin; x <= xMax; x += step)
            {
                float dist = Mathf.Sqrt((x - cursor.x) * (x - cursor.x) + (y - cursor.y) * (y - cursor.y));
                float alpha = Mathf.Clamp01(1f - dist / radius);
                var col = new Color(CursorGridColor.r, CursorGridColor.g, CursorGridColor.b, alpha);
                verts.Add(new Vector3(x, y - halfW, -0.04f));
                verts.Add(new Vector3(x, y + halfW, -0.04f));
                colors.Add(col);
                colors.Add(col);
            }
            int segments = (xMax - xMin) / step;
            for (int i = 0; i < segments; i++)
            {
                int a = firstIdx + i * 2;
                tris.Add(a); tris.Add(a + 3); tris.Add(a + 1);
                tris.Add(a); tris.Add(a + 2); tris.Add(a + 3);
            }
        }

        /// <summary>Procedural flat circle mesh (regular polygon) in the local XY plane,
        /// centered at origin. Used to draw road-graph nodes.</summary>
        private static Mesh BuildCircleMesh(float radius = 1.0f, int sides = 24)
        {
            var mesh = new Mesh { name = "Circle" };
            var verts = new List<Vector3> { new(0, 0, 0) };
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                verts.Add(new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0));
            }
            var tris = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                tris.Add(0);
                tris.Add(1 + (i + 1) % sides);
                tris.Add(1 + i);
            }
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void SyncEdgeRenderers(SimState state)
        {
            // Remove renderers for edges that no longer exist.
            var stale = new List<long>();
            foreach (var kv in _edgeRenderers)
                if (!state.NetworkEdges.ContainsKey(kv.Key)) stale.Add(kv.Key);
            foreach (var id in stale)
            {
                if (_edgeRenderers[id] != null) Destroy(_edgeRenderers[id].gameObject);
                _edgeRenderers.Remove(id);
            }

            // Create / refresh renderers for current edges.
            foreach (var edge in state.NetworkEdges.Values)
            {
                if (!state.City.Structures.TryGetValue(edge.SourceStructureId, out var src)) continue;
                if (!state.City.Structures.TryGetValue(edge.TargetStructureId, out var tgt)) continue;
                if (src.X < 0 || tgt.X < 0) continue;

                if (!_edgeRenderers.TryGetValue(edge.Id, out var lr))
                {
                    var go = new GameObject($"Edge#{edge.Id}");
                    go.transform.SetParent(_edgeContainer.transform, worldPositionStays: false);
                    lr = go.AddComponent<LineRenderer>();
                    lr.material = _edgeMaterial;
                    lr.positionCount = 2;
                    lr.startWidth = 0.1f;
                    lr.endWidth = 0.1f;
                    lr.useWorldSpace = false;
                    lr.numCapVertices = 2;
                    _edgeRenderers[edge.Id] = lr;
                }
                var (sw, sh) = Footprint.For(src.Type);
                var (tw, th) = Footprint.For(tgt.Type);
                // Local coords inside the rotated grid: (cellX, cellY, 0). After parent's
                // +90° X rotation these render at world (cellX, 0, cellY). Place line ends at
                // each structure's footprint center, lifted slightly so it sits above tiles.
                var a = new Vector3(src.X + sw * 0.5f, src.Y + sh * 0.5f, -0.05f);
                var b = new Vector3(tgt.X + tw * 0.5f, tgt.Y + th * 0.5f, -0.05f);
                lr.SetPosition(0, a);
                lr.SetPosition(1, b);
                var color = edge.Kind == NetworkKind.Power
                    ? new Color(1f, 0.92f, 0.35f, 0.9f)
                    : new Color(0.40f, 0.75f, 0.95f, 0.9f);
                lr.startColor = color;
                lr.endColor = color;
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

            // Yield to road-node drag — clicking a node should start a drag, not also
            // select a structure underneath.
            if (_draggingNodeId.HasValue || _hoveredNodeId.HasValue) return;

            var mousePos = Mouse.current.position.ReadValue();
            // Skip clicks over the UI (sidebar on left, top bar on top).
            if (placement != null && mousePos.x < SidebarWidthPx) return;
            if (mousePos.y > Screen.height - TopBarHeightPx) return;

            var tile = MouseToTile(Camera.main);
            if (tile is null) return;
            _selectedStructureId = _bootstrap.Sim?.State.Region.Tilemap.StructureAt(tile.Value.x, tile.Value.y);
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

            // Per-type overrides for utility production + distribution so the player can tell
            // them apart at a glance: bold red/blue for producers, muted versions for the
            // matching distributors.
            switch (s.Type)
            {
                case StructureType.Generator: return new Color(0.90f, 0.30f, 0.25f, 1f);            // bold red
                case StructureType.ElectricityDistribution: return new Color(0.95f, 0.65f, 0.40f, 1f);  // pale orange-red
                case StructureType.Well: return new Color(0.20f, 0.45f, 0.85f, 1f);                 // bold blue
                case StructureType.WaterDistribution: return new Color(0.55f, 0.80f, 0.95f, 1f);    // pale cyan
            }

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
                StructureCategory.Utility => new Color(0.55f, 0.70f, 0.85f, 1f),  // fallback
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
