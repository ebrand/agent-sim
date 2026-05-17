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
        private struct ZoneSelStart { public Vector2 World; public long EdgeId; public int AlongCell; public int Side; }
        private ZoneSelStart? _zoneSelStart;
        private GameObject? _zoneSelHighlightGO;
        private Mesh? _zoneSelHighlightMesh;
        private readonly List<(long edgeId, int alongCell, int side)> _zoneSelHighlightCells = new();
        private static readonly Color ZoneSelHighlightColor = new(1.0f, 0.85f, 0.30f, 0.35f);

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

        // Sidebar geometry.
        private const int SidebarWidth = 220;
        private const int SidebarPad = 12;

        public bool IsActive => _mode != Mode.Inspect;
        public Mode CurrentMode => _mode;
        public ZoneType CurrentZoneType => _pendingZoneType;
        public StructureType CurrentStructureType => _pendingStructureType;
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
                if (_pendingZoneType == ZoneType.Residential)
                {
                    // Two-click marquee: 1st click = start, 2nd click = commit.
                    if (Mouse.current.leftButton.wasPressedThisFrame)
                        HandleResidentialZoneClick();
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
                // Single-cell outline at cursor + (if selection in progress) filled rectangle
                // highlight from start to current.
                UpdateZoneCellOutline();
                if (_zoneSelStart.HasValue)
                {
                    var cur = MouseGroundPoint();
                    if (cur.HasValue)
                        UpdateZoneSelHighlight(_zoneSelStart.Value.World,
                                               SnapToHoveredCellCenter(cur.Value));
                    else HideZoneSelHighlight();
                }
                else
                {
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
                                              cell.Value.AlongCell, 0, cell.Value.Side, stepSize))
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

            float alongMin = alongCell * stepSize;
            float alongMax = Mathf.Min(alongMin + stepSize, len);
            float perpMin = setback;
            float perpMax = setback + stepSize;
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

        /// <summary>Handle a click in Residential paint mode. First click = capture the
        /// start cell. Second click = commit (zones every highlighted cell). Click off-
        /// corridor while starting = no-op; click off-corridor while committing = still
        /// commits whatever cells are currently in the rectangle.</summary>
        private void HandleResidentialZoneClick()
        {
            var sim = _bootstrap.Sim;
            if (sim == null) return;
            var world = MouseGroundPoint();
            if (world is null) return;
            float stepSize = Mathf.Max(1f, GridSnapStep);

            if (_zoneSelStart is null)
            {
                var cell = CorridorIndex.LocateCellAt(sim.State, world.Value.x, world.Value.y,
                                                      stepSize, SimVisualizer.CorridorDepthCells);
                if (cell is null) return;
                if (!CorridorIndex.IsCellRendered(sim.State, cell.Value.EdgeId,
                                                  cell.Value.AlongCell, 0, cell.Value.Side, stepSize))
                    return;
                // Store the cell's CENTER (not the click pixel) so the AABB-vs-center test
                // includes the start cell when the user commits without moving.
                var (csx, csy) = CorridorIndex.CellCenterWorld(sim.State, cell.Value.EdgeId,
                    cell.Value.AlongCell, 0, cell.Value.Side, stepSize);
                _zoneSelStart = new ZoneSelStart
                {
                    World = new Vector2(csx, csy),
                    EdgeId = cell.Value.EdgeId,
                    AlongCell = cell.Value.AlongCell,
                    Side = cell.Value.Side,
                };
            }
            else
            {
                // Snap commit endpoint to the hovered cell's center too — matches how the
                // highlight previews the selection.
                var endWorld = SnapToHoveredCellCenter(world.Value);
                ComputeSelectionCells(_zoneSelStart.Value.World, endWorld,
                                      _zoneSelStart.Value.EdgeId, _zoneSelStart.Value.Side,
                                      _zoneSelHighlightCells);
                foreach (var (eid, ac, sd) in _zoneSelHighlightCells)
                    sim.ZoneCorridorCellResidential(eid, ac, sd);
                _zoneSelStart = null;
                _zoneSelHighlightCells.Clear();
                HideZoneSelHighlight();
            }
        }

        /// <summary>If the cursor (or any world point) sits inside a Voronoi-rendered
        /// corridor cell, return that cell's center; otherwise return the raw point.
        /// Used to snap both endpoints of the selection rectangle to cell centers.</summary>
        private Vector2 SnapToHoveredCellCenter(Vector2 raw)
        {
            var sim = _bootstrap.Sim;
            if (sim == null) return raw;
            float stepSize = Mathf.Max(1f, GridSnapStep);
            var cell = CorridorIndex.LocateCellAt(sim.State, raw.x, raw.y,
                                                  stepSize, SimVisualizer.CorridorDepthCells);
            if (cell is null) return raw;
            if (!CorridorIndex.IsCellRendered(sim.State, cell.Value.EdgeId,
                                              cell.Value.AlongCell, 0, cell.Value.Side, stepSize))
                return raw;
            var (cx, cy) = CorridorIndex.CellCenterWorld(sim.State, cell.Value.EdgeId,
                cell.Value.AlongCell, 0, cell.Value.Side, stepSize);
            return new Vector2(cx, cy);
        }

        /// <summary>Walk every road edge and collect (edgeId, alongCell, side) tuples whose
        /// cell center falls inside the AABB from <paramref name="startWorld"/> to
        /// <paramref name="currentWorld"/>. Only includes Voronoi-rendered cells. On the
        /// START edge, only the same side as the start cell is considered — otherwise the
        /// AABB tends to sweep across the road and pick up the opposite-side cells. Other
        /// edges allow both sides so corners / cross-street zoning still works.</summary>
        private void ComputeSelectionCells(Vector2 startWorld, Vector2 currentWorld,
                                           long startEdgeId, int startSide,
                                           List<(long, int, int)> output)
        {
            output.Clear();
            var sim = _bootstrap.Sim;
            if (sim == null) return;
            float stepSize = Mathf.Max(1f, GridSnapStep);
            float minX = Mathf.Min(startWorld.x, currentWorld.x);
            float maxX = Mathf.Max(startWorld.x, currentWorld.x);
            float minY = Mathf.Min(startWorld.y, currentWorld.y);
            float maxY = Mathf.Max(startWorld.y, currentWorld.y);

            foreach (var e in sim.State.RoadEdges.Values)
            {
                if (!sim.State.RoadNodes.TryGetValue(e.FromNodeId, out var fn)) continue;
                if (!sim.State.RoadNodes.TryGetValue(e.ToNodeId, out var tn)) continue;
                float dx = tn.Position.X - fn.Position.X;
                float dy = tn.Position.Y - fn.Position.Y;
                float len = Mathf.Sqrt(dx * dx + dy * dy);
                if (len < 1e-4f) continue;
                float ddx = dx / len, ddy = dy / len;
                float pdx = -ddy, pdy = ddx;
                float setback = e.WidthTiles * 0.5f;
                int cellsAlong = Mathf.CeilToInt(len / stepSize);

                for (int side = -1; side <= 1; side += 2)
                {
                    if (e.Id == startEdgeId && side != startSide) continue;
                    float perpCenter = side * (setback + 0.5f * stepSize);
                    for (int a = 0; a < cellsAlong; a++)
                    {
                        float alongCenter = (a + 0.5f) * stepSize;
                        if (alongCenter > len) continue;
                        float cx = fn.Position.X + alongCenter * ddx + perpCenter * pdx;
                        float cy = fn.Position.Y + alongCenter * ddy + perpCenter * pdy;
                        if (cx < minX || cx > maxX || cy < minY || cy > maxY) continue;
                        if (!CorridorIndex.IsCellRendered(sim.State, e.Id, a, 0, side, stepSize))
                            continue;
                        output.Add((e.Id, a, side));
                    }
                }
            }
        }

        private void HideZoneSelHighlight()
        {
            if (_zoneSelHighlightGO != null) _zoneSelHighlightGO.SetActive(false);
        }

        /// <summary>Update the filled-cell highlight mesh for the in-progress marquee.
        /// Lazy-creates the GO + mesh, parented under SimGrid so the corridor's local-XY
        /// coordinates map onto the world ground plane via the +90° X rotation.</summary>
        private void UpdateZoneSelHighlight(Vector2 startWorld, Vector2 currentWorld)
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

            ComputeSelectionCells(startWorld, currentWorld,
                                  _zoneSelStart!.Value.EdgeId, _zoneSelStart.Value.Side,
                                  _zoneSelHighlightCells);
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
                ZoneSelHighlightColor,
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
                    $"Zoning Residential: click again to commit selection  (Esc to cancel)",
                Mode.PaintZone when _pendingZoneType == ZoneType.Residential =>
                    $"Zoning Residential: click a corridor cell to start, click again to commit  (Esc to cancel)",
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
            _zoneSelStart = null;
            _zoneSelHighlightCells.Clear();
            HideZoneSelHighlight();
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
