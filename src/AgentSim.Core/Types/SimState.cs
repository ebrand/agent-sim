using AgentSim.Core.Rng;

namespace AgentSim.Core.Types;

/// <summary>
/// Root simulation state. Everything mutable lives here (or under Region/City).
/// </summary>
public sealed class SimState
{
    public required Region Region { get; init; }
    public required City City { get; init; }
    public required Prng Prng { get; init; }
    public required SimConfig Config { get; init; }

    /// <summary>Day count from sim start. 1 tick = 1 day.</summary>
    public int CurrentTick { get; set; }

    /// <summary>Whether the bootstrap settler burst has fired (only triggers once, on first residential zone creation).</summary>
    public bool BootstrapFired { get; set; }

    // ID counters
    private long _nextAgentId = 1;
    private long _nextStructureId = 1;
    private long _nextZoneId = 1;

    public long AllocateAgentId() => _nextAgentId++;
    public long AllocateStructureId() => _nextStructureId++;
    public long AllocateZoneId() => _nextZoneId++;

    /// <summary>Append-only log of player-facing events (placements, construction, game over, ...).
    /// Capped at <see cref="EventLogMax"/> entries — oldest dropped first to avoid unbounded growth.</summary>
    public List<SimEvent> EventLog { get; } = new();

    private const int EventLogMax = 10_000;

    public void LogEvent(SimEventSeverity severity, string category, string message)
    {
        EventLog.Add(new SimEvent(CurrentTick, severity, category, message));
        if (EventLog.Count > EventLogMax)
        {
            EventLog.RemoveRange(0, EventLog.Count - EventLogMax);
        }
    }
}
