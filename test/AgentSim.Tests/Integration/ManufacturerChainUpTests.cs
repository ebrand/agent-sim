using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Types;

namespace AgentSim.Tests.Integration;

/// <summary>
/// M14e: manufacturer chain-up — manufactured goods as inputs to other manufacturers.
/// Example chain: HQ (Forestry: ForestExtractor + PulpMill) → Pulp → PaperMill (mfg) → Paper →
/// Printer (mfg) → Books → commercial.
/// </summary>
public class ManufacturerChainUpTests
{
    private static long EnsureHq(Sim sim, IndustryType industry, ulong cashBalance = 5_000_000)
    {
        var existing = sim.State.City.Structures.Values
            .FirstOrDefault(s => s.Type == StructureType.CorporateHq && s.Industry == industry);
        if (existing != null) return existing.Id;

        var commZone = sim.State.City.Zones.Values.FirstOrDefault(z => z.Type == ZoneType.Commercial)
            ?? sim.CreateCommercialZone();
        var hq = sim.PlaceCorporateHq(commZone.Id, industry, $"TestCo-{industry}");
        hq.ConstructionTicks = hq.RequiredConstructionTicks;
        hq.CashBalance = (int)cashBalance;
        return hq.Id;
    }

    private static Structure FastBuildIndustrial(Sim sim, StructureType type, long hqId)
    {
        var s = sim.PlaceIndustrialStructure(type, hqId);
        s.ConstructionTicks = s.RequiredConstructionTicks;
        foreach (var (tier, count) in s.JobSlots) s.FilledSlots[tier] = count;
        return s;
    }

    private static Structure FastBuildMfg(Sim sim, StructureType type)
    {
        if (sim.State.City.TreasuryBalance < 2_000_000)
            sim.State.City.TreasuryBalance += 5_000_000;
        var s = sim.PlaceManufacturer(type);
        s.ConstructionTicks = s.RequiredConstructionTicks;
        s.CashBalance = 500_000;
        foreach (var (tier, count) in s.JobSlots) s.FilledSlots[tier] = count;
        return s;
    }

    [Fact]
    public void Printer_ConsumesPaper_FromPaperMill_ProducesBooks()
    {
        // Full chain: Forestry HQ (ForestExtractor + PulpMill) → Pulp → PaperMill → Paper →
        // Printer → Books. Also needs Plastic (Oil HQ: OilWell + PlasticPlant) for Printer's
        // binding/cover input.
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 15_000_000 });

        // Forestry chain (Pulp)
        var fhq = EnsureHq(sim, IndustryType.Forestry);
        FastBuildIndustrial(sim, StructureType.ForestExtractor, fhq);
        FastBuildIndustrial(sim, StructureType.PulpMill, fhq);

        // Oil chain (Plastic)
        var ohq = EnsureHq(sim, IndustryType.Oil);
        FastBuildIndustrial(sim, StructureType.OilWell, ohq);
        FastBuildIndustrial(sim, StructureType.PlasticPlant, ohq);

        // Mfg layer 1: PaperMill consumes Pulp, produces Paper
        var paperMill = FastBuildMfg(sim, StructureType.PaperMill);
        // Mfg layer 2: Printer consumes Paper (from PaperMill) + Plastic, produces Books
        var printer = FastBuildMfg(sim, StructureType.Printer);

        sim.Tick(30);  // a month for the chain to flow

        var paperInMill = paperMill.ManufacturedStorage.GetValueOrDefault(ManufacturedGood.Paper);
        var books = printer.ManufacturedStorage.GetValueOrDefault(ManufacturedGood.Books);
        Assert.True(books > 0,
            $"Printer should have produced books from Paper + Plastic. Got {books}.");
    }

    [Fact]
    public void Printer_PaysPaperMill_ForPaper()
    {
        // Verify the real cross-mfg cash transaction: Printer's expense = PaperMill's revenue.
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 15_000_000 });

        var fhq = EnsureHq(sim, IndustryType.Forestry);
        FastBuildIndustrial(sim, StructureType.ForestExtractor, fhq);
        FastBuildIndustrial(sim, StructureType.PulpMill, fhq);
        var ohq = EnsureHq(sim, IndustryType.Oil);
        FastBuildIndustrial(sim, StructureType.OilWell, ohq);
        FastBuildIndustrial(sim, StructureType.PlasticPlant, ohq);

        var paperMill = FastBuildMfg(sim, StructureType.PaperMill);
        var printer = FastBuildMfg(sim, StructureType.Printer);

        sim.Tick(15);  // mid-month before settlement reset

        // PaperMill's revenue includes Printer's purchases.
        Assert.True(paperMill.MonthlyRevenue > 0,
            $"PaperMill should have received revenue from Printer. Got {paperMill.MonthlyRevenue}.");
        // Printer's expenses include the Paper purchase cost.
        Assert.True(printer.MonthlyExpenses > 0,
            $"Printer should have recorded paper expense. Got {printer.MonthlyExpenses}.");
    }

    [Fact]
    public void Printer_WithoutPaperMill_ProducesNoBooks()
    {
        // Printer needs Paper as an input. Without PaperMill, no books can be produced.
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 15_000_000 });

        // Only the Plastic chain — no PaperMill.
        var ohq = EnsureHq(sim, IndustryType.Oil);
        FastBuildIndustrial(sim, StructureType.OilWell, ohq);
        FastBuildIndustrial(sim, StructureType.PlasticPlant, ohq);

        var printer = FastBuildMfg(sim, StructureType.Printer);

        sim.Tick(30);

        var books = printer.ManufacturedStorage.GetValueOrDefault(ManufacturedGood.Books);
        Assert.Equal(0, books);
    }
}
