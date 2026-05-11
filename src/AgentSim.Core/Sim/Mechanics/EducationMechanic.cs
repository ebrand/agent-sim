using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Daily education progression. Per `agents.md`:
///   - School-aged agents enroll in matching schools when seats are available.
///   - Enrolled agents accumulate attendance days; on hitting the per-tier duration they complete
///     the tier (EducationTier += 1), vacate the seat, and immediately try to enroll in the next tier.
///   - When no next-tier seats exist (or no age eligibility), the agent stops schooling at their
///     current tier and enters the workforce on reaching working age.
///   - Aging past a tier's age band mid-attendance does NOT kick the student out — completion is
///     attendance-based once enrolled.
/// </summary>
public static class EducationMechanic
{
    public static void RunDaily(SimState state)
    {
        // Snapshot agents to allow mutation during the loop (enrollment/unenroll).
        var agents = state.City.Agents.Values.ToList();
        foreach (var agent in agents)
        {
            // Working-age and beyond never enroll (locked at current tier).
            // Babies (< BabyEndDay) are never eligible.
            // Already at top tier: nothing to do.
            if (agent.EducationTier == EducationTier.College) continue;

            if (agent.EnrolledStructureId is long structureId)
            {
                AdvanceStudent(state, agent, structureId);
            }
            else
            {
                TryEnroll(state, agent);
            }
        }
    }

    private static void AdvanceStudent(SimState state, Agent agent, long structureId)
    {
        agent.EducationProgressDays++;

        var requiredDays = Education.DurationDaysForNextTier(agent.EducationTier);
        if (agent.EducationProgressDays < requiredDays) return;

        // Completion. Vacate seat, advance tier.
        if (state.City.Structures.TryGetValue(structureId, out var school))
        {
            school.EnrolledStudentIds.Remove(agent.Id);
        }
        agent.EnrolledStructureId = null;
        agent.EducationProgressDays = 0;
        agent.EducationTier = (EducationTier)((int)agent.EducationTier + 1);

        // Try to enroll in the next tier immediately (same tick).
        // Note: after the bump, the agent's eligibility window for the NEW next-tier may or may
        // not include their current age. AgeEligibleToEnroll handles this — e.g., a 13-year-old
        // just completing primary is in the secondary band (11-16) so they can enroll.
        TryEnroll(state, agent);
    }

    private static void TryEnroll(SimState state, Agent agent)
    {
        if (agent.EducationTier == EducationTier.College) return;

        if (!Education.AgeEligibleToEnroll(agent.AgeDays, agent.EducationTier)) return;

        var targetType = Education.SchoolTypeForNextTier(agent.EducationTier);
        if (targetType is not StructureType type) return;

        // Find an operational, non-inactive school of the target type with an open seat. Pick the
        // first one we find — M8 keeps allocation simple; FIFO across multiple schools isn't worth
        // modelling at this stage.
        var school = state.City.Structures.Values.FirstOrDefault(s =>
            s.Type == type
            && s.Operational
            && !s.Inactive
            && s.EnrolledStudentIds.Count < s.SeatCapacity);

        if (school is null) return;

        school.EnrolledStudentIds.Add(agent.Id);
        agent.EnrolledStructureId = school.Id;
        agent.EducationProgressDays = 0;
    }
}
