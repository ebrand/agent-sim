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
    /// Boots the sim and ticks it at the player-chosen speed. Time control is exposed via
    /// <see cref="CurrentSpeed"/>; the HUD reads/writes it. The Inspector knobs remain as
    /// starting state.
    /// </summary>
    [RequireComponent(typeof(SimVisualizer))]
    [RequireComponent(typeof(PlacementController))]
    [RequireComponent(typeof(HudController))]
    public class SimBootstrap : MonoBehaviour
    {
        public enum ScenarioChoice { Minimal, SelfSustaining, MidGame }
        public enum TimeSpeed { Paused, Normal, Fast, VeryFast }

        [Header("Scenario")]
        public ScenarioChoice Scenario = ScenarioChoice.Minimal;

        [Header("Time")]
        [Tooltip("Starting speed. The HUD overrides this at runtime.")]
        public TimeSpeed StartingSpeed = TimeSpeed.Normal;

        private Sim? _sim;
        private string _scenarioName = "";
        private float _tickAccumulator;

        public Sim? Sim => _sim;
        public string ScenarioName => _scenarioName;

        /// <summary>Current player-selected speed. The HUD writes this.</summary>
        public TimeSpeed CurrentSpeed { get; set; } = TimeSpeed.Normal;

        /// <summary>Ticks per real-time second at the current speed.</summary>
        public float TicksPerSecond => CurrentSpeed switch
        {
            TimeSpeed.Paused => 0f,
            TimeSpeed.Normal => 1f,
            TimeSpeed.Fast => 4f,
            TimeSpeed.VeryFast => 30f,
            _ => 0f,
        };

        public bool IsPaused => CurrentSpeed == TimeSpeed.Paused;

        /// <summary>Advance the sim by one tick. No-op if sim is null or game-over.</summary>
        public void Step()
        {
            if (_sim is null) return;
            if (_sim.State.City.GameOver) return;
            _sim.Tick(1);
        }

        void Awake()
        {
            // All sim tilemaps are parented to this transform. Force it to the world origin
            // so cell coords == world coords; otherwise ScreenToWorldPoint -> tile mapping
            // breaks (mouse picks a different tile than the ghost paints).
            transform.position = Vector3.zero;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        void Start()
        {
            CurrentSpeed = StartingSpeed;
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

            float tps = TicksPerSecond;
            if (tps > 0f)
            {
                _tickAccumulator += Time.deltaTime * tps;
                while (_tickAccumulator >= 1f)
                {
                    _sim.Tick(1);
                    _tickAccumulator -= 1f;
                }
            }
            else
            {
                _tickAccumulator = 0f;
            }
        }

        private static (Sim sim, string name) LoadScenario(ScenarioChoice c) => c switch
        {
            ScenarioChoice.SelfSustaining => (Scenarios.BuildSelfSustaining(), "B: Self-sustaining"),
            ScenarioChoice.MidGame => (Scenarios.BuildMidGame(), "C: Mid-game"),
            _ => (Scenarios.BuildMinimal(), "A: Minimal"),
        };
    }
}
