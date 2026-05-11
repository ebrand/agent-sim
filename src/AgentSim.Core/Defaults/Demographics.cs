namespace AgentSim.Core.Defaults;

public static class Demographics
{
    /// <summary>Total agent budget across city + reservoir.</summary>
    public const int TotalAgentCap = 60_000;

    /// <summary>Days in a game-year.</summary>
    public const int DaysPerYear = 360;

    /// <summary>Days in a game-month.</summary>
    public const int DaysPerMonth = 30;

    /// <summary>Lifespan in days (60 game-years).</summary>
    public const int LifespanDays = 60 * DaysPerYear;

    // Age band thresholds (days, exclusive upper bound)
    public const int BabyEndDay = 5 * DaysPerYear;             // 1,800
    public const int PrimaryAgeEndDay = 11 * DaysPerYear;      // 3,960
    public const int SecondaryAgeEndDay = 16 * DaysPerYear;    // 5,760
    public const int CollegeAgeEndDay = 21 * DaysPerYear;      // 7,560
    public const int WorkingAgeStartDay = CollegeAgeEndDay;    // 7,560

    // Bootstrap
    public const int SettlerCount = 50;
    public const double SettlerUneducatedFraction = 0.6;
    public const double SettlerPrimaryFraction = 0.4;

    /// <summary>Settler age range upper bound (days). Settlers are uniformly young adults
    /// (21 to 30 game-years old) so they don't die from old age during the founding decade.</summary>
    public const int SettlerMaxAgeDays = 30 * DaysPerYear;  // 10,800 days = 30 game-years

    // Birth
    /// <summary>Monthly birth rate as fraction of working-age population (per `agents.md`).</summary>
    public const double MonthlyBirthRate = 0.005;
}
