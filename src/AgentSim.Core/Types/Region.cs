namespace AgentSim.Core.Types;

/// <summary>
/// The top-level simulation entity. Holds environmental quality and the agent reservoir.
/// M16: no named goods; per-good regional reservoirs are gone. Manufacturers buffer their
/// own output and stop producing when full.
/// </summary>
public sealed class Region
{
    public required double Climate { get; set; }
    public required double Nature { get; set; }

    public AgentReservoir AgentReservoir { get; } = new();
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
