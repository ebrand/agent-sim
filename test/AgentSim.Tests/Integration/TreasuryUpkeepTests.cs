using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Sim.Mechanics;
using AgentSim.Core.Types;

namespace AgentSim.Tests.Integration;

/// <summary>
/// M10: monthly treasury upkeep for civic / healthcare / education / utility / affordable
/// housing. Bankruptcy collapses service satisfaction; 6 consecutive months negative → game over.
/// </summary>
public class TreasuryUpkeepTests
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

    [Fact]
    public void Upkeep_NoTreasuryFundedStructures_NoDeduction()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 500_000 });
        var before = sim.State.City.TreasuryBalance;

        sim.Tick(1);  // day 1

        Assert.Equal(before, sim.State.City.TreasuryBalance);
    }

    [Fact]
    public void Upkeep_OperationalStructures_DeductsExpectedAmountOnDay1()
    {
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 1_000_000,
            ServiceEmigrationEnabled = false,
        });
        // 1 PoliceStation ($30k) + 1 Clinic ($60k) + 1 Generator ($80k) + 1 PrimarySchool ($60k) = $230k
        FastBuildService(sim, StructureType.PoliceStation);
        FastBuildService(sim, StructureType.Clinic);
        FastBuildService(sim, StructureType.Generator);
        FastBuildSchool(sim, StructureType.PrimarySchool);

        var before = sim.State.City.TreasuryBalance;
        sim.Tick(1);  // day 1

        Assert.Equal(before - 230_000, sim.State.City.TreasuryBalance);
    }

    [Fact]
    public void Upkeep_UnderConstruction_PaysNothing()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        sim.PlaceServiceStructure(StructureType.Hospital);  // under construction
        var before = sim.State.City.TreasuryBalance;

        sim.Tick(1);

        Assert.Equal(before, sim.State.City.TreasuryBalance);
    }

    [Fact]
    public void Upkeep_InactiveStructures_PayNothing()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        var hospital = FastBuildService(sim, StructureType.Hospital);
        hospital.Inactive = true;
        var before = sim.State.City.TreasuryBalance;

        sim.Tick(1);

        Assert.Equal(before, sim.State.City.TreasuryBalance);
    }

    [Fact]
    public void Upkeep_FiresOnceAtStartOfEachMonth()
    {
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 1_000_000,
            ServiceEmigrationEnabled = false,
        });
        FastBuildService(sim, StructureType.PoliceStation);  // $30k/mo
        var before = sim.State.City.TreasuryBalance;

        sim.Tick(30);  // exactly one month — upkeep fires on day 1 only

        // We expect $30k deducted for upkeep. Other day-1 settlements (rent, wages) and others
        // will also have run on real bootstrapped settlers, but with 0 settlers here there's no
        // residential bootstrap → no rent. Same with no industrial → no other flows.
        // Caveat: this test has no residential zone → 0 agents → no rent flow.
        Assert.Equal(before - 30_000, sim.State.City.TreasuryBalance);
    }

    [Fact]
    public void Upkeep_AllowsOverdraft_TreasuryGoesNegative()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 10_000 });
        FastBuildService(sim, StructureType.PoliceStation);  // $30k/mo — exceeds treasury

        sim.Tick(1);

        Assert.Equal(10_000 - 30_000, sim.State.City.TreasuryBalance);  // -$20k
    }

    [Fact]
    public void Bankruptcy_ServicesReadZero_WhenTreasuryNegative()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000 });
        sim.CreateResidentialZone();
        FastBuildService(sim, StructureType.PoliceStation);  // $30k upkeep
        FastBuildService(sim, StructureType.Clinic);
        FastBuildService(sim, StructureType.Generator);

        sim.Tick(1);  // day 1 upkeep fires; treasury goes very negative
        Assert.True(sim.State.City.TreasuryBalance < 0);

        var snap = ServiceSatisfactionMechanic.Compute(sim.State);
        // All treasury-funded categories should read 0% — capacities are zeroed despite having
        // operational, non-inactive structures.
        Assert.Equal(0.0, snap.CivicPercent);
        Assert.Equal(0.0, snap.HealthcarePercent);
        Assert.Equal(0.0, snap.UtilityPercent);
    }

    [Fact]
    public void Bankruptcy_PositiveTreasury_ServicesUseRealCapacity()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 5_000_000 });
        sim.CreateResidentialZone();
        FastBuildService(sim, StructureType.PoliceStation);
        FastBuildService(sim, StructureType.Clinic);
        FastBuildService(sim, StructureType.Generator);

        sim.Tick(1);
        Assert.True(sim.State.City.TreasuryBalance > 0);

        var snap = ServiceSatisfactionMechanic.Compute(sim.State);
        Assert.True(snap.CivicPercent > 0);
        Assert.True(snap.HealthcarePercent > 0);
        Assert.True(snap.UtilityPercent > 0);
    }

    [Fact]
    public void BankruptcyCounter_IncrementsEachMonthNegative()
    {
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 10_000,
            ServiceEmigrationEnabled = false,
        });
        FastBuildService(sim, StructureType.Hospital);  // $250k/month → instantly negative

        sim.Tick(30);
        Assert.Equal(1, sim.State.City.ConsecutiveMonthsBankrupt);

        sim.Tick(30);
        Assert.Equal(2, sim.State.City.ConsecutiveMonthsBankrupt);

        sim.Tick(30);
        Assert.Equal(3, sim.State.City.ConsecutiveMonthsBankrupt);
    }

    [Fact]
    public void BankruptcyCounter_ResetsWhenTreasuryRecovers()
    {
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 10_000,
            ServiceEmigrationEnabled = false,
        });
        FastBuildService(sim, StructureType.Hospital);  // $250k/month

        sim.Tick(30);
        Assert.Equal(1, sim.State.City.ConsecutiveMonthsBankrupt);

        // Cash injection: treasury recovers above zero before next end-of-month.
        sim.State.City.TreasuryBalance = 5_000_000;
        sim.Tick(30);  // pays $250k upkeep on day 1, treasury = $4.75M (positive)

        Assert.Equal(0, sim.State.City.ConsecutiveMonthsBankrupt);
    }

    [Fact]
    public void GameOver_TriggersAt6ConsecutiveBankruptMonths()
    {
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 10_000,
            ServiceEmigrationEnabled = false,
        });
        FastBuildService(sim, StructureType.Hospital);  // $250k/month

        // 5 months negative — not yet game-over.
        sim.Tick(30 * 5);
        Assert.False(sim.State.City.GameOver);
        Assert.Equal(5, sim.State.City.ConsecutiveMonthsBankrupt);

        // Month 6 — game over.
        sim.Tick(30);
        Assert.True(sim.State.City.GameOver);
        Assert.Equal(6, sim.State.City.ConsecutiveMonthsBankrupt);
    }

    [Fact]
    public void GameOver_DoesNotHaltTicks_SimContinuesToOperate()
    {
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 10_000,
            ServiceEmigrationEnabled = false,
        });
        FastBuildService(sim, StructureType.Hospital);

        sim.Tick(30 * 7);  // 7 months — game-over fires at month 6
        Assert.True(sim.State.City.GameOver);
        Assert.Equal(7 * 30, sim.State.CurrentTick);  // tick counter advanced past game-over
    }

    [Fact]
    public void GameOver_StaysSetEvenIfTreasuryRecovers()
    {
        // Per design intent: game-over is terminal. Even if the player somehow refills treasury
        // afterward, GameOver remains true. (Game-over flag is set permanently the moment
        // ConsecutiveMonthsBankrupt hits 6.)
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 10_000,
            ServiceEmigrationEnabled = false,
        });
        FastBuildService(sim, StructureType.Hospital);

        sim.Tick(30 * 6);
        Assert.True(sim.State.City.GameOver);

        sim.State.City.TreasuryBalance = 100_000_000;
        sim.Tick(30);
        Assert.True(sim.State.City.GameOver);  // still over
    }

    [Fact]
    public void UpkeepOrder_FiresBeforeRent_OnDay1()
    {
        // Per `time-and-pacing.md` outflows-before-inflows: treasury upkeep (treasury outflow)
        // settles before agent rent (treasury inflow).
        // With 50 settlers and $800 rent: $40k rent revenue per day-1.
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 100_000,
            ServiceEmigrationEnabled = false,
        });
        sim.CreateResidentialZone();
        FastBuildService(sim, StructureType.Hospital);  // $250k upkeep

        sim.Tick(1);  // day 1: upkeep -$250k, then rent +$40k

        // Treasury = 100k - 250k + 40k = -110k
        Assert.Equal(-110_000, sim.State.City.TreasuryBalance);
    }

    [Fact]
    public void Bankruptcy_AmplifiesServiceEmigration_PopulationCollapses()
    {
        // Wire a city that goes bankrupt fast → services read 0% → 1.2%/mo emigration max.
        // Over 4 months, expect notable population loss.
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 1_000,
            InitialReservoirSize = 60_000,
        });
        sim.CreateResidentialZone();
        // Even with all 4 service types built, treasury can't sustain them.
        FastBuildService(sim, StructureType.PoliceStation);
        FastBuildService(sim, StructureType.Clinic);
        FastBuildService(sim, StructureType.Generator);
        FastBuildSchool(sim, StructureType.PrimarySchool);
        // Cushion savings so insolvency emigration doesn't compete.
        foreach (var a in sim.State.City.Agents.Values) a.Savings = 100_000;

        sim.Tick(30 * 4);

        // Bankrupt → services collapse → service-pressure emigration fires. With seed 42 the
        // exact count is deterministic; we just check direction.
        Assert.True(sim.State.City.Population < 50,
            $"Expected service-pressure emigration under bankruptcy. Got {sim.State.City.Population}.");
        Assert.True(sim.State.City.TreasuryBalance < 0);
    }

    [Fact]
    public void Determinism_Bankruptcy_SameSeedSameOutcome()
    {
        Sim Build()
        {
            var sim = Sim.Create(new SimConfig
            {
                Seed = 7,
                StartingTreasury = 1_000,
                InitialReservoirSize = 60_000,
            });
            sim.CreateResidentialZone();
            FastBuildService(sim, StructureType.Hospital);
            sim.Tick(30 * 6);
            return sim;
        }

        var a = Build();
        var b = Build();
        Assert.Equal(a.State.City.TreasuryBalance, b.State.City.TreasuryBalance);
        Assert.Equal(a.State.City.ConsecutiveMonthsBankrupt, b.State.City.ConsecutiveMonthsBankrupt);
        Assert.Equal(a.State.City.GameOver, b.State.City.GameOver);
        Assert.Equal(a.State.City.Population, b.State.City.Population);
    }

    [Fact]
    public void Upkeep_AffordableHousing_Charged()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        // Manually wire an operational affordable housing structure.
        var ah = new Structure
        {
            Id = sim.State.AllocateStructureId(),
            Type = StructureType.AffordableHousing,
            ZoneId = 0,
            ResidentialCapacity = 10,
            ConstructionTicks = 90,
            RequiredConstructionTicks = 90,
        };
        sim.State.City.Structures[ah.Id] = ah;

        var before = sim.State.City.TreasuryBalance;
        sim.Tick(1);

        Assert.Equal(before - Upkeep.AffordableHousing, sim.State.City.TreasuryBalance);
    }
}
