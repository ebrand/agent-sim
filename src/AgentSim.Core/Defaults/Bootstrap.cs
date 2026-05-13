using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Bootstrap settler burst. M16: bootstrap no longer stages construction goods — houses are spawned
/// free (M17 will properly cost construction through the Construction commercial sector).
/// </summary>
public static class Bootstrap
{
    /// <summary>Immigrant starting savings by education tier (post-bootstrap immigrants).</summary>
    public static int StartingSavings(EducationTier tier) => tier switch
    {
        EducationTier.Uneducated => 1_800,
        EducationTier.Primary => 3_000,
        EducationTier.Secondary => 4_000,
        EducationTier.College => 6_000,
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    /// <summary>
    /// Founders' starting savings — for the one-time bootstrap settler burst. Tier-scaled so
    /// each tier survives ~5 months of pre-commercial expenses (rent + utility under the M18
    /// per-tier rent model). Tuned so primary settlers (higher rent) don't emigrate sooner than
    /// uneducated, preserving the "settlers survive ~5 months without jobs" invariant.
    /// </summary>
    public static int FoundersStartingSavings(EducationTier tier) => tier switch
    {
        // Tuned for ~5 months of rent + utility + 50% sector COL while waiting for jobs.
        EducationTier.Uneducated => 8_000,
        EducationTier.Primary => 13_000,
        EducationTier.Secondary => 17_000,
        EducationTier.College => 26_000,
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };
}
