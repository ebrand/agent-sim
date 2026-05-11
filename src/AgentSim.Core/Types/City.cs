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

    /// <summary>Number of consecutive months the city has been in partial-pay mode (treasury
    /// below total upkeep at the day-1 check). Resets to 0 when the city pays full upkeep at
    /// day 1. 6 → game-over per `feedback-loops.md`.</summary>
    public int ConsecutiveMonthsBankrupt { get; set; }

    /// <summary>Set true when ConsecutiveMonthsBankrupt reaches 6. Halts the simulation: subsequent
    /// Tick() calls become no-ops until the flag is cleared by external code (e.g. UI reset).</summary>
    public bool GameOver { get; set; }

    /// <summary>Fraction of monthly upkeep that was funded on day 1 of the CURRENT month. 1.0 when
    /// the city paid full upkeep; less than 1.0 when treasury was insufficient and partial-pay
    /// kicked in. Drives service capacity scaling for the month — services run at this fraction
    /// of their nominal capacity. Reset to 1.0 at the start of each new month's upkeep payment.</summary>
    public double UpkeepFundingFraction { get; set; } = 1.0;
}
