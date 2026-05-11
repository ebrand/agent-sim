using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Commercial structure defaults — job slots per tier, structure value, monthly utility cost.
/// Construction recipe and build duration are in `Residential.cs` (same 3-month uniform duration);
/// here we only encode the operational parameters.
/// </summary>
public static class Commercial
{
    /// <summary>Job slot count per education tier for each commercial structure type.</summary>
    public static IReadOnlyDictionary<EducationTier, int> JobSlots(StructureType type) => type switch
    {
        StructureType.Shop => new Dictionary<EducationTier, int>
        {
            [EducationTier.College] = 1,
            [EducationTier.Secondary] = 1,
            [EducationTier.Primary] = 2,
            [EducationTier.Uneducated] = 1,
        },
        StructureType.Marketplace => new Dictionary<EducationTier, int>
        {
            [EducationTier.College] = 2,
            [EducationTier.Secondary] = 3,
            [EducationTier.Primary] = 5,
            [EducationTier.Uneducated] = 5,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(type), $"{type} is not a commercial structure"),
    };

    /// <summary>Total worker count = sum of JobSlots.</summary>
    public static int TotalJobSlots(StructureType type) => JobSlots(type).Values.Sum();

    /// <summary>Structure value used to compute property tax (2% per month per `levers.md`).</summary>
    public static int StructureValue(StructureType type) => type switch
    {
        StructureType.Shop => 200_000,
        StructureType.Marketplace => 400_000,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>Monthly utility cost (placeholder per `economy.md`).</summary>
    public static int MonthlyUtility(StructureType type) => type switch
    {
        StructureType.Shop => 2_000,
        StructureType.Marketplace => 4_000,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>
    /// Construction recipe — same scale as residential, slightly bigger for marketplaces.
    /// (For M3, build duration is the uniform 3 months = 90 ticks; this is the goods cost.)
    /// </summary>
    public static IReadOnlyDictionary<ManufacturedGood, int> ConstructionRecipe(StructureType type) => type switch
    {
        StructureType.Shop => new Dictionary<ManufacturedGood, int>
        {
            [ManufacturedGood.BldgSupplies] = 15,
            [ManufacturedGood.Concrete] = 30,
            [ManufacturedGood.MetalGoods] = 15,
            [ManufacturedGood.GlassGoods] = 15,
        },
        StructureType.Marketplace => new Dictionary<ManufacturedGood, int>
        {
            [ManufacturedGood.BldgSupplies] = 25,
            [ManufacturedGood.Concrete] = 50,
            [ManufacturedGood.MetalGoods] = 25,
            [ManufacturedGood.GlassGoods] = 20,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
