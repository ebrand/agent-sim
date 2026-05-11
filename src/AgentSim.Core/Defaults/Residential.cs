using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Residential structure defaults — capacity, rent, construction recipes.
/// </summary>
public static class Residential
{
    /// <summary>Construction duration in ticks (90 = 3 months) for all residential types.</summary>
    public const int BuildDurationTicks = 90;

    public static int Capacity(StructureType type) => type switch
    {
        StructureType.House => 4,
        StructureType.Apartment => 40,
        StructureType.Townhouse => 12,
        StructureType.Condo => 25,
        StructureType.AffordableHousing => 40,
        _ => throw new ArgumentOutOfRangeException(nameof(type), $"{type} is not a residential structure"),
    };

    public static int MonthlyRent(StructureType type) => type switch
    {
        StructureType.House => 800,
        StructureType.Apartment => 1_400,
        StructureType.Townhouse => 1_800,
        StructureType.Condo => 2_800,
        StructureType.AffordableHousing => 500,
        _ => throw new ArgumentOutOfRangeException(nameof(type), $"{type} is not a residential structure"),
    };

    /// <summary>Construction recipe in manufactured-good units.</summary>
    public static IReadOnlyDictionary<ManufacturedGood, int> ConstructionRecipe(StructureType type)
    {
        return type switch
        {
            StructureType.House => new Dictionary<ManufacturedGood, int>
            {
                [ManufacturedGood.BldgSupplies] = 10,
                [ManufacturedGood.Concrete] = 5,
                [ManufacturedGood.GlassGoods] = 2,
            },
            StructureType.Apartment => new Dictionary<ManufacturedGood, int>
            {
                [ManufacturedGood.BldgSupplies] = 30,
                [ManufacturedGood.Concrete] = 50,
                [ManufacturedGood.MetalGoods] = 30,
                [ManufacturedGood.GlassGoods] = 15,
            },
            StructureType.Townhouse => new Dictionary<ManufacturedGood, int>
            {
                [ManufacturedGood.BldgSupplies] = 18,
                [ManufacturedGood.Concrete] = 25,
                [ManufacturedGood.MetalGoods] = 15,
                [ManufacturedGood.GlassGoods] = 8,
            },
            StructureType.Condo => new Dictionary<ManufacturedGood, int>
            {
                [ManufacturedGood.BldgSupplies] = 25,
                [ManufacturedGood.Concrete] = 40,
                [ManufacturedGood.MetalGoods] = 25,
                [ManufacturedGood.GlassGoods] = 12,
            },
            StructureType.AffordableHousing => new Dictionary<ManufacturedGood, int>
            {
                [ManufacturedGood.BldgSupplies] = 25,
                [ManufacturedGood.Concrete] = 40,
                [ManufacturedGood.MetalGoods] = 25,
                [ManufacturedGood.GlassGoods] = 10,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };
    }
}
