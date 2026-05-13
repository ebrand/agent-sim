using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Civic / education / utility structures employ workers; wages are paid by the city treasury.
/// Tier mix favors lower-tier roles so a bootstrap workforce (mostly uneducated + primary) can
/// fill most of the slots; the higher tiers represent admin/professional layers that grow in
/// when education infrastructure produces those graduates.
/// </summary>
public static class CivicEmployment
{
    public static IReadOnlyDictionary<EducationTier, int> JobSlots(StructureType type) => type switch
    {
        // Calibration: smaller civic workforces. Each civic structure employs a token core team
        // (not a full staffed institution). Keeps treasury-funded wage burden low enough that the
        // city remains solvent at small scale.
        StructureType.PoliceStation => new Dictionary<EducationTier, int>
        {
            [EducationTier.Secondary] = 1, [EducationTier.Primary] = 1, [EducationTier.Uneducated] = 1,
        },
        StructureType.FireStation => new Dictionary<EducationTier, int>
        {
            [EducationTier.Secondary] = 1, [EducationTier.Primary] = 1, [EducationTier.Uneducated] = 1,
        },
        StructureType.TownHall => new Dictionary<EducationTier, int>
        {
            [EducationTier.College] = 1, [EducationTier.Secondary] = 1, [EducationTier.Primary] = 1,
        },
        StructureType.Clinic => new Dictionary<EducationTier, int>
        {
            [EducationTier.College] = 1, [EducationTier.Primary] = 1, [EducationTier.Uneducated] = 1,
        },
        StructureType.Hospital => new Dictionary<EducationTier, int>
        {
            [EducationTier.College] = 1, [EducationTier.Secondary] = 2, [EducationTier.Primary] = 2, [EducationTier.Uneducated] = 1,
        },
        StructureType.PrimarySchool => new Dictionary<EducationTier, int>
        {
            [EducationTier.Secondary] = 1, [EducationTier.Primary] = 1, [EducationTier.Uneducated] = 1,
        },
        StructureType.SecondarySchool => new Dictionary<EducationTier, int>
        {
            [EducationTier.College] = 1, [EducationTier.Secondary] = 1, [EducationTier.Primary] = 1,
        },
        StructureType.College => new Dictionary<EducationTier, int>
        {
            [EducationTier.College] = 1, [EducationTier.Secondary] = 2, [EducationTier.Primary] = 1,
        },
        StructureType.Generator => new Dictionary<EducationTier, int>
        {
            [EducationTier.Primary] = 1, [EducationTier.Uneducated] = 1,
        },
        StructureType.Well => new Dictionary<EducationTier, int>
        {
            [EducationTier.Primary] = 1, [EducationTier.Uneducated] = 1,
        },
        StructureType.ElectricityDistribution => new Dictionary<EducationTier, int>
        {
            [EducationTier.Primary] = 1, [EducationTier.Uneducated] = 1,
        },
        StructureType.WaterDistribution => new Dictionary<EducationTier, int>
        {
            [EducationTier.Primary] = 1, [EducationTier.Uneducated] = 1,
        },
        _ => new Dictionary<EducationTier, int>(),
    };

    /// <summary>True when the employer is funded out of the city treasury (vs HQ or self).</summary>
    public static bool IsTreasuryEmployer(StructureCategory category) =>
        category == StructureCategory.Civic
        || category == StructureCategory.Healthcare
        || category == StructureCategory.Education
        || category == StructureCategory.Utility;
}
