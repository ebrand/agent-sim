using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// M18: rent and utility are functions of the agent's education tier (proxy for income), not
/// the residence structure type. Each tier has a fixed rent; utility is 10% of rent.
///
/// AffordableHousing is an exception — it charges a single discounted rate regardless of tier,
/// preserving its role as subsidized housing.
/// </summary>
public static class Rent
{
    /// <summary>Fixed monthly rent per education tier. ~22-23% of tier wage — leaves room for the
    /// 50% sector COL plus modest savings.</summary>
    public static int MonthlyRent(EducationTier tier) => tier switch
    {
        EducationTier.Uneducated => 450,
        EducationTier.Primary => 800,
        EducationTier.Secondary => 1_000,
        EducationTier.College => 1_550,
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    /// <summary>Affordable-housing flat rent (overrides tier-based rent for AH residents).</summary>
    public const int AffordableHousingRent = 300;

    /// <summary>Utility share of rent (10%).</summary>
    public const double UtilityFractionOfRent = 0.10;

    /// <summary>Rent owed by an agent based on their tier and where they live.</summary>
    public static int RentForAgent(Agent agent, Structure residence)
    {
        if (residence.Type == StructureType.AffordableHousing) return AffordableHousingRent;
        return MonthlyRent(agent.EducationTier);
    }

    /// <summary>Utility owed by an agent — 10% of rent.</summary>
    public static int UtilityForAgent(Agent agent, Structure residence)
    {
        return (int)(RentForAgent(agent, residence) * UtilityFractionOfRent);
    }
}
