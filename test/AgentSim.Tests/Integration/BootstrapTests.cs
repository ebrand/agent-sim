using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Types;

namespace AgentSim.Tests.Integration;

public class BootstrapTests
{
    [Fact]
    public void NewSim_HasNoPopulation()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });

        Assert.Equal(0, sim.State.City.Population);
        Assert.Empty(sim.State.City.Structures);
        Assert.Empty(sim.State.City.Zones);
    }

    [Fact]
    public void NewSim_RegionalReservoirAtCap()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, RegionalReservoirSize = 60_000 });

        Assert.Equal(60_000, sim.State.Region.AgentReservoir.Total);
    }

    [Fact]
    public void NewSim_StartsAtTickZero()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });

        Assert.Equal(0, sim.State.CurrentTick);
    }

    [Fact]
    public void CreateResidentialZone_FiresBootstrapBurst()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });

        sim.CreateResidentialZone();

        Assert.Equal(Demographics.SettlerCount, sim.State.City.Population);
    }

    [Fact]
    public void Bootstrap_DrawsSettlersFromReservoir()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, RegionalReservoirSize = 60_000 });

        sim.CreateResidentialZone();

        Assert.Equal(60_000 - Demographics.SettlerCount, sim.State.Region.AgentReservoir.Total);
    }

    [Fact]
    public void Bootstrap_SettlerDistributionIs60_40()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });

        sim.CreateResidentialZone();

        var uneducated = sim.State.City.Agents.Values.Count(a => a.EducationTier == EducationTier.Uneducated);
        var primary = sim.State.City.Agents.Values.Count(a => a.EducationTier == EducationTier.Primary);
        var secondary = sim.State.City.Agents.Values.Count(a => a.EducationTier == EducationTier.Secondary);
        var college = sim.State.City.Agents.Values.Count(a => a.EducationTier == EducationTier.College);

        Assert.Equal(30, uneducated);
        Assert.Equal(20, primary);
        Assert.Equal(0, secondary);
        Assert.Equal(0, college);
    }

    [Fact]
    public void Bootstrap_Spawns13Houses()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });

        sim.CreateResidentialZone();

        var houses = sim.State.City.Structures.Values
            .Where(s => s.Type == StructureType.House)
            .ToList();
        Assert.Equal(13, houses.Count);
    }

    [Fact]
    public void Bootstrap_HousesAreOperationalImmediately()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });

        sim.CreateResidentialZone();

        var houses = sim.State.City.Structures.Values
            .Where(s => s.Type == StructureType.House)
            .ToList();
        Assert.All(houses, h => Assert.True(h.Operational, "Bootstrap house should be operational immediately"));
    }

    [Fact]
    public void Bootstrap_AllSettlersHaveHomes()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });

        sim.CreateResidentialZone();

        Assert.All(sim.State.City.Agents.Values, a => Assert.NotNull(a.ResidenceStructureId));
    }

    [Fact]
    public void Bootstrap_ConsumesGoodsStock()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });

        sim.CreateResidentialZone();

        // 13 houses × {10 bldg supplies, 5 concrete, 2 glass goods}
        // = 130 bldg supplies, 65 concrete, 26 glass goods consumed from {200, 100, 40} stock
        // Remaining: 70 bldg supplies, 35 concrete, 14 glass goods, 0 metal goods
        Assert.Equal(70, sim.State.Region.GoodsReservoir[ManufacturedGood.BldgSupplies]);
        Assert.Equal(35, sim.State.Region.GoodsReservoir[ManufacturedGood.Concrete]);
        Assert.Equal(14, sim.State.Region.GoodsReservoir[ManufacturedGood.GlassGoods]);
        Assert.Equal(0, sim.State.Region.GoodsReservoir[ManufacturedGood.MetalGoods]);
    }

    [Fact]
    public void Bootstrap_FiresOnlyOnce()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });

        sim.CreateResidentialZone();
        sim.CreateResidentialZone();  // second zone — should NOT trigger another settler burst

        Assert.Equal(Demographics.SettlerCount, sim.State.City.Population);
    }

    [Fact]
    public void Bootstrap_SettlersGetFoundersBonusSavings()
    {
        // Settlers receive a founders' bonus (flat $5,000) instead of the per-tier immigrant savings.
        // This gives them ~5 months of pre-commercial cushion to survive while the player builds commercial.
        var sim = Sim.Create(new SimConfig { Seed = 42 });

        sim.CreateResidentialZone();

        Assert.All(sim.State.City.Agents.Values,
            a => Assert.Equal(Bootstrap.FoundersStartingSavings, a.Savings));
    }

    [Fact]
    public void Tick_AdvancesCurrentTick()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });

        sim.Tick(30);

        Assert.Equal(30, sim.State.CurrentTick);
    }

    [Fact]
    public void DeterministicSeed_ProducesIdenticalResults()
    {
        var sim1 = Sim.Create(new SimConfig { Seed = 42 });
        var sim2 = Sim.Create(new SimConfig { Seed = 42 });

        sim1.CreateResidentialZone();
        sim2.CreateResidentialZone();

        // Settler ages should be identical between two seeded runs
        var ages1 = sim1.State.City.Agents.Values.Select(a => a.AgeDays).OrderBy(x => x).ToList();
        var ages2 = sim2.State.City.Agents.Values.Select(a => a.AgeDays).OrderBy(x => x).ToList();

        Assert.Equal(ages1, ages2);
    }
}
