using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Monthly treasury upkeep per structure type. Settled on day 1 each month, before agent rent
/// (per `time-and-pacing.md` "outflows before inflows" sub-ordering).
///
/// Alpha-1 calibration target: a modest founding city (1 PoliceStation + 1 Clinic + 1 PrimarySchool
/// + 1 Generator + 1 Well = $115k/month) sustains ~6 months of full-pay against a $500k starting
/// treasury and ~$50k/month bootstrap income (50 settlers' rent + utilities, no wages yet). After
/// 6 months of full pay, treasury drops below the upkeep threshold and partial-pay kicks in for
/// another ~6 months before game-over fires — giving the player ~12 months total to react.
/// These values are lower than the design table in `economy.md` (which is calibrated for a 50k
/// mid-game city); revisit when scaling up.
/// </summary>
public static class Upkeep
{
    public const int PoliceStation = 15_000;
    public const int FireStation = 15_000;
    public const int TownHall = 50_000;
    public const int Clinic = 25_000;
    public const int Hospital = 120_000;
    public const int PrimarySchool = 25_000;
    public const int SecondarySchool = 50_000;
    public const int College = 100_000;
    public const int Generator = 30_000;
    public const int Well = 20_000;
    public const int AffordableHousing = 10_000;

    /// <summary>Monthly upkeep cost for the given structure type. 0 for non-treasury-funded types.</summary>
    public static int MonthlyCost(StructureType type) => type switch
    {
        StructureType.PoliceStation => PoliceStation,
        StructureType.FireStation => FireStation,
        StructureType.TownHall => TownHall,
        StructureType.Clinic => Clinic,
        StructureType.Hospital => Hospital,
        StructureType.PrimarySchool => PrimarySchool,
        StructureType.SecondarySchool => SecondarySchool,
        StructureType.College => College,
        StructureType.Generator => Generator,
        StructureType.Well => Well,
        StructureType.AffordableHousing => AffordableHousing,
        _ => 0,
    };

    /// <summary>Whether a structure type is funded by the treasury (rather than self-funded via revenue).</summary>
    public static bool IsTreasuryFunded(StructureType type) => MonthlyCost(type) > 0;

    /// <summary>Months of negative treasury before game-over per `feedback-loops.md`.</summary>
    public const int BankruptcyMonthsToGameOver = 6;
}
