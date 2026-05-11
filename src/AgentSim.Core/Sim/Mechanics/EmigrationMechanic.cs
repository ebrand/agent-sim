using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// End-of-month emigration check. Per `agents.md` and `economy.md`:
///   At end of each month, every agent runs one check:
///     - If wage + savings ≥ monthly expenses: stay.
///     - Otherwise: one-time attempt to move to affordable housing; else emigrate next tick.
///
/// Implementation: in the cadence-based settlement model, this month's expenses have already been
/// deducted from savings (rent on day 1, utilities on day 15). The end-of-month check therefore
/// evaluates whether the agent's savings is non-negative — if savings went negative this month,
/// the agent failed an expense and should be considered insolvent.
///
/// This is the "did you survive the month" reading of the rule. Equivalent to the forward-looking
/// "wage + savings ≥ expenses" formulation under the simplifying assumption that next month's
/// expenses will be similar to this month's.
/// </summary>
public static class EmigrationMechanic
{
    public static void EndOfMonthCheck(SimState state)
    {
        // Snapshot to avoid mutating the dict while iterating
        var agents = state.City.Agents.Values.ToList();
        foreach (var agent in agents)
        {
            if (agent.Savings >= 0)
            {
                // Survived this month. Reset the affordable-housing attempt flag for the next shortfall episode.
                agent.AffordableHousingAttemptUsed = false;
                continue;
            }

            // Failed: one attempt to move to affordable housing if eligible & seat available.
            if (TryMoveToAffordableHousing(state, agent))
            {
                continue;
            }

            // Emigrate.
            Emigrate(state, agent);
        }
    }

    private static bool TryMoveToAffordableHousing(SimState state, Agent agent)
    {
        if (agent.AffordableHousingAttemptUsed) return false;

        // Eligibility: employed AND wage strictly under $2,000, OR wageless because employer is inactive.
        var eligible = (agent.CurrentWage > 0 && agent.CurrentWage < 2_000)
                       || agent.CurrentWage == 0;
        if (!eligible) return false;

        // Find an affordable housing structure with a vacant seat.
        var ahWithVacancy = state.City.Structures.Values
            .FirstOrDefault(s => s.Type == StructureType.AffordableHousing
                                 && s.Operational
                                 && s.ResidentIds.Count < s.ResidentialCapacity);
        if (ahWithVacancy is null) return false;

        // Move the agent.
        if (agent.ResidenceStructureId is long oldId
            && state.City.Structures.TryGetValue(oldId, out var oldHome))
        {
            oldHome.ResidentIds.Remove(agent.Id);
        }
        ahWithVacancy.ResidentIds.Add(agent.Id);
        agent.ResidenceStructureId = ahWithVacancy.Id;
        agent.AffordableHousingAttemptUsed = true;
        return true;
    }

    private static void Emigrate(SimState state, Agent agent)
    {
        // Remove from current residence.
        if (agent.ResidenceStructureId is long resId
            && state.City.Structures.TryGetValue(resId, out var residence))
        {
            residence.ResidentIds.Remove(agent.Id);
        }

        // Vacate employment slot (if any). Use CurrentJobTier — the agent may be over-qualified
        // relative to EducationTier, and the slot bucket to decrement is whichever they were hired into.
        if (agent.EmployerStructureId is long empId
            && state.City.Structures.TryGetValue(empId, out var employer)
            && agent.CurrentJobTier is EducationTier jobTier)
        {
            employer.EmployeeIds.Remove(agent.Id);
            if (employer.FilledSlots.TryGetValue(jobTier, out var count) && count > 0)
            {
                employer.FilledSlots[jobTier] = count - 1;
            }
        }

        // Remove from city, return to regional reservoir at agent's CURRENT education tier.
        // Savings travel with the agent (lost from city economy).
        state.City.Agents.Remove(agent.Id);
        state.Region.AgentReservoir.Increment(agent.EducationTier);
    }
}
