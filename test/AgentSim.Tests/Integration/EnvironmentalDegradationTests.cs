using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Sim.Mechanics;
using AgentSim.Core.Types;

namespace AgentSim.Tests.Integration;

/// <summary>
/// M15: industrial production degrades Region.Climate and Region.Nature; restoration structures
/// reverse some of that. Environmental quality (the worse of climate / nature) feeds into the
/// worst-of-services emigration formula.
/// </summary>
public class EnvironmentalDegradationTests
{
    private static (Sim sim, Structure hq) NewSimWithHq(IndustryType industry)
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 10_000_000 });
        var commZone = sim.CreateCommercialZone();
        var hq = sim.PlaceCorporateHq(commZone.Id, industry, $"TestCo-{industry}");
        hq.ConstructionTicks = hq.RequiredConstructionTicks;
        hq.CashBalance = 5_000_000;
        return (sim, hq);
    }

    private static Structure StaffAndFastBuild(Sim sim, StructureType type, long hqId)
    {
        var s = sim.PlaceIndustrialStructure(type, hqId);
        s.ConstructionTicks = s.RequiredConstructionTicks;
        foreach (var (tier, count) in s.JobSlots) s.FilledSlots[tier] = count;
        return s;
    }

    [Fact]
    public void ClimateDegrades_WhenIndustryProduces()
    {
        var (sim, hq) = NewSimWithHq(IndustryType.Oil);
        var well = StaffAndFastBuild(sim, StructureType.OilWell, hq.Id);
        var climateBefore = sim.State.Region.Climate;

        sim.Tick(30);

        Assert.True(sim.State.Region.Climate < climateBefore,
            $"Climate should degrade with active OilWell. Before: {climateBefore}, after: {sim.State.Region.Climate}");
    }

    [Fact]
    public void OilIndustry_DegradesClimateFasterThanAgriculture()
    {
        // Same setup but different industries — Oil should hit climate harder.
        var simOil = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 10_000_000 });
        var z1 = simOil.CreateCommercialZone();
        var ohq = simOil.PlaceCorporateHq(z1.Id, IndustryType.Oil, "OilCo");
        ohq.ConstructionTicks = ohq.RequiredConstructionTicks;
        ohq.CashBalance = 5_000_000;
        StaffAndFastBuild(simOil, StructureType.OilWell, ohq.Id);

        var simAg = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 10_000_000 });
        var z2 = simAg.CreateCommercialZone();
        var ahq = simAg.PlaceCorporateHq(z2.Id, IndustryType.Agriculture, "AgriCo");
        ahq.ConstructionTicks = ahq.RequiredConstructionTicks;
        ahq.CashBalance = 5_000_000;
        StaffAndFastBuild(simAg, StructureType.Farm, ahq.Id);

        simOil.Tick(30);
        simAg.Tick(30);

        var oilLoss = 0.7 - simOil.State.Region.Climate;
        var agLoss = 0.7 - simAg.State.Region.Climate;
        Assert.True(oilLoss > agLoss,
            $"Oil should degrade climate more than Agriculture. Oil loss: {oilLoss}, Ag loss: {agLoss}");
    }

    [Fact]
    public void Climate_FloorsAt05_NeverGoesLower()
    {
        // Run extreme degradation long enough that climate would go negative without the floor.
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 50_000_000 });
        var z = sim.CreateCommercialZone();
        var hq = sim.PlaceCorporateHq(z.Id, IndustryType.Oil, "OilCo");
        hq.ConstructionTicks = hq.RequiredConstructionTicks;
        hq.CashBalance = 25_000_000;
        // Multiple OilWells produce concentrated impact.
        for (int i = 0; i < 5; i++)
        {
            StaffAndFastBuild(sim, StructureType.OilWell, hq.Id);
        }

        sim.Tick(360 * 5);  // 5 game-years of heavy industry

        Assert.True(sim.State.Region.Climate >= AgentSim.Core.Defaults.Environment.EnvironmentFloor,
            $"Climate should be clamped at the floor. Got {sim.State.Region.Climate}.");
    }

    [Fact]
    public void Park_RestoresClimate()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        // Start with a slightly degraded climate so restoration has room to climb.
        sim.State.Region.Climate = 0.50;
        sim.State.Region.Nature = 0.50;
        var park = sim.PlaceRestorationStructure(StructureType.Park);
        park.ConstructionTicks = park.RequiredConstructionTicks;

        sim.Tick(30);

        Assert.True(sim.State.Region.Climate > 0.50,
            $"Park should restore climate. Got {sim.State.Region.Climate}.");
    }

    [Fact]
    public void ReforestationSite_RestoresMoreThanPark()
    {
        // Both restoration types running for the same period; reforestation should out-pace.
        var simPark = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        simPark.State.Region.Climate = 0.50;
        var park = simPark.PlaceRestorationStructure(StructureType.Park);
        park.ConstructionTicks = park.RequiredConstructionTicks;

        var simReforest = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        simReforest.State.Region.Climate = 0.50;
        var refSite = simReforest.PlaceRestorationStructure(StructureType.ReforestationSite);
        refSite.ConstructionTicks = refSite.RequiredConstructionTicks;

        simPark.Tick(60);
        simReforest.Tick(60);

        Assert.True(simReforest.State.Region.Climate > simPark.State.Region.Climate,
            $"Reforestation should restore more than a Park. Reforest: {simReforest.State.Region.Climate}, Park: {simPark.State.Region.Climate}");
    }

    [Fact]
    public void EnvironmentalQuality_FeedsServiceEmigration()
    {
        // With healthy services but degraded environment, worst-of should reflect the environment.
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.State.Region.Climate = 0.30;  // 30% — well below the 60% emigration threshold
        sim.State.Region.Nature = 0.30;
        var snap = ServiceSatisfactionMechanic.Compute(sim.State);
        Assert.Equal(30.0, snap.EnvironmentalPercent);

        // A working-age agent with no enrollment — full service satisfaction baseline.
        var agent = new Agent
        {
            Id = sim.State.AllocateAgentId(),
            EducationTier = EducationTier.College,
            AgeDays = 30 * 360,
        };
        sim.State.City.Agents[agent.Id] = agent;

        var fakeSnap = new ServiceSatisfactionMechanic.Snapshot
        {
            CivicPercent = 100,
            HealthcarePercent = 100,
            UtilityPercent = 100,
            PrimaryEducationPercent = 100,
            SecondaryEducationPercent = 100,
            CollegeEducationPercent = 100,
            EnvironmentalPercent = 30,    // the bad one
        };

        var worst = ServiceSatisfactionMechanic.WorstOfForAgent(agent, fakeSnap);
        Assert.Equal(30.0, worst);
    }

    [Fact]
    public void InactiveStructure_DoesNotDegrade()
    {
        var (sim, hq) = NewSimWithHq(IndustryType.Oil);
        var well = StaffAndFastBuild(sim, StructureType.OilWell, hq.Id);
        well.Inactive = true;
        var climateBefore = sim.State.Region.Climate;

        sim.Tick(60);

        Assert.Equal(climateBefore, sim.State.Region.Climate);
    }
}
