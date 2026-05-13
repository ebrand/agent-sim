using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Cost-of-living split across commercial sectors. M16: sector buckets replace per-good demand.
/// Agents pay each sector fraction of gross wage into that sector's commercial revenue pool;
/// disposable is saved.
///
/// Fractions per the M16 design:
///   Food          20%
///   Retail        10%
///   Entertainment  5%
///   Disposable     5%   (goes to agent savings — not spent)
/// </summary>
public static class CostOfLiving
{
    // Invariant-driven calibration: total commercial COL ~36% leaves agents a healthy buffer.
    // Net wage after 5% tax = 0.95 × wage. Required spend = rent + util + sectorCol ≈ 0.55-0.60 ×
    // wage at low tiers. Leaves ≥15% slack to grow savings.
    public const double FoodFraction = 0.16;
    public const double RetailFraction = 0.12;
    public const double EntertainmentFraction = 0.10;
    public const double DisposableFraction = 0.07;

    /// <summary>
    /// Fraction of wage spent on commercial sectors (food + retail + entertainment).
    /// Excludes disposable (which stays with the agent).
    /// </summary>
    public const double SpendFraction =
        FoodFraction + RetailFraction + EntertainmentFraction;  // 0.35

    /// <summary>
    /// Total fraction of wage allocated to COL bookkeeping (including disposable).
    /// </summary>
    public const double TotalFraction = SpendFraction + DisposableFraction;  // 0.40

    /// <summary>Total monthly COL dollar amount for an agent at the given education tier
    /// (commercial sectors only — excludes disposable).</summary>
    public static int MonthlyCol(EducationTier tier) =>
        (int)(Wages.MonthlyWage(tier) * SpendFraction);

    /// <summary>Per-sector dollar amount for the given tier.</summary>
    public static int SectorAmount(EducationTier tier, CommercialSector sector)
    {
        var wage = Wages.MonthlyWage(tier);
        return sector switch
        {
            CommercialSector.Food => (int)(wage * FoodFraction),
            CommercialSector.Retail => (int)(wage * RetailFraction),
            CommercialSector.Entertainment => (int)(wage * EntertainmentFraction),
            CommercialSector.Construction => 0,  // construction is funded by treasury, not agents
            _ => throw new ArgumentOutOfRangeException(nameof(sector)),
        };
    }

    /// <summary>Per-sector total across all agents in the city.</summary>
    public static int DisposableAmount(EducationTier tier) =>
        (int)(Wages.MonthlyWage(tier) * DisposableFraction);
}
