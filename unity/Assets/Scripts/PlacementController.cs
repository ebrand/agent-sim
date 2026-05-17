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
            PlaceCurvedRoad,
        }

        private SimBootstrap _bootstrap = null!;
        private SimVisualizer _visualizer = null!;
        private UnityTilemap _ghostTilemap = null!;
        private Texture2D _whiteTex = null!;
        private Sprite _whiteSprite = null!;
        private readonly Dictionary<Color, Tile> _tileCache = new();
        private readonly HashSet<Vector3Int> _ghostCells = new();
        // Rotated ghost: when placing a House (or any corridor-snapped type), show a
        // road-aligned rectangle + a small triangle pointing to the road, instead of the
        // axis-aligned tile rect. Lazy-allocated; tinted per-frame by validity.
        private LineRenderer? _houseGhostRect;
        private LineRenderer? _houseGhostMarker;
        // Highlight outline around the corridor cell under the cursor when in Residential
        // paint mode. Lazy-allocated; hidden (SetActive false) instead of destroyed between
        // frames to keep GC quiet.
        private LineRenderer? _zoneCellOutline;
        private Material? _houseGhostMaterial;
        private static readonly Color ZoneCellHoverColor = new(1.0f, 0.85f, 0.30f, 0.9f);
        // Residential zoning selection: two-click marquee. First click captures _zoneSelStart;
        // subsequent mouse moves update the highlight mesh (all corridor cells whose center
        // lies in the AABB from start to cursor); second click zones those cells.
        private struct ZoneSelStart { public Vector2 World; public long EdgeId; public int AlongCell; public int PerpCell; public int Side; public bool IsAdd; }
        private ZoneSelStart? _zoneSelStart;
        private GameObject? _zoneSelHighlightGO;
        private Mesh? _zoneSelHighlightMesh;
        private GameObject? _zoneSelRectGO;
        private Mesh? _zoneSelRectMesh;
        private readonly List<(long edgeId, int alongCell, int perpCell, int side)> _zoneSelHighlightCells = new();
        private static readonly Color ZoneSelHighlightColor = new(1.0f, 0.85f, 0.30f, 0.35f);
        private static readonly Color ZoneSelRemoveColor    = new(0.95f, 0.30f, 0.25f, 0.40f);

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
        /// <summary>Grid-snap step in meters. Treated as one "sim unit" (u) for display
        /// purposes — e.g. a 65m distance shows as 13u at the default 5m step.</summary>
        public float GridSnapStep = 5f;

        // Anchored-start flag: true once drag starts from an existing node OR on an existing
        // edge; while true (and constraint enabled), the cursor angle is snapped to 15°.
        private bool _startIsAnchored;

        private Vector3Int? _zoneDragStart;
        private Vector2 _sidebarScroll;

        // Road drawing: float-precision world-XZ points (matches Sim's Point2 coord space).
        private Vector2? _roadDragStart;
        private Vector2? _roadDragCurrent;

        // Curved road: 3-click flow. P0 = start node (must snap to existing); P1 = control
        // point (free); P2 = end (cursor, optional snap). Preview LineRenderer reuses the
        // road-preview material. _curveStartNodeId is captured at click 1 so we can derive
        // extension guides from the node's attached edges (one dashed ray per edge,
        // pointing away from the other endpoint). Cursor snaps to those rays during click 2.
        private Vector2? _curveP0;
        private Vector2? _curveP1;
        private long? _curveStartNodeId;
        private LineRenderer? _curvePreview;
        private GameObject? _curveExtensionsGO;
        private Mesh? _curveExtensionsMesh;
        // Axis-alignment guide for clicks 2 + 3: dashed lines through the X/Y-aligned
        // node when the cursor lines up with another existing node on either axis.
        private GameObject? _curveAlignmentGO;
        private Mesh? _curveAlignmentMesh;
        // Translucent ghost circle that follows the snapped cursor during click 2 + click 3,
        // matching the straight-road tool's "where the new node lands" affordance.
        private GameObject? _curveGhostNode;
        // All multi-click curve UX guides (extensions, alignment, etc.) share this cyan.
        private static readonly Color CurveGuideColor = new(0.20f, 0.85f, 0.95f, 0.80f);
        // Snap radius for the cursor-vs-extension test, in tile units (1u at default step).
        private const float ExtensionSnapRadiusTiles = 5f;
        // How far past the node each extension line is drawn, in tile units.
        private const float ExtensionLineLengthTiles = 200f;

        // Sidebar geometry.
        private const int SidebarWidth = 220;
        private const int SidebarPad = 12;

        public bool IsActive => _mode != Mode.Inspect;
        public Mode CurrentMode => _mode;
        public ZoneType CurrentZoneType => _pendingZoneType;
        public StructureType CurrentStructureType => _pendingStructureType;
        /// <summary>True when right-click is bound to "remove from zone selection" — the
        /// camera should not interpret right-button as a pan drag while this is active.</summary>
        public bool RightClickReservedForZoning =>
            _mode == Mode.PaintZone && _pendingZoneType == ZoneType.Residential;
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
            if (_mode == Mode.PlaceCurvedRoad) DrawCurveDistanceLabels();
        }

        /// <summary>Show "Nu" labels at the midpoint of each tangent segment while the
        /// curve is being placed. State 1: distance from start node to snapped cursor.
        /// State 2: locked P0→P1 distance + live P1→cursor distance.</summary>
        private void DrawCurveDistanceLabels()
        {
            if (_curveP0 is null) return;
            var cam = Camera.main;
            if (cam == null) return;
            float step = Mathf.Max(1f, GridSnapStep);

            var raw = MouseGroundPoint();
            if (_curveP1 is null)
            {
                if (!raw.HasValue) return;
                var snapped = SnapCurveCursor(raw.Value, isClick2: true);
                float d = Vector2.Distance(_curveP0.Value, snapped);
                if (d > 0.5f)
                    DrawSegmentLabel(cam, _curveP0.Value, snapped,
                                     $"{Mathf.RoundToInt(d / step)}u");
            }
            else
            {
                float d1 = Vector2.Distance(_curveP0.Value, _curveP1.Value);
                if (d1 > 0.5f)
                    DrawSegmentLabel(cam, _curveP0.Value, _curveP1.Value,
                                     $"{Mathf.RoundToInt(d1 / step)}u");
                if (raw.HasValue)
                {
                    var snapped = SnapCurveCursor(raw.Value, isClick2: false);
                    if (FindSnappedNodeId(snapped) is null)
                    {
                        float d2 = Vector2.Distance(_curveP1.Value, snapped);
                        if (d2 > 0.5f)
                            DrawSegmentLabel(cam, _curveP1.Value, snapped,
                                             $"{Mathf.RoundToInt(d2 / step)}u");
                    }
                }
            }
        }

        private static void DrawSegmentLabel(Camera cam, Vector2 fromTile, Vector2 toTile, string text)
        {
            var mid = (fromTile + toTile) * 0.5f;
            var screen = cam.WorldToScreenPoint(new Vector3(mid.x, 0f, mid.y));
            if (screen.z < 0) return;
            const float w = 50f, h = 20f;
            var rect = new Rect(screen.x - w * 0.5f, Screen.height - screen.y - h * 0.5f, w, h);
            GUI.Box(rect, text);
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
            if (_mode == Mode.PlaceCurvedRoad)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame)
                    HandleCurvedRoadClick();
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
                if (_pendingZoneType == ZoneType.Residential)
                {
                    // LMB = add, RMB = remove. Same drag model either way; only the matching
                    // button's release commits, so cross-button presses don't accidentally
                    // toggle the operation mid-drag.
                    if (Mouse.current.leftButton.wasPressedThisFrame)
                        BeginResidentialZoneSelection(isAdd: true);
                    else if (Mouse.current.rightButton.wasPressedThisFrame)
                        BeginResidentialZoneSelection(isAdd: false);
                    else if (Mouse.current.leftButton.wasReleasedThisFrame
                          && _zoneSelStart.HasValue && _zoneSelStart.Value.IsAdd)
                        CommitResidentialZoneSelection();
                    else if (Mouse.current.rightButton.wasReleasedThisFrame
                          && _zoneSelStart.HasValue && !_zoneSelStart.Value.IsAdd)
                        CommitResidentialZoneSelection();
                }
                else
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
            HideZoneCellOutline();  // re-shown below if we're in residential paint mode
            if (_mode == Mode.PlaceCurvedRoad) UpdateCurvePreview();
            else HideCurvePreview();
            if (_mode == Mode.Inspect) return;
            if (MouseInSidebar()) return;
            var tile = MouseToTile();
            if (!tile.HasValue) return;

            if (_mode == Mode.PlaceStructure)
            {
                var (w, h) = Footprint.For(_pendingStructureType);
                if (RequiresCorridorSnap(_pendingStructureType))
                {
                    var snap = ComputeCorridorSnap();
                    if (snap.HasValue) UpdateHouseGhost(snap.Value);
                    else ClearHouseGhost();
                    // Don't paint the axis-aligned tile rect here — the rotated LineRenderer
                    // ghost above is the load-bearing visual.
                }
                else
                {
                    bool valid = IsPlacementValid(_pendingStructureType, tile.Value.x, tile.Value.y);
                    PaintGhostRect(tile.Value.x, tile.Value.y, w, h, valid ? GoodColor : BadColor);
                }
            }
            else if (_mode == Mode.PaintZone && _pendingZoneType == ZoneType.Residential)
            {
                if (_zoneSelStart.HasValue)
                {
                    // During drag: hide the single-cell hover, show the dashed marquee +
                    // filled cell highlight.
                    HideZoneCellOutline();
                    var cur = MouseGroundPoint();
                    if (cur.HasValue)
                    {
                        UpdateZoneSelRect(_zoneSelStart.Value, cur.Value);
                        UpdateZoneSelHighlight(cur.Value);
                    }
                    else
                    {
                        HideZoneSelRect();
                        HideZoneSelHighlight();
                    }
                }
                else
                {
                    // Idle: just the hover outline showing what would be the start cell.
                    UpdateZoneCellOutline();
                    HideZoneSelRect();
                    HideZoneSelHighlight();
                }
                return;
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
            ClearHouseGhost();
        }

        private void ClearHouseGhost()
        {
            if (_houseGhostRect != null) _houseGhostRect.gameObject.SetActive(false);
            if (_houseGhostMarker != null) _houseGhostMarker.gameObject.SetActive(false);
        }

        private void HideZoneCellOutline()
        {
            if (_zoneCellOutline != null) _zoneCellOutline.gameObject.SetActive(false);
        }

        /// <summary>Render a road-aligned 4u × 4u rectangle outline + a small front-edge
        /// triangle for the corridor-snap ghost. Replaces the axis-aligned PaintGhostRect
        /// in residential placement so the user can see actual lot orientation on diagonal
        /// roads.</summary>
        private void UpdateHouseGhost(CorridorSnap snap)
        {
            var sim = _bootstrap.Sim;
            if (sim == null) { ClearHouseGhost(); return; }
            if (!sim.State.RoadEdges.TryGetValue(snap.EdgeId, out var edge)) { ClearHouseGhost(); return; }
            if (!sim.State.RoadNodes.TryGetValue(edge.FromNodeId, out var fn)) { ClearHouseGhost(); return; }
            if (!sim.State.RoadNodes.TryGetValue(edge.ToNodeId, out var tn)) { ClearHouseGhost(); return; }

            float stepSize = Mathf.Max(1f, GridSnapStep);
            float dx = tn.Position.X - fn.Position.X;
            float dy = tn.Position.Y - fn.Position.Y;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) { ClearHouseGhost(); return; }
            float ddx = dx / len, ddy = dy / len;
            float pdx = -ddy, pdy = ddx;
            int side = snap.Side;
            float setback = edge.WidthTiles * 0.5f;

            // Lot center in world tile coords (cursor's snapped front-center).
            float alongMid = (snap.AlongStart + 2) * stepSize;
            float perpMid  = side * (setback + 2 * stepSize);
            float cx = fn.Position.X + alongMid * ddx + perpMid * pdx;
            float cy = fn.Position.Y + alongMid * ddy + perpMid * pdy;

            // Corner offsets in road-local (along ±2 cells, perp ±2 cells from center).
            // Perp toward road = -side; perp away from road = +side.
            float halfLen = 2 * stepSize;
            float halfDep = 2 * stepSize;
            const float yLift = 0.07f;  // above ground, just below the cursor grid

            Vector3 W(float aOff, float pOff) => new(cx + aOff * ddx + pOff * pdx,
                                                     yLift,
                                                     cy + aOff * ddy + pOff * pdy);

            var frontL = W(-halfLen, -side * halfDep);
            var frontR = W( halfLen, -side * halfDep);
            var backR  = W( halfLen, +side * halfDep);
            var backL  = W(-halfLen, +side * halfDep);

            bool valid = snap.FrontEdgeValid && snap.TilesFree && snap.NoSetbackEncroach;
            Color col = valid ? GoodColor : BadColor;

            EnsureGhostLine(ref _houseGhostRect, "HouseGhostRect", 5);
            _houseGhostRect!.SetPosition(0, frontL);
            _houseGhostRect.SetPosition(1, frontR);
            _houseGhostRect.SetPosition(2, backR);
            _houseGhostRect.SetPosition(3, backL);
            _houseGhostRect.SetPosition(4, frontL);
            _houseGhostRect.startColor = col;
            _houseGhostRect.endColor = col;

            // Front-edge marker triangle: small isoceles pointing toward the road. Apex
            // sits near the inside of the front edge; base is set back about 1 cell deep.
            float triHalfBase = stepSize * 0.30f;
            float triDepth    = stepSize * 0.55f;
            float triInset    = stepSize * 0.10f;
            var apex  = W(0, -side * (halfDep - triInset));
            var baseL = W(-triHalfBase, -side * (halfDep - triInset - triDepth));
            var baseR = W( triHalfBase, -side * (halfDep - triInset - triDepth));

            EnsureGhostLine(ref _houseGhostMarker, "HouseGhostMarker", 4);
            _houseGhostMarker!.SetPosition(0, apex);
            _houseGhostMarker.SetPosition(1, baseL);
            _houseGhostMarker.SetPosition(2, baseR);
            _houseGhostMarker.SetPosition(3, apex);
            _houseGhostMarker.startColor = col;
            _houseGhostMarker.endColor = col;
        }

        private void EnsureGhostLine(ref LineRenderer? lr, string name, int points)
        {
            if (_houseGhostMaterial == null)
            {
                _houseGhostMaterial = new Material(Shader.Find("Sprites/Default"));
            }
            if (lr == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(transform, worldPositionStays: false);
                lr = go.AddComponent<LineRenderer>();
                lr.material = _houseGhostMaterial;
                lr.useWorldSpace = true;
                lr.startWidth = 0.18f;
                lr.endWidth = 0.18f;
                lr.numCapVertices = 1;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
            }
            lr.gameObject.SetActive(true);
            lr.positionCount = points;
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

        // === Corridor snap (for residential placement) ===

        /// <summary>Result of snapping a hover/click to a corridor cell. AABB X,Y is the
        /// bottom-left tile of the 20×20 footprint that contains the rotated lot.</summary>
        private struct CorridorSnap
        {
            public int X;
            public int Y;
            public float RotationDegrees;
            public long EdgeId;
            public int AlongStart;
            public int Side;
            public bool FrontEdgeValid;       // all 4 front cells are corridor-rendered
            public bool TilesFree;            // AABB doesn't overlap an existing structure
            public bool NoSetbackEncroach;    // no lot corner/edge sits inside any road's setback
        }

        /// <summary>Compute the corridor snap for the current cursor position. Returns null
        /// when the cursor isn't over any corridor band.</summary>
        private CorridorSnap? ComputeCorridorSnap()
        {
            var sim = _bootstrap.Sim;
            if (sim == null) return null;
            var world = MouseGroundPoint();
            if (world is null) return null;

            float stepSize = Mathf.Max(1f, GridSnapStep);
            var cell = CorridorIndex.LocateCellAt(sim.State, world.Value.x, world.Value.y,
                                                  stepSize, SimVisualizer.CorridorDepthCells);
            if (cell is null) return null;

            // Snap: cursor at front-center of lot. Lot is 4 cells along × 4 cells perp.
            // Front-center along = (alongStart + 2) * stepSize  ⇒  alongStart = round(cursor/step - 2).
            int alongStart = Mathf.RoundToInt(cell.Value.AlongInRoadMeters / stepSize - 2f);

            // Compute lot center in world coords from front-center along + side-center perp.
            float alongMid = (alongStart + 2) * stepSize;
            float perpMid  = cell.Value.Side * (cell.Value.Setback + 2 * stepSize);
            float ddx = cell.Value.DirX, ddy = cell.Value.DirY;
            float pdx = -ddy, pdy = ddx;
            // Resolve the edge endpoints for absolute coords.
            if (!sim.State.RoadEdges.TryGetValue(cell.Value.EdgeId, out var edge)) return null;
            if (!sim.State.RoadNodes.TryGetValue(edge.FromNodeId, out var fn)) return null;
            float centerX = fn.Position.X + alongMid * ddx + perpMid * pdx;
            float centerY = fn.Position.Y + alongMid * ddy + perpMid * pdy;

            // AABB bottom-left (20×20 footprint at 4u × 4u). Approximate — at 45° rotation
            // the true bounding box is ~28×28 but we use the inscribed square for tile
            // occupancy. Per-edge overlap is the load-bearing check.
            var (w, h) = Footprint.For(_pendingStructureType);
            int aabbX = Mathf.FloorToInt(centerX - w * 0.5f);
            int aabbY = Mathf.FloorToInt(centerY - h * 0.5f);

            // Rotation: lot's +Y faces the road, i.e., away from its side.
            int side = cell.Value.Side;
            float frontX = side * ddy;
            float frontY = -side * ddx;
            float rot = Mathf.Atan2(frontX, frontY) * Mathf.Rad2Deg;

            // Validate front 4 cells are all rendered (not Voronoi-culled).
            bool frontOk = true;
            for (int k = 0; k < 4; k++)
            {
                if (!CorridorIndex.IsCellRendered(sim.State, cell.Value.EdgeId,
                                                  alongStart + k, 0, side, stepSize))
                {
                    frontOk = false;
                    break;
                }
            }

            // Tile-AABB overlap with existing structures (legacy check, conservative).
            var tm = sim.State.Region.Tilemap;
            bool tilesFree = tm.InBounds(aabbX, aabbY, w, h) && tm.IsAreaFree(aabbX, aabbY, w, h);

            // Setback encroachment: sample 4 corners + 4 edge midpoints of the rotated lot
            // and reject if any sample falls inside ANY road's setback band. Sampling 8
            // points catches the diagonal-edge-grazes-setback case that a corners-only
            // check would miss.
            bool noSetback = !SamplesEncroachSetback(sim.State, centerX, centerY,
                                                    ddx, ddy, pdx, pdy,
                                                    side, stepSize);

            return new CorridorSnap
            {
                X = aabbX, Y = aabbY,
                RotationDegrees = rot,
                EdgeId = cell.Value.EdgeId,
                AlongStart = alongStart,
                Side = side,
                FrontEdgeValid = frontOk,
                TilesFree = tilesFree,
                NoSetbackEncroach = noSetback,
            };
        }

        /// <summary>True if any of the 4 corners or 4 edge midpoints of the rotated lot
        /// fall inside any road edge's setback band (|perp| ≤ setback, along ∈ [0, len]).
        /// A small epsilon excludes points exactly on the boundary so the lot's own front
        /// edge (which sits at perp = ±setback for its placement edge) is not a false hit.</summary>
        private static bool SamplesEncroachSetback(SimState state,
                                                   float cx, float cy,
                                                   float ddx, float ddy, float pdx, float pdy,
                                                   int side, float stepSize)
        {
            float halfLen = 2 * stepSize;
            float halfDep = 2 * stepSize;
            // Lot-local offsets (relative to lot center) in road-local (along, perp).
            // perp = -side*halfDep is the FRONT side (closer to road).
            // perp = +side*halfDep is the BACK side (away from road).
            float f = -side * halfDep;
            float b = +side * halfDep;
            // 4 corners + 4 edge midpoints.
            var samples = new (float a, float p)[]
            {
                (-halfLen, f), ( halfLen, f), ( halfLen, b), (-halfLen, b),  // corners
                ( 0f,      f), ( halfLen, 0f), ( 0f,     b), (-halfLen, 0f), // edge mids
            };
            const float eps = 0.05f;

            foreach (var e in state.RoadEdges.Values)
            {
                if (e.ControlPoint.HasValue) continue;  // v1: curves don't have setbacks for encroachment
                if (!state.RoadNodes.TryGetValue(e.FromNodeId, out var fn)) continue;
                if (!state.RoadNodes.TryGetValue(e.ToNodeId, out var tn)) continue;
                float fx = fn.Position.X, fy = fn.Position.Y;
                float edx = tn.Position.X - fx, edy = tn.Position.Y - fy;
                float elen = Mathf.Sqrt(edx * edx + edy * edy);
                if (elen < 1e-4f) continue;
                float eddx = edx / elen, eddy = edy / elen;
                float epdx = -eddy, epdy = eddx;
                float esetback = e.WidthTiles * 0.5f;

                foreach (var (a, p) in samples)
                {
                    float sx = cx + a * ddx + p * pdx;
                    float sy = cy + a * ddy + p * pdy;
                    float ox = sx - fx, oy = sy - fy;
                    float along = ox * eddx + oy * eddy;
                    float perp  = ox * epdx + oy * epdy;
                    if (along < 0f || along > elen) continue;
                    if (Mathf.Abs(perp) < esetback - eps) return true;
                }
            }
            return false;
        }

        private static bool RequiresCorridorSnap(StructureType type) =>
            type.Category() == StructureCategory.Residential;

        /// <summary>Outline the corridor cell under the cursor so the user can see exactly
        /// which cell will be painted on click. Hidden when the cursor isn't over a
        /// Voronoi-rendered cell.</summary>
        private void UpdateZoneCellOutline()
        {
            var sim = _bootstrap.Sim;
            if (sim == null) { HideZoneCellOutline(); return; }
            var world = MouseGroundPoint();
            if (world is null) { HideZoneCellOutline(); return; }
            float stepSize = Mathf.Max(1f, GridSnapStep);
            var cell = CorridorIndex.LocateCellAt(sim.State, world.Value.x, world.Value.y,
                                                  stepSize, SimVisualizer.CorridorDepthCells);
            if (cell is null) { HideZoneCellOutline(); return; }
            if (!CorridorIndex.IsCellRendered(sim.State, cell.Value.EdgeId,
                                              cell.Value.AlongCell, cell.Value.PerpCell,
                                              cell.Value.Side, stepSize))
            { HideZoneCellOutline(); return; }

            if (!sim.State.RoadEdges.TryGetValue(cell.Value.EdgeId, out var edge)) { HideZoneCellOutline(); return; }
            if (!sim.State.RoadNodes.TryGetValue(edge.FromNodeId, out var fn))     { HideZoneCellOutline(); return; }
            if (!sim.State.RoadNodes.TryGetValue(edge.ToNodeId, out var tn))       { HideZoneCellOutline(); return; }

            float dx = tn.Position.X - fn.Position.X;
            float dy = tn.Position.Y - fn.Position.Y;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) { HideZoneCellOutline(); return; }
            float ddx = dx / len, ddy = dy / len;
            float pdx = -ddy, pdy = ddx;
            float setback = edge.WidthTiles * 0.5f;
            int side = cell.Value.Side;
            int alongCell = cell.Value.AlongCell;
            int perpCell = cell.Value.PerpCell;

            float alongMin = alongCell * stepSize;
            float alongMax = Mathf.Min(alongMin + stepSize, len);
            float perpMin = setback + perpCell * stepSize;
            float perpMax = perpMin + stepSize;
            if (side < 0) { float t = perpMin; perpMin = -perpMax; perpMax = -t; }
            const float yLift = 0.08f;  // above zone overlay fill, below house ghost

            Vector3 W(float a, float p) => new(fn.Position.X + a * ddx + p * pdx,
                                                yLift,
                                                fn.Position.Y + a * ddy + p * pdy);
            var c00 = W(alongMin, perpMin);
            var c10 = W(alongMax, perpMin);
            var c11 = W(alongMax, perpMax);
            var c01 = W(alongMin, perpMax);

            EnsureGhostLine(ref _zoneCellOutline, "ZoneCellOutline", 5);
            _zoneCellOutline!.SetPosition(0, c00);
            _zoneCellOutline.SetPosition(1, c10);
            _zoneCellOutline.SetPosition(2, c11);
            _zoneCellOutline.SetPosition(3, c01);
            _zoneCellOutline.SetPosition(4, c00);
            _zoneCellOutline.startColor = ZoneCellHoverColor;
            _zoneCellOutline.endColor = ZoneCellHoverColor;
        }

        /// <summary>Mouse-down handler. Captures the start cell (cursor must be over a
        /// Voronoi-rendered corridor cell). The selection rectangle uses this edge's
        /// orientation, so all four sides of the marquee are road-aligned, not world-aligned.</summary>
        private void BeginResidentialZoneSelection(bool isAdd)
        {
            var sim = _bootstrap.Sim;
            if (sim == null) return;
            var world = MouseGroundPoint();
            if (world is null) return;
            float stepSize = Mathf.Max(1f, GridSnapStep);
            var cell = CorridorIndex.LocateCellAt(sim.State, world.Value.x, world.Value.y,
                                                  stepSize, SimVisualizer.CorridorDepthCells);
            if (cell is null) return;
            // Start cell can be in ANY perp row, not just the front. Cursor's row wins.
            if (!CorridorIndex.IsCellRendered(sim.State, cell.Value.EdgeId,
                                              cell.Value.AlongCell, cell.Value.PerpCell,
                                              cell.Value.Side, stepSize))
                return;
            var (csx, csy) = CorridorIndex.CellCenterWorld(sim.State, cell.Value.EdgeId,
                cell.Value.AlongCell, cell.Value.PerpCell, cell.Value.Side, stepSize);
            _zoneSelStart = new ZoneSelStart
            {
                World = new Vector2(csx, csy),
                EdgeId = cell.Value.EdgeId,
                AlongCell = cell.Value.AlongCell,
                PerpCell = cell.Value.PerpCell,
                Side = cell.Value.Side,
                IsAdd = isAdd,
            };
        }

        /// <summary>Mouse-up handler. Zones every cell currently inside the road-aligned
        /// rectangle. If the cursor is off-screen at release, just clears state.</summary>
        private void CommitResidentialZoneSelection()
        {
            var sim = _bootstrap.Sim;
            if (sim == null || _zoneSelStart is null) { ClearZoneSelection(); return; }
            var world = MouseGroundPoint();
            if (world is null) { ClearZoneSelection(); return; }
            ComputeSelectionCells(_zoneSelStart.Value, world.Value, _zoneSelHighlightCells);
            if (_zoneSelStart.Value.IsAdd)
            {
                foreach (var (eid, ac, pc, sd) in _zoneSelHighlightCells)
                    sim.ZoneCorridorCellResidential(eid, ac, pc, sd);
            }
            else
            {
                foreach (var (eid, ac, pc, sd) in _zoneSelHighlightCells)
                    sim.UnzoneCorridorCell(eid, ac, pc, sd);
            }
            ClearZoneSelection();
        }

        private void ClearZoneSelection()
        {
            _zoneSelStart = null;
            _zoneSelHighlightCells.Clear();
            HideZoneSelHighlight();
            HideZoneSelRect();
        }

        /// <summary>Project a world point onto the start edge's road-local frame
        /// (along, perp). Used to build the road-aligned AABB and to test cell membership.</summary>
        private static (float along, float perp) ProjectToEdge(Vector2 p,
                                                                float fx, float fy,
                                                                float ddx, float ddy,
                                                                float pdx, float pdy)
        {
            float ox = p.x - fx, oy = p.y - fy;
            return (ox * ddx + oy * ddy, ox * pdx + oy * pdy);
        }

        private static Vector2 EdgeLocalToWorld(float fx, float fy,
                                                float ddx, float ddy, float pdx, float pdy,
                                                float along, float perp)
            => new(fx + along * ddx + perp * pdx, fy + along * ddy + perp * pdy);

        /// <summary>Compute the road-aligned AABB (in start-edge local frame) of the
        /// selection from start to current. Caller uses the resulting (minA, maxA, minP, maxP)
        /// to build the dashed quad's 4 world-space corners and to filter cells.</summary>
        private bool TryComputeEdgeFrame(ZoneSelStart start, out float fx, out float fy,
                                         out float ddx, out float ddy, out float pdx, out float pdy)
        {
            fx = fy = ddx = ddy = pdx = pdy = 0f;
            var sim = _bootstrap.Sim;
            if (sim == null) return false;
            if (!sim.State.RoadEdges.TryGetValue(start.EdgeId, out var edge)) return false;
            if (!sim.State.RoadNodes.TryGetValue(edge.FromNodeId, out var fn)) return false;
            if (!sim.State.RoadNodes.TryGetValue(edge.ToNodeId, out var tn)) return false;
            fx = fn.Position.X; fy = fn.Position.Y;
            float dx = tn.Position.X - fx, dy = tn.Position.Y - fy;
            float len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) return false;
            ddx = dx / len; ddy = dy / len;
            pdx = -ddy; pdy = ddx;
            return true;
        }

        /// <summary>Collect cells whose center projects into the start-edge-local AABB
        /// formed by the start cell center and the current cursor. Iterates EVERY perp row
        /// (not just front) so the user can zone deeper cells too — only PerpCell=0 cells
        /// drive auto-spawn, but all selected cells get zoned and visually persist.</summary>
        private void ComputeSelectionCells(ZoneSelStart start, Vector2 currentWorld,
                                           List<(long, int, int, int)> output)
        {
            output.Clear();
            var sim = _bootstrap.Sim;
            if (sim == null) return;
            if (!TryComputeEdgeFrame(start, out var sfx, out var sfy,
                                     out var ddx, out var ddy, out var pdx, out var pdy))
                return;
            float stepSize = Mathf.Max(1f, GridSnapStep);
            int maxPerpCell = SimVisualizer.CorridorDepthCells;

            var (a0, p0) = ProjectToEdge(start.World, sfx, sfy, ddx, ddy, pdx, pdy);
            var (a1, p1) = ProjectToEdge(currentWorld, sfx, sfy, ddx, ddy, pdx, pdy);
            float minA = Mathf.Min(a0, a1), maxA = Mathf.Max(a0, a1);
            float minP = Mathf.Min(p0, p1), maxP = Mathf.Max(p0, p1);

            foreach (var e in sim.State.RoadEdges.Values)
            {
                if (e.ControlPoint.HasValue) continue;  // v1: curves don't host zonable cells
                if (!sim.State.RoadNodes.TryGetValue(e.FromNodeId, out var fn)) continue;
                if (!sim.State.RoadNodes.TryGetValue(e.ToNodeId, out var tn)) continue;
                float dx = tn.Position.X - fn.Position.X;
                float dy = tn.Position.Y - fn.Position.Y;
                float len = Mathf.Sqrt(dx * dx + dy * dy);
                if (len < 1e-4f) continue;
                float eddx = dx / len, eddy = dy / len;
                float epdx = -eddy, epdy = eddx;
                float setback = e.WidthTiles * 0.5f;
                int cellsAlong = Mathf.CeilToInt(len / stepSize);

                for (int side = -1; side <= 1; side += 2)
                {
                    for (int perpCell = 0; perpCell < maxPerpCell; perpCell++)
                    {
                        float perpCenter = side * (setback + (perpCell + 0.5f) * stepSize);
                        for (int a = 0; a < cellsAlong; a++)
                        {
                            float alongCenter = (a + 0.5f) * stepSize;
                            if (alongCenter > len) continue;
                            float cellX = fn.Position.X + alongCenter * eddx + perpCenter * epdx;
                            float cellY = fn.Position.Y + alongCenter * eddy + perpCenter * epdy;
                            var (ca, cp) = ProjectToEdge(new Vector2(cellX, cellY),
                                                          sfx, sfy, ddx, ddy, pdx, pdy);
                            if (ca < minA || ca > maxA || cp < minP || cp > maxP) continue;
                            if (!CorridorIndex.IsCellRendered(sim.State, e.Id, a, perpCell, side, stepSize))
                                continue;
                            // In remove mode, only show cells that are currently zoned —
                            // selecting empty cells doesn't preview as a no-op.
                            if (!start.IsAdd
                                && !sim.State.City.ZonedResidentialCells.Contains((e.Id, a, perpCell, side)))
                                continue;
                            output.Add((e.Id, a, perpCell, side));
                        }
                    }
                }
            }
        }

        private void HideZoneSelHighlight()
        {
            if (_zoneSelHighlightGO != null) _zoneSelHighlightGO.SetActive(false);
        }

        private void HideZoneSelRect()
        {
            if (_zoneSelRectGO != null) _zoneSelRectGO.SetActive(false);
        }

        /// <summary>Update the dashed-rectangle marquee. Computed in start-edge local
        /// coords so the rect is rotated to match the road, then the 4 corners are
        /// converted back to world coords for the mesh.</summary>
        private void UpdateZoneSelRect(ZoneSelStart start, Vector2 currentWorld)
        {
            if (!TryComputeEdgeFrame(start, out var sfx, out var sfy,
                                     out var ddx, out var ddy, out var pdx, out var pdy))
            { HideZoneSelRect(); return; }

            var (a0, p0) = ProjectToEdge(start.World, sfx, sfy, ddx, ddy, pdx, pdy);
            var (a1, p1) = ProjectToEdge(currentWorld, sfx, sfy, ddx, ddy, pdx, pdy);
            float minA = Mathf.Min(a0, a1), maxA = Mathf.Max(a0, a1);
            float minP = Mathf.Min(p0, p1), maxP = Mathf.Max(p0, p1);

            var c0 = EdgeLocalToWorld(sfx, sfy, ddx, ddy, pdx, pdy, minA, minP);
            var c1 = EdgeLocalToWorld(sfx, sfy, ddx, ddy, pdx, pdy, maxA, minP);
            var c2 = EdgeLocalToWorld(sfx, sfy, ddx, ddy, pdx, pdy, maxA, maxP);
            var c3 = EdgeLocalToWorld(sfx, sfy, ddx, ddy, pdx, pdy, minA, maxP);

            if (_zoneSelRectMesh == null)
            {
                _zoneSelRectMesh = new Mesh { name = "ZoneSelRect" };
                _zoneSelRectMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            if (_zoneSelRectGO == null)
            {
                _zoneSelRectGO = new GameObject("ZoneSelRect");
                var gridT = _visualizer.Grid != null ? _visualizer.Grid.transform : transform;
                _zoneSelRectGO.transform.SetParent(gridT, worldPositionStays: false);
                var mf = _zoneSelRectGO.AddComponent<MeshFilter>();
                mf.sharedMesh = _zoneSelRectMesh;
                var mr = _zoneSelRectGO.AddComponent<MeshRenderer>();
                if (_houseGhostMaterial == null)
                    _houseGhostMaterial = new Material(Shader.Find("Sprites/Default"));
                mr.sharedMaterial = _houseGhostMaterial;
                mr.receiveShadows = false;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _zoneSelRectGO.SetActive(true);

            float stepSize = Mathf.Max(1f, GridSnapStep);
            float dashLen = stepSize * 0.6f;
            float gapLen  = stepSize * 0.4f;
            float lineW   = stepSize * 0.08f;
            Color rectCol = start.IsAdd
                ? ZoneCellHoverColor
                : new Color(ZoneSelRemoveColor.r, ZoneSelRemoveColor.g, ZoneSelRemoveColor.b, 0.9f);
            ProceduralMesh.BuildDashedQuad(c0, c1, c2, c3,
                                            dashLen, gapLen, lineW,
                                            rectCol,
                                            reuseMesh: _zoneSelRectMesh);
        }

        /// <summary>Update the filled-cell highlight mesh for the in-progress marquee.
        /// Lazy-creates the GO + mesh, parented under SimGrid so the corridor's local-XY
        /// coordinates map onto the world ground plane via the +90° X rotation.</summary>
        private void UpdateZoneSelHighlight(Vector2 currentWorld)
        {
            var sim = _bootstrap.Sim;
            if (sim == null) return;

            if (_zoneSelHighlightMesh == null)
            {
                _zoneSelHighlightMesh = new Mesh { name = "ZoneSelHighlight" };
                _zoneSelHighlightMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            if (_zoneSelHighlightGO == null)
            {
                _zoneSelHighlightGO = new GameObject("ZoneSelHighlight");
                var gridT = _visualizer.Grid != null ? _visualizer.Grid.transform : transform;
                _zoneSelHighlightGO.transform.SetParent(gridT, worldPositionStays: false);
                var mf = _zoneSelHighlightGO.AddComponent<MeshFilter>();
                mf.sharedMesh = _zoneSelHighlightMesh;
                var mr = _zoneSelHighlightGO.AddComponent<MeshRenderer>();
                if (_houseGhostMaterial == null)
                    _houseGhostMaterial = new Material(Shader.Find("Sprites/Default"));
                mr.sharedMaterial = _houseGhostMaterial;
                mr.receiveShadows = false;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _zoneSelHighlightGO.SetActive(true);

            ComputeSelectionCells(_zoneSelStart!.Value, currentWorld, _zoneSelHighlightCells);
            float stepSize = Mathf.Max(1f, GridSnapStep);
            ProceduralMesh.BuildMultiEdgeCellsOverlay(
                _zoneSelHighlightCells,
                edgeId =>
                {
                    if (!sim.State.RoadEdges.TryGetValue(edgeId, out var e)) return null;
                    if (!sim.State.RoadNodes.TryGetValue(e.FromNodeId, out var fn)) return null;
                    if (!sim.State.RoadNodes.TryGetValue(e.ToNodeId, out var tn)) return null;
                    return (fn.Position.X, fn.Position.Y, tn.Position.X, tn.Position.Y, e.WidthTiles * 0.5f);
                },
                stepSize,
                _zoneSelStart.Value.IsAdd ? ZoneSelHighlightColor : ZoneSelRemoveColor,
                reuseMesh: _zoneSelHighlightMesh);
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
            var sim = _bootstrap.Sim;
            if (sim == null) return;

            if (RequiresCorridorSnap(_pendingStructureType))
            {
                var snap = ComputeCorridorSnap();
                if (snap is null) return;
                if (!snap.Value.FrontEdgeValid || !snap.Value.TilesFree || !snap.Value.NoSetbackEncroach) return;
                try
                {
                    var s = sim.PlaceResidentialStructureFreeform(_pendingStructureType, snap.Value.X, snap.Value.Y);
                    s.RotationDegrees    = snap.Value.RotationDegrees;
                    s.PlacementEdgeId    = snap.Value.EdgeId;
                    s.PlacementAlongCell = snap.Value.AlongStart;
                    s.PlacementSide      = snap.Value.Side;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Placement] {_pendingStructureType} failed: {e.Message}");
                }
                return;
            }

            if (!IsPlacementValid(_pendingStructureType, tile.x, tile.y)) return;
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
        /// <summary>3-click flow for curved roads. Click 1 must hit an existing node (we
        /// need the attached edge as a tangent reference in future iterations). Click 2 is
        /// the free 2D control point. Click 3 is the end (snaps to existing node if close,
        /// otherwise creates a new one).</summary>
        private void HandleCurvedRoadClick()
        {
            var world = MouseGroundPoint();
            if (world is null) return;
            var sim = _bootstrap.Sim;
            if (sim == null) return;

            if (_curveP0 is null)
            {
                var nodeId = FindSnappedNodeId(world.Value);
                if (nodeId is null) return;  // click 1 requires an existing node
                var n = sim.State.RoadNodes[nodeId.Value];
                _curveP0 = new Vector2(n.Position.X, n.Position.Y);
                _curveStartNodeId = nodeId;
            }
            else if (_curveP1 is null)
            {
                _curveP1 = SnapCurveCursor(world.Value, isClick2: true);
            }
            else
            {
                var endSnapped = SnapCurveCursor(world.Value, isClick2: false);
                try
                {
                    sim.PlaceCurvedRoad(
                        new Point2(_curveP0.Value.x, _curveP0.Value.y),
                        new Point2(_curveP1.Value.x, _curveP1.Value.y),
                        new Point2(endSnapped.x, endSnapped.y));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Placement] curved road failed: {e.Message}");
                }
                _curveP0 = null;
                _curveP1 = null;
                _curveStartNodeId = null;
                HideCurvePreview();
                HideCurveExtensions();
                HideCurveAlignment();
            }
        }

        /// <summary>Snap stack for curve clicks 2 and 3. Precedence (highest first):
        /// (0) intersection of any two extension rays in range — locks the cursor in 2D
        /// when two guide lines cross near it; (1) click-1 node's extension ray (click 2
        /// only — tangent continuity); (2) any OTHER node's edge-extension ray;
        /// (3) snap-to-existing-node; (4) world-grid snap when GridSnapEnabled.</summary>
        private Vector2 SnapCurveCursor(Vector2 raw, bool isClick2)
        {
            bool altHeld = Keyboard.current is not null && Keyboard.current.altKey.isPressed;
            bool effectiveGridSnap = GridSnapEnabled ^ altHeld;

            var (intsect, found) = TrySnapToExtensionIntersection(raw);
            if (found) return intsect;

            if (isClick2)
            {
                var ext = SnapToExtensionRay(raw);
                if (ext != raw) return ext;
            }
            var (otherExt, _) = FindNearestOtherNodeExtension(raw);
            if (otherExt != raw) return otherExt;
            var node = SnapToExistingNode(raw);
            if (node != raw) return node;
            if (effectiveGridSnap) return SnapToStep(raw, GridSnapStep);
            return raw;
        }

        /// <summary>Compute every extension ray for every node, look for pairwise
        /// intersections that lie on both rays' forward sides, and return the closest
        /// intersection to the cursor if it's within ExtensionSnapRadiusTiles.</summary>
        private (Vector2 snapped, bool found) TrySnapToExtensionIntersection(Vector2 cursor)
        {
            var sim = _bootstrap.Sim;
            if (sim == null) return (cursor, false);

            var rays = new List<(Vector2 a, Vector2 d)>();
            foreach (var n in sim.State.RoadNodes.Values)
                rays.AddRange(ComputeExtensionRaysForNode(n.Id));
            if (rays.Count < 2) return (cursor, false);

            // Intersection only wins over single-ray snap when the cursor is meaningfully
            // closer to the intersection point than to either individual ray. Using a
            // tighter radius avoids the "yanked to intersection" feeling described in the
            // post-mortem.
            float intersectionRadius = ExtensionSnapRadiusTiles * 0.5f;
            Vector2 best = cursor;
            float bestDist = intersectionRadius;
            bool found = false;
            for (int i = 0; i < rays.Count; i++)
            {
                for (int j = i + 1; j < rays.Count; j++)
                {
                    // Skip same-origin pairs (both rays from one node trivially "intersect"
                    // at that node, which would pull the cursor back to the node).
                    if (Vector2.Distance(rays[i].a, rays[j].a) < 1e-3f) continue;
                    // Skip near-parallel pairs — their intersection moves wildly with small
                    // cursor motion and the snap feels unstable. |dot| > 0.995 ≈ < ~5.7°.
                    if (Mathf.Abs(Vector2.Dot(rays[i].d, rays[j].d)) > 0.995f) continue;
                    if (!TryRayIntersect(rays[i].a, rays[i].d, rays[j].a, rays[j].d, out var ipt))
                        continue;
                    float dist = Vector2.Distance(cursor, ipt);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = ipt;
                        found = true;
                    }
                }
            }
            return (best, found);
        }

        /// <summary>Intersect two rays (origin + dir). Returns true with the intersection
        /// when both parametric scalars are non-negative (cursor is on the forward side
        /// of each ray); false when parallel or the intersection is behind either origin.</summary>
        private static bool TryRayIntersect(Vector2 a1, Vector2 d1, Vector2 a2, Vector2 d2,
                                            out Vector2 intersection)
        {
            intersection = Vector2.zero;
            float det = d2.x * d1.y - d1.x * d2.y;
            if (Mathf.Abs(det) < 1e-6f) return false;
            Vector2 diff = a2 - a1;
            float t = (d2.x * diff.y - d2.y * diff.x) / det;
            float s = (d1.x * diff.y - d1.y * diff.x) / det;
            if (t < 0 || s < 0) return false;
            intersection = a1 + d1 * t;
            return true;
        }

        /// <summary>Extension rays at <paramref name="nodeId"/>: one per attached edge,
        /// each starting at the node and pointing AWAY from the edge's other endpoint
        /// (so the ray visually continues the edge through the node and out the other side).</summary>
        private List<(Vector2 from, Vector2 dir)> ComputeExtensionRaysForNode(long nodeId)
        {
            var result = new List<(Vector2, Vector2)>();
            var sim = _bootstrap.Sim;
            if (sim == null) return result;
            if (!sim.State.RoadNodes.TryGetValue(nodeId, out var n)) return result;
            Vector2 a = new(n.Position.X, n.Position.Y);
            foreach (var e in sim.State.RoadEdges.Values)
            {
                long otherId;
                if (e.FromNodeId == n.Id) otherId = e.ToNodeId;
                else if (e.ToNodeId == n.Id) otherId = e.FromNodeId;
                else continue;
                if (!sim.State.RoadNodes.TryGetValue(otherId, out var o)) continue;
                Vector2 b = new(o.Position.X, o.Position.Y);
                Vector2 d = a - b;
                float len = d.magnitude;
                if (len < 1e-4f) continue;
                result.Add((a, d / len));
            }
            return result;
        }

        private List<(Vector2 from, Vector2 dir)> ComputeExtensionRays()
            => _curveStartNodeId is null
                ? new List<(Vector2, Vector2)>()
                : ComputeExtensionRaysForNode(_curveStartNodeId.Value);

        /// <summary>Scan every other node's extension rays. Return the (snapped position,
        /// owning node id) for the ray whose perpendicular distance to the cursor is
        /// smallest — provided it's within ExtensionSnapRadiusTiles and the cursor lies
        /// on the forward side of the ray. Null nodeId = no snap.</summary>
        private (Vector2 snapped, long? activeNodeId) FindNearestOtherNodeExtension(Vector2 raw)
        {
            var sim = _bootstrap.Sim;
            if (sim == null) return (raw, null);
            Vector2 best = raw;
            float bestDist = ExtensionSnapRadiusTiles;
            long? bestNodeId = null;
            foreach (var n in sim.State.RoadNodes.Values)
            {
                if (n.Id == _curveStartNodeId) continue;  // already covered by SnapToExtensionRay
                foreach (var (a, d) in ComputeExtensionRaysForNode(n.Id))
                {
                    float t = Vector2.Dot(raw - a, d);
                    if (t <= 0) continue;
                    Vector2 proj = a + d * t;
                    float dist = Vector2.Distance(raw, proj);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = proj;
                        bestNodeId = n.Id;
                    }
                }
            }
            return (best, bestNodeId);
        }

        private Vector2 SnapToExistingNode(Vector2 raw)
        {
            var sim = _bootstrap.Sim;
            if (sim is null) return raw;
            float bestSq = Sim.NodeSnapRadiusTiles * Sim.NodeSnapRadiusTiles;
            Point2? best = null;
            foreach (var node in sim.State.RoadNodes.Values)
            {
                float dx = node.Position.X - raw.x;
                float dy = node.Position.Y - raw.y;
                float dsq = dx * dx + dy * dy;
                if (dsq <= bestSq) { bestSq = dsq; best = node.Position; }
            }
            return best.HasValue ? new Vector2(best.Value.X, best.Value.Y) : raw;
        }

        /// <summary>If <paramref name="cur"/> is within ExtensionSnapRadiusTiles of any
        /// extension ray, return the projection onto that ray. Otherwise return cur.</summary>
        private Vector2 SnapToExtensionRay(Vector2 cur)
        {
            var rays = ComputeExtensionRays();
            Vector2 best = cur;
            float bestDist = ExtensionSnapRadiusTiles;
            foreach (var (a, dir) in rays)
            {
                float t = Vector2.Dot(cur - a, dir);
                if (t <= 0) continue;  // behind the node — would re-enter the existing edge
                Vector2 proj = a + dir * t;
                float dist = Vector2.Distance(cur, proj);
                if (dist < bestDist) { bestDist = dist; best = proj; }
            }
            return best;
        }

        private void HideCurveExtensions()
        {
            if (_curveExtensionsGO != null) _curveExtensionsGO.SetActive(false);
        }

        private void HideCurveAlignment()
        {
            if (_curveAlignmentGO != null) _curveAlignmentGO.SetActive(false);
        }

        private void HideCurveGhostNode()
        {
            if (_curveGhostNode != null) _curveGhostNode.SetActive(false);
        }

        /// <summary>Show a translucent disc at the snapped cursor (where the next click
        /// would land). Hidden when the cursor is already coincident with an existing
        /// node — that node will be highlighted via its own hover state instead.</summary>
        private void UpdateCurveGhostNode(Vector2 snappedCursor)
        {
            var sim = _bootstrap.Sim;
            if (sim is null) { HideCurveGhostNode(); return; }
            float sq = (float)(Sim.NodeSnapRadiusTiles * Sim.NodeSnapRadiusTiles * 0.5f);
            foreach (var n in sim.State.RoadNodes.Values)
            {
                float dx = n.Position.X - snappedCursor.x;
                float dy = n.Position.Y - snappedCursor.y;
                if (dx * dx + dy * dy <= sq) { HideCurveGhostNode(); return; }
            }
            if (_curveGhostNode == null)
            {
                _curveGhostNode = new GameObject("CurveGhostNode");
                var gridT = _visualizer.Grid != null ? _visualizer.Grid.transform : transform;
                _curveGhostNode.transform.SetParent(gridT, worldPositionStays: false);
                var mf = _curveGhostNode.AddComponent<MeshFilter>();
                mf.sharedMesh = _visualizer.NodeMesh;
                var mr = _curveGhostNode.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _visualizer.GhostNodeMaterial;
                mr.receiveShadows = false;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _curveGhostNode.SetActive(true);
            _curveGhostNode.transform.localPosition =
                new Vector3(snappedCursor.x, snappedCursor.y, -0.12f);
        }

        /// <summary>Render the extension rays of the "active" node — the one whose own
        /// extension ray the cursor is currently closest to. Cursor snap math (in
        /// SnapCurveCursor) is what defines "active"; here we just visualize that node's
        /// rays so the user can see what they're aligning against.</summary>
        private void UpdateCurveAlignment(Vector2 cursor)
        {
            var (_, activeNodeId) = FindNearestOtherNodeExtension(cursor);
            if (activeNodeId is null) { HideCurveAlignment(); return; }

            var rays = ComputeExtensionRaysForNode(activeNodeId.Value);
            if (rays.Count == 0) { HideCurveAlignment(); return; }

            var lines = new List<(Vector2 from, Vector2 to)>(rays.Count);
            foreach (var (a, dir) in rays)
                lines.Add((a, a + dir * ExtensionLineLengthTiles));

            if (_curveAlignmentMesh == null)
            {
                _curveAlignmentMesh = new Mesh { name = "CurveAlignment" };
                _curveAlignmentMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            if (_curveAlignmentGO == null)
            {
                _curveAlignmentGO = new GameObject("CurveAlignment");
                var gridT = _visualizer.Grid != null ? _visualizer.Grid.transform : transform;
                _curveAlignmentGO.transform.SetParent(gridT, worldPositionStays: false);
                var mf = _curveAlignmentGO.AddComponent<MeshFilter>();
                mf.sharedMesh = _curveAlignmentMesh;
                var mr = _curveAlignmentGO.AddComponent<MeshRenderer>();
                if (_houseGhostMaterial == null)
                    _houseGhostMaterial = new Material(Shader.Find("Sprites/Default"));
                mr.sharedMaterial = _houseGhostMaterial;
                mr.receiveShadows = false;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _curveAlignmentGO.SetActive(true);

            float stepSize = Mathf.Max(1f, GridSnapStep);
            ProceduralMesh.BuildDashedLines(
                lines,
                dashLen: stepSize * 0.5f,
                gapLen:  stepSize * 0.5f,
                lineWidth: stepSize * 0.06f,
                color: CurveGuideColor,
                reuseMesh: _curveAlignmentMesh);
        }

        private void UpdateCurveExtensions(Vector2 snappedCursor)
        {
            if (_curveStartNodeId is null) { HideCurveExtensions(); return; }
            var rays = ComputeExtensionRays();

            var lines = new List<(Vector2 from, Vector2 to)>(rays.Count + 1);
            foreach (var (a, dir) in rays)
                lines.Add((a, a + dir * ExtensionLineLengthTiles));

            // State 2 only: also draw a guide line from the control point (P1) to the
            // cursor, since that segment IS the tangent at click 3. Suppress when the
            // cursor is snapping to an existing node — that node would supply its own
            // tangent reference (not implemented yet, but the line is misleading either way).
            if (_curveP1.HasValue && FindSnappedNodeId(snappedCursor) is null)
                lines.Add((_curveP1.Value, snappedCursor));

            if (lines.Count == 0) { HideCurveExtensions(); return; }

            if (_curveExtensionsMesh == null)
            {
                _curveExtensionsMesh = new Mesh { name = "CurveExtensions" };
                _curveExtensionsMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            if (_curveExtensionsGO == null)
            {
                _curveExtensionsGO = new GameObject("CurveExtensions");
                var gridT = _visualizer.Grid != null ? _visualizer.Grid.transform : transform;
                _curveExtensionsGO.transform.SetParent(gridT, worldPositionStays: false);
                var mf = _curveExtensionsGO.AddComponent<MeshFilter>();
                mf.sharedMesh = _curveExtensionsMesh;
                var mr = _curveExtensionsGO.AddComponent<MeshRenderer>();
                if (_houseGhostMaterial == null)
                    _houseGhostMaterial = new Material(Shader.Find("Sprites/Default"));
                mr.sharedMaterial = _houseGhostMaterial;
                mr.receiveShadows = false;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _curveExtensionsGO.SetActive(true);

            float stepSize = Mathf.Max(1f, GridSnapStep);
            ProceduralMesh.BuildDashedLines(
                lines,
                dashLen: stepSize * 0.5f,
                gapLen:  stepSize * 0.5f,
                lineWidth: stepSize * 0.06f,
                color: CurveGuideColor,
                reuseMesh: _curveExtensionsMesh);
        }

        private long? FindSnappedNodeId(Vector2 p)
        {
            var sim = _bootstrap.Sim;
            if (sim is null) return null;
            float bestSq = Sim.NodeSnapRadiusTiles * Sim.NodeSnapRadiusTiles;
            long? best = null;
            foreach (var n in sim.State.RoadNodes.Values)
            {
                float dx = n.Position.X - p.x;
                float dy = n.Position.Y - p.y;
                float dsq = dx * dx + dy * dy;
                if (dsq <= bestSq) { bestSq = dsq; best = n.Id; }
            }
            return best;
        }

        private void HideCurvePreview()
        {
            if (_curvePreview != null) _curvePreview.gameObject.SetActive(false);
        }

        /// <summary>Update the in-progress curve preview. State 0 = no preview; state 1
        /// (P0 set) = line from P0 to cursor; state 2 (P0 + P1 set) = quadratic Bezier
        /// from P0 through P1 to cursor.</summary>
        private void UpdateCurvePreview()
        {
            if (_curveP0 is null) { HideCurvePreview(); HideCurveExtensions(); HideCurveAlignment(); HideCurveGhostNode(); return; }
            var raw = MouseGroundPoint();
            if (raw is null) { HideCurvePreview(); HideCurveExtensions(); HideCurveAlignment(); HideCurveGhostNode(); return; }
            // The preview tracks the FINAL snap position so what you see is what you'd commit.
            bool isClick2 = _curveP1 is null;
            var cur = SnapCurveCursor(raw.Value, isClick2);
            UpdateCurveAlignment(cur);
            UpdateCurveGhostNode(cur);

            if (_curvePreview == null)
            {
                var go = new GameObject("CurvePreview");
                var gridT = _visualizer.Grid != null ? _visualizer.Grid.transform : transform;
                go.transform.SetParent(gridT, worldPositionStays: false);
                _curvePreview = go.AddComponent<LineRenderer>();
                if (_houseGhostMaterial == null)
                    _houseGhostMaterial = new Material(Shader.Find("Sprites/Default"));
                _curvePreview.material = _houseGhostMaterial;
                _curvePreview.useWorldSpace = false;
                _curvePreview.startWidth = 0.5f;
                _curvePreview.endWidth = 0.5f;
                _curvePreview.numCapVertices = 2;
                _curvePreview.startColor = CurveGuideColor;
                _curvePreview.endColor = CurveGuideColor;
                _curvePreview.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _curvePreview.receiveShadows = false;
            }
            _curvePreview.gameObject.SetActive(true);

            UpdateCurveExtensions(cur);
            if (_curveP1 is null)
            {
                // State 1: straight line from P0 to snapped cursor.
                _curvePreview.positionCount = 2;
                _curvePreview.SetPosition(0, new Vector3(_curveP0.Value.x, _curveP0.Value.y, -0.06f));
                _curvePreview.SetPosition(1, new Vector3(cur.x,             cur.y,             -0.06f));
            }
            else
            {
                // State 2: full Bezier preview, cursor = prospective end point.
                const int samples = 24;
                _curvePreview.positionCount = samples;
                float x0 = _curveP0.Value.x, y0 = _curveP0.Value.y;
                float x1 = _curveP1.Value.x, y1 = _curveP1.Value.y;
                float x2 = cur.x,            y2 = cur.y;
                for (int i = 0; i < samples; i++)
                {
                    float t = i / (float)(samples - 1);
                    float u = 1f - t;
                    float bx = u * u * x0 + 2f * u * t * x1 + t * t * x2;
                    float by = u * u * y0 + 2f * u * t * y1 + t * t * y2;
                    _curvePreview.SetPosition(i, new Vector3(bx, by, -0.06f));
                }
            }
        }

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
            CurvedRoadButton();
            ConnectButton();
            DemolishButton();

            GUILayout.Space(8);
            GUILayout.Label("ROAD SETTINGS");
            GridSnapEnabled        = GUILayout.Toggle(GridSnapEnabled,        " Grid snap (Alt inverts)");
            AlignmentGuidesEnabled = GUILayout.Toggle(AlignmentGuidesEnabled, " Alignment guides");
            AngleConstraintEnabled = GUILayout.Toggle(AngleConstraintEnabled, " 15° angle constraint");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"1u = {GridSnapStep:F0} m", GUILayout.Width(110));
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

        private void CurvedRoadButton()
        {
            bool active = _mode == Mode.PlaceCurvedRoad;
            var bg = GUI.backgroundColor;
            GUI.backgroundColor = active ? Color.green : new Color(0.20f, 0.55f, 0.30f, 1f);
            if (GUILayout.Button("CURVE"))
            {
                _mode = Mode.PlaceCurvedRoad;
                _curveP0 = null;
                _curveP1 = null;
                _curveStartNodeId = null;
                HideCurvePreview();
                HideCurveExtensions();
                HideCurveAlignment();
                HideCurveGhostNode();
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
                Mode.PaintZone when _pendingZoneType == ZoneType.Residential && _zoneSelStart.HasValue =>
                    $"Zoning Residential: drag to size selection, release to commit  (Esc to cancel)",
                Mode.PaintZone when _pendingZoneType == ZoneType.Residential =>
                    $"Zoning Residential: click + drag on a corridor cell to select  (Esc to cancel)",
                Mode.PaintZone => $"Painting: {_pendingZoneType}  (drag rectangle, Esc to cancel)",
                Mode.Demolish => "DEMOLISH: click a structure to remove it  (Esc to cancel)",
                Mode.Connect => _connectSourceId.HasValue
                    ? "CONNECT: click another distributor to link  (Esc to cancel)"
                    : "CONNECT: click a distributor (power or water)  (Esc to cancel)",
                Mode.PlaceRoad => _roadDragStart.HasValue
                    ? "ROAD: release left mouse to commit segment  (Esc to cancel)"
                    : "ROAD: click + drag to draw a road segment  (Esc to cancel)",
                Mode.PlaceCurvedRoad when _curveP0 is null =>
                    "CURVED ROAD: click an existing node to start  (Esc to cancel)",
                Mode.PlaceCurvedRoad when _curveP1 is null =>
                    "CURVED ROAD: click to place the control point  (Esc to cancel)",
                Mode.PlaceCurvedRoad =>
                    "CURVED ROAD: click to place the end point  (Esc to cancel)",
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
            ClearZoneSelection();
            _connectSourceId = null;
            _roadDragStart = null;
            _roadDragCurrent = null;
            _curveP0 = null;
            _curveP1 = null;
            _curveStartNodeId = null;
            HideCurvePreview();
            HideCurveExtensions();
            HideCurveAlignment();
            HideCurveGhostNode();
            ClearGhost();
        }

        // === Colors ===

        private static readonly Color GoodColor = new(0.30f, 0.85f, 0.35f, 0.55f);
        private static readonly Color BadColor = new(0.85f, 0.30f, 0.30f, 0.55f);
    }
}
