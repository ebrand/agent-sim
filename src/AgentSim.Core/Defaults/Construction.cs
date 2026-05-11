using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// One-time construction cost per structure type, deducted from the city treasury at placement.
/// Per design discussion (M11): capital expense should dominate operating expense — roughly 10×
/// monthly upkeep for treasury-funded structures. This makes placement a real decision and prevents
/// spam-building. Commercial / industrial pick numbers by category scale.
///
/// Placement is rejected (throws InvalidOperationException) when treasury can't cover the cost —
/// the city can't start a building it can't afford. Residential is exempt: it auto-spawns inside
/// zones and is not user-placed.
/// </summary>
public static class Construction
{
    // Treasury-funded (~10× monthly upkeep)
    public const int PoliceStation = 150_000;
    public const int FireStation = 150_000;
    public const int TownHall = 500_000;
    public const int Clinic = 250_000;
    public const int Hospital = 1_200_000;
    public const int PrimarySchool = 250_000;
    public const int SecondarySchool = 500_000;
    public const int College = 1_000_000;
    public const int Generator = 300_000;
    public const int Well = 200_000;
    public const int AffordableHousing = 100_000;

    // Commercial (no upkeep — revenue-funded; sized by relative gameplay scale)
    public const int Shop = 100_000;
    public const int Marketplace = 300_000;

    // Industrial — extractors / processors / manufacturers / storage
    public const int ForestExtractor = 150_000;
    public const int Mine = 150_000;
    public const int CoalMine = 150_000;
    public const int Quarry = 150_000;
    public const int SandPit = 150_000;
    public const int Farm = 150_000;
    public const int Sawmill = 250_000;
    public const int Smelter = 250_000;
    public const int Mill = 250_000;
    public const int AggregatePlant = 250_000;
    public const int SilicatePlant = 250_000;
    public const int FuelRefinery = 250_000;
    public const int HouseholdFactory = 400_000;
    public const int BldgSuppliesFactory = 400_000;
    public const int MetalGoodsFactory = 400_000;
    public const int FoodPackingPlant = 400_000;
    public const int ClothingFactory = 400_000;
    public const int ConcretePlant = 400_000;
    public const int GlassWorks = 400_000;
    public const int Storage = 150_000;
    public const int FuelStorage = 150_000;

    /// <summary>
    /// One-time construction cost in dollars for the given structure type. 0 for residential
    /// (auto-spawned, not user-placed) and any type without a specified cost.
    /// </summary>
    public static int Cost(StructureType type) => type switch
    {
        StructureType.PoliceStation => PoliceStation,
        StructureType.FireStation => FireStation,
        StructureType.TownHall => TownHall,
        StructureType.Clinic => Clinic,
        StructureType.Hospital => Hospital,
        StructureType.PrimarySchool => PrimarySchool,
        StructureType.SecondarySchool => SecondarySchool,
        StructureType.College => College,
        StructureType.Generator => Generator,
        StructureType.Well => Well,
        StructureType.AffordableHousing => AffordableHousing,
        StructureType.Shop => Shop,
        StructureType.Marketplace => Marketplace,
        StructureType.ForestExtractor => ForestExtractor,
        StructureType.Mine => Mine,
        StructureType.CoalMine => CoalMine,
        StructureType.Quarry => Quarry,
        StructureType.SandPit => SandPit,
        StructureType.Farm => Farm,
        StructureType.Sawmill => Sawmill,
        StructureType.Smelter => Smelter,
        StructureType.Mill => Mill,
        StructureType.AggregatePlant => AggregatePlant,
        StructureType.SilicatePlant => SilicatePlant,
        StructureType.FuelRefinery => FuelRefinery,
        StructureType.HouseholdFactory => HouseholdFactory,
        StructureType.BldgSuppliesFactory => BldgSuppliesFactory,
        StructureType.MetalGoodsFactory => MetalGoodsFactory,
        StructureType.FoodPackingPlant => FoodPackingPlant,
        StructureType.ClothingFactory => ClothingFactory,
        StructureType.ConcretePlant => ConcretePlant,
        StructureType.GlassWorks => GlassWorks,
        StructureType.Storage => Storage,
        StructureType.FuelStorage => FuelStorage,
        _ => 0,  // residential (auto-spawned), restoration (TBD), others not yet costed
    };
}
