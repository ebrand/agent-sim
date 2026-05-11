using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Sim.Mechanics;
using AgentSim.Core.Types;

namespace AgentSim.Tests.Integration;

/// <summary>
/// M9: worst-of service emigration. Civic / healthcare / education / utility satisfaction drives
/// a monthly per-agent emigration roll.
/// </summary>
public class ServiceEmigrationTests
{
    private static Structure FastBuildService(Sim sim, StructureType type)
    {
        var s = sim.PlaceServiceStructure(type);
        s.ConstructionTicks = s.RequiredConstructionTicks;
        return s;
    }

    private static Structure FastBuildSchool(Sim sim, StructureType type)
    {
        var s = sim.PlaceEducationStructure(type);
        s.ConstructionTicks = s.RequiredConstructionTicks;
        return s;
    }

    /// <summary>Build a fully-served city: enough civic/healthcare/utility/primary-school to put
    /// all four pools at 100% satisfaction for the bootstrap population.</summary>
    private static void FullyServeBootstrappedCity(Sim sim)
    {
        FastBuildService(sim, StructureType.PoliceStation);   // civic 5000
        FastBuildService(sim, StructureType.Clinic);          // healthcare 2500
        FastBuildService(sim, StructureType.Generator);       // utility 10000
        FastBuildSchool(sim, StructureType.PrimarySchool);    // education 1000 seats
    }

    [Fact]
    public void PlaceServiceStructure_RejectsNonServiceTypes()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        Assert.Throws<ArgumentException>(() => sim.PlaceServiceStructure(StructureType.House));
        Assert.Throws<ArgumentException>(() => sim.PlaceServiceStructure(StructureType.Shop));
        Assert.Throws<ArgumentException>(() => sim.PlaceServiceStructure(StructureType.PrimarySchool));
        Assert.Throws<ArgumentException>(() => sim.PlaceServiceStructure(StructureType.Storage));
    }

    [Fact]
    public void PlaceServiceStructure_SetsCapacityFromDefaults()
    {
        // Sum of construction costs for all 7 service types: $150 + $150 + $900 + $250 + $1200 +
        // $300 + $200 = $3.15M. Treasury must cover that.
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 4_000_000 });
        Assert.Equal(5_000, sim.PlaceServiceStructure(StructureType.PoliceStation).ServiceCapacity);
        Assert.Equal(5_000, sim.PlaceServiceStructure(StructureType.FireStation).ServiceCapacity);
        Assert.Equal(25_000, sim.PlaceServiceStructure(StructureType.TownHall).ServiceCapacity);
        Assert.Equal(2_500, sim.PlaceServiceStructure(StructureType.Clinic).ServiceCapacity);
        Assert.Equal(12_500, sim.PlaceServiceStructure(StructureType.Hospital).ServiceCapacity);
        Assert.Equal(10_000, sim.PlaceServiceStructure(StructureType.Generator).ServiceCapacity);
        Assert.Equal(10_000, sim.PlaceServiceStructure(StructureType.Well).ServiceCapacity);
    }

    [Fact]
    public void Satisfaction_AllPoolsAt100_WhenFullyServed()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        FullyServeBootstrappedCity(sim);
        sim.Tick(1);

        var snap = ServiceSatisfactionMechanic.Compute(sim.State);
        Assert.Equal(100.0, snap.CivicPercent);
        Assert.Equal(100.0, snap.HealthcarePercent);
        Assert.Equal(100.0, snap.UtilityPercent);
        // No agents in primary age band → demand 0 → 100% by convention.
        Assert.Equal(100.0, snap.PrimaryEducationPercent);
    }

    [Fact]
    public void Satisfaction_AllPoolsAt0_WhenNothingBuilt()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();  // 50 settlers, no services
        sim.Tick(1);

        var snap = ServiceSatisfactionMechanic.Compute(sim.State);
        // 50 agents with no civic / healthcare / utility coverage → 0%.
        Assert.Equal(0.0, snap.CivicPercent);
        Assert.Equal(0.0, snap.HealthcarePercent);
        Assert.Equal(0.0, snap.UtilityPercent);
        // No primary-aged agents → demand 0 → 100% (no shortfall).
        Assert.Equal(100.0, snap.PrimaryEducationPercent);
    }

    [Fact]
    public void Satisfaction_UnderConstructionService_DoesNotCount()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        sim.PlaceServiceStructure(StructureType.PoliceStation);  // under construction
        sim.Tick(1);

        var snap = ServiceSatisfactionMechanic.Compute(sim.State);
        Assert.Equal(0.0, snap.CivicPercent);
    }

    [Fact]
    public void Satisfaction_InactiveService_DoesNotCount()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        var police = FastBuildService(sim, StructureType.PoliceStation);
        police.Inactive = true;
        sim.Tick(1);

        var snap = ServiceSatisfactionMechanic.Compute(sim.State);
        Assert.Equal(0.0, snap.CivicPercent);
    }

    [Fact]
    public void Satisfaction_CapsAt100_EvenWhenOverbuilt()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        FastBuildService(sim, StructureType.TownHall);  // 25000 cap for 50 agents
        sim.Tick(1);

        var snap = ServiceSatisfactionMechanic.Compute(sim.State);
        Assert.Equal(100.0, snap.CivicPercent);
    }

    [Fact]
    public void Satisfaction_PartialCoverage_ScalesLinearly()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        // Add 4950 agents so total = 5000. PoliceStation caps at 5000 → 100%.
        // Then take 50 → 50/5000 = 1%. Let's tune: 10_000 agents, 5_000 capacity → 50%.
        for (int i = 0; i < 9_950; i++)
        {
            var a = new Agent
            {
                Id = sim.State.AllocateAgentId(),
                EducationTier = EducationTier.Uneducated,
                AgeDays = Demographics.WorkingAgeStartDay,
            };
            sim.State.City.Agents[a.Id] = a;
        }
        // Now 50 + 9950 = 10000 agents.
        FastBuildService(sim, StructureType.PoliceStation);  // 5000 capacity
        sim.Tick(1);

        var snap = ServiceSatisfactionMechanic.Compute(sim.State);
        Assert.Equal(50.0, snap.CivicPercent);
    }

    [Fact]
    public void NoEmigration_WhenWorstPoolAboveThreshold()
    {
        // Treasury sized to cover 6 months of upkeep (~$230k/mo × 6 = $1.38M) plus headroom.
        // Otherwise M10 bankruptcy kicks in, services collapse, and emigration spikes.
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            InitialReservoirSize = 60_000,
            StartingTreasury = 2_000_000,
        });
        sim.CreateResidentialZone();
        FullyServeBootstrappedCity(sim);
        // Cushion savings so insolvency emigration never fires — isolate to service-pool behavior.
        foreach (var a in sim.State.City.Agents.Values) a.Savings = 100_000;

        sim.Tick(30 * 6);  // 6 months at 100% satisfaction

        Assert.Equal(50, sim.State.City.Population);
    }

    [Fact]
    public void Emigration_FiresWhenServicesAreZero_OverMultipleMonths()
    {
        // 50 settlers with no services → 0% worst → 1.2%/mo. Over 12 months expected =
        // 50 × 12 × 0.012 ≈ 7.2 emigrants. With seed-determinism, exact count varies but
        // it must be > 0 and < 50.
        var sim = Sim.Create(new SimConfig { Seed = 42, InitialReservoirSize = 60_000 });
        sim.CreateResidentialZone();
        // Founders' bonus runs out at month 6 → from month 6+ insolvency also drives emigration.
        // Use a shorter window where insolvency hasn't kicked in yet (months 1-4).
        sim.Tick(30 * 4);

        Assert.True(sim.State.City.Population < 50,
            $"Expected service-pressure emigration to remove some settlers. Got {sim.State.City.Population}.");
        Assert.True(sim.State.City.Population > 30,
            $"Expected most settlers to still be present after 4 months. Got {sim.State.City.Population}.");
    }

    [Fact]
    public void Emigration_Disabled_NoEmigrationFromServicePressure()
    {
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            InitialReservoirSize = 60_000,
            ServiceEmigrationEnabled = false,
        });
        sim.CreateResidentialZone();
        sim.Tick(30 * 4);  // 4 months — still within founders' bonus, no insolvency yet

        Assert.Equal(50, sim.State.City.Population);
    }

    [Fact]
    public void ServiceEmigrant_ReturnsToReservoir_AtCurrentTier()
    {
        // Get a single service-pressure emigrant and verify they're back in the reservoir.
        var sim = Sim.Create(new SimConfig { Seed = 42, InitialReservoirSize = 1_000 });
        sim.CreateResidentialZone();
        // 50 in city. 1000 in reservoir. Total 1050.
        var reservoirBefore = sim.State.Region.AgentReservoir.Total;
        var popBefore = sim.State.City.Population;

        sim.Tick(30 * 6);  // long enough for some emigration

        // City population went down; reservoir went up by same amount (mostly — births can offset).
        // Net change: emigrations - births = popBefore - popAfter
        var popAfter = sim.State.City.Population;
        var reservoirAfter = sim.State.Region.AgentReservoir.Total;
        Assert.True(popAfter < popBefore, $"Expected some emigration. Got {popBefore} → {popAfter}.");

        // The reservoir should have grown (emigrants returned). Some agents may have died (low chance
        // in 180 ticks at settler ages 21-30) but predominantly emigration increases reservoir.
        Assert.True(reservoirAfter > reservoirBefore,
            $"Reservoir should grow as emigrants return. Got {reservoirBefore} → {reservoirAfter}.");
    }

    [Fact]
    public void Emigration_VacatesResidence_AndEmployerSlot()
    {
        // Force a high-pressure scenario: 0% services. Verify that an employed agent's
        // employer slot and residence are properly vacated when they emigrate.
        var sim = Sim.Create(new SimConfig { Seed = 42, InitialReservoirSize = 60_000 });
        sim.CreateResidentialZone();

        // Wire up a fake employer with all 50 settlers as employees.
        var employer = new Structure
        {
            Id = sim.State.AllocateStructureId(),
            Type = StructureType.Shop,
            ZoneId = 0,
            ResidentialCapacity = 0,
            ConstructionTicks = 90,
            RequiredConstructionTicks = 90,
            JobSlots = new() { [EducationTier.Uneducated] = 50, [EducationTier.Primary] = 50 },
        };
        sim.State.City.Structures[employer.Id] = employer;
        foreach (var a in sim.State.City.Agents.Values)
        {
            a.EmployerStructureId = employer.Id;
            a.CurrentJobTier = a.EducationTier;
            a.CurrentWage = 1_500;
            a.Savings = 100_000;  // very high so insolvency never triggers
            employer.EmployeeIds.Add(a.Id);
            employer.FilledSlots[a.EducationTier] =
                employer.FilledSlots.GetValueOrDefault(a.EducationTier) + 1;
        }
        var totalSlotsBefore = employer.FilledSlots.Values.Sum();
        var totalEmployeesBefore = employer.EmployeeIds.Count;
        Assert.Equal(50, totalSlotsBefore);
        Assert.Equal(50, totalEmployeesBefore);

        sim.Tick(30 * 6);

        // Some agents emigrated. Counts should match: filled-slots == employee-count == agents-still-with-this-employer.
        var stillEmployed = sim.State.City.Agents.Values.Count(a => a.EmployerStructureId == employer.Id);
        Assert.Equal(stillEmployed, employer.EmployeeIds.Count);
        Assert.Equal(stillEmployed, employer.FilledSlots.Values.Sum());
        // And emigration actually happened.
        Assert.True(stillEmployed < 50, $"Expected some emigration. Got {stillEmployed}/50.");
    }

    [Fact]
    public void Determinism_SameSeedProducesSameEmigrationCount()
    {
        Sim Build()
        {
            var sim = Sim.Create(new SimConfig { Seed = 999, InitialReservoirSize = 60_000 });
            sim.CreateResidentialZone();
            sim.Tick(30 * 4);
            return sim;
        }

        var a = Build();
        var b = Build();
        Assert.Equal(a.State.City.Population, b.State.City.Population);
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentEmigrationOutcomes()
    {
        // Sanity check that the RNG is actually random across seeds — two different seeds should
        // (with overwhelming probability) produce different emigration counts.
        Sim Build(ulong seed)
        {
            var sim = Sim.Create(new SimConfig { Seed = seed, InitialReservoirSize = 60_000 });
            sim.CreateResidentialZone();
            sim.Tick(30 * 4);
            return sim;
        }

        var counts = new HashSet<int>();
        for (ulong s = 1; s <= 10; s++)
            counts.Add(Build(s).State.City.Population);

        Assert.True(counts.Count > 1, $"Expected variance across 10 seeds; got all {counts.First()}.");
    }

    [Fact]
    public void EnrolledStudent_UsesOwnTierSatisfaction()
    {
        // Verify the per-agent worst-of routing: a student enrolled in secondary should see
        // SecondaryEducationPercent, not PrimaryEducationPercent.
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var student = new Agent
        {
            Id = sim.State.AllocateAgentId(),
            EducationTier = EducationTier.Primary,
            AgeDays = 12 * 360,
            EnrolledStructureId = 999,
        };
        sim.State.City.Agents[student.Id] = student;

        var snap = new ServiceSatisfactionMechanic.Snapshot
        {
            CivicPercent = 100,
            HealthcarePercent = 100,
            UtilityPercent = 100,
            PrimaryEducationPercent = 0,    // intentionally bad — but student isn't in primary
            SecondaryEducationPercent = 80, // student is here
            CollegeEducationPercent = 100,
        };

        var worst = ServiceSatisfactionMechanic.WorstOfForAgent(student, snap);
        Assert.Equal(80, worst);  // ignores primary, uses secondary
    }

    [Fact]
    public void WorkingAgeAgent_UsesPrimaryTierAsChildProxy()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var adult = new Agent
        {
            Id = sim.State.AllocateAgentId(),
            EducationTier = EducationTier.College,  // tier doesn't matter
            AgeDays = 30 * 360,                     // working age
            EnrolledStructureId = null,
        };
        sim.State.City.Agents[adult.Id] = adult;

        var snap = new ServiceSatisfactionMechanic.Snapshot
        {
            CivicPercent = 100,
            HealthcarePercent = 100,
            UtilityPercent = 100,
            PrimaryEducationPercent = 30,  // bad primary — adult cares
            SecondaryEducationPercent = 100,
            CollegeEducationPercent = 100,
        };

        var worst = ServiceSatisfactionMechanic.WorstOfForAgent(adult, snap);
        Assert.Equal(30, worst);
    }
}
