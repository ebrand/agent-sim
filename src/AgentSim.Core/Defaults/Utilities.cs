using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Per-residential-type fixed monthly utility cost (paid by agent on day 15).
/// Per `economy.md` — these match the "% of wage" expectation in the tier-matched case.
/// </summary>
public static class Utilities
{
    public static int MonthlyResidentialUtility(StructureType type) => type switch
    {
        StructureType.House => 200,
        StructureType.Apartment => 350,
        StructureType.Townhouse => 450,
        StructureType.Condo => 700,
        StructureType.AffordableHousing => 200,
        _ => throw new ArgumentOutOfRangeException(nameof(type), $"{type} is not a residential structure"),
    };
}
