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
}
