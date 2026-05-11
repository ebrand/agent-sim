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

    /// <summary>
    /// Initial size of the regional agent reservoir. Lower than the total agent cap (60,000)
    /// so the city has room to grow via both immigration (drains reservoir) and births
    /// (require headroom under the cap). Default 2,000.
    /// </summary>
    public int InitialReservoirSize { get; init; } = 2_000;
}
