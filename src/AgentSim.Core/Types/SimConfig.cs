namespace AgentSim.Core.Types;

/// <summary>
/// Game-start configuration. Values typically chosen by the player at new-game UI.
/// </summary>
public sealed record SimConfig
{
    public ulong Seed { get; init; } = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public double Climate { get; init; } = 0.7;
    public double Nature { get; init; } = 0.7;

    /// <summary>
    /// Starting city treasury. Sized for the alpha-1 calibration target: a modest founding city
    /// (1 each of police / clinic / primary school / generator / well) costs ~$1.15M to construct
    /// (M11: 10× monthly upkeep per structure) and runs at a ~$65k/month deficit while pre-commercial,
    /// so $1.8M leaves ~6 months of full-pay operation post-construction.
    /// </summary>
    public int StartingTreasury { get; init; } = 1_800_000;

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
    public bool ServiceEmigrationEnabled { get; set; } = true;

    /// <summary>
    /// Whether monthly immigration (M-calibration) is active. Default true — vacant labor demand
    /// attracts agents from the reservoir each month, bounded by housing capacity. Disable in tests
    /// that need a static post-bootstrap population.
    /// </summary>
    public bool ImmigrationEnabled { get; set; } = true;

    /// <summary>
    /// Whether founding-phase subsidies (first 12 months) are active. Default true. Disable
    /// in tests that need the standard tax/upkeep flow regardless of sim age.
    /// </summary>
    public bool FoundingPhaseEnabled { get; set; } = true;

    /// <summary>When true, the initial settler burst is gated on the city having both a
    /// Generator and a Well placed (in addition to a residential zone). When false (default
    /// for test fixtures), bootstrap auto-fires on first residential zone creation. Set true
    /// in the "Empty" scenario so players must build utilities to start their city.</summary>
    public bool GateBootstrapOnUtilities { get; set; } = false;

    /// <summary>When true, all RequiredConstructionTicks are forced to 0 — structures become
    /// operational the tick after placement. Speeds up dev / playtest iteration. Default false.</summary>
    public bool InstantConstruction { get; set; } = false;
}
