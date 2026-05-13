#nullable enable
using AgentSim.Core.Calibration;
using AgentSim.Core.Sim;
using AgentSim.Core.Sim.Mechanics;
using AgentSim.Core.Types;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AgentSimUnity
{
    /// <summary>
    /// Phase B smoke test: load AgentSim.Core via the netstandard2.1 plugin DLL, create a sim,
    /// tick it at a configurable speed, and surface key state through Debug.Log + an OnGUI overlay.
    ///
    /// Drop on any GameObject in a scene and press Play. Phase C will replace the OnGUI overlay
    /// with a real Tilemap + UI Toolkit dashboard.
    /// </summary>
    [RequireComponent(typeof(SimVisualizer))]
    [RequireComponent(typeof(PlacementController))]
    public class SimBootstrap : MonoBehaviour
    {
        public enum ScenarioChoice { Minimal, SelfSustaining, MidGame }

        [Header("Scenario")]
        public ScenarioChoice Scenario = ScenarioChoice.Minimal;

        [Header("Ticking")]
        [Tooltip("Ticks (days) per real-time second. 1 = realtime, 30 = 1 month/sec.")]
        public float TicksPerSecond = 10f;

        [Tooltip("Auto-run on Start. If false, press Space to step one tick.")]
        public bool AutoRun = true;

        private Sim? _sim;
        private string _scenarioName = "";
        private float _tickAccumulator;

        /// <summary>Underlying sim — null until Start() runs.</summary>
        public Sim? Sim => _sim;
        public string ScenarioName => _scenarioName;

        void Start()
        {
            (_sim, _scenarioName) = LoadScenario(Scenario);
            Debug.Log($"[SimBootstrap] Loaded {_scenarioName}. " +
                      $"Pop={_sim.State.City.Population}, " +
                      $"Treasury=${_sim.State.City.TreasuryBalance:N0}, " +
                      $"Structures={_sim.State.City.Structures.Count}");
        }

        void Update()
        {
            if (_sim == null) return;
            if (_sim.State.City.GameOver) return;

            if (AutoRun)
            {
                _tickAccumulator += Time.deltaTime * TicksPerSecond;
                while (_tickAccumulator >= 1f)
                {
                    _sim.Tick(1);
                    _tickAccumulator -= 1f;
                }
            }
            else if (Keyboard.current is not null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                _sim.Tick(1);
            }
        }

        void OnGUI()
        {
            if (_sim == null) return;

            var s = _sim.State;
            var snap = ServiceSatisfactionMechanic.Compute(s);
            var worst = Mathf.Min((float)snap.CivicPercent, (float)snap.HealthcarePercent,
                Mathf.Min((float)snap.UtilityPercent, (float)snap.EnvironmentalPercent));

            // Offset to clear the placement sidebar (width 220) + mode strip.
            const int hudX = 230;
            const int hudY = 40;
            GUI.Box(new Rect(hudX, hudY, 360, 200), $"AgentSim — {_scenarioName}");
            GUILayout.BeginArea(new Rect(hudX + 10, hudY + 20, 340, 180));
            GUILayout.Label($"Day {s.CurrentTick} (M{(s.CurrentTick + 29) / 30})");
            GUILayout.Label($"Pop {s.City.Population}   Employed {EmployedCount(s)}");
            GUILayout.Label($"Treasury ${s.City.TreasuryBalance:N0}");
            GUILayout.Label($"Climate {(s.Region.Climate * 100):F0}%   Nature {(s.Region.Nature * 100):F0}%");
            GUILayout.Label($"Worst service {worst:F0}%");
            GUILayout.Label($"Founding phase: {FoundingPhaseLabel(s)}");
            GUILayout.Label($"Tick rate: {TicksPerSecond:F1}/s");
            GUILayout.Label(_sim.State.City.GameOver ? "GAME OVER" : "Running");
            GUILayout.EndArea();
        }

        private static int EmployedCount(SimState s)
        {
            int n = 0;
            foreach (var a in s.City.Agents.Values)
                if (a.EmployerStructureId != null) n++;
            return n;
        }

        private static string FoundingPhaseLabel(SimState s) =>
            AgentSim.Core.Defaults.FoundingPhase.IsActive(s) ? "yes" : "no";

        private static (Sim sim, string name) LoadScenario(ScenarioChoice c) => c switch
        {
            ScenarioChoice.SelfSustaining => (Scenarios.BuildSelfSustaining(), "B: Self-sustaining"),
            ScenarioChoice.MidGame => (Scenarios.BuildMidGame(), "C: Mid-game"),
            _ => (Scenarios.BuildMinimal(), "A: Minimal"),
        };
    }
}
