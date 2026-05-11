using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// School durations, seat capacities, and the mapping between tier and structure type.
/// Per `agents.md` and `structures.md`.
/// </summary>
public static class Education
{
    // Per-tier duration in days the agent must attend to complete the tier.
    // From agents.md: primary 6y, secondary 5y, college 5y.
    public const int PrimaryDurationDays = 6 * Demographics.DaysPerYear;     // 2,160
    public const int SecondaryDurationDays = 5 * Demographics.DaysPerYear;   // 1,800
    public const int CollegeDurationDays = 5 * Demographics.DaysPerYear;     // 1,800

    // Per-school seat capacities (from structures.md "Service Structure Capacities").
    public const int PrimarySchoolSeats = 1_000;
    public const int SecondarySchoolSeats = 1_500;
    public const int CollegeSeats = 2_500;

    /// <summary>Required days to advance from the given current tier to the next.</summary>
    public static int DurationDaysForNextTier(EducationTier current) => current switch
    {
        EducationTier.Uneducated => PrimaryDurationDays,
        EducationTier.Primary => SecondaryDurationDays,
        EducationTier.Secondary => CollegeDurationDays,
        EducationTier.College => 0,
        _ => 0,
    };

    /// <summary>The school StructureType that teaches the agent's next tier.</summary>
    public static StructureType? SchoolTypeForNextTier(EducationTier current) => current switch
    {
        EducationTier.Uneducated => StructureType.PrimarySchool,
        EducationTier.Primary => StructureType.SecondarySchool,
        EducationTier.Secondary => StructureType.College,
        _ => null,
    };

    /// <summary>Seat capacity for a given school structure type. 0 for non-schools.</summary>
    public static int SeatCapacityFor(StructureType type) => type switch
    {
        StructureType.PrimarySchool => PrimarySchoolSeats,
        StructureType.SecondarySchool => SecondarySchoolSeats,
        StructureType.College => CollegeSeats,
        _ => 0,
    };

    /// <summary>
    /// Whether the agent's current age falls inside the band that allows enrolling in the next tier.
    /// Per `agents.md` age bands:
    ///   - Primary school age: 1,800 – 3,960 (days 5–11 in years)
    ///   - Secondary school age: 3,960 – 5,760
    ///   - College age: 5,760 – 7,560
    /// </summary>
    public static bool AgeEligibleToEnroll(int ageDays, EducationTier currentTier) => currentTier switch
    {
        EducationTier.Uneducated => ageDays >= Demographics.BabyEndDay && ageDays < Demographics.PrimaryAgeEndDay,
        EducationTier.Primary => ageDays >= Demographics.PrimaryAgeEndDay && ageDays < Demographics.SecondaryAgeEndDay,
        EducationTier.Secondary => ageDays >= Demographics.SecondaryAgeEndDay && ageDays < Demographics.CollegeAgeEndDay,
        _ => false,
    };
}
