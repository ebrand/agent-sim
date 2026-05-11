namespace AgentSim.Core.Types;

/// <summary>
/// The player-managed city. Holds zones, structures, agents, and the city treasury balance.
/// </summary>
public sealed class City
{
    /// <summary>City treasury balance in dollars. Starts at $500,000 (configurable).</summary>
    public int TreasuryBalance { get; set; }

    public Dictionary<long, Agent> Agents { get; } = new();
    public Dictionary<long, Structure> Structures { get; } = new();
    public Dictionary<long, Zone> Zones { get; } = new();

    public int Population => Agents.Count;

    /// <summary>Fractional births accumulator. When this exceeds 1.0, integer babies are born and the accumulator decreases.</summary>
    public double BirthFractionalAccumulator { get; set; }

    /// <summary>Number of consecutive end-of-months the treasury has been negative. Resets to 0
    /// when the treasury is non-negative at end-of-month. 6 → game-over per `feedback-loops.md`.</summary>
    public int ConsecutiveMonthsBankrupt { get; set; }

    /// <summary>Set true when the treasury has been negative for 6 consecutive end-of-months.
    /// Sim continues to tick — UI / player code consumes this flag.</summary>
    public bool GameOver { get; set; }
}
