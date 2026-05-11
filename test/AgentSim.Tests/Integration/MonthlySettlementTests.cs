using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Sim.Mechanics;
using AgentSim.Core.Types;

namespace AgentSim.Tests.Integration;

public class MonthlySettlementTests
{
    [Fact]
    public void DayOfMonth_MapsTicksCorrectly()
    {
        Assert.Equal(1, SettlementMechanic.DayOfMonth(1));
        Assert.Equal(15, SettlementMechanic.DayOfMonth(15));
        Assert.Equal(30, SettlementMechanic.DayOfMonth(30));
        Assert.Equal(1, SettlementMechanic.DayOfMonth(31));
        Assert.Equal(15, SettlementMechanic.DayOfMonth(45));
        Assert.Equal(30, SettlementMechanic.DayOfMonth(60));
    }

    [Fact]
    public void Day1_PaysRentToTreasury()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 0 });
        sim.CreateResidentialZone();

        sim.Tick(1);  // tick to day 1

        // 50 settlers × $800 rent = $40,000
        Assert.Equal(50 * 800, sim.State.City.TreasuryBalance);
    }

    [Fact]
    public void Day1_DeductsRentFromAgentSavings()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        sim.Tick(1);

        var uneducated = sim.State.City.Agents.Values.First(a => a.EducationTier == EducationTier.Uneducated);
        Assert.Equal(Bootstrap.FoundersStartingSavings - 800, uneducated.Savings);
    }

    [Fact]
    public void Day15_PaysUtilitiesToTreasury()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 0 });
        sim.CreateResidentialZone();

        sim.Tick(15);  // through day 15

        // Day 1: rent $800 × 50 = $40,000
        // Day 15: utilities $200 × 50 = $10,000
        // Total: $50,000
        Assert.Equal(50 * 800 + 50 * 200, sim.State.City.TreasuryBalance);
    }

    [Fact]
    public void Day15_DeductsUtilitiesFromAgentSavings()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        sim.Tick(15);

        var uneducated = sim.State.City.Agents.Values.First(a => a.EducationTier == EducationTier.Uneducated);
        // Founders' bonus $5,000 minus $800 rent (day 1) minus $200 utilities (day 15) = $4,000
        Assert.Equal(Bootstrap.FoundersStartingSavings - 800 - 200, uneducated.Savings);
    }

    [Fact]
    public void AfterOneMonth_TreasuryHasFullRentAndUtilities()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 0 });
        sim.CreateResidentialZone();

        sim.Tick(30);  // full month

        Assert.Equal(50 * (800 + 200), sim.State.City.TreasuryBalance);
    }

    [Fact]
    public void AfterOneMonth_AllSettlersStillInCity()
    {
        // Settlers without jobs should survive month 1 — starting savings cover one month of expenses.
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        sim.Tick(30);

        Assert.Equal(50, sim.State.City.Population);
    }

    [Fact]
    public void AfterFiveMonths_AllSettlersStillInCity_NoJobs()
    {
        // Founders' bonus = $5,000. Pre-commercial expenses = $1,000/mo (rent + utilities, no COL).
        // End month 1: $4,000 / 2: $3,000 / 3: $2,000 / 4: $1,000 / 5: $0 (still non-negative).
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        sim.Tick(30 * 5);  // 5 months

        Assert.Equal(50, sim.State.City.Population);
    }

    [Fact]
    public void AfterSixMonths_SettlersEmigrate_NoJobs()
    {
        // End of month 6: $5,000 - 6 × $1,000 = -$1,000 → fail emigration check → no AH → emigrate.
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        sim.Tick(30 * 6);  // 6 months

        Assert.Equal(0, sim.State.City.Population);
    }

    [Fact]
    public void EmigratedAgents_ReturnToReservoir()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, RegionalReservoirSize = 60_000 });
        sim.CreateResidentialZone();

        Assert.Equal(60_000 - 50, sim.State.Region.AgentReservoir.Total);

        sim.Tick(30 * 6);  // 6 months — all settlers emigrate

        // All 50 returned to reservoir at their education tiers (30 uneducated + 20 primary)
        Assert.Equal(60_000, sim.State.Region.AgentReservoir.Total);
    }

    [Fact]
    public void Month5_AllSettlersHaveZeroSavings_ButStillInCity()
    {
        // End of month 5: $5,000 - 5 × $1,000 = $0 (non-negative — passes check)
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        sim.Tick(30 * 5);  // 5 months

        Assert.Equal(50, sim.State.City.Population);
        Assert.All(sim.State.City.Agents.Values, a => Assert.Equal(0, a.Savings));
    }

    [Fact]
    public void NoWageMeansNoIncomeTax()
    {
        // Settlers have $0 wage in M2 (no jobs). Income tax should be $0.
        // Treasury growth = rent + utilities only (no income tax contribution).
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 0 });
        sim.CreateResidentialZone();

        sim.Tick(30);

        Assert.Equal(50 * (800 + 200), sim.State.City.TreasuryBalance);  // no income tax added
    }

    [Fact]
    public void Determinism_TwoMonths_IdenticalResults()
    {
        var sim1 = Sim.Create(new SimConfig { Seed = 42 });
        var sim2 = Sim.Create(new SimConfig { Seed = 42 });

        sim1.CreateResidentialZone();
        sim2.CreateResidentialZone();

        sim1.Tick(60);
        sim2.Tick(60);

        Assert.Equal(sim1.State.City.Population, sim2.State.City.Population);
        Assert.Equal(sim1.State.City.TreasuryBalance, sim2.State.City.TreasuryBalance);
        Assert.Equal(sim1.State.Region.AgentReservoir.Total, sim2.State.Region.AgentReservoir.Total);
    }
}
