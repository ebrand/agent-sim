using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Commercial structure defaults — job slots per tier, structure value, monthly utility cost.
/// M16: construction recipes dropped (free construction until M17 wires Construction-sector mfg flow).
/// </summary>
public static class Commercial
{
    /// <summary>Job slot count per education tier for each commercial structure type.
    /// Calibration: lower-tier-weighted to be cheap to operate at small pop scale. Marketplace is
    /// the upscale alternative requiring more skilled labor.</summary>
    public static IReadOnlyDictionary<EducationTier, int> JobSlots(StructureType type) => type switch
    {
        // Calibration: 2-worker shop sized so 30% margin covers wages at moderate sector demand.
        StructureType.Shop => new Dictionary<EducationTier, int>
        {
            [EducationTier.Primary] = 1,
            [EducationTier.Uneducated] = 1,
        },
        StructureType.Marketplace => new Dictionary<EducationTier, int>
        {
            [EducationTier.College] = 1,
            [EducationTier.Secondary] = 2,
            [EducationTier.Primary] = 4,
            [EducationTier.Uneducated] = 5,
        },
        StructureType.CorporateHq => new Dictionary<EducationTier, int>(),
        _ => throw new ArgumentOutOfRangeException(nameof(type), $"{type} is not a commercial structure"),
    };

    public static int TotalJobSlots(StructureType type) => JobSlots(type).Values.Sum();

    // Calibration: smaller per-structure overhead so a single Shop can be profitable at ~20 pop.
    // CorporateHq is an admin entity with no physical overhead — it's the company's books, not a
    // building. Its sub-structures' values cover the city-side property-tax base.
    public static int StructureValue(StructureType type) => type switch
    {
        StructureType.Shop => 80_000,
        StructureType.Marketplace => 250_000,
        StructureType.CorporateHq => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    public static int MonthlyUtility(StructureType type) => type switch
    {
        StructureType.Shop => 500,
        StructureType.Marketplace => 2_000,
        StructureType.CorporateHq => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
