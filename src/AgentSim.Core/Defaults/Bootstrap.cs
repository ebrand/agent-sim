using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Bootstrap stock added to the regional goods reservoir when the first residential zone is created.
/// Sized to build housing for the founding 50 settlers.
/// </summary>
public static class Bootstrap
{
    public static IReadOnlyDictionary<ManufacturedGood, int> BootstrapGoodsStock { get; } =
        new Dictionary<ManufacturedGood, int>
        {
            [ManufacturedGood.BldgSupplies] = 200,
            [ManufacturedGood.Concrete] = 100,
            [ManufacturedGood.GlassGoods] = 40,
            [ManufacturedGood.MetalGoods] = 0,
        };

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
    /// Founders' starting savings — for the one-time bootstrap settler burst. Substantially
    /// higher than regular immigrant savings to cover the 90-day window during which the player
    /// builds the first commercial structure.
    ///
    /// Rationale: regular immigrant savings ($1,800–$6,000) are sized for "1 month of expenses"
    /// assuming commercial infrastructure exists. Bootstrap settlers arrive before any commercial
    /// exists; commercial takes 90 days to build; without a founders' bonus, all settlers would
    /// run out of savings and emigrate before the first shop opens. The founders' bonus provides
    /// ~5 months of cushion (rent + utilities = $1,000/mo for tier-1 housing; $5,000 ≈ 5 months).
    ///
    /// Flat across all tiers since pre-commercial expenses (rent + utilities, no COL) are the same.
    /// </summary>
    public const int FoundersStartingSavings = 5_000;
}
