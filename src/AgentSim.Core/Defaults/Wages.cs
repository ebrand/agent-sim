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
        EducationTier.Uneducated => 2_000,
        EducationTier.Primary => 3_500,
        EducationTier.Secondary => 4_500,
        EducationTier.College => 7_000,
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };
}
