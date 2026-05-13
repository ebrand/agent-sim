using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Per-structure environmental impact rates (climate and nature degradation per unit of
/// production output) and restoration rates. Per `feedback-loops.md`:
///   "Industrial structures degrade climate and/or nature each tick at a rate scaled by
///    their current operation level."
///
/// Floor of 0.05 prevents fully dead regions. Restoration structures (Park,
/// ReforestationSite, WetlandRestoration) restore upward.
/// </summary>
public static class Environment
{
    /// <summary>Lowest allowed value for Region.Climate / Region.Nature.</summary>
    public const double EnvironmentFloor = 0.05;

    /// <summary>Highest allowed value (caps recovery from restoration).</summary>
    public const double EnvironmentCeiling = 1.0;

    /// <summary>
    /// Climate degradation per unit of output. Oil + chemical industries heaviest;
    /// Agriculture lightest.
    /// </summary>
    public static double ClimateImpactPerUnit(StructureType type) => type switch
    {
        // Calibration: rates cut ~5× for small-pop viability (originally sized for high-throughput).
        StructureType.OilWell => 0.0000030,
        StructureType.FuelRefinery => 0.0000040,
        StructureType.PlasticPlant => 0.0000036,
        StructureType.Mine => 0.0000016,
        StructureType.Smelter => 0.0000036,
        StructureType.Quarry => 0.0000008,
        StructureType.AggregatePlant => 0.0000012,
        StructureType.ChalkPlant => 0.0000020,
        StructureType.SandPit => 0.0000004,
        StructureType.SilicatePlant => 0.0000024,
        StructureType.ForestExtractor => 0.0000004,
        StructureType.Sawmill => 0.0000008,
        StructureType.PulpMill => 0.0000012,
        StructureType.Farm => 0.0000002,
        StructureType.Mill => 0.0000004,
        StructureType.Ranch => 0.0000006,
        StructureType.Slaughterhouse => 0.0000004,
        StructureType.CottonFarm => 0.0000002,
        StructureType.Ginnery => 0.0000004,
        StructureType.HouseholdFactory => 0.0000012,
        StructureType.BldgSuppliesFactory => 0.0000012,
        StructureType.MetalGoodsFactory => 0.0000016,
        StructureType.FoodPackingPlant => 0.0000008,
        StructureType.ClothingFactory => 0.0000008,
        StructureType.ConcretePlant => 0.0000020,
        StructureType.PaperMill => 0.0000012,
        StructureType.Brewery => 0.0000008,
        StructureType.ElectronicsFactory => 0.0000016,
        StructureType.PharmaPlant => 0.0000020,
        _ => 0.0,
    };

    /// <summary>
    /// Nature degradation per unit of output. Extractors (resource depletion / land disturbance)
    /// heaviest; downstream manufacturers lighter.
    /// </summary>
    public static double NatureImpactPerUnit(StructureType type) => type switch
    {
        // Extractors most directly damage nature (land + water + habitat).
        // Calibration: rates cut ~5×.
        StructureType.OilWell => 0.0000040,
        StructureType.Mine => 0.0000036,
        StructureType.Quarry => 0.0000016,
        StructureType.SandPit => 0.0000012,
        StructureType.ForestExtractor => 0.0000020,
        StructureType.Farm => 0.0000008,
        StructureType.Ranch => 0.0000012,
        StructureType.CottonFarm => 0.0000008,
        StructureType.Sawmill => 0.0000004,
        StructureType.Smelter => 0.0000012,
        StructureType.Mill => 0.0000002,
        StructureType.AggregatePlant => 0.0000004,
        StructureType.SilicatePlant => 0.0000008,
        StructureType.FuelRefinery => 0.0000016,
        StructureType.PulpMill => 0.0000008,
        StructureType.PlasticPlant => 0.0000012,
        StructureType.Slaughterhouse => 0.0000002,
        StructureType.Ginnery => 0.0000002,
        StructureType.ChalkPlant => 0.0000008,
        StructureType.HouseholdFactory => 0.0000004,
        StructureType.BldgSuppliesFactory => 0.0000004,
        StructureType.MetalGoodsFactory => 0.0000004,
        StructureType.FoodPackingPlant => 0.0000002,
        StructureType.ClothingFactory => 0.0000002,
        StructureType.ConcretePlant => 0.0000004,
        StructureType.PaperMill => 0.0000004,
        StructureType.Brewery => 0.0000002,
        StructureType.ElectronicsFactory => 0.0000004,
        StructureType.PharmaPlant => 0.0000006,
        _ => 0.0,
    };

    /// <summary>
    /// Climate restoration per day from a restoration structure. Parks improve climate via shade
    /// and carbon sequestration; reforestation more so; wetland helps both climate and nature.
    /// </summary>
    public static double ClimateRestorationPerDay(StructureType type) => type switch
    {
        StructureType.Park => 0.00010,
        StructureType.ReforestationSite => 0.00030,
        StructureType.WetlandRestoration => 0.00020,
        _ => 0.0,
    };

    /// <summary>Nature restoration per day from a restoration structure.</summary>
    public static double NatureRestorationPerDay(StructureType type) => type switch
    {
        StructureType.Park => 0.00010,
        StructureType.ReforestationSite => 0.00040,
        StructureType.WetlandRestoration => 0.00030,
        _ => 0.0,
    };
}
