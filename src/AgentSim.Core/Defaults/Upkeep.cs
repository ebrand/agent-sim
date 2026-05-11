using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Monthly treasury upkeep per structure type. Settled on day 1 each month, before agent rent
/// (per `time-and-pacing.md` "outflows before inflows" sub-ordering).
///
/// Values from `economy.md` "Monthly upkeep" table. Alpha-1 calibration is loose; the design
/// targets a 50k-population mid-game where rent revenue (~$75M/month) dwarfs upkeep (~$2.5M/month).
/// Early-game (50 settlers) cannot afford even one of each type — gameplay arc requires the player
/// to delay service buildout until population can fund it.
/// </summary>
public static class Upkeep
{
    public const int PoliceStation = 30_000;
    public const int FireStation = 30_000;
    public const int TownHall = 90_000;
    public const int Clinic = 60_000;
    public const int Hospital = 250_000;
    public const int PrimarySchool = 60_000;
    public const int SecondarySchool = 90_000;
    public const int College = 180_000;
    public const int Generator = 80_000;
    public const int Well = 50_000;
    public const int AffordableHousing = 20_000;

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
