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
        private UnityTilemap _backgroundTilemap = null!;
        private UnityTilemap _zoneTilemap = null!;
        private UnityTilemap _structureTilemap = null!;
        private UnityTilemap _landValueTilemap = null!;
        private UnityTilemap _hoverTilemap = null!;
        private Tile _gridTile = null!;

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
            PaintGridBackground();
            SetupCamera();
        }

        void Update()
        {
            HandleCameraControl();
            UpdateGridVisibility();
            UpdateHoverHighlight();
        }

        // Warm-yellow translucent overlay on the footprint of whichever structure the mouse is
        // over. Skipped while placement mode is active so it doesn't fight with the ghost.
        private void UpdateHoverHighlight()
        {
            if (_bootstrap.Sim is null) return;

            var placement = GetComponent<PlacementController>();
            if (placement != null && placement.IsActive)
            {
                if (_lastHoveredStructureId.HasValue) ClearHoverPaint();
                _lastHoveredStructureId = null;
                return;
            }

            var hoveredId = ResolveHoveredStructureId();
            if (hoveredId == _lastHoveredStructureId) return;

            ClearHoverPaint();
            if (hoveredId is long id) PaintHoverFor(id);
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

        private void PaintHoverFor(long structureId)
        {
            if (!_bootstrap.Sim!.State.City.Structures.TryGetValue(structureId, out var s)) return;
            if (s.X < 0 || s.Y < 0) return;
            var (w, h) = Footprint.For(s.Type);
            var color = new Color(1f, 0.92f, 0.45f, 0.38f);
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
        private const float CameraInitialDistance = 140f;
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

            HandleClick();
        }

        void OnGUI()
        {
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
            GUILayout.Label($"Cash: ${s.CashBalance:N0}");
            GUILayout.Label($"Jobs: {s.EmployeeIds.Count}/{s.JobSlotsTotal()}");
            if (GUILayout.Button("Close")) _selectedStructureId = null;
            GUILayout.EndArea();
        }

        // Lightweight tooltip floating next to the cursor — just type + status + jobs.
        // Full detail comes from clicking (the detail panel above).
        private void DrawHoverTooltip()
        {
            if (_selectedStructureId.HasValue) return;  // detail panel is showing
            if (!_lastHoveredStructureId.HasValue) return;
            if (!_bootstrap.Sim!.State.City.Structures.TryGetValue(_lastHoveredStructureId.Value, out var s)) return;
            if (Mouse.current is null) return;

            // Convert Input-System screen Y (origin bottom-left) to IMGUI Y (origin top-left).
            var mp = Mouse.current.position.ReadValue();
            float guiX = mp.x + 16f;
            float guiY = Screen.height - mp.y + 16f;
            var rect = new Rect(guiX, guiY, 220f, 78f);
            // Clamp so the tooltip stays on-screen.
            if (rect.x + rect.width > Screen.width) rect.x = Screen.width - rect.width - 4;
            if (rect.y + rect.height > Screen.height) rect.y = Screen.height - rect.height - 4;
            GUI.Box(rect, $"{s.Type} #{s.Id}");
            GUILayout.BeginArea(new Rect(rect.x + 8, rect.y + 20, rect.width - 16, rect.height - 24));
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
            GUILayout.EndArea();
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

            _backgroundTilemap = MakeTilemap(gridGo.transform, "Background", sortingOrder: -10);
            _zoneTilemap = MakeTilemap(gridGo.transform, "Zones", sortingOrder: 0);
            _structureTilemap = MakeTilemap(gridGo.transform, "Structures", sortingOrder: 1);
            _landValueTilemap = MakeTilemap(gridGo.transform, "LandValue", sortingOrder: 2);
            _hoverTilemap = MakeTilemap(gridGo.transform, "Hover", sortingOrder: 3);
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
            var target = new Vector3(16f, 0f, 16f);
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

        // ===== Interaction =====

        private void HandleClick()
        {
            if (Mouse.current is null) return;
            if (!Mouse.current.leftButton.wasPressedThisFrame) return;

            // Yield to PlacementController if it's in an active mode.
            var placement = GetComponent<PlacementController>();
            if (placement != null && placement.IsActive) return;

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
