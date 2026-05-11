using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Monthly birth check: babies are born in-city at a rate proportional to working-age population.
/// Per `agents.md`: birth rate is 0.5% of working-age population per month, gated by housing
/// availability (no births when there is a residential housing waitlist).
///
/// Babies are created with AgeDays=0, EducationTier=Uneducated, no employer, no residence. They
/// don't pay rent or COL until they reach working age (when they need to find work and housing).
/// </summary>
public static class BirthMechanic
{
    /// <summary>Fires on day 30 (monthly), after settlements.</summary>
    public static void RunMonthlyBirths(SimState state)
    {
        // Gate: if there's a housing waitlist (working-age agents without residence), halt births.
        var waitlist = state.City.Agents.Values
            .Count(a => a.AgeDays >= Demographics.WorkingAgeStartDay && a.ResidenceStructureId == null);
        if (waitlist > 0) return;

        // Gate: total agent records (city + reservoir) must be below the 60k cap.
        var inCity = state.City.Agents.Count;
        var inReservoir = state.Region.AgentReservoir.Total;
        var totalAgents = inCity + inReservoir;
        var capacityRemaining = Demographics.TotalAgentCap - totalAgents;
        if (capacityRemaining <= 0) return;

        // Compute births. Use a fractional accumulator so small populations still get occasional
        // births over time (e.g., 50 settlers × 0.5% = 0.25 babies/month accumulates to 1 every 4 months).
        var workingAgeCount = state.City.Agents.Values
            .Count(a => a.AgeDays >= Demographics.WorkingAgeStartDay);
        state.City.BirthFractionalAccumulator += workingAgeCount * Demographics.MonthlyBirthRate;
        var babiesToBirth = (int)Math.Floor(state.City.BirthFractionalAccumulator);
        if (babiesToBirth <= 0) return;
        state.City.BirthFractionalAccumulator -= babiesToBirth;

        babiesToBirth = Math.Min(babiesToBirth, capacityRemaining);

        for (int i = 0; i < babiesToBirth; i++)
        {
            var baby = new Agent
            {
                Id = state.AllocateAgentId(),
                EducationTier = EducationTier.Uneducated,
                AgeDays = 0,
                Savings = 0,
                CurrentWage = 0,
                EmployerStructureId = null,
                ResidenceStructureId = null,  // babies live with family abstractly; no individual residence
            };
            state.City.Agents[baby.Id] = baby;
        }
    }
}
