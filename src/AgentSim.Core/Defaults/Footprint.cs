using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Tile footprint (in 1m × 1m tiles) per structure type. All structures are axis-aligned
/// rectangles. Houses 1×1, shops/most extractors 2-3×, civic/processors 4×, manufacturers 5×,
/// big institutional 6×.
/// </summary>
public static class Footprint
{
    /// <summary>(Width, Height) in tiles.</summary>
    public static (int W, int H) For(StructureType type) => type switch
    {
        // 1x1
        StructureType.House => (1, 1),
        StructureType.Park => (1, 1),

        // 2x2
        StructureType.Shop => (2, 2),
        StructureType.AffordableHousing => (2, 2),
        StructureType.ReforestationSite => (2, 2),

        // 3x3
        StructureType.Marketplace => (3, 3),
        StructureType.Restaurant => (3, 3),
        StructureType.Theater => (3, 3),
        StructureType.Apartment => (3, 3),
        StructureType.Townhouse => (3, 3),
        StructureType.ForestExtractor => (3, 3),
        StructureType.Farm => (3, 3),
        StructureType.Ranch => (3, 3),
        StructureType.CottonFarm => (3, 3),
        StructureType.Quarry => (3, 3),
        StructureType.SandPit => (3, 3),
        StructureType.CorporateHq => (3, 3),

        // 4x4
        StructureType.Condo => (4, 4),
        StructureType.PoliceStation => (4, 4),
        StructureType.Clinic => (4, 4),
        StructureType.PrimarySchool => (4, 4),
        StructureType.Generator => (4, 4),
        StructureType.Well => (4, 4),
        StructureType.Sawmill => (4, 4),
        StructureType.PulpMill => (4, 4),
        StructureType.Smelter => (4, 4),
        StructureType.Slaughterhouse => (4, 4),
        StructureType.Mill => (4, 4),
        StructureType.AggregatePlant => (4, 4),
        StructureType.SilicatePlant => (4, 4),
        StructureType.FuelRefinery => (4, 4),
        StructureType.PlasticPlant => (4, 4),
        StructureType.Ginnery => (4, 4),
        StructureType.ChalkPlant => (4, 4),
        StructureType.WetlandRestoration => (4, 4),

        // 5x5
        StructureType.HouseholdFactory => (5, 5),
        StructureType.BldgSuppliesFactory => (5, 5),
        StructureType.MetalGoodsFactory => (5, 5),
        StructureType.FoodPackingPlant => (5, 5),
        StructureType.ClothingFactory => (5, 5),
        StructureType.ConcretePlant => (5, 5),
        StructureType.PaperMill => (5, 5),
        StructureType.Brewery => (5, 5),
        StructureType.ElectronicsFactory => (5, 5),
        StructureType.PharmaPlant => (5, 5),
        StructureType.SecondarySchool => (5, 5),
        StructureType.FireStation => (5, 5),
        StructureType.ElectricityDistribution => (5, 5),
        StructureType.WaterDistribution => (5, 5),

        // 6x6
        StructureType.Hospital => (6, 6),
        StructureType.College => (6, 6),
        StructureType.TownHall => (6, 6),
        StructureType.Mine => (6, 6),
        StructureType.OilWell => (6, 6),

        _ => throw new ArgumentOutOfRangeException(nameof(type), $"No footprint defined for {type}"),
    };

    /// <summary>True when the structure occupies tiles in a zone (residential/commercial).
    /// Non-zoned structures (civic/industrial outside HQ/restoration) place anywhere on the map.</summary>
    public static bool IsZoned(StructureType type) =>
        type.Category() == StructureCategory.Residential
        || type.Category() == StructureCategory.Commercial;
}
