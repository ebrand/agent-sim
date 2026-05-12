using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Types;

namespace AgentSim.Tests.Integration;

/// <summary>
/// M6: commercial structures consume goods from storage (or region, or imports) to fulfill COL demand.
/// </summary>
public class CommercialGoodsConsumptionTests
{
    /// <summary>Place an operational commercial structure, skip construction, seed cash.</summary>
    private static Structure PlaceOperationalShop(Sim sim, long zoneId, int seedCash = 100_000)
    {
        var s = sim.PlaceCommercialStructure(zoneId, StructureType.Shop);
        s.ConstructionTicks = s.RequiredConstructionTicks;
        s.CashBalance = seedCash;
        return s;
    }

    /// <summary>Ensure a Forestry HQ exists in the sim and return its id.</summary>
    private static long EnsureHq(Sim sim)
    {
        var existing = sim.State.City.Structures.Values
            .FirstOrDefault(s => s.Type == StructureType.CorporateHq);
        if (existing != null) return existing.Id;
        var commZone = sim.State.City.Zones.Values.FirstOrDefault(z => z.Type == ZoneType.Commercial)
            ?? sim.CreateCommercialZone();
        var hq = sim.PlaceCorporateHq(commZone.Id, IndustryType.Forestry, "TestCo");
        hq.ConstructionTicks = hq.RequiredConstructionTicks;
        hq.CashBalance = 50_000_000;
        return hq.Id;
    }

    /// <summary>
    /// Seed a standalone manufacturer with pre-stocked manufactured goods. M14: commercial pulls
    /// from manufacturers directly (no Storage layer). Tests using "storage" via this helper
    /// actually exercise the manufacturer-as-seller flow now.
    /// </summary>
    private static Structure SeedManufacturerWithGoods(Sim sim, int foodUnits = 0, int clothingUnits = 0, int householdUnits = 0)
    {
        // Pick any manufacturer type that has the InternalStorageCapacity field — HouseholdFactory
        // works. Treasury needs to cover construction.
        if (sim.State.City.TreasuryBalance < 2_000_000)
            sim.State.City.TreasuryBalance += 2_000_000;
        var s = sim.PlaceManufacturer(StructureType.HouseholdFactory);
        s.ConstructionTicks = s.RequiredConstructionTicks;
        s.CashBalance = 100_000;
        foreach (var (tier, count) in s.JobSlots)
        {
            s.FilledSlots[tier] = count;
        }
        if (foodUnits > 0) s.ManufacturedStorage[ManufacturedGood.Food] = foodUnits;
        if (clothingUnits > 0) s.ManufacturedStorage[ManufacturedGood.Clothing] = clothingUnits;
        if (householdUnits > 0) s.ManufacturedStorage[ManufacturedGood.Household] = householdUnits;
        return s;
    }

    /// <summary>Legacy name kept for tests that still reference SeedStorageWithGoods.</summary>
    private static Structure SeedStorageWithGoods(Sim sim, int foodUnits = 0, int clothingUnits = 0, int householdUnits = 0)
        => SeedManufacturerWithGoods(sim, foodUnits, clothingUnits, householdUnits);

    [Fact]
    public void Commercial_PullsGoodsFromLocalStorage_WhenAvailable()
    {
        // Setup: bootstrap settlers, commercial structure, storage seeded with goods
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        var commZone = sim.CreateCommercialZone();
        var shop = PlaceOperationalShop(sim, commZone.Id);

        // Storage seeded with WAY more than agents will demand in one month
        var storage = SeedStorageWithGoods(sim, foodUnits: 10_000, clothingUnits: 10_000, householdUnits: 5_000);

        var storageFoodBefore = storage.ManufacturedStorage[ManufacturedGood.Food];
        sim.Tick(30);  // through day 30 COL flow

        var storageFoodAfter = storage.ManufacturedStorage.GetValueOrDefault(ManufacturedGood.Food);
        Assert.True(storageFoodAfter < storageFoodBefore,
            $"Storage food should be consumed by commercial. Before: {storageFoodBefore}, After: {storageFoodAfter}");
    }

    [Fact]
    public void Commercial_PaysManufacturer_WhenPullingGoods()
    {
        // M14: commercial buys directly from standalone manufacturers. Revenue accrues to the
        // manufacturer's own CashBalance.
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        var commZone = sim.CreateCommercialZone();
        PlaceOperationalShop(sim, commZone.Id);
        var mfg = SeedStorageWithGoods(sim, foodUnits: 10_000, clothingUnits: 10_000, householdUnits: 5_000);

        var mfgCashBefore = mfg.CashBalance;
        sim.Tick(30);

        Assert.True(mfg.CashBalance > mfgCashBefore,
            $"Manufacturer should gain cash from commercial sales. Before: {mfgCashBefore}, After: {mfg.CashBalance}");
    }

    [Fact]
    public void Commercial_NoStorage_FallsThroughToImports_AtUpcharge()
    {
        // Without storage or regional reservoir, commercial buys imports at 25% upcharge.
        // Compare: with storage backing, commercial is profitable. Without, commercial loses
        // money on imports. This test asserts the import path is exercised (cash deficit
        // bigger than would occur without goods cost at all).
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        var commZone = sim.CreateCommercialZone();
        var shop = PlaceOperationalShop(sim, commZone.Id);

        sim.Tick(30);

        // With imports, shop should end the month with LESS cash than it started
        // (revenue offset by expensive imports + utilities + property tax + wages)
        Assert.True(shop.CashBalance < 100_000,
            $"Shop should have lost money on imports + costs. CashBalance: {shop.CashBalance}");
    }

    [Fact]
    public void CommercialWithStorageBacking_HasBetterMargin_ThanCommercialWithoutStorage()
    {
        // Two parallel sims: one with stocked storage, one without.
        // The one WITH storage should have higher CashBalance after a month (cheaper goods cost).

        // Sim 1: shop + stocked storage
        var sim1 = Sim.Create(new SimConfig { Seed = 42 });
        sim1.CreateResidentialZone();
        var c1 = sim1.CreateCommercialZone();
        var shop1 = PlaceOperationalShop(sim1, c1.Id);
        SeedStorageWithGoods(sim1, foodUnits: 10_000, clothingUnits: 10_000, householdUnits: 5_000);

        // Sim 2: shop only (forces imports)
        var sim2 = Sim.Create(new SimConfig { Seed = 42 });
        sim2.CreateResidentialZone();
        var c2 = sim2.CreateCommercialZone();
        var shop2 = PlaceOperationalShop(sim2, c2.Id);

        sim1.Tick(30);
        sim2.Tick(30);

        Assert.True(shop1.CashBalance > shop2.CashBalance,
            $"Shop with storage should be more profitable than shop with imports. Sim1: {shop1.CashBalance}, Sim2: {shop2.CashBalance}");
    }

    [Fact]
    public void NoCommercial_ColSilentFail_AgentSavingsPreserved()
    {
        // Existing behavior: if no commercial structure exists, COL spending fails silently.
        // Should still hold post-M6.
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        sim.Tick(30);

        var uneducated = sim.State.City.Agents.Values.First(a => a.EducationTier == EducationTier.Uneducated);
        // Founders' bonus $5,000 - $800 rent - $200 utilities = $4,000. No COL deducted.
        Assert.Equal(Bootstrap.FoundersStartingSavings - 800 - 200, uneducated.Savings);
    }

    [Fact]
    public void ManufacturerReceivesRevenue_FromCommercialSales()
    {
        // M14: commercial pulls from manufacturers; revenue accrues to manufacturer (standalone).
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        var commZone = sim.CreateCommercialZone();
        PlaceOperationalShop(sim, commZone.Id);
        var mfg = SeedStorageWithGoods(sim, foodUnits: 10_000);

        sim.Tick(15);  // mid-month, before any monthly reset

        Assert.True(mfg.MonthlyRevenue > 0,
            $"Manufacturer should have received revenue from commercial purchases. Got {mfg.MonthlyRevenue}.");
    }

    [Fact]
    public void Determinism_GoodsConsumption_SameSeedSameResult()
    {
        Sim BuildSim()
        {
            var sim = Sim.Create(new SimConfig { Seed = 42 });
            sim.CreateResidentialZone();
            var cz = sim.CreateCommercialZone();
            PlaceOperationalShop(sim, cz.Id);
            SeedStorageWithGoods(sim, foodUnits: 5_000, clothingUnits: 5_000, householdUnits: 2_000);
            sim.Tick(30);
            return sim;
        }

        var sim1 = BuildSim();
        var sim2 = BuildSim();

        var storage1 = sim1.State.City.Structures.Values.First(s => s.Type == StructureType.HouseholdFactory);
        var storage2 = sim2.State.City.Structures.Values.First(s => s.Type == StructureType.HouseholdFactory);

        Assert.Equal(storage1.CashBalance, storage2.CashBalance);
        Assert.Equal(
            storage1.ManufacturedStorage.GetValueOrDefault(ManufacturedGood.Food),
            storage2.ManufacturedStorage.GetValueOrDefault(ManufacturedGood.Food));
    }
}
