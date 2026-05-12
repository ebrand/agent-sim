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
        Structure s;
        if (Industrial.IsManufacturer(type))
        {
            // M14: manufacturers are standalone (no HQ). Bump treasury to cover construction.
            if (sim.State.City.TreasuryBalance < 2_000_000)
                sim.State.City.TreasuryBalance += 2_000_000;
            s = sim.PlaceManufacturer(type);
        }
        else
        {
            var hqId = EnsureHqForType(sim, type);
            s = sim.PlaceIndustrialStructure(type, hqId);
        }
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
    public void Manufacturer_AccumulatesOutputInOwnBuffer()
    {
        // M14: with no storage layer, manufacturer holds its output in its own ManufacturedStorage.
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        PlaceAndOperationalize(sim, StructureType.ForestExtractor);
        PlaceAndOperationalize(sim, StructureType.Sawmill);
        var factory = PlaceAndOperationalize(sim, StructureType.HouseholdFactory);

        sim.Tick(30);  // one month of production

        var factoryHousehold = factory.ManufacturedStorage.GetValueOrDefault(ManufacturedGood.Household);
        Assert.True(factoryHousehold > 0,
            $"Manufacturer should have accumulated household goods in its own buffer. Got {factoryHousehold}.");
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
    public void HqEarns_WhenManufacturerBuysFromProcessor()
    {
        // M14: the manufacturer (standalone) pays the processor's HQ for processed goods.
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        var extractor = PlaceAndOperationalize(sim, StructureType.ForestExtractor);
        PlaceAndOperationalize(sim, StructureType.Sawmill);
        var mfg = PlaceAndOperationalize(sim, StructureType.HouseholdFactory);
        var hq = sim.State.City.Structures[extractor.OwnerHqId!.Value];

        sim.Tick(15);  // mid-month, before reset

        // Mfg's purchases of lumber from sawmill = revenue to the HQ.
        Assert.True(hq.MonthlyRevenue > 0,
            $"HQ should record revenue from Mfg lumber purchases. Got {hq.MonthlyRevenue}.");
        // Mfg has matching expenses.
        Assert.True(mfg.MonthlyExpenses > 0,
            $"Mfg should record expense for buying lumber. Got {mfg.MonthlyExpenses}.");
    }

    [Fact]
    public void Determinism_FullChain_SameSeedProducesSameOutput()
    {
        Sim BuildSim() {
            var sim = Sim.Create(new SimConfig { Seed = 42 });
            sim.CreateResidentialZone();
            PlaceAndOperationalize(sim, StructureType.ForestExtractor);
            PlaceAndOperationalize(sim, StructureType.Sawmill);
            PlaceAndOperationalize(sim, StructureType.HouseholdFactory);
            sim.Tick(360);
            return sim;
        }

        var sim1 = BuildSim();
        var sim2 = BuildSim();

        Assert.Equal(
            sim1.State.Region.GoodsReservoir.GetValueOrDefault(ManufacturedGood.Household),
            sim2.State.Region.GoodsReservoir.GetValueOrDefault(ManufacturedGood.Household));
        Assert.Equal(sim1.State.City.Population, sim2.State.City.Population);
    }
}
