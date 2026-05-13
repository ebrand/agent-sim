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
    public void TickBeforeDay30_NoMoneyFlows()
    {
        // Under the single-day settlement model, days 1-29 are economic no-ops.
        // Treasury and agent savings should be unchanged until tick 30.
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 0 });
        sim.CreateResidentialZone();
        var treasuryStart = sim.State.City.TreasuryBalance;
        var agentSavingsStart = sim.State.City.Agents.Values.First().Savings;

        sim.Tick(29);

        Assert.Equal(treasuryStart, sim.State.City.TreasuryBalance);
        Assert.Equal(agentSavingsStart, sim.State.City.Agents.Values.First().Savings);
    }

    [Fact]
    public void Day30_RentAndUtilitiesFlowToTreasury()
    {
        // All flows happen on day 30 in a single settlement.
        // 50 settlers × ($800 rent + $200 utilities) = $50,000.
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 0 });
        sim.CreateResidentialZone();

        sim.Tick(30);

        // 30 Uneducated × ($800 rent + $80 util) + 20 Primary × ($1,400 rent + $140 util) = $57,200.
        Assert.Equal(30 * (450 + 45) + 20 * (800 + 80), sim.State.City.TreasuryBalance);
    }

    [Fact]
    public void Day30_AgentPaysRentAndUtilities()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        sim.Tick(30);

        // No commercial → no COL. Settler savings after one month = founders bonus - rent - utilities.
        var uneducated = sim.State.City.Agents.Values.First(a => a.EducationTier == EducationTier.Uneducated);
        // Uneducated: $5000 - $800 rent - $80 utility (10% of rent) = $4120
        Assert.Equal(Bootstrap.FoundersStartingSavings(EducationTier.Uneducated) - 450 - 45, uneducated.Savings);
    }

    [Fact]
    public void AfterOneMonth_TreasuryHasFullRentAndUtilities()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 0 });
        sim.CreateResidentialZone();

        sim.Tick(30);  // full month

        // 30 Uneducated × ($800 rent + $80 util) + 20 Primary × ($1,400 rent + $140 util) = $57,200.
        Assert.Equal(30 * (450 + 45) + 20 * (800 + 80), sim.State.City.TreasuryBalance);
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
        // Disable births (full reservoir) and service emigration; test focuses on settler insolvency only.
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            InitialReservoirSize = 60_000,
            ServiceEmigrationEnabled = false,
        });
        sim.CreateResidentialZone();

        sim.Tick(30 * 5);  // 5 months

        Assert.Equal(50, sim.State.City.Population);
    }

    [Fact]
    public void After18Months_SettlersEmigrate_NoJobs()
    {
        // Founders bonus + lower rents stretch to ~14-16 months. By month 18 all settlers
        // have exhausted savings and emigrated.
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            InitialReservoirSize = 60_000,
            ImmigrationEnabled = false,
        });
        sim.CreateResidentialZone();

        sim.Tick(30 * 18);

        Assert.Equal(0, sim.State.City.Population);
    }

    [Fact]
    public void EmigratedAgents_ReturnToReservoir()
    {
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            InitialReservoirSize = 60_000,
            ImmigrationEnabled = false,
        });
        sim.CreateResidentialZone();

        Assert.Equal(60_000 - 50, sim.State.Region.AgentReservoir.Total);

        sim.Tick(30 * 18);

        Assert.Equal(60_000, sim.State.Region.AgentReservoir.Total);
    }

    [Fact]
    public void Month5_AllSettlersStillInCity_LowSavings()
    {
        // M18: tier-scaled bonus tuned so both tiers still have non-negative savings at end of m5.
        //   Uneducated: $5000 - 5 × $880  = $600
        //   Primary:    $8000 - 5 × $1540 = $300
        // Disable births and service emigration; test focuses on settler insolvency only.
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            InitialReservoirSize = 60_000,
            ServiceEmigrationEnabled = false,
        });
        sim.CreateResidentialZone();

        sim.Tick(30 * 5);

        Assert.Equal(50, sim.State.City.Population);
        Assert.All(sim.State.City.Agents.Values, a => Assert.True(a.Savings >= 0,
            $"All settlers should have non-negative savings at end of m5. Got tier={a.EducationTier} savings={a.Savings}."));
    }

    [Fact]
    public void NoWageMeansNoIncomeTax()
    {
        // Settlers have $0 wage in M2 (no jobs). Income tax should be $0.
        // Treasury growth = rent + utilities only (no income tax contribution).
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 0 });
        sim.CreateResidentialZone();

        sim.Tick(30);

        // 30 Uneducated × ($800 rent + $80 util) + 20 Primary × ($1,400 rent + $140 util) = $57,200.
        Assert.Equal(30 * (450 + 45) + 20 * (800 + 80), sim.State.City.TreasuryBalance);  // no income tax added
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
