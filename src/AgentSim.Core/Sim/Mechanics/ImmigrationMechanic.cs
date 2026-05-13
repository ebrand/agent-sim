using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Monthly immigration: agents from the regional reservoir move to the city to fill labor demand.
/// Fires on day 30 after births. Pull rate is fraction of per-tier vacancies that fill in one month;
/// housing capacity caps total intake. Each new immigrant moves into an existing residence and
/// receives the per-tier `Bootstrap.StartingSavings` cushion.
/// </summary>
public static class ImmigrationMechanic
{
    /// <summary>Fraction of per-tier job vacancies that immigrate each month. Reduced from 0.5
    /// to 0.25 to let entities prove solvency before pop grows.</summary>
    public const double MonthlyPullFraction = 0.25;

    /// <summary>Fraction of vacant housing that immigrates each month even without job demand.
    /// Reduced from 0.10 to 0.05 — less aggressive housing-driven growth.</summary>
    public const double HousingPullFraction = 0.05;

    public static void RunMonthlyImmigration(SimState state)
    {
        // 1. Determine housing capacity remaining.
        var housingCapacity = 0;
        var residencesWithVacancy = new List<Structure>();
        foreach (var s in state.City.Structures.Values)
        {
            if (s.Category != StructureCategory.Residential) continue;
            if (!s.Operational || s.Inactive) continue;
            var vacancy = s.ResidentialCapacity - s.ResidentIds.Count;
            if (vacancy <= 0) continue;
            housingCapacity += vacancy;
            residencesWithVacancy.Add(s);
        }
        if (housingCapacity <= 0) return;

        // 2. For each tier, count vacant job slots across operational employers.
        var demandByTier = new Dictionary<EducationTier, int>
        {
            [EducationTier.Uneducated] = 0,
            [EducationTier.Primary] = 0,
            [EducationTier.Secondary] = 0,
            [EducationTier.College] = 0,
        };
        foreach (var s in state.City.Structures.Values)
        {
            if (!s.Operational || s.Inactive) continue;
            foreach (var (tier, slotCount) in s.JobSlots)
            {
                var filled = s.FilledSlots.GetValueOrDefault(tier);
                var vacant = Math.Max(0, slotCount - filled);
                demandByTier[tier] += vacant;
            }
        }

        // 3. For each tier, compute desired intake (pull fraction × vacancy, capped by reservoir).
        var desired = new Dictionary<EducationTier, int>();
        int totalDesired = 0;
        foreach (var (tier, vacancy) in demandByTier)
        {
            var pull = (int)Math.Ceiling(vacancy * MonthlyPullFraction);
            var available = state.Region.AgentReservoir.Get(tier);
            var actual = Math.Min(pull, available);
            desired[tier] = actual;
            totalDesired += actual;
        }

        // Housing-driven base immigration: even without specific job vacancies, a fraction of
        // vacant housing attracts immigrants in tier proportions matching the reservoir.
        var housingBasePull = (int)Math.Ceiling(housingCapacity * HousingPullFraction);
        if (housingBasePull > 0)
        {
            var resTotal = state.Region.AgentReservoir.Total;
            if (resTotal > 0)
            {
                foreach (EducationTier tier in (EducationTier[])Enum.GetValues(typeof(EducationTier)))
                {
                    var tierReservoir = state.Region.AgentReservoir.Get(tier);
                    if (tierReservoir <= 0) continue;
                    var tierShare = (int)((double)tierReservoir / resTotal * housingBasePull);
                    // Don't double-count over the reservoir; sum with prior job-driven pull.
                    var currentPull = desired.GetValueOrDefault(tier);
                    var combined = currentPull + tierShare;
                    var capped = Math.Min(combined, tierReservoir);
                    if (capped > currentPull)
                    {
                        totalDesired += (capped - currentPull);
                        desired[tier] = capped;
                    }
                }
            }
        }

        if (totalDesired <= 0) return;

        // 4. Cap total intake by housing capacity. Scale tiers proportionally if over capacity.
        int totalIntake = Math.Min(totalDesired, housingCapacity);
        if (totalIntake < totalDesired)
        {
            var scaled = new Dictionary<EducationTier, int>();
            int allocated = 0;
            foreach (var (tier, want) in desired)
            {
                var share = (int)((double)want / totalDesired * totalIntake);
                scaled[tier] = share;
                allocated += share;
            }
            // Distribute leftover (rounding remainder) to the largest-demand tier.
            var leftover = totalIntake - allocated;
            if (leftover > 0)
            {
                var biggestTier = desired.OrderByDescending(kv => kv.Value).First().Key;
                scaled[biggestTier] += leftover;
            }
            desired = scaled;
        }

        // 5. Immigrate agents per tier.
        foreach (var (tier, count) in desired)
        {
            for (int i = 0; i < count; i++)
            {
                ImmigrateOne(state, tier, residencesWithVacancy);
            }
        }
    }

    private static void ImmigrateOne(SimState state, EducationTier tier, List<Structure> residencesWithVacancy)
    {
        // Find a house with vacancy (recheck since previous immigrants this turn may have filled some).
        var home = residencesWithVacancy.FirstOrDefault(r => r.ResidentIds.Count < r.ResidentialCapacity);
        if (home == null) return;

        state.Region.AgentReservoir.Decrement(tier);

        var ageDays = Demographics.WorkingAgeStartDay
            + state.Prng.NextInt(Demographics.SettlerMaxAgeDays - Demographics.WorkingAgeStartDay);

        var agent = new Agent
        {
            Id = state.AllocateAgentId(),
            EducationTier = tier,
            AgeDays = ageDays,
            Savings = Bootstrap.StartingSavings(tier),
        };
        state.City.Agents[agent.Id] = agent;
        home.ResidentIds.Add(agent.Id);
        agent.ResidenceStructureId = home.Id;
    }
}
