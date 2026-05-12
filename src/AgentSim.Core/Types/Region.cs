namespace AgentSim.Core.Types;

/// <summary>
/// The top-level simulation entity. Holds environmental quality, the agent reservoir, and the regional goods reservoir.
/// </summary>
public sealed class Region
{
    public required double Climate { get; set; }
    public required double Nature { get; set; }

    public AgentReservoir AgentReservoir { get; } = new();

    /// <summary>
    /// Regional goods reservoir per manufactured good. Initialized empty; bootstrap stock is added when first
    /// residential zone is created.
    /// </summary>
    public Dictionary<ManufacturedGood, int> GoodsReservoir { get; } = new();

    /// <summary>
    /// Regional reservoir of processed goods (M14): processors overflow their buffers to the region
    /// when no local manufacturer is buying. Standalone manufacturers can pull from here as a
    /// fallback before resorting to imports.
    /// </summary>
    public Dictionary<ProcessedGood, int> ProcessedGoodsReservoir { get; } = new();
}

/// <summary>
/// The "well of souls" — agents waiting to be drawn into the city by immigration.
/// Total cap of 60,000 across all four tiers.
/// </summary>
public sealed class AgentReservoir
{
    public int Uneducated { get; set; }
    public int Primary { get; set; }
    public int Secondary { get; set; }
    public int College { get; set; }

    public int Total => Uneducated + Primary + Secondary + College;

    public int Get(EducationTier tier) => tier switch
    {
        EducationTier.Uneducated => Uneducated,
        EducationTier.Primary => Primary,
        EducationTier.Secondary => Secondary,
        EducationTier.College => College,
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    public void Decrement(EducationTier tier, int count = 1)
    {
        switch (tier)
        {
            case EducationTier.Uneducated: Uneducated -= count; break;
            case EducationTier.Primary: Primary -= count; break;
            case EducationTier.Secondary: Secondary -= count; break;
            case EducationTier.College: College -= count; break;
            default: throw new ArgumentOutOfRangeException(nameof(tier));
        }
        if (Get(tier) < 0) throw new InvalidOperationException($"Reservoir tier {tier} went negative");
    }

    public void Increment(EducationTier tier, int count = 1)
    {
        switch (tier)
        {
            case EducationTier.Uneducated: Uneducated += count; break;
            case EducationTier.Primary: Primary += count; break;
            case EducationTier.Secondary: Secondary += count; break;
            case EducationTier.College: College += count; break;
            default: throw new ArgumentOutOfRangeException(nameof(tier));
        }
    }
}
