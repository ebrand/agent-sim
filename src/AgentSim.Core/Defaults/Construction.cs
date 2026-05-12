using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// One-time construction cost per structure type, deducted from the city treasury at placement.
/// Per design discussion (M11): each structure type defines its own upkeep AND its own
/// construction multiplier. Treasury-funded structures compute cost = monthly upkeep × multiplier,
/// where the multiplier reflects how capital-intensive that building type is.
///
///   - Most institutional buildings (police, fire, schools, hospitals, utilities): 10× — big
///     capital outlay relative to operating budget.
///   - Affordable housing: 2× — residential-style construction, cheap to build, expensive to
///     run as social infrastructure.
///
/// Commercial / industrial structures have no monthly upkeep (they fund themselves via revenue),
/// so they use flat construction costs sized by category scale.
///
/// Placement is rejected (throws InvalidOperationException) when treasury can't cover the cost.
/// Residential is exempt (auto-spawned in zones, not user-placed).
/// </summary>
public static class Construction
{
    // Flat costs for commercial / industrial (no monthly upkeep to derive from).
    public const int Shop = 100_000;
    public const int Marketplace = 300_000;
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

    // M14b additions
    public const int OilWell = 250_000;        // oil drilling — more expensive than simple mine
    public const int PulpMill = 200_000;        // wood pulping
    public const int PlasticPlant = 350_000;    // petrochemical
    public const int PaperMill = 300_000;       // paper manufacturer

    // M14c additions (Agriculture diversification)
    public const int Ranch = 150_000;           // livestock extractor
    public const int CottonFarm = 150_000;      // cotton extractor
    public const int Slaughterhouse = 250_000;  // livestock processor
    public const int Ginnery = 250_000;         // cotton processor

    /// <summary>
    /// Construction multiplier (× monthly upkeep) for treasury-funded structures. Lets each
    /// structure type set its own capital-to-operating ratio. Returns 0 for non-treasury-funded.
    /// </summary>
    public static int Multiplier(StructureType type) => type switch
    {
        // Big capital outlay for institutional buildings.
        StructureType.PoliceStation => 10,
        StructureType.FireStation => 10,
        StructureType.TownHall => 10,
        StructureType.Clinic => 10,
        StructureType.Hospital => 10,
        StructureType.PrimarySchool => 10,
        StructureType.SecondarySchool => 10,
        StructureType.College => 10,
        StructureType.Generator => 10,
        StructureType.Well => 10,

        // Lighter construction for residential-style social infrastructure.
        StructureType.AffordableHousing => 2,

        _ => 0,
    };

    /// <summary>
    /// One-time construction cost in dollars for the given structure type. Computed as
    /// monthly upkeep × multiplier for treasury-funded structures; flat values for commercial /
    /// industrial. Returns 0 for residential (auto-spawned) and any type without a cost.
    /// </summary>
    public static int Cost(StructureType type)
    {
        // Treasury-funded: cost derives from upkeep × multiplier.
        if (Upkeep.IsTreasuryFunded(type))
        {
            return Upkeep.MonthlyCost(type) * Multiplier(type);
        }

        // Commercial / industrial: flat values.
        return type switch
        {
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
            // M14b
            StructureType.OilWell => OilWell,
            StructureType.PulpMill => PulpMill,
            StructureType.PlasticPlant => PlasticPlant,
            StructureType.PaperMill => PaperMill,
            // M14c (Agriculture diversification)
            StructureType.Ranch => Ranch,
            StructureType.CottonFarm => CottonFarm,
            StructureType.Slaughterhouse => Slaughterhouse,
            StructureType.Ginnery => Ginnery,
            _ => 0,
        };
    }
}
