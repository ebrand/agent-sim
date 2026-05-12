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
    /// Max units a structure produces per day at 100% staffing. Smoke-test calibration: at 25
    /// the chain produced too little revenue to cover wages; at 100 a 10-worker structure runs
    /// 3,000 units/month which gives reasonable revenue against ~$36k wages. Tune further as
    /// gameplay emerges.
    /// </summary>
    public const int MaxOutputPerDay = 100;

    /// <summary>Default internal storage capacity for extractor / processor / manufacturer structures.</summary>
    public const int InternalStorageCapacity = 1_000;

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
    /// Manufacturer recipe: lists of (input, units-per-output) for both processed-good and
    /// manufactured-good inputs, plus a single output manufactured good. M14e: a manufacturer
    /// can consume other manufacturers' outputs, enabling chains like
    /// Pulp → Paper → Books or similar deeper supply structures.
    ///
    /// Returns null for non-manufacturer types.
    /// </summary>
    public static (
        IReadOnlyList<(ProcessedGood Input, int Units)> ProcessedInputs,
        IReadOnlyList<(ManufacturedGood Input, int Units)> ManufacturedInputs,
        ManufacturedGood Output
    )? ManufacturerRecipe(StructureType type) => type switch
    {
        StructureType.HouseholdFactory => (
            new (ProcessedGood, int)[] {
                (ProcessedGood.Lumber, 5),
                (ProcessedGood.Steel, 2),
                (ProcessedGood.Silicate, 1),
                (ProcessedGood.Plastic, 1),
            }, Array.Empty<(ManufacturedGood, int)>(), ManufacturedGood.Household),
        StructureType.BldgSuppliesFactory => (
            new (ProcessedGood, int)[] {
                (ProcessedGood.Lumber, 6),
                (ProcessedGood.Steel, 2),
                (ProcessedGood.Plastic, 1),
            }, Array.Empty<(ManufacturedGood, int)>(), ManufacturedGood.BldgSupplies),
        StructureType.MetalGoodsFactory => (
            new (ProcessedGood, int)[] {
                (ProcessedGood.Steel, 3),
                (ProcessedGood.Plastic, 1),
            }, Array.Empty<(ManufacturedGood, int)>(), ManufacturedGood.MetalGoods),
        StructureType.FoodPackingPlant => (
            new (ProcessedGood, int)[] {
                (ProcessedGood.Grain, 3),
                (ProcessedGood.Meat, 2),
                (ProcessedGood.Plastic, 1),
                (ProcessedGood.Silicate, 1),
            }, Array.Empty<(ManufacturedGood, int)>(), ManufacturedGood.Food),
        StructureType.ClothingFactory => (
            new (ProcessedGood, int)[] {
                (ProcessedGood.Cotton, 2),
                (ProcessedGood.Plastic, 1),
            }, Array.Empty<(ManufacturedGood, int)>(), ManufacturedGood.Clothing),
        StructureType.ConcretePlant => (
            new (ProcessedGood, int)[] {
                (ProcessedGood.Aggregate, 4),
                (ProcessedGood.Chalk, 1),
            }, Array.Empty<(ManufacturedGood, int)>(), ManufacturedGood.Concrete),
        StructureType.PaperMill => (
            new (ProcessedGood, int)[] {
                (ProcessedGood.Pulp, 3),
            }, Array.Empty<(ManufacturedGood, int)>(), ManufacturedGood.Paper),
        // M14e: Printer chains up off PaperMill's output. Consumes Paper (manufactured) + Plastic (processed).
        StructureType.Printer => (
            new (ProcessedGood, int)[] {
                (ProcessedGood.Plastic, 1),    // book binding / cover plastic
            }, new (ManufacturedGood, int)[] {
                (ManufacturedGood.Paper, 5),   // 5 sheets of paper per book
            }, ManufacturedGood.Books),
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
        RawMaterial.Petroleum => 5,    // M14b
        RawMaterial.Livestock => 4,    // M14c
        RawMaterial.RawCotton => 2,    // M14c
        _ => throw new ArgumentOutOfRangeException(nameof(good)),
    };

    public static int ProcessedGoodPrice(ProcessedGood good) => good switch
    {
        // M14 calibration: processed-good prices ~3× prior values. Mfg pays HQ at these prices,
        // so this is the lever that brings HQ revenue closer to its wage/utility overhead at the
        // current 10-worker scale. Consumer dollar COL stays the same; consumers just buy fewer
        // units per dollar at the resulting higher manufactured-good prices.
        ProcessedGood.Lumber => 12,
        ProcessedGood.Steel => 24,
        ProcessedGood.Grain => 9,
        ProcessedGood.Textiles => 6,
        ProcessedGood.Aggregate => 18,
        ProcessedGood.Silicate => 12,
        ProcessedGood.Fuel => 24,
        ProcessedGood.Plastic => 30,
        ProcessedGood.Pulp => 9,
        ProcessedGood.Meat => 21,
        ProcessedGood.Cotton => 12,
        ProcessedGood.Chalk => 15,
        _ => throw new ArgumentOutOfRangeException(nameof(good)),
    };

    public static int ManufacturedGoodPrice(ManufacturedGood good) => good switch
    {
        // M14 calibration: manufactured-good prices ~3× initial M14b values to match the 3×
        // processed-good price bump and give manufacturers a margin above overhead.
        ManufacturedGood.Household => 300,
        ManufacturedGood.BldgSupplies => 360,
        ManufacturedGood.MetalGoods => 270,
        ManufacturedGood.Food => 150,
        ManufacturedGood.Clothing => 90,
        ManufacturedGood.Concrete => 240,
        ManufacturedGood.Paper => 90,
        ManufacturedGood.Books => 240,
        _ => throw new ArgumentOutOfRangeException(nameof(good)),
    };

    // ===== Structure values (drives property tax at 0.5% / month) =====

    public static int StructureValue(StructureType type) => type switch
    {
        // Extractors
        StructureType.ForestExtractor => 80_000,
        StructureType.Mine => 150_000,
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
        StructureType.PaperMill => 250_000,         // M14b
        StructureType.Printer => 300_000,           // M14e
        _ => throw new ArgumentOutOfRangeException(nameof(type), $"{type} is not an industrial structure"),
    };

    // ===== Monthly utility cost =====

    public static int MonthlyUtility(StructureType type) => type switch
    {
        // Extractors
        StructureType.ForestExtractor => 1_000,
        StructureType.Mine => 1_500,
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
        StructureType.PaperMill => 3_000,           // M14b
        StructureType.Printer => 4_000,             // M14e
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    // ===== Job slots =====

    /// <summary>
    /// M14 calibration: 10-worker mix per industrial structure (1 college / 2 secondary /
    /// 4 primary / 3 uneducated). The prior 100-worker mix was sized for a 50k-population
    /// mid-game city; in a 50-settler bootstrap, ~5% staffing made production effectively zero.
    /// At 10 workers each, a chain with 5 industrial structures plus a manufacturer fits within
    /// the settler pool while leaving room for commercial and service-sector employment.
    /// </summary>
    public static IReadOnlyDictionary<EducationTier, int> JobSlots(StructureType type)
    {
        return new Dictionary<EducationTier, int>
        {
            [EducationTier.College] = 1,
            [EducationTier.Secondary] = 2,
            [EducationTier.Primary] = 4,
            [EducationTier.Uneducated] = 3,
        };
    }

    // ===== Convenience predicates =====

    public static bool IsExtractor(StructureType type) =>
        type.Category() == StructureCategory.IndustrialExtractor;

    public static bool IsProcessor(StructureType type) =>
        type.Category() == StructureCategory.IndustrialProcessor;

    public static bool IsManufacturer(StructureType type) =>
        type.Category() == StructureCategory.IndustrialManufacturer;

    public static bool IsIndustrial(StructureType type)
    {
        var cat = type.Category();
        return cat == StructureCategory.IndustrialExtractor
            || cat == StructureCategory.IndustrialProcessor
            || cat == StructureCategory.IndustrialManufacturer;
    }
}
