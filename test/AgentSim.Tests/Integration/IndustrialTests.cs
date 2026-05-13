using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Types;

namespace AgentSim.Tests.Integration;

public class IndustrialTests
{
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

    private static Structure PlaceAndOperationalize(Sim sim, StructureType type, int seedCash = 100_000)
    {
        Structure s;
        if (Industrial.IsManufacturer(type))
        {
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
        foreach (var (tier, count) in s.JobSlots) s.FilledSlots[tier] = count;
        return s;
    }

    [Fact]
    public void PlaceIndustrialStructure_AddsStructureUnderConstruction()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var hqId = EnsureHq(sim);
        var extractor = sim.PlaceIndustrialStructure(StructureType.ForestExtractor, hqId);

        Assert.False(extractor.Operational);
        Assert.Equal(0, extractor.ZoneId);
    }

    [Fact]
    public void PlaceCommercialAsIndustrial_Throws()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var hqId = EnsureHq(sim);
        Assert.Throws<ArgumentException>(() => sim.PlaceIndustrialStructure(StructureType.Shop, hqId));
    }

    [Fact]
    public void ForestExtractor_ProducesRawUnitsWhenStaffed()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var extractor = PlaceAndOperationalize(sim, StructureType.ForestExtractor);

        sim.Tick(1);

        Assert.True(extractor.RawUnitsInStock > 0,
            $"Forest extractor should accumulate raw units. Got {extractor.RawUnitsInStock}.");
    }

    [Fact]
    public void Sawmill_ProducesWoodMfgInput_FromForestExtractor()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        PlaceAndOperationalize(sim, StructureType.ForestExtractor);
        var sawmill = PlaceAndOperationalize(sim, StructureType.Sawmill);

        sim.Tick(5);

        var wood = sawmill.MfgInputStorage.GetValueOrDefault(MfgInput.Wood);
        Assert.True(wood > 0, $"Sawmill should produce Wood. Got {wood}.");
    }

    [Fact]
    public void PaperMill_ProducesOutputUnits_FromPulp()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        PlaceAndOperationalize(sim, StructureType.ForestExtractor);
        PlaceAndOperationalize(sim, StructureType.PulpMill);
        var mill = PlaceAndOperationalize(sim, StructureType.PaperMill);

        sim.Tick(30);

        Assert.True(mill.MfgOutputStock > 0,
            $"PaperMill should accumulate output units. Got {mill.MfgOutputStock}.");
    }

    [Fact]
    public void HouseholdFactory_RequiresAllInputs_Produces()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        // Wood
        PlaceAndOperationalize(sim, StructureType.ForestExtractor);
        PlaceAndOperationalize(sim, StructureType.Sawmill);
        // Steel
        PlaceAndOperationalize(sim, StructureType.Mine);
        PlaceAndOperationalize(sim, StructureType.Smelter);
        // Glass
        PlaceAndOperationalize(sim, StructureType.SandPit);
        PlaceAndOperationalize(sim, StructureType.SilicatePlant);
        // Plastic
        PlaceAndOperationalize(sim, StructureType.OilWell);
        PlaceAndOperationalize(sim, StructureType.PlasticPlant);

        var factory = PlaceAndOperationalize(sim, StructureType.HouseholdFactory);

        sim.Tick(30);

        Assert.True(factory.MfgOutputStock > 0,
            $"HouseholdFactory should produce sector units. Got {factory.MfgOutputStock}.");
    }

    [Fact]
    public void HouseholdFactory_MissingInputs_DoesNotProduce()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        // Only Forestry — missing Steel + Glass + Plastic.
        PlaceAndOperationalize(sim, StructureType.ForestExtractor);
        PlaceAndOperationalize(sim, StructureType.Sawmill);
        var factory = PlaceAndOperationalize(sim, StructureType.HouseholdFactory);

        sim.Tick(30);

        Assert.Equal(0, factory.MfgOutputStock);
    }

    [Fact]
    public void Manufacturer_DeclaresSectorsAndUnitPrice()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        var mfg = PlaceAndOperationalize(sim, StructureType.HouseholdFactory);

        Assert.Contains(CommercialSector.Retail, mfg.ManufacturerSectors);
        Assert.Equal(300, mfg.MfgUnitPrice);
    }

    [Fact]
    public void IndustrialStructure_PaysUtilitiesMonthly()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 200_000 });
        sim.CreateResidentialZone();
        PlaceAndOperationalize(sim, StructureType.ForestExtractor);

        var treasuryBefore = sim.State.City.TreasuryBalance;
        sim.Tick(30);

        Assert.True(sim.State.City.TreasuryBalance > treasuryBefore + 50 * 450,
            "Treasury should have received industrial utility payment on top of rent.");
    }

    [Fact]
    public void IndustrialStructure_PropertyTax_ChargedToHq()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        var extractor = PlaceAndOperationalize(sim, StructureType.ForestExtractor);
        var hq = sim.State.City.Structures[extractor.OwnerHqId!.Value];

        var hqCashBefore = hq.CashBalance;
        var extractorCashBefore = extractor.CashBalance;
        sim.Tick(30);

        Assert.Equal(extractorCashBefore, extractor.CashBalance);
        Assert.True(hq.CashBalance < hqCashBefore,
            $"HQ should have paid sub expenses. Before: {hqCashBefore}, After: {hq.CashBalance}");
    }

    [Fact]
    public void HqEarns_WhenManufacturerBuysMfgInputFromProcessor()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        var extractor = PlaceAndOperationalize(sim, StructureType.ForestExtractor);
        PlaceAndOperationalize(sim, StructureType.PulpMill);
        var mfg = PlaceAndOperationalize(sim, StructureType.PaperMill);
        var hq = sim.State.City.Structures[extractor.OwnerHqId!.Value];

        sim.Tick(15);

        Assert.True(hq.MonthlyRevenue > 0,
            $"HQ should record revenue from Mfg pulp purchases. Got {hq.MonthlyRevenue}.");
        Assert.True(mfg.MonthlyExpenses > 0,
            $"Mfg should record expense for buying pulp. Got {mfg.MonthlyExpenses}.");
    }

    [Fact]
    public void Determinism_FullChain_SameSeedProducesSameOutput()
    {
        Sim BuildSim() {
            var sim = Sim.Create(new SimConfig { Seed = 42 });
            sim.CreateResidentialZone();
            PlaceAndOperationalize(sim, StructureType.ForestExtractor);
            PlaceAndOperationalize(sim, StructureType.PulpMill);
            var mfg = PlaceAndOperationalize(sim, StructureType.PaperMill);
            sim.Tick(360);
            return sim;
        }

        var sim1 = BuildSim();
        var sim2 = BuildSim();

        var mfg1 = sim1.State.City.Structures.Values.First(s => s.Type == StructureType.PaperMill);
        var mfg2 = sim2.State.City.Structures.Values.First(s => s.Type == StructureType.PaperMill);
        Assert.Equal(mfg1.MfgOutputStock, mfg2.MfgOutputStock);
        Assert.Equal(sim1.State.City.Population, sim2.State.City.Population);
    }
}
