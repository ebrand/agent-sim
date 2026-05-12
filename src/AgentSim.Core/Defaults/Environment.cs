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
        // Oil industry — highest carbon emissions
        StructureType.OilWell => 0.0000150,
        StructureType.FuelRefinery => 0.0000200,
        StructureType.PlasticPlant => 0.0000180,
        // Mining + Smelting — heavy CO2 from blast furnaces
        StructureType.Mine => 0.0000080,
        StructureType.Smelter => 0.0000180,
        // Stone — moderate (kiln operations for chalk)
        StructureType.Quarry => 0.0000040,
        StructureType.AggregatePlant => 0.0000060,
        StructureType.ChalkPlant => 0.0000100,
        // Glass — moderate (silica heating)
        StructureType.SandPit => 0.0000020,
        StructureType.SilicatePlant => 0.0000120,
        // Forestry — light
        StructureType.ForestExtractor => 0.0000020,
        StructureType.Sawmill => 0.0000040,
        StructureType.PulpMill => 0.0000060,
        // Agriculture — lightest
        StructureType.Farm => 0.0000010,
        StructureType.Mill => 0.0000020,
        StructureType.Ranch => 0.0000030,                // methane
        StructureType.Slaughterhouse => 0.0000020,
        StructureType.CottonFarm => 0.0000010,
        StructureType.Ginnery => 0.0000020,
        // Manufacturers — moderate (factory emissions)
        StructureType.HouseholdFactory => 0.0000060,
        StructureType.BldgSuppliesFactory => 0.0000060,
        StructureType.MetalGoodsFactory => 0.0000080,
        StructureType.FoodPackingPlant => 0.0000040,
        StructureType.ClothingFactory => 0.0000040,
        StructureType.ConcretePlant => 0.0000100,        // cement is carbon-intensive
        StructureType.PaperMill => 0.0000060,
        StructureType.Printer => 0.0000040,
        _ => 0.0,
    };

    /// <summary>
    /// Nature degradation per unit of output. Extractors (resource depletion / land disturbance)
    /// heaviest; downstream manufacturers lighter.
    /// </summary>
    public static double NatureImpactPerUnit(StructureType type) => type switch
    {
        // Extractors most directly damage nature (land + water + habitat).
        StructureType.OilWell => 0.0000200,           // worst — oil drilling
        StructureType.Mine => 0.0000180,
        StructureType.Quarry => 0.0000080,
        StructureType.SandPit => 0.0000060,
        StructureType.ForestExtractor => 0.0000100,   // deforestation
        StructureType.Farm => 0.0000040,
        StructureType.Ranch => 0.0000060,
        StructureType.CottonFarm => 0.0000040,
        // Processors moderate.
        StructureType.Sawmill => 0.0000020,
        StructureType.Smelter => 0.0000060,
        StructureType.Mill => 0.0000010,
        StructureType.AggregatePlant => 0.0000020,
        StructureType.SilicatePlant => 0.0000040,
        StructureType.FuelRefinery => 0.0000080,
        StructureType.PulpMill => 0.0000040,
        StructureType.PlasticPlant => 0.0000060,
        StructureType.Slaughterhouse => 0.0000010,
        StructureType.Ginnery => 0.0000010,
        StructureType.ChalkPlant => 0.0000040,
        // Manufacturers lightest — they're indoor industrial.
        StructureType.HouseholdFactory => 0.0000020,
        StructureType.BldgSuppliesFactory => 0.0000020,
        StructureType.MetalGoodsFactory => 0.0000020,
        StructureType.FoodPackingPlant => 0.0000010,
        StructureType.ClothingFactory => 0.0000010,
        StructureType.ConcretePlant => 0.0000020,
        StructureType.PaperMill => 0.0000020,
        StructureType.Printer => 0.0000010,
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
