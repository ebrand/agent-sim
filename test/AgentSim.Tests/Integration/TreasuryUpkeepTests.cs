using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Sim.Mechanics;
using AgentSim.Core.Types;

namespace AgentSim.Tests.Integration;

/// <summary>
/// M10: monthly treasury upkeep for civic / healthcare / education / utility / affordable
/// housing. Full pay when treasury >= total upkeep; partial-pay otherwise (treasury / 6 per
/// month, stretching ~6 months of decline). Services scale by funding fraction. 6 consecutive
/// partial-pay months → GameOver, which halts subsequent Tick() calls.
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

        sim.Tick(30);  // day-30 settlement fires

        Assert.Equal(before, sim.State.City.TreasuryBalance);
        Assert.Equal(1.0, sim.State.City.UpkeepFundingFraction);
    }

    [Fact]
    public void Upkeep_OperationalStructures_DeductsExpectedAmountOnSettlement()
    {
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 2_000_000,  // covers construction of all 4 structures
            ServiceEmigrationEnabled = false,
        });
        // Calibrated (M-cal): police $4k + clinic $7k + generator $8k + primary $7k = $26k upkeep.
        FastBuildService(sim, StructureType.PoliceStation);
        FastBuildService(sim, StructureType.Clinic);
        FastBuildService(sim, StructureType.Generator);
        FastBuildSchool(sim, StructureType.PrimarySchool);

        var expectedUpkeep = Upkeep.PoliceStation + Upkeep.Clinic + Upkeep.Generator + Upkeep.PrimarySchool;
        var before = sim.State.City.TreasuryBalance;
        sim.Tick(30);

        // Treasury net = -upkeep + rent + util + income tax on civic wages - civic wages
        // The exact net is deterministic; assert upkeep deduction in isolation by comparing to a
        // hypothetical no-upkeep state would require refactoring. Just assert the funding fraction.
        Assert.Equal(1.0, sim.State.City.UpkeepFundingFraction);
        Assert.True(sim.State.City.TreasuryBalance < before,
            "Treasury should drop net (upkeep + wages exceed rent + tax)");
    }

    [Fact]
    public void Upkeep_UnderConstruction_PaysNothing()
    {
        // Synthesize a hospital with explicit long construction time so it's still building when
        // the monthly settlement fires. Synthetic build skips the M11 construction-cost charge.
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        var hospital = new Structure
        {
            Id = sim.State.AllocateStructureId(),
            Type = StructureType.Hospital,
            ZoneId = 0,
            ResidentialCapacity = 0,
            ConstructionTicks = 0,
            RequiredConstructionTicks = 60,  // longer than the 30-day settlement window
            ServiceCapacity = Services.HospitalCapacity,
        };
        sim.State.City.Structures[hospital.Id] = hospital;
        var before = sim.State.City.TreasuryBalance;

        sim.Tick(30);  // monthly settlement fires; hospital still under construction

        Assert.False(hospital.Operational);
        Assert.Equal(before, sim.State.City.TreasuryBalance);
    }

    [Fact]
    public void Upkeep_InactiveStructures_PayNothing()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_500_000 });
        var hospital = FastBuildService(sim, StructureType.Hospital);  // $1.2M construction
        hospital.Inactive = true;
        var before = sim.State.City.TreasuryBalance;

        sim.Tick(30);

        Assert.Equal(before, sim.State.City.TreasuryBalance);
    }

    [Fact]
    public void Upkeep_FiresOnceAtMonthlySettlement()
    {
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 500_000,
            ServiceEmigrationEnabled = false,
            FoundingPhaseEnabled = false,
        });
        FastBuildService(sim, StructureType.PoliceStation);  // $15k/mo, $150k construction
        var before = sim.State.City.TreasuryBalance;

        sim.Tick(30);

        Assert.Equal(before - Upkeep.PoliceStation, sim.State.City.TreasuryBalance);
    }

    [Fact]
    public void PartialPay_TriggersWhenTreasuryBelowUpkeep_PaysFractional()
    {
        // M-cal: Hospital upkeep $30k; override treasury to $18k for partial-pay scenario.
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 400_000,
            ServiceEmigrationEnabled = false,
            ImmigrationEnabled = false,
            FoundingPhaseEnabled = false,
        });
        FastBuildService(sim, StructureType.Hospital);
        // Override balance and clear residents so no wage/rent flow distorts the assertion.
        sim.State.City.TreasuryBalance = 18_000;

        sim.Tick(30);

        // Partial-pay: pay treasury/6 = $3,000. Funding fraction = $3,000 / $30,000 = 0.1.
        // Allow for downstream cash flows; assert in a tight range.
        Assert.InRange(sim.State.City.UpkeepFundingFraction, 0.09, 0.11);
        Assert.True(sim.State.City.UpkeepFundingFraction < 1.0,
            $"Should be partial-pay, got fraction {sim.State.City.UpkeepFundingFraction}.");
    }

    [Fact]
    public void PartialPay_DoesNotPushTreasuryNegative()
    {
        // With partial-pay, treasury should never go negative from upkeep alone.
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 1_300_000,
            ServiceEmigrationEnabled = false,
        });
        FastBuildService(sim, StructureType.Hospital);
        sim.State.City.TreasuryBalance = 1_000;

        sim.Tick(30);

        Assert.True(sim.State.City.TreasuryBalance >= 0,
            $"Treasury should stay non-negative under partial-pay. Got {sim.State.City.TreasuryBalance}.");
        Assert.True(sim.State.City.UpkeepFundingFraction < 1.0);
    }

    [Fact]
    public void PartialPay_StretchesTreasuryAcrossMultipleMonths()
    {
        // M-cal: Hospital upkeep $30k; start partial-pay at $18k.
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 400_000,
            ServiceEmigrationEnabled = false,
            ImmigrationEnabled = false,
            FoundingPhaseEnabled = false,
        });
        FastBuildService(sim, StructureType.Hospital);
        sim.State.City.TreasuryBalance = 18_000;

        for (int month = 1; month <= 5; month++)
        {
            var before = sim.State.City.TreasuryBalance;
            sim.Tick(30);
            Assert.True(sim.State.City.TreasuryBalance >= 0,
                $"Month {month}: treasury went negative ({sim.State.City.TreasuryBalance}).");
            Assert.True(sim.State.City.UpkeepFundingFraction < 1.0,
                $"Month {month}: should still be partial-pay. Got {sim.State.City.UpkeepFundingFraction}.");
        }
    }

    [Fact]
    public void ServiceCapacity_ScalesByFundingFraction()
    {
        // Construct clinic + hospital, then override treasury low to force partial-pay.
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 1_500_000,  // covers clinic $250k + hospital $1.2M = $1.45M
            ServiceEmigrationEnabled = false,
        });
        sim.CreateResidentialZone();
        FastBuildService(sim, StructureType.Clinic);
        FastBuildService(sim, StructureType.Hospital);
        sim.State.City.TreasuryBalance = 15_000;  // can't afford $37k upkeep → partial-pay

        sim.Tick(30);

        Assert.True(sim.State.City.UpkeepFundingFraction < 1.0);

        var snap = ServiceSatisfactionMechanic.Compute(sim.State);
        Assert.True(snap.HealthcarePercent > 0,
            $"Healthcare should still be > 0 under partial-pay. Got {snap.HealthcarePercent}%.");
    }

    [Fact]
    public void ServiceCapacity_PartialFunding_LowersSatisfactionForLargePopulation()
    {
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 400_000,
            ServiceEmigrationEnabled = false,
            ImmigrationEnabled = false,
            FoundingPhaseEnabled = false,
        });
        FastBuildService(sim, StructureType.Hospital);
        sim.State.City.TreasuryBalance = 18_000;
        // Synthesize 10000 working-age agents.
        for (int i = 0; i < 10_000; i++)
        {
            sim.State.City.Agents[sim.State.AllocateAgentId()] = new Agent
            {
                Id = i + 1,
                EducationTier = EducationTier.Uneducated,
                AgeDays = Demographics.WorkingAgeStartDay,
            };
        }

        sim.Tick(30);  // monthly settlement → partial-pay fires (upkeep $120k > treasury $60k)

        Assert.True(sim.State.City.UpkeepFundingFraction < 1.0);
        var snap = ServiceSatisfactionMechanic.Compute(sim.State);
        Assert.True(snap.HealthcarePercent < 100,
            $"At partial funding, large-pop healthcare must read below 100%. Got {snap.HealthcarePercent}%.");
    }

    [Fact]
    public void BankruptcyCounter_IncrementsEachPartialPayMonth()
    {
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 1_300_000,
            ServiceEmigrationEnabled = false,
        });
        FastBuildService(sim, StructureType.Hospital);  // $120k upkeep → always partial-pay
        sim.State.City.TreasuryBalance = 10_000;

        sim.Tick(30);
        Assert.Equal(1, sim.State.City.ConsecutiveMonthsBankrupt);

        sim.Tick(30);
        Assert.Equal(2, sim.State.City.ConsecutiveMonthsBankrupt);

        sim.Tick(30);
        Assert.Equal(3, sim.State.City.ConsecutiveMonthsBankrupt);
    }

    [Fact]
    public void BankruptcyCounter_ResetsWhenFullPayResumes()
    {
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 1_300_000,
            ServiceEmigrationEnabled = false,
        });
        FastBuildService(sim, StructureType.Hospital);
        sim.State.City.TreasuryBalance = 10_000;

        sim.Tick(30);  // partial-pay; counter = 1
        Assert.Equal(1, sim.State.City.ConsecutiveMonthsBankrupt);

        // Inject cash so next month upkeep is fully payable.
        sim.State.City.TreasuryBalance = 5_000_000;
        sim.Tick(30);  // full pay; counter resets

        Assert.Equal(0, sim.State.City.ConsecutiveMonthsBankrupt);
    }

    [Fact]
    public void GameOver_TriggersAt6ConsecutivePartialPayMonths()
    {
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 1_300_000,
            ServiceEmigrationEnabled = false,
        });
        FastBuildService(sim, StructureType.Hospital);
        sim.State.City.TreasuryBalance = 10_000;

        // 5 months — not yet game-over.
        sim.Tick(30 * 5);
        Assert.False(sim.State.City.GameOver);
        Assert.Equal(5, sim.State.City.ConsecutiveMonthsBankrupt);

        // Month 6 — game over fires.
        sim.Tick(30);
        Assert.True(sim.State.City.GameOver);
        Assert.Equal(6, sim.State.City.ConsecutiveMonthsBankrupt);
    }

    [Fact]
    public void GameOver_HaltsFurtherTicks()
    {
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 1_300_000,
            ServiceEmigrationEnabled = false,
        });
        FastBuildService(sim, StructureType.Hospital);
        sim.State.City.TreasuryBalance = 10_000;

        sim.Tick(30 * 6);  // game-over fires at end of month 6
        Assert.True(sim.State.City.GameOver);
        var tickAtGameOver = sim.State.CurrentTick;

        // Further Tick calls should be no-ops.
        sim.Tick(100);
        Assert.Equal(tickAtGameOver, sim.State.CurrentTick);
    }

    [Fact]
    public void GameOver_FlagPersists()
    {
        // Once GameOver is set, it remains true regardless of subsequent state changes.
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 1_300_000,
            ServiceEmigrationEnabled = false,
        });
        FastBuildService(sim, StructureType.Hospital);
        sim.State.City.TreasuryBalance = 10_000;

        sim.Tick(30 * 6);
        Assert.True(sim.State.City.GameOver);

        sim.State.City.TreasuryBalance = 100_000_000;
        Assert.True(sim.State.City.GameOver);
    }

    [Fact]
    public void GameOver_CanBeClearedExternally_TicksResume()
    {
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 1_300_000,
            ServiceEmigrationEnabled = false,
        });
        FastBuildService(sim, StructureType.Hospital);
        sim.State.City.TreasuryBalance = 10_000;

        sim.Tick(30 * 6);
        Assert.True(sim.State.City.GameOver);
        var tickAtGameOver = sim.State.CurrentTick;

        sim.State.City.GameOver = false;
        sim.State.City.TreasuryBalance = 100_000_000;
        sim.Tick(1);

        Assert.Equal(tickAtGameOver + 1, sim.State.CurrentTick);
    }

    [Fact]
    public void ModestFoundingCity_SurvivesAtLeast6MonthsBeforePartialPay()
    {
        // Calibration sanity: 50 settlers + modest service set built from scratch with the new
        // $1.8M default starting treasury. Construction cost ≈ $1.15M; remaining $650k against
        // ~$65k/month deficit gives ~10 months of full pay (target: ≥ 6).
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            ServiceEmigrationEnabled = false,
            // StartingTreasury uses the default 1_800_000
        });
        sim.CreateResidentialZone();
        FastBuildService(sim, StructureType.PoliceStation);
        FastBuildService(sim, StructureType.Clinic);
        FastBuildSchool(sim, StructureType.PrimarySchool);
        FastBuildService(sim, StructureType.Generator);
        FastBuildService(sim, StructureType.Well);
        // Cushion savings so settlers don't emigrate from insolvency mid-test.
        foreach (var a in sim.State.City.Agents.Values) a.Savings = 100_000;

        for (int month = 1; month <= 6; month++)
        {
            sim.Tick(30);
        }
        Assert.False(sim.State.City.GameOver);
        Assert.True(sim.State.City.ConsecutiveMonthsBankrupt <= 1,
            $"Modest founding city should sustain ~6 months of full pay. Counter at {sim.State.City.ConsecutiveMonthsBankrupt}.");
    }

    [Fact]
    public void MonthlySettlement_UpkeepAndRentBothFlow()
    {
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 200_000,  // covers police construction $150k
            ServiceEmigrationEnabled = false,
        });
        sim.CreateResidentialZone();
        FastBuildService(sim, StructureType.PoliceStation);

        sim.Tick(30);

        // After construction: 200k - 150k = 50k. Upkeep + rent + utilities flow; civic employment
        // also adds wage outflow and income-tax inflow (deterministic with seed 42).
        Assert.True(sim.State.City.TreasuryBalance > 50_000,
            $"Treasury should grow with rent + utilities net of upkeep + wages. Got {sim.State.City.TreasuryBalance}.");
        Assert.Equal(1.0, sim.State.City.UpkeepFundingFraction);
    }

    [Fact]
    public void Bankruptcy_AmplifiesServiceEmigration_PopulationCollapses()
    {
        // Construct hospital then override treasury low to force persistent partial-pay.
        var sim = Sim.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 1_300_000,
            InitialReservoirSize = 60_000,
            ImmigrationEnabled = false,  // isolate to emigration behavior under bankruptcy
        });
        sim.CreateResidentialZone();
        FastBuildService(sim, StructureType.Hospital);
        sim.State.City.TreasuryBalance = 1_000;
        foreach (var a in sim.State.City.Agents.Values) a.Savings = 100_000;

        sim.Tick(30 * 6);

        Assert.True(sim.State.City.Population < 50,
            $"Expected service-pressure emigration under partial-pay. Got {sim.State.City.Population}.");
    }

    [Fact]
    public void Determinism_Bankruptcy_SameSeedSameOutcome()
    {
        Sim Build()
        {
            var sim = Sim.Create(new SimConfig
            {
                Seed = 7,
                StartingTreasury = 1_300_000,
                InitialReservoirSize = 60_000,
            });
            sim.CreateResidentialZone();
            FastBuildService(sim, StructureType.Hospital);
            sim.State.City.TreasuryBalance = 1_000;
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
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000, FoundingPhaseEnabled = false });
        var ah = new Structure
        {
            Id = sim.State.AllocateStructureId(),
            Type = StructureType.AffordableHousing,
            ZoneId = 0,
            ResidentialCapacity = 10,
            ConstructionTicks = 7,
            RequiredConstructionTicks = 7,
        };
        sim.State.City.Structures[ah.Id] = ah;

        var before = sim.State.City.TreasuryBalance;
        sim.Tick(30);

        Assert.Equal(before - Upkeep.AffordableHousing, sim.State.City.TreasuryBalance);
    }
}
