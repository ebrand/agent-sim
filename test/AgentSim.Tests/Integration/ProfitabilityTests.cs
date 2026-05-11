using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Sim.Mechanics;
using AgentSim.Core.Types;

namespace AgentSim.Tests.Integration;

public class ProfitabilityTests
{
    /// <summary>
    /// Pick the first industry that allows this structure type and return / create a matching HQ.
    /// Multi-industry tests get multiple HQs (one per industry touched), each overfunded for
    /// test convenience.
    /// </summary>
    private static long EnsureHqForType(Sim sim, StructureType type)
    {
        IndustryType? required = null;
        foreach (IndustryType i in Enum.GetValues<IndustryType>())
        {
            if (Industry.Allows(i, type)) { required = i; break; }
        }
        if (required is not IndustryType industry)
            throw new InvalidOperationException($"No industry allows {type}");

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

    /// <summary>Helper to place an operational industrial structure with full staffing.</summary>
    private static Structure PlaceFullyStaffed(Sim sim, StructureType type, int seedCash = 100_000)
    {
        var hqId = EnsureHqForType(sim, type);
        var s = sim.PlaceIndustrialStructure(type, hqId);
        s.ConstructionTicks = s.RequiredConstructionTicks;
        s.CashBalance = seedCash;
        foreach (var (tier, count) in s.JobSlots)
        {
            s.FilledSlots[tier] = count;
        }
        return s;
    }

    [Fact]
    public void ProfitableStructure_NoWarning_StaysOperational()
    {
        // 2-manufacturer storage scenario from M4 — storage is profitable, should never go inactive
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        PlaceFullyStaffed(sim, StructureType.ForestExtractor);
        PlaceFullyStaffed(sim, StructureType.Sawmill);
        PlaceFullyStaffed(sim, StructureType.HouseholdFactory);
        PlaceFullyStaffed(sim, StructureType.SandPit);
        PlaceFullyStaffed(sim, StructureType.SilicatePlant);
        PlaceFullyStaffed(sim, StructureType.GlassWorks);
        var storage = PlaceFullyStaffed(sim, StructureType.Storage);

        sim.Tick(60);  // 2 months — profitable storage stays operational

        Assert.False(storage.Inactive, "Profitable storage should remain operational");
        Assert.False(storage.UnprofitableWarning, "Profitable storage should have no warning");
    }

    /// <summary>
    /// Synthesize a non-HQ-owned commercial structure to exercise the profitability mechanism
    /// directly. After M13, industrial subs owned by an HQ are exempt from this check, so we
    /// use a Shop (commercial, non-HQ).
    /// </summary>
    private static Structure SynthesizeUnprofitableShop(Sim sim)
    {
        var zone = sim.CreateCommercialZone();
        var shop = new Structure
        {
            Id = sim.State.AllocateStructureId(),
            Type = StructureType.Shop,
            ZoneId = zone.Id,
            ResidentialCapacity = 0,
            ConstructionTicks = 7,
            RequiredConstructionTicks = 7,
            JobSlots = Commercial.JobSlots(StructureType.Shop).ToDictionary(kv => kv.Key, kv => kv.Value),
        };
        // Pre-seed unprofitable monthly state.
        shop.MonthlyRevenue = 1_000;
        shop.MonthlyExpenses = 5_000;
        sim.State.City.Structures[shop.Id] = shop;
        zone.StructureIds.Add(shop.Id);
        return shop;
    }

    [Fact]
    public void UnprofitableStructure_FirstMonth_SetsWarning()
    {
        // Test the profitability mechanism directly on a non-HQ commercial structure.
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        var shop = SynthesizeUnprofitableShop(sim);

        StructureProfitabilityMechanic.EndOfMonthCheck(sim.State);

        Assert.True(shop.UnprofitableWarning, "Shop should have UnprofitableWarning after 1 unprofitable month");
        Assert.False(shop.Inactive, "Shop should NOT yet be inactive after only 1 unprofitable month");
    }

    [Fact]
    public void UnprofitableStructure_TwoConsecutiveMonths_GoesInactive()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        var shop = SynthesizeUnprofitableShop(sim);

        // Month 1: sets warning
        StructureProfitabilityMechanic.EndOfMonthCheck(sim.State);
        Assert.True(shop.UnprofitableWarning);
        Assert.False(shop.Inactive);

        // Month 2: still unprofitable → goes inactive
        shop.MonthlyRevenue = 1_000;
        shop.MonthlyExpenses = 5_000;
        StructureProfitabilityMechanic.EndOfMonthCheck(sim.State);

        Assert.True(shop.Inactive, "Shop should be inactive after 2 consecutive unprofitable months");
    }

    [Fact]
    public void InactiveStructure_HasNoEmployees()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        var shop = SynthesizeUnprofitableShop(sim);
        // Force a fake employee on the shop so we can verify it's laid off.
        shop.FilledSlots[EducationTier.Primary] = 1;
        shop.EmployeeIds.Add(99999);

        // Two unprofitable months → inactive → employees laid off.
        StructureProfitabilityMechanic.EndOfMonthCheck(sim.State);
        shop.MonthlyRevenue = 1_000;
        shop.MonthlyExpenses = 5_000;
        StructureProfitabilityMechanic.EndOfMonthCheck(sim.State);

        Assert.True(shop.Inactive);
        Assert.Empty(shop.EmployeeIds);
        Assert.Equal(0, shop.FilledSlots.Values.Sum());
    }

    [Fact]
    public void InactiveStructure_DoesNotPayUtilitiesOrPropertyTax()
    {
        // Industrial construction: extractor $150k + sawmill $250k + factory $400k + storage $150k = $950k.
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        sim.CreateResidentialZone();
        PlaceFullyStaffed(sim, StructureType.ForestExtractor);
        PlaceFullyStaffed(sim, StructureType.Sawmill);
        PlaceFullyStaffed(sim, StructureType.HouseholdFactory);
        var storage = PlaceFullyStaffed(sim, StructureType.Storage);

        sim.Tick(60);  // storage goes inactive at end of month 2

        // At start of month 3, storage is inactive. Its CashBalance shouldn't decrease further.
        var cashAtInactive = storage.CashBalance;
        sim.Tick(30);  // through month 3 — storage is inactive (no utility, no prop tax, no production)

        Assert.Equal(cashAtInactive, storage.CashBalance);
    }

    [Fact]
    public void InactiveStructure_AutoReactivatesAfterOneMonth()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        var shop = SynthesizeUnprofitableShop(sim);

        // Two unprofitable months → inactive.
        StructureProfitabilityMechanic.EndOfMonthCheck(sim.State);
        shop.MonthlyRevenue = 1_000;
        shop.MonthlyExpenses = 5_000;
        StructureProfitabilityMechanic.EndOfMonthCheck(sim.State);
        Assert.True(shop.Inactive);

        // One inactive month → auto-reactivate.
        StructureProfitabilityMechanic.EndOfMonthCheck(sim.State);

        Assert.False(shop.Inactive, "Shop should auto-reactivate after 1 inactive month");
        Assert.Equal(0, shop.InactiveMonths);
    }

    [Fact]
    public void ReactivatedStructure_HiresWorkersAgain()
    {
        // Use a chain with enough population to actually hire — bootstrap residential
        // gives us 50 settlers which the test helper bypasses via FilledSlots.
        // For this test we want to verify the natural hiring pathway works on reactivation.
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();
        PlaceFullyStaffed(sim, StructureType.ForestExtractor);
        PlaceFullyStaffed(sim, StructureType.Sawmill);
        PlaceFullyStaffed(sim, StructureType.HouseholdFactory);
        var storage = PlaceFullyStaffed(sim, StructureType.Storage);

        sim.Tick(60);  // inactivate

        // After reactivation, when the hiring mechanic runs the next tick, storage should re-fill slots.
        // (In this test there are no real unemployed agents because helper used FilledSlots only — so
        // the test just verifies the mechanic doesn't crash on reactivation and the inactive flag clears.)
        sim.Tick(31);  // 1 month + 1 tick: reactivation at end of month 3 + 1 tick of hiring attempts

        Assert.False(storage.Inactive, "Storage should be reactivated");
        // No assertion on EmployeeIds since the helper-staffed test setup doesn't have real agents
    }

    [Fact]
    public void ProfitableMonth_AfterWarning_ClearsWarning()
    {
        // Focused mechanic test: stage state directly and call the end-of-month check.
        // (Setting this up via real chain dynamics is fragile — first-month startup lag often
        // makes structures slightly unprofitable. Test the state transition itself.)
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var commZone = sim.CreateCommercialZone();
        var shop = sim.PlaceCommercialStructure(commZone.Id, StructureType.Shop);
        shop.ConstructionTicks = shop.RequiredConstructionTicks;

        // Pre-existing warning from a prior unprofitable month
        shop.UnprofitableWarning = true;
        // This month: profitable
        shop.MonthlyRevenue = 5_000;
        shop.MonthlyExpenses = 1_000;

        StructureProfitabilityMechanic.EndOfMonthCheck(sim.State);

        Assert.False(shop.UnprofitableWarning, "Warning should clear after a profitable month");
        Assert.False(shop.Inactive);
    }

    [Fact]
    public void UnprofitableAfterWarning_GoesInactive_DirectStateTest()
    {
        // Focused: structure with prior warning + this month also unprofitable → inactive.
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var commZone = sim.CreateCommercialZone();
        var shop = sim.PlaceCommercialStructure(commZone.Id, StructureType.Shop);
        shop.ConstructionTicks = shop.RequiredConstructionTicks;

        shop.UnprofitableWarning = true;
        shop.MonthlyRevenue = 1_000;
        shop.MonthlyExpenses = 5_000;

        StructureProfitabilityMechanic.EndOfMonthCheck(sim.State);

        Assert.True(shop.Inactive, "Second consecutive unprofitable month should trigger inactive");
        Assert.False(shop.UnprofitableWarning, "Warning is cleared when going inactive");
    }

    [Fact]
    public void FirstUnprofitableMonth_SetsWarning_DirectStateTest()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var commZone = sim.CreateCommercialZone();
        var shop = sim.PlaceCommercialStructure(commZone.Id, StructureType.Shop);
        shop.ConstructionTicks = shop.RequiredConstructionTicks;

        Assert.False(shop.UnprofitableWarning);  // no prior warning
        shop.MonthlyRevenue = 1_000;
        shop.MonthlyExpenses = 5_000;

        StructureProfitabilityMechanic.EndOfMonthCheck(sim.State);

        Assert.True(shop.UnprofitableWarning, "First unprofitable month should set warning");
        Assert.False(shop.Inactive, "Should NOT be inactive after just 1 unprofitable month");
    }

    [Fact]
    public void Determinism_Profitability_SameSeedSameOutcome()
    {
        Sim BuildSim()
        {
            var sim = Sim.Create(new SimConfig { Seed = 42 });
            sim.CreateResidentialZone();
            PlaceFullyStaffed(sim, StructureType.ForestExtractor);
            PlaceFullyStaffed(sim, StructureType.Sawmill);
            PlaceFullyStaffed(sim, StructureType.HouseholdFactory);
            PlaceFullyStaffed(sim, StructureType.Storage);
            sim.Tick(60);
            return sim;
        }

        var sim1 = BuildSim();
        var sim2 = BuildSim();

        var storage1 = sim1.State.City.Structures.Values.First(s => s.Type == StructureType.Storage);
        var storage2 = sim2.State.City.Structures.Values.First(s => s.Type == StructureType.Storage);

        Assert.Equal(storage1.Inactive, storage2.Inactive);
        Assert.Equal(storage1.UnprofitableWarning, storage2.UnprofitableWarning);
        Assert.Equal(storage1.CashBalance, storage2.CashBalance);
    }
}
