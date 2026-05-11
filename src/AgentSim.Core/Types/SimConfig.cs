namespace AgentSim.Core.Types;

/// <summary>
/// Game-start configuration. Values typically chosen by the player at new-game UI.
/// </summary>
public sealed record SimConfig
{
    public ulong Seed { get; init; } = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public double Climate { get; init; } = 0.7;
    public double Nature { get; init; } = 0.7;

    public int StartingTreasury { get; init; } = 500_000;

    public int RegionalReservoirSize { get; init; } = 60_000;
}
