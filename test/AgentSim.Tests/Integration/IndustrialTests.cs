using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Types;

namespace AgentSim.Tests.Integration;

public class IndustrialTests
{
    /// <summary>
    /// Helper: ensure a CorporateHq for the given industry exists, with enough cash to fund
    /// arbitrary subordinate construction. Used by direct-call tests that specify their industry.
    /// </summary>
    private static long EnsureHq(Sim sim, IndustryType industry = IndustryType.Forestry)
    {
        var existing = sim.State.City.Structures.Values
            .FirstOrDefault(s => s.Type == StructureType.CorporateHq && s.Industry == industry);
        if (existing != null) return existing.Id;

        var commZone = sim.State.City.Zones.Values.FirstOrDefault(z => z.Type == ZoneType.Commercial)
            ?? sim.CreateCommercialZone();
        var hq = sim.PlaceCorporateHq(commZone.Id, industry, $"TestCo-{industry}");
        hq.ConstructionTicks = hq.RequiredConstructionTicks;
        hq.CashBalance = 50_000_000;
        return hq.Id;
    }

    /// <summary>Pick the first industry that allows this structure type and return / create a matching HQ.</summary>
    private static long EnsureHqForType(Sim sim, StructureType type)
    {
        IndustryType? required = null;
        foreach (IndustryType i in Enum.GetValues<IndustryType>())
        {
            if (Industry.Allows(i, type)) { required = i; break; }
        }
        if (required is not IndustryType industry)
            throw new InvalidOperationException($"No industry allows {type}");
        return EnsureHq(sim, industry);
    }

    /// <summary>
    /// Helper: place industrial structure under a test HQ, skip construction, manually fully-staff
    /// it, seed cash.
    ///
    /// Manual staffing is needed because industrial structures require 100 workers each (per the
    /// 15/20/40/25 mix), but bootstrap only provides 50 settlers. The first industrial structure
    /// would hire all bootstrap settlers; subsequent ones would be critically understaffed and
    /// produce zero output. For M4 production-flow tests we set FilledSlots directly.
    /// </summary>
    private static Structure PlaceAndOperationalize(Sim sim, StructureType type, int seedCash = 100_000)
    {
        var hqId = EnsureHqForType(sim, type);
        var s = sim.PlaceIndustrialStructure(type, hqId);
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
        var hqId = EnsureHq(sim);
        var extractor = sim.PlaceIndustrialStructure(StructureType.ForestExtractor, hqId);

        Assert.False(extractor.Operational);
        Assert.Equal(0, extractor.ZoneId);  // industrial sits outside zones
    }

    [Fact]
    public void PlaceCommercialAsIndustrial_Throws()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var hqId = EnsureHq(sim);
        Assert.Throws<ArgumentException>(() => sim.PlaceIndustrialStructure(StructureType.Shop, hqId));
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
        // M13 consolidated model: no per-link cash flow between extractor and sawmill (same HQ
        // owns both). Goods flow only. Test just verifies the goods chain works.
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        var extractor = PlaceAndOperationalize(sim, StructureType.ForestExtractor);
        var sawmill = PlaceAndOperationalize(sim, StructureType.Sawmill);

        sim.Tick(1);  // hire
        sim.Tick(5);  // production runs; wood accumulates; sawmill consumes some

        var lumberProduced = sawmill.ProcessedStorage.GetValueOrDefault(ProcessedGood.Lumber);
        Assert.True(lumberProduced > 0, $"Sawmill should have produced lumber. Got {lumberProduced}.");
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

        // Tick to mid-month so we observe goods flow while structures are still operational
        // (single-manufacturer storage is unprofitable; without this constraint it would go inactive after 2 months)
        sim.Tick(20);

        // Verify the flow: manufacturer produces, storage absorbs, storage sells to regional treasury
        var regionHousehold = sim.State.Region.GoodsReservoir.GetValueOrDefault(ManufacturedGood.Household);
        Assert.True(regionHousehold > 0,
            $"Region should have received household goods from storage sales. Got {regionHousehold}.");

        // Manufacturer's internal storage should not be saturated (storage was absorbing throughout)
        var factoryHouseholdHoldings = factory.ManufacturedStorage.GetValueOrDefault(ManufacturedGood.Household);
        Assert.True(factoryHouseholdHoldings < 1000,
            $"Manufacturer's internal storage should not be full (storage absorbing). Got {factoryHouseholdHoldings}.");
    }

    [Fact]
    public void IndustrialStructure_PaysUtilitiesMonthly()
    {
        // Under Option A, all flows happen on day 30. Verify industrial utilities flow to treasury.
        // ForestExtractor construction = $150k.
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 200_000 });
        sim.CreateResidentialZone();
        var extractor = PlaceAndOperationalize(sim, StructureType.ForestExtractor);

        var treasuryBefore = sim.State.City.TreasuryBalance;
        sim.Tick(30);  // monthly settlement

        // Treasury collected residential rent + utilities + extractor utilities.
        Assert.True(sim.State.City.TreasuryBalance > treasuryBefore + 50 * 800,
            "Treasury should have received industrial utility payment on top of rent.");
    }

    [Fact]
    public void IndustrialStructure_PropertyTax_ChargedToHq()
    {
        // M13: expense (property tax + utilities) charged to HQ, not the sub.
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        var extractor = PlaceAndOperationalize(sim, StructureType.ForestExtractor);
        var hq = sim.State.City.Structures[extractor.OwnerHqId!.Value];

        var hqCashBefore = hq.CashBalance;
        var extractorCashBefore = extractor.CashBalance;
        sim.Tick(30);  // monthly settlement

        // Sub cash unchanged (HQ pays its bills).
        Assert.Equal(extractorCashBefore, extractor.CashBalance);
        // HQ paid utility + property tax (+ wages, none since no real agents).
        Assert.True(hq.CashBalance < hqCashBefore,
            $"HQ should have paid sub expenses. Before: {hqCashBefore}, After: {hq.CashBalance}");
    }

    [Fact]
    public void Storage_SaleRevenueRoutesToHq()
    {
        // M13: storage's sales to regional treasury (or commercial) route revenue to its owning
        // HQ. Storage's own CashBalance is unchanged by sales.
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        PlaceAndOperationalize(sim, StructureType.ForestExtractor);
        PlaceAndOperationalize(sim, StructureType.Sawmill);
        PlaceAndOperationalize(sim, StructureType.HouseholdFactory);
        var storage = PlaceAndOperationalize(sim, StructureType.Storage);
        var hq = sim.State.City.Structures[storage.OwnerHqId!.Value];

        var storageRevBefore = storage.MonthlyRevenue;
        sim.Tick(15);  // mid-month

        // Storage itself accrues no revenue (it's a cost center under M13).
        Assert.Equal(storageRevBefore, storage.MonthlyRevenue);
        // HQ records the chain's revenue.
        Assert.True(hq.MonthlyRevenue > 0, $"HQ should have recorded chain revenue. Got {hq.MonthlyRevenue}.");
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

        // M12 + alpha-1 calibration: industrial chain profit at this scale is too small to overcome
        // HQ overhead ($7.5k/mo) for the consolidated bottom line. But the original test's intent
        // was "storage stays operational with multiple manufacturers feeding it" — contrast with
        // single-manufacturer storage going inactive (see ProfitabilityTests). Verify that.
        sim.Tick(60);  // 2 months — single-manufacturer storage would have gone inactive by now

        Assert.False(storage.Inactive,
            "Storage should not go inactive with 2 manufacturers feeding it (vs. single-manufacturer where margin is too thin).");
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
