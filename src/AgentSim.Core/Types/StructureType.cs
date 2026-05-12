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
    OilWell,           // M14b: petroleum extraction for the Oil industry
    Ranch,             // M14c: livestock under the Agriculture industry
    CottonFarm,        // M14c: raw cotton under the Agriculture industry

    // Industrial — processors
    Sawmill,
    Smelter,
    Mill,
    AggregatePlant,
    SilicatePlant,
    FuelRefinery,
    PulpMill,          // M14b: Wood → Pulp
    PlasticPlant,      // M14b: Petroleum → Plastic
    Slaughterhouse,    // M14c: Livestock → Meat
    Ginnery,           // M14c: RawCotton → Cotton
    ChalkPlant,        // M14d: Rock → Chalk

    // Industrial — manufacturers
    HouseholdFactory,
    BldgSuppliesFactory,
    MetalGoodsFactory,
    FoodPackingPlant,
    ClothingFactory,
    ConcretePlant,
    PaperMill,         // M14b: Pulp → Paper
    GlassWorks,        // M14b: deprecated — Silicate is now consumed directly by other manufacturers

    // Industrial — storage (vestigial after M14; no longer placeable, no role in production)
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
            or StructureType.Quarry or StructureType.SandPit or StructureType.Farm
            or StructureType.OilWell or StructureType.Ranch
            or StructureType.CottonFarm => StructureCategory.IndustrialExtractor,

        StructureType.Sawmill or StructureType.Smelter or StructureType.Mill
            or StructureType.AggregatePlant or StructureType.SilicatePlant
            or StructureType.FuelRefinery or StructureType.PulpMill
            or StructureType.PlasticPlant or StructureType.Slaughterhouse
            or StructureType.Ginnery or StructureType.ChalkPlant => StructureCategory.IndustrialProcessor,

        StructureType.HouseholdFactory or StructureType.BldgSuppliesFactory
            or StructureType.MetalGoodsFactory or StructureType.FoodPackingPlant
            or StructureType.ClothingFactory or StructureType.ConcretePlant
            or StructureType.GlassWorks or StructureType.PaperMill => StructureCategory.IndustrialManufacturer,

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
