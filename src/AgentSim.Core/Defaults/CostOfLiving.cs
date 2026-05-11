using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Cost-of-living percentages per `economy.md`. These are fractions of gross wage spent on commercial goods/services.
/// For settlers and unemployed agents (wage = $0), COL is calculated against the wage they WOULD be earning at
/// their tier (used to size their starting savings).
/// </summary>
public static class CostOfLiving
{
    public const double FoodFraction = 0.15;
    public const double ClothingFraction = 0.05;
    public const double HouseholdFraction = 0.10;
    public const double EntertainmentFraction = 0.05;

    public const double TotalFraction =
        FoodFraction + ClothingFraction + HouseholdFraction + EntertainmentFraction;  // 0.35

    /// <summary>
    /// Total monthly COL dollar amount for an agent at the given education tier.
    /// Based on the tier's default wage (not the agent's current wage) — settlers and unemployed
    /// still budget COL against their tier expectation.
    /// </summary>
    public static int MonthlyCol(EducationTier tier) =>
        (int)(Wages.MonthlyWage(tier) * TotalFraction);
}
