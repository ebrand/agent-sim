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
        Assert.Equal(Bootstrap.StartingSavings(EducationTier.Uneducated) - 800, uneducated.Savings);
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
        // Started $1,800, paid $800 rent + $200 utilities = $1,000 left
        Assert.Equal(1_800 - 800 - 200, uneducated.Savings);
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
    public void AfterTwoMonths_UneducatedSettlersEmigrate_NoJobs()
    {
        // Uneducated settler: $1,800 starting → -$1,000/mo expenses
        // End of month 1: $800 (passes check)
        // End of month 2: -$200 (savings goes negative when paying utilities) → fails → no AH → emigrate
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        sim.Tick(60);  // 2 months

        var uneducatedRemaining = sim.State.City.Agents.Values
            .Count(a => a.EducationTier == EducationTier.Uneducated);
        Assert.Equal(0, uneducatedRemaining);
    }

    [Fact]
    public void EmigratedAgents_ReturnToReservoir()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, RegionalReservoirSize = 60_000 });
        sim.CreateResidentialZone();

        var reservoirBeforeEmigration = sim.State.Region.AgentReservoir.Total;
        Assert.Equal(60_000 - 50, reservoirBeforeEmigration);

        sim.Tick(60);  // uneducated emigrate at end of month 2

        // 30 uneducated returned to reservoir, 20 primary still in city
        Assert.Equal(60_000 - 20, sim.State.Region.AgentReservoir.Total);
    }

    [Fact]
    public void AfterFourMonths_PrimarySettlersEmigrate_NoJobs()
    {
        // Primary settler: $3,000 starting → -$1,000/mo expenses
        // End month 1: $2,000 (pass)
        // End month 2: $1,000 (pass)
        // End month 3: $0 (pass — savings non-negative)
        // End month 4: -$1,000 (fail) → no AH → emigrate
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        sim.Tick(30 * 4);  // 4 months

        var primaryRemaining = sim.State.City.Agents.Values
            .Count(a => a.EducationTier == EducationTier.Primary);
        Assert.Equal(0, primaryRemaining);
    }

    [Fact]
    public void Month3_PrimarySettlersHaveZeroSavings_ButStillInCity()
    {
        // Primary settler at end of month 3: savings = $0 (non-negative — passes check)
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        sim.Tick(30 * 3);  // 3 months

        var primaryAgents = sim.State.City.Agents.Values
            .Where(a => a.EducationTier == EducationTier.Primary)
            .ToList();
        Assert.Equal(20, primaryAgents.Count);
        Assert.All(primaryAgents, a => Assert.Equal(0, a.Savings));
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
