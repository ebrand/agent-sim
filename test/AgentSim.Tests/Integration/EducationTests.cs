using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Types;

namespace AgentSim.Tests.Integration;

/// <summary>
/// M8: education progression — enrollment, attendance, completion, tier upgrade.
/// </summary>
public class EducationTests
{
    /// <summary>Insert a synthetic school-age agent at a specified tier and age, with optional residence.</summary>
    private static Agent AddStudent(Sim sim, int ageDays, EducationTier startingTier)
    {
        var agent = new Agent
        {
            Id = sim.State.AllocateAgentId(),
            EducationTier = startingTier,
            AgeDays = ageDays,
        };
        sim.State.City.Agents[agent.Id] = agent;
        return agent;
    }

    private static Structure FastBuildSchool(Sim sim, StructureType type)
    {
        var school = sim.PlaceEducationStructure(type);
        school.ConstructionTicks = school.RequiredConstructionTicks;  // skip construction
        return school;
    }

    [Fact]
    public void PrimarySchool_CanBePlaced_AndHasSeatCapacity()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var school = sim.PlaceEducationStructure(StructureType.PrimarySchool);

        Assert.Equal(Education.PrimarySchoolSeats, school.SeatCapacity);
        Assert.Equal(StructureType.PrimarySchool, school.Type);
    }

    [Fact]
    public void SchoolCapacities_MatchStructuresDoc()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var primary = sim.PlaceEducationStructure(StructureType.PrimarySchool);
        var secondary = sim.PlaceEducationStructure(StructureType.SecondarySchool);
        var college = sim.PlaceEducationStructure(StructureType.College);

        Assert.Equal(1_000, primary.SeatCapacity);
        Assert.Equal(1_500, secondary.SeatCapacity);
        Assert.Equal(2_500, college.SeatCapacity);
    }

    [Fact]
    public void NonEducationType_RejectedByPlaceEducationStructure()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        Assert.Throws<ArgumentException>(() => sim.PlaceEducationStructure(StructureType.House));
    }

    [Fact]
    public void SchoolAgedAgent_EnrollsInOperationalSchool_WithOpenSeats()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var school = FastBuildSchool(sim, StructureType.PrimarySchool);
        var student = AddStudent(sim, ageDays: 6 * 360, startingTier: EducationTier.Uneducated);

        sim.Tick(1);

        Assert.Equal(school.Id, student.EnrolledStructureId);
        Assert.Contains(student.Id, school.EnrolledStudentIds);
        Assert.Equal(0, student.EducationProgressDays);  // just enrolled this tick; progression starts next tick
    }

    [Fact]
    public void UnderConstructionSchool_DoesNotAcceptStudents()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var school = sim.PlaceEducationStructure(StructureType.PrimarySchool);  // NOT fast-built
        var student = AddStudent(sim, ageDays: 6 * 360, startingTier: EducationTier.Uneducated);

        sim.Tick(10);

        Assert.Null(student.EnrolledStructureId);
        Assert.Empty(school.EnrolledStudentIds);
    }

    [Fact]
    public void BabyTooYoung_DoesNotEnroll()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        FastBuildSchool(sim, StructureType.PrimarySchool);
        var baby = AddStudent(sim, ageDays: 1_000, startingTier: EducationTier.Uneducated);  // age < BabyEndDay

        sim.Tick(1);

        Assert.Null(baby.EnrolledStructureId);
    }

    [Fact]
    public void AgentPastPrimaryBand_DoesNotEnrollInPrimary()
    {
        // Agent age = 12 years (4320 days), past primary band (1800-3960). Uneducated.
        // They can't enroll in primary anymore. They'd need to be in secondary band for secondary,
        // but they're uneducated and can't skip primary. So no enrollment ever.
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        FastBuildSchool(sim, StructureType.PrimarySchool);
        FastBuildSchool(sim, StructureType.SecondarySchool);
        var agent = AddStudent(sim, ageDays: 12 * 360, startingTier: EducationTier.Uneducated);

        sim.Tick(30);

        Assert.Null(agent.EnrolledStructureId);
        Assert.Equal(EducationTier.Uneducated, agent.EducationTier);
    }

    [Fact]
    public void EnrolledStudent_AccumulatesAttendanceDays()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        FastBuildSchool(sim, StructureType.PrimarySchool);
        var student = AddStudent(sim, ageDays: 6 * 360, startingTier: EducationTier.Uneducated);

        sim.Tick(1);   // enrollment happens
        sim.Tick(99);  // 99 more days of attendance

        Assert.Equal(99, student.EducationProgressDays);
    }

    [Fact]
    public void StudentCompletesPrimary_AdvancesToPrimaryTier_AndEnrollsInSecondary()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var primary = FastBuildSchool(sim, StructureType.PrimarySchool);
        var secondary = FastBuildSchool(sim, StructureType.SecondarySchool);
        var student = AddStudent(sim, ageDays: 5 * 360, startingTier: EducationTier.Uneducated);

        // Enroll (1 tick to enroll) + 2160 days to complete primary.
        // After completion they should be primary-educated and re-enrolled in secondary.
        sim.Tick(1 + Education.PrimaryDurationDays);

        Assert.Equal(EducationTier.Primary, student.EducationTier);
        Assert.DoesNotContain(student.Id, primary.EnrolledStudentIds);
        Assert.Contains(student.Id, secondary.EnrolledStudentIds);
        Assert.Equal(secondary.Id, student.EnrolledStructureId);
        Assert.Equal(0, student.EducationProgressDays);
    }

    [Fact]
    public void StudentCompletesPrimary_NoSecondarySchool_EntersWorkforceAtCurrentTier()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var primary = FastBuildSchool(sim, StructureType.PrimarySchool);
        var student = AddStudent(sim, ageDays: 5 * 360, startingTier: EducationTier.Uneducated);

        sim.Tick(1 + Education.PrimaryDurationDays);

        Assert.Equal(EducationTier.Primary, student.EducationTier);
        Assert.Null(student.EnrolledStructureId);
        Assert.DoesNotContain(student.Id, primary.EnrolledStudentIds);
    }

    [Fact]
    public void StudentCompletesEntirePath_UneducatedToCollege()
    {
        // Service emigration off — 16-game-year ticking would otherwise emigrate the student
        // before they finish (no civic/healthcare/utility coverage in this scenario).
        var sim = Sim.Create(new SimConfig { Seed = 42, ServiceEmigrationEnabled = false });
        FastBuildSchool(sim, StructureType.PrimarySchool);
        FastBuildSchool(sim, StructureType.SecondarySchool);
        FastBuildSchool(sim, StructureType.College);
        var student = AddStudent(sim, ageDays: 5 * 360, startingTier: EducationTier.Uneducated);

        var totalDays = 1
            + Education.PrimaryDurationDays
            + Education.SecondaryDurationDays
            + Education.CollegeDurationDays;
        sim.Tick(totalDays);

        Assert.Equal(EducationTier.College, student.EducationTier);
        Assert.Null(student.EnrolledStructureId);
    }

    [Fact]
    public void SeatsAreLimited_StudentsBeyondCapacityRemainUnenrolled()
    {
        // Place a primary school but artificially set its seat capacity to 2.
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var school = new Structure
        {
            Id = sim.State.AllocateStructureId(),
            Type = StructureType.PrimarySchool,
            ZoneId = 0,
            ResidentialCapacity = 0,
            ConstructionTicks = 90,           // operational immediately
            RequiredConstructionTicks = 90,
            SeatCapacity = 2,
        };
        sim.State.City.Structures[school.Id] = school;

        var s1 = AddStudent(sim, ageDays: 5 * 360, startingTier: EducationTier.Uneducated);
        var s2 = AddStudent(sim, ageDays: 5 * 360, startingTier: EducationTier.Uneducated);
        var s3 = AddStudent(sim, ageDays: 5 * 360, startingTier: EducationTier.Uneducated);

        sim.Tick(1);

        var enrolled = new[] { s1, s2, s3 }.Count(s => s.EnrolledStructureId.HasValue);
        Assert.Equal(2, enrolled);  // 2 seats filled, 1 left out
        Assert.Equal(2, school.EnrolledStudentIds.Count);
    }

    [Fact]
    public void DeadStudent_VacatesSeat()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var school = FastBuildSchool(sim, StructureType.PrimarySchool);
        var student = AddStudent(sim, ageDays: 5 * 360, startingTier: EducationTier.Uneducated);

        sim.Tick(1);
        Assert.Contains(student.Id, school.EnrolledStudentIds);

        // Force death.
        student.AgeDays = Demographics.LifespanDays - 1;
        sim.Tick(1);

        Assert.False(sim.State.City.Agents.ContainsKey(student.Id));
        Assert.DoesNotContain(student.Id, school.EnrolledStudentIds);
    }

    [Fact]
    public void EnrolledStudent_AgesPastBandMidAttendance_StillCompletes()
    {
        // Enroll an agent at age 10y (in primary band 5-11) — they'll be 16y when primary completes
        // (10 + 6 = 16). 16 is past primary band (which ends at 11) but per design they should
        // still complete primary because completion is attendance-based.
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var primary = FastBuildSchool(sim, StructureType.PrimarySchool);
        var student = AddStudent(sim, ageDays: 10 * 360, startingTier: EducationTier.Uneducated);

        sim.Tick(1 + Education.PrimaryDurationDays);

        Assert.Equal(EducationTier.Primary, student.EducationTier);
        // At this point student is ~16 years old, exactly at secondary band start. With no
        // secondary school placed, they remain in the workforce.
        Assert.Null(student.EnrolledStructureId);
    }

    [Fact]
    public void InactiveSchool_DoesNotAcceptStudents()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var school = FastBuildSchool(sim, StructureType.PrimarySchool);
        school.Inactive = true;
        var student = AddStudent(sim, ageDays: 5 * 360, startingTier: EducationTier.Uneducated);

        sim.Tick(1);

        Assert.Null(student.EnrolledStructureId);
        Assert.Empty(school.EnrolledStudentIds);
    }

    [Fact]
    public void CollegeTierAgent_DoesNotTryToEnrollFurther()
    {
        // A college-tier agent should not enroll in anything. Even if school exists.
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        FastBuildSchool(sim, StructureType.College);
        var grad = AddStudent(sim, ageDays: 18 * 360, startingTier: EducationTier.College);

        sim.Tick(10);

        Assert.Null(grad.EnrolledStructureId);
    }

    [Fact]
    public void Settlers_StayUneducatedOrPrimary_DoNotEnroll()
    {
        // Settlers are working-age (21-30 yrs). They should never enroll regardless of schools available.
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        FastBuildSchool(sim, StructureType.PrimarySchool);
        FastBuildSchool(sim, StructureType.SecondarySchool);
        FastBuildSchool(sim, StructureType.College);
        sim.CreateResidentialZone();  // spawns 50 settlers

        sim.Tick(30);

        Assert.All(sim.State.City.Agents.Values, a =>
            Assert.Null(a.EnrolledStructureId));
    }

    [Fact]
    public void BabyBornInCity_GrowsUpAndEnrollsInPrimary_WhenEligible()
    {
        // End-to-end: a baby born via BirthMechanic should reach primary-school age (5 game-years
        // = 1800 days) and then enroll in a primary school that exists.
        // Service emigration off — long tick window would otherwise emigrate the baby or its parents.
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            InitialReservoirSize = 1_000,
            ServiceEmigrationEnabled = false,
        });
        sim.CreateResidentialZone();
        FastBuildSchool(sim, StructureType.PrimarySchool);

        // Tick 4 months for first baby to be born (50 working-age × 0.005 × 4 = 1.0).
        sim.Tick(30 * 4);
        var baby = sim.State.City.Agents.Values
            .FirstOrDefault(a => a.AgeDays < Demographics.WorkingAgeStartDay && a.EducationTier == EducationTier.Uneducated && a.EnrolledStructureId == null);
        Assert.NotNull(baby);

        // Now tick until the baby is past primary-school-age start (1800 days).
        // Already 4 months = 120 days of age. Need ~1680 more to hit eligibility.
        var ticksToAge = Demographics.BabyEndDay - baby!.AgeDays + 1;
        sim.Tick(ticksToAge);

        // After aging into primary band, the EducationMechanic should have enrolled the baby.
        Assert.NotNull(baby.EnrolledStructureId);
    }

    [Fact]
    public void Determinism_EducationProgression_SameSeedSameResult()
    {
        Sim BuildSim()
        {
            var sim = Sim.Create(new SimConfig { Seed = 123 });
            FastBuildSchool(sim, StructureType.PrimarySchool);
            for (int i = 0; i < 5; i++)
                AddStudent(sim, ageDays: 5 * 360 + i, startingTier: EducationTier.Uneducated);
            sim.Tick(Education.PrimaryDurationDays + 1);
            return sim;
        }

        var a = BuildSim();
        var b = BuildSim();

        var tiersA = a.State.City.Agents.Values.Select(x => x.EducationTier).OrderBy(t => t).ToList();
        var tiersB = b.State.City.Agents.Values.Select(x => x.EducationTier).OrderBy(t => t).ToList();
        Assert.Equal(tiersA, tiersB);
    }
}
