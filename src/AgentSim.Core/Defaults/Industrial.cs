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
        ManufacturedGood.Books => 80,         // M14e: assembled good from Paper + Plastic
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
    /// Standard 100-worker mix per industrial structure: 15 college / 20 secondary / 40 primary /
    /// 25 uneducated.
    /// </summary>
    public static IReadOnlyDictionary<EducationTier, int> JobSlots(StructureType type)
    {
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

    public static bool IsIndustrial(StructureType type)
    {
        var cat = type.Category();
        return cat == StructureCategory.IndustrialExtractor
            || cat == StructureCategory.IndustrialProcessor
            || cat == StructureCategory.IndustrialManufacturer;
    }
}
