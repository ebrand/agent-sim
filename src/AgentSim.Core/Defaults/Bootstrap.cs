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

    /// <summary>Immigrant starting savings by education tier.</summary>
    public static int StartingSavings(EducationTier tier) => tier switch
    {
        EducationTier.Uneducated => 1_800,
        EducationTier.Primary => 3_000,
        EducationTier.Secondary => 4_000,
        EducationTier.College => 6_000,
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };
}
