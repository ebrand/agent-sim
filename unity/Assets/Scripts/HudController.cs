#nullable enable
using System.Collections.Generic;
using AgentSim.Core.Sim.Mechanics;
using AgentSim.Core.Types;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace AgentSimUnity
{
    /// <summary>
    /// UI Toolkit HUD: top bar with time controls + key metrics, bottom-right toast stack for
    /// recent events, and a toggleable event log panel. Built entirely in code — no UXML/USS
    /// or PanelSettings asset to wire up. Inline styles, runtime PanelSettings.
    /// </summary>
    [RequireComponent(typeof(SimBootstrap))]
    public class HudController : MonoBehaviour
    {
        private SimBootstrap _bootstrap = null!;
        private UIDocument _doc = null!;
        private PanelSettings _panelSettings = null!;

        // Top-bar labels.
        private Label _dayLabel = null!;
        private Label _popLabel = null!;
        private Label _treasuryLabel = null!;
        private Label _climateLabel = null!;
        private Label _natureLabel = null!;
        private Label _serviceLabel = null!;
        private Label _scenarioLabel = null!;
        private Label _gameOverLabel = null!;

        // Time-control buttons.
        private Button _pauseBtn = null!;
        private Button _normalBtn = null!;
        private Button _fastBtn = null!;
        private Button _veryFastBtn = null!;
        private Button _stepBtn = null!;
        private Button _logBtn = null!;

        // Toast stack (bottom-right).
        private VisualElement _toastList = null!;
        private readonly List<ToastView> _toasts = new();
        private const float ToastDurationSeconds = 6f;
        private const int MaxToasts = 5;

        // Event log panel.
        private VisualElement _logPanel = null!;
        private ScrollView _logScroll = null!;
        private bool _logOpen;

        // Index into SimState.EventLog up to which we've already shown toasts.
        private int _lastEventIndex;

        // Colors.
        private static readonly Color BarBg = new(0.10f, 0.11f, 0.14f, 0.92f);
        private static readonly Color BarText = new(0.92f, 0.94f, 0.98f, 1f);
        private static readonly Color BtnBg = new(0.20f, 0.22f, 0.28f, 1f);
        private static readonly Color BtnActiveBg = new(0.30f, 0.55f, 0.85f, 1f);
        private static readonly Color WarnColor = new(0.95f, 0.75f, 0.30f, 1f);
        private static readonly Color CriticalColor = new(0.95f, 0.40f, 0.40f, 1f);
        private static readonly Color InfoColor = new(0.65f, 0.85f, 0.65f, 1f);

        void Awake()
        {
            _bootstrap = GetComponent<SimBootstrap>();
            BuildUIDocument();
            BuildLayout();
        }

        void Update()
        {
            if (_bootstrap.Sim is null) return;

            UpdateMetrics();
            UpdateSpeedButtons();
            DrainEvents();
            TickToasts(Time.unscaledDeltaTime);
            HandleHotkeys();
        }

        // ===== Setup =====

        private void BuildUIDocument()
        {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _panelSettings.referenceDpi = 96;
            _panelSettings.fallbackDpi = 96;
            _panelSettings.sortingOrder = 100;

            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = _panelSettings;
        }

        private void BuildLayout()
        {
            var root = _doc.rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;
            root.pickingMode = PickingMode.Ignore;  // let clicks through to scene/IMGUI sidebar

            // Without a theme stylesheet, UI Toolkit has no default font and text renders blank.
            // Load Unity's built-in legacy font and apply it at the root so all children inherit.
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font is null)
            {
                font = Font.CreateDynamicFontFromOSFont("Arial", 14);
            }
            if (font is not null)
            {
                root.style.unityFontDefinition = new StyleFontDefinition(font);
                root.style.unityFont = new StyleFont(font);
            }
            else
            {
                Debug.LogWarning("[HudController] No fallback font found — labels will be blank.");
            }

            root.Add(BuildTopBar());

            // Spacer fills middle so toasts/log dock to the bottom.
            var middle = new VisualElement { pickingMode = PickingMode.Ignore };
            middle.style.flexGrow = 1;
            middle.style.flexDirection = FlexDirection.Row;
            root.Add(middle);

            // Right column hosts toasts + log panel.
            var rightCol = new VisualElement { pickingMode = PickingMode.Ignore };
            rightCol.style.flexGrow = 1;
            rightCol.style.alignItems = Align.FlexEnd;
            rightCol.style.justifyContent = Justify.FlexEnd;
            middle.style.flexDirection = FlexDirection.RowReverse;
            middle.Add(rightCol);

            _toastList = new VisualElement { pickingMode = PickingMode.Ignore };
            _toastList.style.flexDirection = FlexDirection.ColumnReverse;
            _toastList.style.marginRight = 16;
            _toastList.style.marginBottom = 16;
            _toastList.style.minWidth = 320;
            _toastList.style.maxWidth = 420;
            rightCol.Add(_toastList);

            _logPanel = BuildLogPanel();
            _logPanel.style.display = DisplayStyle.None;
            root.Add(_logPanel);
        }

        private VisualElement BuildTopBar()
        {
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            bar.style.height = 56;
            bar.style.backgroundColor = BarBg;
            bar.style.paddingLeft = 12;
            bar.style.paddingRight = 12;
            bar.style.borderBottomColor = new Color(0, 0, 0, 0.4f);
            bar.style.borderBottomWidth = 1;
            // Push to start after the placement sidebar (220 wide).
            bar.style.marginLeft = 220;

            // Time controls. Plain text — emoji glyphs aren't in the legacy fallback font.
            _pauseBtn = MakeBtn("Pause", () => _bootstrap.CurrentSpeed = SimBootstrap.TimeSpeed.Paused);
            _normalBtn = MakeBtn("1x", () => _bootstrap.CurrentSpeed = SimBootstrap.TimeSpeed.Normal);
            _fastBtn = MakeBtn("4x", () => _bootstrap.CurrentSpeed = SimBootstrap.TimeSpeed.Fast);
            _veryFastBtn = MakeBtn("30x", () => _bootstrap.CurrentSpeed = SimBootstrap.TimeSpeed.VeryFast);
            _stepBtn = MakeBtn("Step", () => _bootstrap.Step());

            bar.Add(_pauseBtn);
            bar.Add(_normalBtn);
            bar.Add(_fastBtn);
            bar.Add(_veryFastBtn);
            bar.Add(_stepBtn);

            bar.Add(Spacer(16));
            _scenarioLabel = MakeLabel("", bold: true);
            _scenarioLabel.style.minWidth = 160;
            bar.Add(_scenarioLabel);
            bar.Add(Spacer(16));

            _dayLabel = MakeLabel("Day —");
            _dayLabel.style.minWidth = 110;
            bar.Add(_dayLabel);

            _popLabel = MakeLabel("Pop —");
            _popLabel.style.minWidth = 120;
            bar.Add(_popLabel);

            _treasuryLabel = MakeLabel("$—");
            _treasuryLabel.style.minWidth = 140;
            bar.Add(_treasuryLabel);

            _climateLabel = MakeLabel("Climate —");
            _climateLabel.style.minWidth = 110;
            bar.Add(_climateLabel);

            _natureLabel = MakeLabel("Nature —");
            _natureLabel.style.minWidth = 110;
            bar.Add(_natureLabel);

            _serviceLabel = MakeLabel("Service —");
            _serviceLabel.style.minWidth = 130;
            bar.Add(_serviceLabel);

            _gameOverLabel = MakeLabel("");
            _gameOverLabel.style.color = CriticalColor;
            _gameOverLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            bar.Add(_gameOverLabel);

            bar.Add(Flex());

            _logBtn = MakeBtn("Log (L)", ToggleLog);
            bar.Add(_logBtn);

            return bar;
        }

        private VisualElement BuildLogPanel()
        {
            var panel = new VisualElement();
            panel.style.position = Position.Absolute;
            panel.style.right = 0;
            panel.style.top = 56;
            panel.style.bottom = 0;
            panel.style.width = 480;
            panel.style.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 0.96f);
            panel.style.borderLeftColor = new Color(0, 0, 0, 0.5f);
            panel.style.borderLeftWidth = 1;
            panel.style.paddingLeft = 8;
            panel.style.paddingRight = 8;
            panel.style.paddingTop = 8;
            panel.style.paddingBottom = 8;
            panel.style.flexDirection = FlexDirection.Column;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 6;
            var title = MakeLabel("Event Log", bold: true);
            title.style.flexGrow = 1;
            header.Add(title);
            var close = MakeBtn("X", () => ToggleLog());
            header.Add(close);
            panel.Add(header);

            _logScroll = new ScrollView(ScrollViewMode.Vertical);
            _logScroll.style.flexGrow = 1;
            panel.Add(_logScroll);

            return panel;
        }

        // ===== Per-frame =====

        private void UpdateMetrics()
        {
            var s = _bootstrap.Sim!.State;
            _scenarioLabel.text = _bootstrap.ScenarioName;
            int month = (s.CurrentTick + 29) / 30;
            _dayLabel.text = $"Day {s.CurrentTick} (M{month})";
            int employed = 0;
            foreach (var a in s.City.Agents.Values)
                if (a.EmployerStructureId != null) employed++;
            _popLabel.text = $"Pop {s.City.Population} / Emp {employed}";
            _treasuryLabel.text = $"${s.City.TreasuryBalance:N0}";
            _climateLabel.text = $"Climate {(s.Region.Climate * 100):F0}%";
            _natureLabel.text = $"Nature {(s.Region.Nature * 100):F0}%";

            var snap = ServiceSatisfactionMechanic.Compute(s);
            float worst = Mathf.Min((float)snap.CivicPercent, (float)snap.HealthcarePercent,
                Mathf.Min((float)snap.UtilityPercent, (float)snap.EnvironmentalPercent));
            _serviceLabel.text = $"Worst svc {worst:F0}%";
            _serviceLabel.style.color = worst < 30 ? CriticalColor : worst < 60 ? WarnColor : BarText;

            _gameOverLabel.text = s.City.GameOver ? "  GAME OVER" : "";
        }

        private void UpdateSpeedButtons()
        {
            var speed = _bootstrap.CurrentSpeed;
            SetActive(_pauseBtn, speed == SimBootstrap.TimeSpeed.Paused);
            SetActive(_normalBtn, speed == SimBootstrap.TimeSpeed.Normal);
            SetActive(_fastBtn, speed == SimBootstrap.TimeSpeed.Fast);
            SetActive(_veryFastBtn, speed == SimBootstrap.TimeSpeed.VeryFast);
            _stepBtn.SetEnabled(_bootstrap.IsPaused);
        }

        private void DrainEvents()
        {
            var log = _bootstrap.Sim!.State.EventLog;
            // Account for cap-trimming (oldest events dropped): if the log shrunk, reset.
            if (_lastEventIndex > log.Count) _lastEventIndex = log.Count;

            for (int i = _lastEventIndex; i < log.Count; i++)
            {
                var ev = log[i];
                AddToast(ev);
                AppendToLog(ev);
            }
            _lastEventIndex = log.Count;
        }

        private void TickToasts(float dt)
        {
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var t = _toasts[i];
                t.Remaining -= dt;
                if (t.Remaining <= 0)
                {
                    _toastList.Remove(t.Element);
                    _toasts.RemoveAt(i);
                }
                else if (t.Remaining < 1.5f)
                {
                    t.Element.style.opacity = Mathf.Clamp01(t.Remaining / 1.5f);
                }
            }
        }

        private void HandleHotkeys()
        {
            if (Keyboard.current is null) return;
            if (Keyboard.current.lKey.wasPressedThisFrame) ToggleLog();
            if (Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                _bootstrap.CurrentSpeed = _bootstrap.IsPaused
                    ? SimBootstrap.TimeSpeed.Normal
                    : SimBootstrap.TimeSpeed.Paused;
            }
            if (Keyboard.current.digit1Key.wasPressedThisFrame) _bootstrap.CurrentSpeed = SimBootstrap.TimeSpeed.Normal;
            if (Keyboard.current.digit2Key.wasPressedThisFrame) _bootstrap.CurrentSpeed = SimBootstrap.TimeSpeed.Fast;
            if (Keyboard.current.digit3Key.wasPressedThisFrame) _bootstrap.CurrentSpeed = SimBootstrap.TimeSpeed.VeryFast;
            if (Keyboard.current.periodKey.wasPressedThisFrame && _bootstrap.IsPaused) _bootstrap.Step();
        }

        private void ToggleLog()
        {
            _logOpen = !_logOpen;
            _logPanel.style.display = _logOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (_logOpen) _logScroll.scrollOffset = new Vector2(0, float.MaxValue);
        }

        // ===== Toasts + log entries =====

        private void AddToast(SimEvent ev)
        {
            var card = new VisualElement();
            card.pickingMode = PickingMode.Ignore;
            card.style.backgroundColor = new Color(0.12f, 0.13f, 0.17f, 0.94f);
            card.style.borderLeftColor = ColorFor(ev.Severity);
            card.style.borderLeftWidth = 4;
            card.style.paddingTop = 6;
            card.style.paddingBottom = 6;
            card.style.paddingLeft = 10;
            card.style.paddingRight = 10;
            card.style.marginTop = 4;
            card.style.flexDirection = FlexDirection.Column;

            var header = MakeLabel($"[{ev.Category}] day {ev.Tick}", bold: true);
            header.style.color = ColorFor(ev.Severity);
            header.style.fontSize = 11;
            card.Add(header);

            var msg = MakeLabel(ev.Message);
            msg.style.fontSize = 12;
            msg.style.whiteSpace = WhiteSpace.Normal;
            card.Add(msg);

            _toastList.Add(card);
            _toasts.Add(new ToastView { Element = card, Remaining = ToastDurationSeconds });

            // Cap.
            while (_toasts.Count > MaxToasts)
            {
                _toastList.Remove(_toasts[0].Element);
                _toasts.RemoveAt(0);
            }
        }

        private void AppendToLog(SimEvent ev)
        {
            var line = new Label($"d{ev.Tick}  [{ev.Severity}] {ev.Category}: {ev.Message}");
            line.style.color = ColorFor(ev.Severity);
            line.style.fontSize = 11;
            line.style.whiteSpace = WhiteSpace.Normal;
            line.style.marginBottom = 2;
            _logScroll.Add(line);
            if (_logOpen) _logScroll.scrollOffset = new Vector2(0, float.MaxValue);
        }

        private static Color ColorFor(SimEventSeverity sev) => sev switch
        {
            SimEventSeverity.Critical => CriticalColor,
            SimEventSeverity.Warning => WarnColor,
            _ => InfoColor,
        };

        // ===== Builders =====

        private Button MakeBtn(string label, System.Action onClick)
        {
            var b = new Button(onClick) { text = label };
            b.style.backgroundColor = BtnBg;
            b.style.color = BarText;
            b.style.borderTopWidth = 0;
            b.style.borderBottomWidth = 0;
            b.style.borderLeftWidth = 0;
            b.style.borderRightWidth = 0;
            b.style.marginLeft = 2;
            b.style.marginRight = 2;
            b.style.paddingTop = 6;
            b.style.paddingBottom = 6;
            b.style.paddingLeft = 10;
            b.style.paddingRight = 10;
            b.style.minWidth = 48;
            b.style.fontSize = 13;
            b.style.unityTextAlign = TextAnchor.MiddleCenter;
            return b;
        }

        private static void SetActive(Button b, bool active)
        {
            b.style.backgroundColor = active ? BtnActiveBg : BtnBg;
        }

        private static Label MakeLabel(string text, bool bold = false)
        {
            var l = new Label(text);
            l.style.color = BarText;
            l.style.fontSize = 13;
            l.style.marginLeft = 4;
            l.style.marginRight = 4;
            l.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            return l;
        }

        private static VisualElement Spacer(int width)
        {
            var s = new VisualElement { pickingMode = PickingMode.Ignore };
            s.style.width = width;
            return s;
        }

        private static VisualElement Flex()
        {
            var s = new VisualElement { pickingMode = PickingMode.Ignore };
            s.style.flexGrow = 1;
            return s;
        }

        private sealed class ToastView
        {
            public VisualElement Element = null!;
            public float Remaining;
        }
    }
}
