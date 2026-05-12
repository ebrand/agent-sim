using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Industrial structure defaults — extractors, processors, manufacturers, and storage.
/// All extractor / processor / manufacturer structures share a uniform daily production
/// capacity (10 units/day at full staffing). Storage is sized for many days of throughput.
/// </summary>
public static class Industrial
{
    /// <summary>Maximum daily output per industrial structure at 100% staffing.</summary>
    /// <summary>
    /// Max units a structure produces per day at 100% staffing. Calibration target (M14b): a
    /// modest single-Mfg chain (1 extractor + 1 processor + 1 standalone Mfg) should be roughly
    /// break-even at this scale. Throughput is the dominant lever — overhead per structure is
    /// largely fixed.
    /// </summary>
    public const int MaxOutputPerDay = 25;

    /// <summary>Default internal storage capacity for extractor / processor / manufacturer structures.</summary>
    public const int InternalStorageCapacity = 1_000;

    /// <summary>Default storage capacity for the (final) Storage / FuelStorage structures.</summary>
    public const int FinalStorageCapacity = 10_000;

    /// <summary>Storage's pass-through fee: buys from manufacturer at 80% of price, sells at 100%.</summary>
    public const double StoragePassThroughRate = 0.80;

    /// <summary>Standard worker count for non-storage industrial structures.</summary>
    public const int StandardWorkerCount = 100;

    /// <summary>Worker count for storage structures (much smaller than producer structures).</summary>
    public const int StorageWorkerCount = 10;

    // ===== Production mapping =====

    /// <summary>Raw material an extractor produces, or null if not an extractor.</summary>
    public static RawMaterial? ExtractorOutput(StructureType type) => type switch
    {
        StructureType.ForestExtractor => RawMaterial.Wood,
        StructureType.Mine => RawMaterial.IronOre,
        StructureType.CoalMine => RawMaterial.Coal,
        StructureType.Quarry => RawMaterial.Rock,
        StructureType.SandPit => RawMaterial.Sand,
        StructureType.Farm => RawMaterial.Crops,
        StructureType.OilWell => RawMaterial.Petroleum,
        StructureType.Ranch => RawMaterial.Livestock,         // M14c
        StructureType.CottonFarm => RawMaterial.RawCotton,    // M14c
        _ => null,
    };

    /// <summary>(input raw material, output processed good) for processor types, or null.</summary>
    public static (RawMaterial Input, ProcessedGood Output)? ProcessorRecipe(StructureType type) => type switch
    {
        StructureType.Sawmill => (RawMaterial.Wood, ProcessedGood.Lumber),
        StructureType.Smelter => (RawMaterial.IronOre, ProcessedGood.Steel),
        StructureType.Mill => (RawMaterial.Crops, ProcessedGood.Grain),
        StructureType.AggregatePlant => (RawMaterial.Rock, ProcessedGood.Aggregate),
        StructureType.SilicatePlant => (RawMaterial.Sand, ProcessedGood.Silicate),
        StructureType.FuelRefinery => (RawMaterial.Petroleum, ProcessedGood.Fuel),
        StructureType.PulpMill => (RawMaterial.Wood, ProcessedGood.Pulp),
        StructureType.PlasticPlant => (RawMaterial.Petroleum, ProcessedGood.Plastic),
        StructureType.Slaughterhouse => (RawMaterial.Livestock, ProcessedGood.Meat),     // M14c
        StructureType.Ginnery => (RawMaterial.RawCotton, ProcessedGood.Cotton),           // M14c
        StructureType.ChalkPlant => (RawMaterial.Rock, ProcessedGood.Chalk),              // M14d
        _ => null,
    };

    /// <summary>
    /// Manufacturer recipe: a list of (input, units-per-output) and a single output good.
    /// M14b: multi-input recipes. A manufacturer can only produce when ALL inputs are available
    /// in proportion. Returns null for non-manufacturer types.
    /// </summary>
    public static (IReadOnlyList<(ProcessedGood Input, int Units)> Inputs, ManufacturedGood Output)? ManufacturerRecipe(StructureType type) => type switch
    {
        // Multi-input manufacturers per design: BldgSupplies = Wood + Metal + Plastic, etc.
        StructureType.HouseholdFactory => (
            new (ProcessedGood, int)[] {
                (ProcessedGood.Lumber, 5),
                (ProcessedGood.Steel, 2),
                (ProcessedGood.Silicate, 1),   // glass component
                (ProcessedGood.Plastic, 1),
            }, ManufacturedGood.Household),
        StructureType.BldgSuppliesFactory => (
            new (ProcessedGood, int)[] {
                (ProcessedGood.Lumber, 6),
                (ProcessedGood.Steel, 2),
                (ProcessedGood.Plastic, 1),
            }, ManufacturedGood.BldgSupplies),
        StructureType.MetalGoodsFactory => (
            new (ProcessedGood, int)[] {
                (ProcessedGood.Steel, 3),
                (ProcessedGood.Plastic, 1),
            }, ManufacturedGood.MetalGoods),
        StructureType.FoodPackingPlant => (
            new (ProcessedGood, int)[] {
                (ProcessedGood.Grain, 3),
                (ProcessedGood.Meat, 2),       // M14c: Slaughterhouse output
                (ProcessedGood.Plastic, 1),
                (ProcessedGood.Silicate, 1),   // glass jars
            }, ManufacturedGood.Food),
        StructureType.ClothingFactory => (
            new (ProcessedGood, int)[] {
                (ProcessedGood.Cotton, 2),     // M14c: was Textiles; Cotton from Ginnery
                (ProcessedGood.Plastic, 1),    // synthetic fibers
            }, ManufacturedGood.Clothing),
        StructureType.ConcretePlant => (
            new (ProcessedGood, int)[] {
                (ProcessedGood.Aggregate, 4),
                (ProcessedGood.Chalk, 1),       // M14d: cement needs lime/chalk
            }, ManufacturedGood.Concrete),
        StructureType.PaperMill => (
            new (ProcessedGood, int)[] {
                (ProcessedGood.Pulp, 3),
            }, ManufacturedGood.Paper),
        // GlassWorks dropped per M14b design — Silicate is consumed directly by other manufacturers.
        _ => null,
    };

    // ===== Prices =====

    public static int RawMaterialPrice(RawMaterial good) => good switch
    {
        RawMaterial.Wood => 2,
        RawMaterial.IronOre => 4,
        RawMaterial.Crops => 1,
        RawMaterial.Rock => 3,
        RawMaterial.Sand => 2,
        RawMaterial.Coal => 2,
        RawMaterial.Petroleum => 5,    // M14b
        RawMaterial.Livestock => 4,    // M14c
        RawMaterial.RawCotton => 2,    // M14c
        _ => throw new ArgumentOutOfRangeException(nameof(good)),
    };

    public static int ProcessedGoodPrice(ProcessedGood good) => good switch
    {
        ProcessedGood.Lumber => 4,
        ProcessedGood.Steel => 8,
        ProcessedGood.Grain => 3,
        ProcessedGood.Textiles => 2,
        ProcessedGood.Aggregate => 6,
        ProcessedGood.Silicate => 4,
        ProcessedGood.Fuel => 8,
        ProcessedGood.Plastic => 10,   // M14b
        ProcessedGood.Pulp => 3,       // M14b
        ProcessedGood.Meat => 7,       // M14c
        ProcessedGood.Cotton => 4,     // M14c
        ProcessedGood.Chalk => 5,      // M14d
        _ => throw new ArgumentOutOfRangeException(nameof(good)),
    };

    public static int ManufacturedGoodPrice(ManufacturedGood good) => good switch
    {
        // M14b calibration: roughly 2× prior values to keep manufacturer margins above overhead
        // at small chain scale. The exact balance depends on staffing + throughput; tune as
        // the sim runs and gameplay emerges.
        ManufacturedGood.Household => 100,    // was 40 — needs Lumber+Steel+Silicate+Plastic
        ManufacturedGood.BldgSupplies => 120, // was 72 — needs Lumber+Steel+Plastic
        ManufacturedGood.MetalGoods => 90,    // was 48
        ManufacturedGood.Food => 50,          // was 24
        ManufacturedGood.Clothing => 30,      // was 8 — significantly underpriced before
        ManufacturedGood.Concrete => 80,      // was 60
        ManufacturedGood.GlassGoods => 80,    // unchanged (GlassWorks dropped; vestigial)
        ManufacturedGood.Paper => 30,         // was 15
        _ => throw new ArgumentOutOfRangeException(nameof(good)),
    };

    // ===== Structure values (drives property tax at 0.5% / month) =====

    public static int StructureValue(StructureType type) => type switch
    {
        // Extractors
        StructureType.ForestExtractor => 80_000,
        StructureType.Mine => 150_000,
        StructureType.CoalMine => 150_000,
        StructureType.Quarry => 100_000,
        StructureType.SandPit => 80_000,
        StructureType.Farm => 100_000,
        StructureType.OilWell => 250_000,           // M14b
        StructureType.Ranch => 100_000,             // M14c
        StructureType.CottonFarm => 100_000,        // M14c
        // Processors
        StructureType.Sawmill => 200_000,
        StructureType.Smelter => 400_000,
        StructureType.Mill => 200_000,
        StructureType.AggregatePlant => 200_000,
        StructureType.SilicatePlant => 200_000,
        StructureType.FuelRefinery => 400_000,
        StructureType.PulpMill => 200_000,          // M14b
        StructureType.PlasticPlant => 400_000,      // M14b
        StructureType.Slaughterhouse => 200_000,    // M14c
        StructureType.Ginnery => 200_000,           // M14c
        StructureType.ChalkPlant => 200_000,        // M14d
        // Manufacturers
        StructureType.HouseholdFactory => 300_000,
        StructureType.BldgSuppliesFactory => 300_000,
        StructureType.MetalGoodsFactory => 500_000,
        StructureType.FoodPackingPlant => 300_000,
        StructureType.ClothingFactory => 300_000,
        StructureType.ConcretePlant => 300_000,
        StructureType.GlassWorks => 300_000,
        StructureType.PaperMill => 250_000,         // M14b
        // Storage
        StructureType.Storage => 80_000,
        StructureType.FuelStorage => 80_000,
        _ => throw new ArgumentOutOfRangeException(nameof(type), $"{type} is not an industrial structure"),
    };

    // ===== Monthly utility cost =====

    public static int MonthlyUtility(StructureType type) => type switch
    {
        // Extractors
        StructureType.ForestExtractor => 1_000,
        StructureType.Mine => 1_500,
        StructureType.CoalMine => 1_500,
        StructureType.Quarry => 1_000,
        StructureType.SandPit => 1_000,
        StructureType.Farm => 1_000,
        StructureType.OilWell => 3_000,             // M14b
        StructureType.Ranch => 1_000,               // M14c
        StructureType.CottonFarm => 1_000,          // M14c
        // Processors
        StructureType.Sawmill => 3_000,
        StructureType.Smelter => 5_000,
        StructureType.Mill => 3_000,
        StructureType.AggregatePlant => 3_000,
        StructureType.SilicatePlant => 3_000,
        StructureType.FuelRefinery => 5_000,
        StructureType.PulpMill => 3_000,            // M14b
        StructureType.PlasticPlant => 5_000,        // M14b
        StructureType.Slaughterhouse => 3_000,      // M14c
        StructureType.Ginnery => 3_000,             // M14c
        StructureType.ChalkPlant => 3_000,          // M14d
        // Manufacturers
        StructureType.HouseholdFactory => 4_000,
        StructureType.BldgSuppliesFactory => 4_000,
        StructureType.MetalGoodsFactory => 6_000,
        StructureType.FoodPackingPlant => 4_000,
        StructureType.ClothingFactory => 4_000,
        StructureType.ConcretePlant => 4_000,
        StructureType.GlassWorks => 4_000,
        StructureType.PaperMill => 3_000,           // M14b
        // Storage — reduced from $1,000 to $500 so storage breaks even at modest scale
        // (2-3 manufacturers feeding 1 storage is enough to cover utility + property tax)
        StructureType.Storage => 500,
        StructureType.FuelStorage => 500,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    // ===== Job slots =====

    /// <summary>
    /// Job slot count per tier. Standard 100-worker mix: 15 college / 20 secondary / 40 primary / 25 uneducated.
    /// Storage uses a 10-worker mix: 1 college / 2 secondary / 4 primary / 3 uneducated.
    /// </summary>
    public static IReadOnlyDictionary<EducationTier, int> JobSlots(StructureType type)
    {
        if (type == StructureType.Storage || type == StructureType.FuelStorage)
        {
            return new Dictionary<EducationTier, int>
            {
                [EducationTier.College] = 1,
                [EducationTier.Secondary] = 2,
                [EducationTier.Primary] = 4,
                [EducationTier.Uneducated] = 3,
            };
        }

        // Standard 100-worker mix for extractor / processor / manufacturer
        return new Dictionary<EducationTier, int>
        {
            [EducationTier.College] = 15,
            [EducationTier.Secondary] = 20,
            [EducationTier.Primary] = 40,
            [EducationTier.Uneducated] = 25,
        };
    }

    // ===== Convenience predicates =====

    public static bool IsExtractor(StructureType type) =>
        type.Category() == StructureCategory.IndustrialExtractor;

    public static bool IsProcessor(StructureType type) =>
        type.Category() == StructureCategory.IndustrialProcessor;

    public static bool IsManufacturer(StructureType type) =>
        type.Category() == StructureCategory.IndustrialManufacturer;

    public static bool IsStorage(StructureType type) =>
        type.Category() == StructureCategory.IndustrialStorage;

    public static bool IsIndustrial(StructureType type)
    {
        var cat = type.Category();
        return cat == StructureCategory.IndustrialExtractor
            || cat == StructureCategory.IndustrialProcessor
            || cat == StructureCategory.IndustrialManufacturer
            || cat == StructureCategory.IndustrialStorage;
    }
}
