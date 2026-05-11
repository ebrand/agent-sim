using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Compute service-pool satisfaction percentages. Per `feedback-loops.md` and `structures.md`:
///   satisfaction = min(100%, capacity_serving / demand_count)
///
/// Service pools: civic, healthcare, utility, and education-by-tier.
/// </summary>
public static class ServiceSatisfactionMechanic
{
    /// <summary>Snapshot of all pool satisfactions for one tick of evaluation.</summary>
    public readonly struct Snapshot
    {
        public double CivicPercent { get; init; }
        public double HealthcarePercent { get; init; }
        public double UtilityPercent { get; init; }
        public double PrimaryEducationPercent { get; init; }
        public double SecondaryEducationPercent { get; init; }
        public double CollegeEducationPercent { get; init; }
    }

    public static Snapshot Compute(SimState state)
    {
        var city = state.City;

        // M10: treasury-funded services (civic / healthcare / education / utility) contribute
        // zero capacity when the treasury is negative — upkeep failures collapse satisfaction.
        var bankrupt = city.TreasuryBalance < 0;

        // Civic / healthcare demand = total city population. Per `structures.md`,
        // capacities serve "agents" without further qualification.
        var totalAgents = city.Agents.Count;

        // Utility demand = agents + count of operational commercial/industrial structures.
        var commercialOrIndustrial = city.Structures.Values.Count(s =>
            s.Operational
            && !s.Inactive
            && (s.Category == StructureCategory.Commercial
                || s.Category == StructureCategory.IndustrialExtractor
                || s.Category == StructureCategory.IndustrialProcessor
                || s.Category == StructureCategory.IndustrialManufacturer
                || s.Category == StructureCategory.IndustrialStorage));
        var utilityDemand = totalAgents + commercialOrIndustrial;

        // Education demand by tier — count agents whose age falls in each band.
        var primaryAgeDemand = city.Agents.Values.Count(a =>
            a.AgeDays >= Demographics.BabyEndDay && a.AgeDays < Demographics.PrimaryAgeEndDay);
        var secondaryAgeDemand = city.Agents.Values.Count(a =>
            a.AgeDays >= Demographics.PrimaryAgeEndDay && a.AgeDays < Demographics.SecondaryAgeEndDay);
        var collegeAgeDemand = city.Agents.Values.Count(a =>
            a.AgeDays >= Demographics.SecondaryAgeEndDay && a.AgeDays < Demographics.CollegeAgeEndDay);

        // Capacity = sum over operational, non-inactive structures of the relevant capacity field.
        // When bankrupt, treasury-funded categories contribute zero (their upkeep was unpaid this
        // month or the treasury has been overdrawn).
        int CapacityOfCategory(StructureCategory cat) => bankrupt ? 0 : city.Structures.Values
            .Where(s => s.Category == cat && s.Operational && !s.Inactive)
            .Sum(s => s.ServiceCapacity);

        int SchoolCapacity(StructureType type) => bankrupt ? 0 : city.Structures.Values
            .Where(s => s.Type == type && s.Operational && !s.Inactive)
            .Sum(s => s.SeatCapacity);

        return new Snapshot
        {
            CivicPercent = SatisfactionPercent(CapacityOfCategory(StructureCategory.Civic), totalAgents),
            HealthcarePercent = SatisfactionPercent(CapacityOfCategory(StructureCategory.Healthcare), totalAgents),
            UtilityPercent = SatisfactionPercent(CapacityOfCategory(StructureCategory.Utility), utilityDemand),
            PrimaryEducationPercent = SatisfactionPercent(SchoolCapacity(StructureType.PrimarySchool), primaryAgeDemand),
            SecondaryEducationPercent = SatisfactionPercent(SchoolCapacity(StructureType.SecondarySchool), secondaryAgeDemand),
            CollegeEducationPercent = SatisfactionPercent(SchoolCapacity(StructureType.College), collegeAgeDemand),
        };
    }

    /// <summary>
    /// satisfaction = min(100%, capacity / demand). When demand is 0, satisfaction is treated as
    /// 100% — there's no shortfall if nobody needs the service.
    /// </summary>
    private static double SatisfactionPercent(int capacity, int demand)
    {
        if (demand <= 0) return 100.0;
        var ratio = (double)capacity / demand;
        return Math.Min(100.0, ratio * 100.0);
    }

    /// <summary>
    /// Per-agent worst-of satisfaction. For working-age and babies, the education tier is Primary
    /// (the "child-relevant tier" simplification per M9 design discussion). Enrolled students
    /// use the satisfaction of the school tier they're currently enrolled in.
    /// </summary>
    public static double WorstOfForAgent(Agent agent, Snapshot snapshot)
    {
        double educationPct = PrimaryEducationForAgent(agent, snapshot);
        return Math.Min(
            Math.Min(snapshot.CivicPercent, snapshot.HealthcarePercent),
            Math.Min(snapshot.UtilityPercent, educationPct));
    }

    private static double PrimaryEducationForAgent(Agent agent, Snapshot snapshot)
    {
        // Currently-enrolled students use their enrolled tier's satisfaction.
        // EnrolledStructureId is set during enrollment; the tier is implicit from the school type.
        // For simplicity we infer the tier from the agent's EducationTier (they're studying the
        // *next* tier, so EducationTier=Uneducated → primary, Primary → secondary, etc.).
        if (agent.EnrolledStructureId is not null)
        {
            return agent.EducationTier switch
            {
                EducationTier.Uneducated => snapshot.PrimaryEducationPercent,
                EducationTier.Primary => snapshot.SecondaryEducationPercent,
                EducationTier.Secondary => snapshot.CollegeEducationPercent,
                _ => snapshot.PrimaryEducationPercent,
            };
        }

        // Everyone else uses primary education as the "child-relevant tier" proxy.
        return snapshot.PrimaryEducationPercent;
    }
}
