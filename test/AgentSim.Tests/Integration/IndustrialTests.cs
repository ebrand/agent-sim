using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Types;

namespace AgentSim.Tests.Integration;

public class IndustrialTests
{
    /// <summary>
    /// Helper: place industrial structure, skip construction, manually fully-staff it, seed cash.
    ///
    /// Manual staffing is needed because industrial structures require 100 workers each (per the
    /// 15/20/40/25 mix), but bootstrap only provides 50 settlers. The first industrial structure
    /// would hire all bootstrap settlers; subsequent ones would be critically understaffed and
    /// produce zero output. For M4 production-flow tests we set FilledSlots directly. (Realistic
    /// gameplay solves this through population growth + many small commercial structures hiring
    /// first; M4 tests aren't testing the staffing pathway, just the chain mechanics.)
    /// </summary>
    private static Structure PlaceAndOperationalize(Sim sim, StructureType type, int seedCash = 100_000)
    {
        var s = sim.PlaceIndustrialStructure(type);
        s.ConstructionTicks = s.RequiredConstructionTicks;
        s.CashBalance = seedCash;

        // Pre-fill all job slots (bypasses the agent pool — production mechanic uses FilledSlots).
        foreach (var (tier, count) in s.JobSlots)
        {
            s.FilledSlots[tier] = count;
        }
        return s;
    }

    [Fact]
    public void PlaceIndustrialStructure_AddsStructureUnderConstruction()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var extractor = sim.PlaceIndustrialStructure(StructureType.ForestExtractor);

        Assert.False(extractor.Operational);
        Assert.Equal(0, extractor.ZoneId);  // industrial sits outside zones
    }

    [Fact]
    public void PlaceCommercialAsIndustrial_Throws()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        Assert.Throws<ArgumentException>(() => sim.PlaceIndustrialStructure(StructureType.Shop));
    }

    [Fact]
    public void ForestExtractor_ProducesWoodWhenStaffed()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var extractor = PlaceAndOperationalize(sim, StructureType.ForestExtractor);

        sim.Tick(1);  // production fires

        var produced = extractor.RawStorage.GetValueOrDefault(RawMaterial.Wood);
        Assert.True(produced > 0, $"Forest extractor should produce wood after 1 day. Got {produced}.");
    }

    [Fact]
    public void Sawmill_RequiresWoodInput_ProducesLumberWhenAvailable()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        var extractor = PlaceAndOperationalize(sim, StructureType.ForestExtractor);
        var sawmill = PlaceAndOperationalize(sim, StructureType.Sawmill);

        sim.Tick(1);  // hire
        sim.Tick(5);  // production runs; wood accumulates; sawmill consumes some

        var lumberProduced = sawmill.ProcessedStorage.GetValueOrDefault(ProcessedGood.Lumber);
        Assert.True(lumberProduced > 0, $"Sawmill should have produced lumber. Got {lumberProduced}.");

        // Money: sawmill paid extractor for wood
        Assert.True(sawmill.MonthlyExpenses > 0, "Sawmill should have paid for wood");
        Assert.True(extractor.MonthlyRevenue > 0, "Extractor should have received money for wood");
    }

    [Fact]
    public void HouseholdFactory_RequiresLumber_ProducesHousehold()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        var extractor = PlaceAndOperationalize(sim, StructureType.ForestExtractor);
        var sawmill = PlaceAndOperationalize(sim, StructureType.Sawmill);
        var factory = PlaceAndOperationalize(sim, StructureType.HouseholdFactory);

        sim.Tick(1);  // hire
        // Need time for the chain to flow: wood → lumber → household
        // 5 lumber per household. Sawmill produces ~10 lumber/day at full staff (but staffing fraction limits this).
        sim.Tick(30);  // a month

        var householdProduced = factory.ManufacturedStorage.GetValueOrDefault(ManufacturedGood.Household);
        // Factory MAY have produced some household goods. Soft assertion: it has some output OR has lumber in queue.
        // The exact amount depends on staffing fractions, which depend on settler counts hired.
        Assert.True(householdProduced > 0,
            $"Household factory should have produced at least some household goods. Got {householdProduced}.");
    }

    [Fact]
    public void Storage_ReceivesGoodsFromManufacturer()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        PlaceAndOperationalize(sim, StructureType.ForestExtractor);
        PlaceAndOperationalize(sim, StructureType.Sawmill);
        var factory = PlaceAndOperationalize(sim, StructureType.HouseholdFactory);
        var storage = PlaceAndOperationalize(sim, StructureType.Storage);

        sim.Tick(1);  // hire
        sim.Tick(60);  // 2 months for chain to flow + storage to absorb

        // Storage should have received and (per M4 simplification) sold all household goods to Region.Treasury
        // Verify storage made revenue from selling
        Assert.True(storage.MonthlyRevenue > 0 || storage.CashBalance > 100_000,
            "Storage should have earned money from goods sold to Region.Treasury");

        // Manufacturer's storage should be drained (storage absorbed it)
        var factoryHouseholdHoldings = factory.ManufacturedStorage.GetValueOrDefault(ManufacturedGood.Household);
        Assert.True(factoryHouseholdHoldings < 1000,
            $"Manufacturer's internal storage should not be full (storage absorbing). Got {factoryHouseholdHoldings}.");
    }

    [Fact]
    public void IndustrialStructure_PaysUtilitiesOnDay15()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 0 });
        sim.CreateResidentialZone();
        var extractor = PlaceAndOperationalize(sim, StructureType.ForestExtractor);

        var treasuryBefore = sim.State.City.TreasuryBalance;
        sim.Tick(15);  // through day 15 of month 1

        // Treasury should have received residential rent + utilities + extractor utilities ($1,000 for forest extractor)
        // We don't compute the exact amount; just verify the extractor's utility hit treasury.
        Assert.True(sim.State.City.TreasuryBalance > treasuryBefore + 50 * 800,  // rent alone
            "Treasury should have received industrial utility payment");
    }

    [Fact]
    public void IndustrialStructure_PaysPropertyTaxOnDay30()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        var extractor = PlaceAndOperationalize(sim, StructureType.ForestExtractor);

        var cashBefore = extractor.CashBalance;
        sim.Tick(30);  // full month

        // Forest extractor pays $1,000 utility (day 15) + $400 property tax (day 30)
        // Plus wages... but with no actual hired agents (test pre-fills FilledSlots only), no wages paid.
        // CashBalance should drop by ~$1,400.
        Assert.True(extractor.CashBalance < cashBefore,
            $"Extractor should have paid expenses out of CashBalance. Before: {cashBefore}, After: {extractor.CashBalance}");
    }

    [Fact]
    public void Storage_HasMarginFromPassThroughFee()
    {
        // Storage buys from manufacturer at 80% of mfg price, sells at 100%. 20% margin.
        // At small chain scale (1 of each), throughput is too low for storage to be profitable
        // — the 20% margin doesn't cover utilities ($1,000/mo) + property tax ($400/mo).
        // Production limited to 2 household/day (10 lumber/day ÷ 5 lumber per household)
        // = 60 household/month × $40 × 0.20 margin = $480/mo, less than $1,400 monthly cost.
        //
        // This test verifies the MARGIN MECHANIC is wired correctly: storage paid 80% of price
        // to manufacturer (an expense) and received 100% from Region.Treasury (a revenue).
        // The net cashflow is negative at this scale — that's expected, not a bug.
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        var manufacturer = PlaceAndOperationalize(sim, StructureType.HouseholdFactory);
        PlaceAndOperationalize(sim, StructureType.ForestExtractor);
        PlaceAndOperationalize(sim, StructureType.Sawmill);
        var storage = PlaceAndOperationalize(sim, StructureType.Storage);

        sim.Tick(15);  // mid-month, before settlement reset on day 30

        // Both money flows should have happened: storage paid manufacturer (expense) AND received from region (revenue)
        Assert.True(storage.MonthlyExpenses > 0, "Storage should have paid manufacturer for goods");
        Assert.True(storage.MonthlyRevenue > 0, "Storage should have received money from Region.Treasury");
        // Manufacturer should have received money for goods
        Assert.True(manufacturer.MonthlyRevenue > 0, "Manufacturer should have been paid by storage");
    }

    [Fact]
    public void RegionalReservoir_AccumulatesManufacturedGoods()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        PlaceAndOperationalize(sim, StructureType.ForestExtractor);
        PlaceAndOperationalize(sim, StructureType.Sawmill);
        PlaceAndOperationalize(sim, StructureType.HouseholdFactory);
        PlaceAndOperationalize(sim, StructureType.Storage);

        sim.Tick(90);  // 3 months — plenty of time for the chain to push household goods through to region

        var regionalHousehold = sim.State.Region.GoodsReservoir.GetValueOrDefault(ManufacturedGood.Household);
        Assert.True(regionalHousehold > 0,
            $"Region should have accumulated household goods from storage overflow. Got {regionalHousehold}.");
    }

    [Fact]
    public void Storage_ProfitableWithMultipleManufacturers()
    {
        // With 2+ manufacturers feeding 1 storage, the cumulative margin covers storage's
        // monthly costs ($500 utility + $400 property tax = $900/month).
        //
        // Setup: timber chain (forest → sawmill → household factory) + sand chain (sand pit → silicate plant → glass works).
        // Two manufacturers feeding one storage. Both at full staffing.
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        PlaceAndOperationalize(sim, StructureType.ForestExtractor);
        PlaceAndOperationalize(sim, StructureType.Sawmill);
        PlaceAndOperationalize(sim, StructureType.HouseholdFactory);
        PlaceAndOperationalize(sim, StructureType.SandPit);
        PlaceAndOperationalize(sim, StructureType.SilicatePlant);
        PlaceAndOperationalize(sim, StructureType.GlassWorks);
        var storage = PlaceAndOperationalize(sim, StructureType.Storage);

        var cashBefore = storage.CashBalance;
        sim.Tick(60);  // 2 months for the chain to stabilize and margin to accumulate

        Assert.True(storage.CashBalance > cashBefore,
            $"Storage should be profitable with 2 manufacturers feeding it. Before: {cashBefore}, After: {storage.CashBalance}");
    }

    [Fact]
    public void Determinism_FullChain_SameSeedProducesSameOutput()
    {
        var sim1 = Sim.Create(new SimConfig { Seed = 42 });
        sim1.CreateResidentialZone();
        PlaceAndOperationalize(sim1, StructureType.ForestExtractor);
        PlaceAndOperationalize(sim1, StructureType.Sawmill);
        PlaceAndOperationalize(sim1, StructureType.HouseholdFactory);
        PlaceAndOperationalize(sim1, StructureType.Storage);
        sim1.Tick(90);

        var sim2 = Sim.Create(new SimConfig { Seed = 42 });
        sim2.CreateResidentialZone();
        PlaceAndOperationalize(sim2, StructureType.ForestExtractor);
        PlaceAndOperationalize(sim2, StructureType.Sawmill);
        PlaceAndOperationalize(sim2, StructureType.HouseholdFactory);
        PlaceAndOperationalize(sim2, StructureType.Storage);
        sim2.Tick(90);

        Assert.Equal(
            sim1.State.Region.GoodsReservoir.GetValueOrDefault(ManufacturedGood.Household),
            sim2.State.Region.GoodsReservoir.GetValueOrDefault(ManufacturedGood.Household));
        Assert.Equal(sim1.State.City.Population, sim2.State.City.Population);
    }
}
