using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Industrial structure defaults — extractors, processors, manufacturers.
///
/// M16 model:
///   - Extractor: produces raw units (single int RawUnitsInStock) of a NaturalResource.
///   - Processor: pulls raw units from a matching extractor (by NaturalResource), produces an
///     MfgInput into its MfgInputStorage.
///   - Manufacturer: pulls MfgInputs from any processor, produces a generic sector-tagged unit
///     into its MfgOutputStock at a single per-manufacturer UnitPrice.
///
/// All extractor/processor/manufacturer structures share a uniform daily production capacity at
/// full staffing (MaxOutputPerDay = 100).
/// </summary>
public static class Industrial
{
    /// <summary>Max units a structure produces per day at 100% staffing.</summary>
    public const int MaxOutputPerDay = 100;

    /// <summary>Default internal storage capacity for industrial structure buffers.</summary>
    public const int InternalStorageCapacity = 1_000;

    // ===== Production mapping =====

    /// <summary>Which NaturalResource an extractor pulls from (or null if not an extractor).</summary>
    public static NaturalResource? ExtractorSource(StructureType type) => type switch
    {
        StructureType.ForestExtractor => NaturalResource.Forest,
        StructureType.Mine => NaturalResource.Ore,
        StructureType.Quarry => NaturalResource.Stone,
        StructureType.SandPit => NaturalResource.Sand,
        StructureType.Farm => NaturalResource.ArableLand,
        StructureType.OilWell => NaturalResource.Petroleum,
        StructureType.Ranch => NaturalResource.ArableLand,
        StructureType.CottonFarm => NaturalResource.ArableLand,
        _ => null,
    };

    /// <summary>
    /// Processor recipe: which raw NaturalResource the processor pulls from upstream extractors,
    /// and which MfgInput it produces. Null for non-processors.
    /// </summary>
    public static (NaturalResource Source, MfgInput Output)? ProcessorRecipe(StructureType type) => type switch
    {
        StructureType.Sawmill => (NaturalResource.Forest, MfgInput.Wood),
        StructureType.Smelter => (NaturalResource.Ore, MfgInput.Steel),
        StructureType.Mill => (NaturalResource.ArableLand, MfgInput.Flour),
        StructureType.AggregatePlant => (NaturalResource.Stone, MfgInput.Aggregate),
        StructureType.SilicatePlant => (NaturalResource.Sand, MfgInput.Glass),
        StructureType.FuelRefinery => (NaturalResource.Petroleum, MfgInput.Fuel),
        StructureType.PulpMill => (NaturalResource.Forest, MfgInput.Pulp),
        StructureType.PlasticPlant => (NaturalResource.Petroleum, MfgInput.Plastic),
        StructureType.Slaughterhouse => (NaturalResource.ArableLand, MfgInput.Meat),
        StructureType.Ginnery => (NaturalResource.ArableLand, MfgInput.Cotton),
        StructureType.ChalkPlant => (NaturalResource.Stone, MfgInput.Chalk),
        _ => null,
    };

    /// <summary>Manufacturer recipe: input MfgInputs, sectors serviced, and unit price.
    /// Each manufacturer has a unique input set.</summary>
    public readonly record struct MfgRecipe(
        IReadOnlyList<(MfgInput Input, int Units)> Inputs,
        IReadOnlyList<CommercialSector> Sectors,
        int UnitPrice);

    public static MfgRecipe? ManufacturerRecipe(StructureType type) => type switch
    {
        StructureType.HouseholdFactory => new MfgRecipe(
            new (MfgInput, int)[] { (MfgInput.Wood, 5), (MfgInput.Steel, 2), (MfgInput.Glass, 1), (MfgInput.Plastic, 1) },
            new[] { CommercialSector.Retail }, 300),
        StructureType.BldgSuppliesFactory => new MfgRecipe(
            new (MfgInput, int)[] { (MfgInput.Wood, 6), (MfgInput.Steel, 2), (MfgInput.Plastic, 1) },
            new[] { CommercialSector.Construction }, 360),
        StructureType.MetalGoodsFactory => new MfgRecipe(
            new (MfgInput, int)[] { (MfgInput.Steel, 3), (MfgInput.Plastic, 1) },
            new[] { CommercialSector.Construction }, 270),
        StructureType.FoodPackingPlant => new MfgRecipe(
            new (MfgInput, int)[] { (MfgInput.Flour, 3), (MfgInput.Meat, 2), (MfgInput.Plastic, 1), (MfgInput.Glass, 1) },
            new[] { CommercialSector.Food }, 150),
        StructureType.ClothingFactory => new MfgRecipe(
            new (MfgInput, int)[] { (MfgInput.Cotton, 2), (MfgInput.Plastic, 1) },
            new[] { CommercialSector.Retail }, 90),
        StructureType.ConcretePlant => new MfgRecipe(
            new (MfgInput, int)[] { (MfgInput.Aggregate, 4), (MfgInput.Chalk, 1) },
            new[] { CommercialSector.Construction }, 240),
        StructureType.PaperMill => new MfgRecipe(
            new (MfgInput, int)[] { (MfgInput.Pulp, 3) },
            new[] { CommercialSector.Entertainment }, 90),
        _ => null,
    };

    // ===== Prices =====

    /// <summary>Per-unit price processors charge manufacturers for an MfgInput.
    /// Calibration: bumped ~2× from the prior level so HQs can recoup chain wages from sales.</summary>
    public static int MfgInputPrice(MfgInput input) => input switch
    {
        MfgInput.Wood => 14,
        MfgInput.Steel => 28,
        MfgInput.Flour => 10,
        MfgInput.Vegetables => 10,
        MfgInput.Aggregate => 20,
        MfgInput.Glass => 14,
        MfgInput.Fuel => 28,
        MfgInput.Plastic => 36,
        MfgInput.Pulp => 8,
        MfgInput.Meat => 24,
        MfgInput.Cotton => 14,
        MfgInput.Chalk => 18,
        MfgInput.Rock => 8,
        MfgInput.Water => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(input)),
    };

    // ===== Structure values (drives property tax) =====

    public static int StructureValue(StructureType type) => type switch
    {
        StructureType.ForestExtractor => 80_000,
        StructureType.Mine => 150_000,
        StructureType.Quarry => 100_000,
        StructureType.SandPit => 80_000,
        StructureType.Farm => 100_000,
        StructureType.OilWell => 250_000,
        StructureType.Ranch => 100_000,
        StructureType.CottonFarm => 100_000,
        StructureType.Sawmill => 200_000,
        StructureType.Smelter => 400_000,
        StructureType.Mill => 200_000,
        StructureType.AggregatePlant => 200_000,
        StructureType.SilicatePlant => 200_000,
        StructureType.FuelRefinery => 400_000,
        StructureType.PulpMill => 200_000,
        StructureType.PlasticPlant => 400_000,
        StructureType.Slaughterhouse => 200_000,
        StructureType.Ginnery => 200_000,
        StructureType.ChalkPlant => 200_000,
        StructureType.HouseholdFactory => 300_000,
        StructureType.BldgSuppliesFactory => 300_000,
        StructureType.MetalGoodsFactory => 500_000,
        StructureType.FoodPackingPlant => 300_000,
        StructureType.ClothingFactory => 300_000,
        StructureType.ConcretePlant => 300_000,
        StructureType.PaperMill => 250_000,
        _ => throw new ArgumentOutOfRangeException(nameof(type), $"{type} is not an industrial structure"),
    };

    // ===== Monthly utility cost =====
    // Calibration: halved for low-pop viability.

    public static int MonthlyUtility(StructureType type) => type switch
    {
        StructureType.ForestExtractor => 500,
        StructureType.Mine => 800,
        StructureType.Quarry => 500,
        StructureType.SandPit => 500,
        StructureType.Farm => 500,
        StructureType.OilWell => 1_500,
        StructureType.Ranch => 500,
        StructureType.CottonFarm => 500,
        StructureType.Sawmill => 1_500,
        StructureType.Smelter => 2_500,
        StructureType.Mill => 1_500,
        StructureType.AggregatePlant => 1_500,
        StructureType.SilicatePlant => 1_500,
        StructureType.FuelRefinery => 2_500,
        StructureType.PulpMill => 1_500,
        StructureType.PlasticPlant => 2_500,
        StructureType.Slaughterhouse => 1_500,
        StructureType.Ginnery => 1_500,
        StructureType.ChalkPlant => 1_500,
        StructureType.HouseholdFactory => 2_000,
        StructureType.BldgSuppliesFactory => 2_000,
        StructureType.MetalGoodsFactory => 3_000,
        StructureType.FoodPackingPlant => 2_000,
        StructureType.ClothingFactory => 2_000,
        StructureType.ConcretePlant => 2_000,
        StructureType.PaperMill => 1_500,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    // ===== Job slots =====

    public static IReadOnlyDictionary<EducationTier, int> JobSlots(StructureType type)
    {
        // Calibration: 2 workers per industrial structure. Smaller staffing = smaller wage burden
        // per facility. Bigger industrial chains require multiple structures to absorb labor.
        return new Dictionary<EducationTier, int>
        {
            [EducationTier.Primary] = 1,
            [EducationTier.Uneducated] = 1,
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
