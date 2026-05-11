namespace AgentSim.Core.Types;

/// <summary>
/// One simulation agent. Mutable state is updated each tick by the sim.
/// </summary>
public sealed class Agent
{
    public required long Id { get; init; }
    public required EducationTier EducationTier { get; set; }

    /// <summary>Age in days. 1 tick = 1 day.</summary>
    public required int AgeDays { get; set; }

    public long? EmployerStructureId { get; set; }

    /// <summary>
    /// Tier of the job slot the agent currently fills. May differ from EducationTier when the agent
    /// is over-qualified (e.g. college-educated agent in a primary-tier slot). Used to decrement
    /// the correct FilledSlots bucket on death, emigration, or layoff. Null when unemployed.
    /// </summary>
    public EducationTier? CurrentJobTier { get; set; }

    public long? ResidenceStructureId { get; set; }

    /// <summary>Current monthly wage. Zero when wageless / unemployed.</summary>
    public int CurrentWage { get; set; }

    /// <summary>Personal savings buffer (dollars).</summary>
    public int Savings { get; set; }

    /// <summary>Whether the agent has used their one-time affordable-housing move attempt this shortfall episode.</summary>
    public bool AffordableHousingAttemptUsed { get; set; }
}
