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

    /// <summary>
    /// Whether worst-of service emigration (M9) is active. Default true — services
    /// (civic / healthcare / education / utility) drive monthly per-agent emigration rolls based
    /// on the lowest service satisfaction. Set false for tests that need to isolate other
    /// mechanics from random service-pressure emigration.
    /// </summary>
    public bool ServiceEmigrationEnabled { get; init; } = true;
}
