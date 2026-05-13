using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Default wages per (employer category × education tier). All categories share the same defaults
/// (each is independently adjustable per `levers.md`).
/// </summary>
public static class Wages
{
    public static int MonthlyWage(EducationTier tier) => tier switch
    {
        // Reset to M18 baseline. Roughly $24k-$84k annual range — fits small-business reality.
        EducationTier.Uneducated => 2_000,
        EducationTier.Primary => 3_500,
        EducationTier.Secondary => 4_500,
        EducationTier.College => 7_000,
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    /// <summary>M18: agent wages vary ±5% around the tier base at hire time.</summary>
    public const double WageVarianceMin = 0.95;
    public const double WageVarianceSpread = 0.10;
}
