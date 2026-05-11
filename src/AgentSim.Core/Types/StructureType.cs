namespace AgentSim.Core.Types;

/// <summary>
/// Every concrete structure type in the simulation. Categorized by the StructureCategory of each.
/// </summary>
public enum StructureType
{
    // Residential
    House,
    Apartment,
    Townhouse,
    Condo,
    AffordableHousing,

    // Commercial
    Shop,
    Marketplace,
    CorporateHq,  // M12: industrial supply chain headquarters

    // Industrial — extractors
    ForestExtractor,
    Mine,
    CoalMine,
    Quarry,
    SandPit,
    Farm,

    // Industrial — processors
    Sawmill,
    Smelter,
    Mill,
    AggregatePlant,
    SilicatePlant,
    FuelRefinery,

    // Industrial — manufacturers
    HouseholdFactory,
    BldgSuppliesFactory,
    MetalGoodsFactory,
    FoodPackingPlant,
    ClothingFactory,
    ConcretePlant,
    GlassWorks,

    // Industrial — storage
    Storage,
    FuelStorage,

    // Civic
    PoliceStation,
    FireStation,
    TownHall,

    // Healthcare
    Clinic,
    Hospital,

    // Education
    PrimarySchool,
    SecondarySchool,
    College,

    // Utility
    Generator,
    Well,

    // Restoration
    Park,
    ReforestationSite,
    WetlandRestoration,
}

public enum StructureCategory
{
    Residential,
    Commercial,
    IndustrialExtractor,
    IndustrialProcessor,
    IndustrialManufacturer,
    IndustrialStorage,
    Civic,
    Healthcare,
    Education,
    Utility,
    Restoration,
}

public static class StructureTypeExtensions
{
    public static StructureCategory Category(this StructureType type) => type switch
    {
        StructureType.House or StructureType.Apartment or StructureType.Townhouse
            or StructureType.Condo or StructureType.AffordableHousing => StructureCategory.Residential,

        StructureType.Shop or StructureType.Marketplace
            or StructureType.CorporateHq => StructureCategory.Commercial,

        StructureType.ForestExtractor or StructureType.Mine or StructureType.CoalMine
            or StructureType.Quarry or StructureType.SandPit or StructureType.Farm => StructureCategory.IndustrialExtractor,

        StructureType.Sawmill or StructureType.Smelter or StructureType.Mill
            or StructureType.AggregatePlant or StructureType.SilicatePlant
            or StructureType.FuelRefinery => StructureCategory.IndustrialProcessor,

        StructureType.HouseholdFactory or StructureType.BldgSuppliesFactory
            or StructureType.MetalGoodsFactory or StructureType.FoodPackingPlant
            or StructureType.ClothingFactory or StructureType.ConcretePlant
            or StructureType.GlassWorks => StructureCategory.IndustrialManufacturer,

        StructureType.Storage or StructureType.FuelStorage => StructureCategory.IndustrialStorage,

        StructureType.PoliceStation or StructureType.FireStation
            or StructureType.TownHall => StructureCategory.Civic,

        StructureType.Clinic or StructureType.Hospital => StructureCategory.Healthcare,

        StructureType.PrimarySchool or StructureType.SecondarySchool
            or StructureType.College => StructureCategory.Education,

        StructureType.Generator or StructureType.Well => StructureCategory.Utility,

        StructureType.Park or StructureType.ReforestationSite
            or StructureType.WetlandRestoration => StructureCategory.Restoration,

        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}
