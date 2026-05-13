using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Sim.Mechanics;
using AgentSim.Core.Types;

namespace AgentSim.Tests.Integration;

/// <summary>
/// M17: construction cost is deducted from treasury/HQ (as M11), then routed through any
/// operational Construction-sector commercial(s) and on to Construction-sector manufacturers.
/// If no construction commercial exists, the cost dollars leak abroad (treasury still pays the
/// cost). The build always succeeds — imports are implicit.
/// </summary>
public class ConstructionGoodsRoutingTests
{
    /// <summary>Place an operational Construction-sector commercial (skip the 7-tick build).</summary>
    private static Structure PlaceOperationalConstructionShop(Sim sim, long zoneId)
    {
        var c = sim.PlaceCommercialStructure(zoneId, StructureType.Shop, CommercialSector.Construction);
        c.ConstructionTicks = c.RequiredConstructionTicks;
        c.CashBalance = 0;  // reset so we measure deltas from zero
        return c;
    }

    /// <summary>Place and operationalize a Construction-sector manufacturer with pre-stocked output.</summary>
    private static Structure SeedConstructionMfg(Sim sim, StructureType type, int outputUnits)
    {
        if (sim.State.City.TreasuryBalance < 2_000_000) sim.State.City.TreasuryBalance += 2_000_000;
        var m = sim.PlaceManufacturer(type);
        m.ConstructionTicks = m.RequiredConstructionTicks;
        m.CashBalance = 0;
        foreach (var (tier, count) in m.JobSlots) m.FilledSlots[tier] = count;
        m.MfgOutputStock = outputUnits;
        return m;
    }

    [Fact]
    public void NoConstructionCommercial_TreasuryStillPays_NoRouting()
    {
        // Existing M11 invariant must hold: treasury balance drops by the full cost.
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        var before = sim.State.City.TreasuryBalance;

        sim.PlaceServiceStructure(StructureType.PoliceStation);

        Assert.Equal(before - Construction.Cost(StructureType.PoliceStation), sim.State.City.TreasuryBalance);
    }

    [Fact]
    public void TreasuryConstruction_RoutesToConstructionCommercial()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 2_000_000 });
        var commZone = sim.CreateCommercialZone();
        var hub = PlaceOperationalConstructionShop(sim, commZone.Id);

        sim.PlaceServiceStructure(StructureType.PoliceStation);
        var cost = Construction.Cost(StructureType.PoliceStation);

        // Full cost credited to commercial's MonthlyRevenue. Cash deltas reflect imports outflow.
        Assert.Equal(cost, hub.MonthlyRevenue);
    }

    [Fact]
    public void ConstructionCommercial_SpendsGoodsBudget_OnConstructionMfg()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 5_000_000 });
        var commZone = sim.CreateCommercialZone();
        var hub = PlaceOperationalConstructionShop(sim, commZone.Id);
        // Stock a construction-sector mfg with plenty of units; ConcretePlant services Construction.
        var concrete = SeedConstructionMfg(sim, StructureType.ConcretePlant, outputUnits: 10_000);
        var concreteCashBefore = concrete.CashBalance;
        var concreteStockBefore = concrete.MfgOutputStock;

        sim.PlaceServiceStructure(StructureType.PoliceStation);

        Assert.True(concrete.CashBalance > concreteCashBefore,
            $"ConcretePlant should have earned revenue. Before {concreteCashBefore}, after {concrete.CashBalance}.");
        Assert.True(concrete.MfgOutputStock < concreteStockBefore,
            $"ConcretePlant stock should have decreased. Before {concreteStockBefore}, after {concrete.MfgOutputStock}.");
    }

    [Fact]
    public void GoodsBudgetIs70Pct_RemainingIsCommercialMargin()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 5_000_000 });
        var commZone = sim.CreateCommercialZone();
        var hub = PlaceOperationalConstructionShop(sim, commZone.Id);
        // Plenty of supply so the full goods budget is sourced locally (no imports outflow).
        SeedConstructionMfg(sim, StructureType.ConcretePlant, outputUnits: 10_000);
        // Reset accumulators after seeding so we measure only the PoliceStation event below.
        hub.CashBalance = 0; hub.MonthlyRevenue = 0; hub.MonthlyExpenses = 0;

        sim.PlaceServiceStructure(StructureType.PoliceStation);
        var cost = Construction.Cost(StructureType.PoliceStation);

        var maxMfgSpend = (int)(cost * ConstructionGoodsMechanic.GoodsCostFraction);
        // Tolerance for unit-price rounding: leftover dollars under one unit price may trigger a
        // small imports outflow (× 1.25 upcharge), so spending can slightly exceed maxMfgSpend.
        var tolerance = 500;
        Assert.True(hub.MonthlyExpenses <= maxMfgSpend + tolerance,
            $"Hub's spend should be roughly ≤ 70% of cost. Spent {hub.MonthlyExpenses}, max ~{maxMfgSpend + tolerance}.");
        var expectedMin = cost - maxMfgSpend - tolerance;
        Assert.True(hub.CashBalance >= expectedMin,
            $"Hub's net cash should reflect roughly 30% margin. Cash {hub.CashBalance}, expected ≥ {expectedMin}.");
    }

    [Fact]
    public void HqFundedConstruction_AlsoRoutesThroughConstructionCommercial()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 5_000_000 });
        var commZone = sim.CreateCommercialZone();
        var hub = PlaceOperationalConstructionShop(sim, commZone.Id);

        var hq = sim.PlaceCorporateHq(commZone.Id, IndustryType.Forestry, "ForestCo");
        hq.ConstructionTicks = hq.RequiredConstructionTicks;
        hq.CashBalance = 5_000_000;

        sim.PlaceIndustrialStructure(StructureType.ForestExtractor, hq.Id);
        var cost = Construction.Cost(StructureType.ForestExtractor);

        // Routing fires: hub records cost as MonthlyRevenue. Cash net depends on import behavior.
        Assert.Equal(cost, hub.MonthlyRevenue);
    }

    [Fact]
    public void NoSupply_ConstructionCommercial_PaysImports_WithUpcharge()
    {
        // No local Construction-sector mfg → hub pays imports for 70% of cost at 25% upcharge.
        // Net cash retained ≈ cost × (1 - 0.70 × 1.25) = cost × 0.125 (margin minus import overhead).
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        var commZone = sim.CreateCommercialZone();
        var hub = PlaceOperationalConstructionShop(sim, commZone.Id);
        var hubCashBefore = hub.CashBalance;

        sim.PlaceServiceStructure(StructureType.PoliceStation);
        var cost = Construction.Cost(StructureType.PoliceStation);

        // Hub should NOT keep the full cost — imports drained most of the goods budget.
        Assert.True(hub.CashBalance < hubCashBefore + cost,
            $"Hub shouldn't keep full cost with no local supply (imports drain it). Got {hub.CashBalance}.");
        Assert.True(hub.MonthlyExpenses > 0,
            $"Hub should record import expenses. Got {hub.MonthlyExpenses}.");
    }

    [Fact]
    public void RoutingSplitsProRata_AcrossMultipleConstructionCommercials()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 2_000_000 });
        var commZone = sim.CreateCommercialZone();
        var a = PlaceOperationalConstructionShop(sim, commZone.Id);
        var b = PlaceOperationalConstructionShop(sim, commZone.Id);
        a.CashBalance = 0; a.MonthlyRevenue = 0;
        b.CashBalance = 0; b.MonthlyRevenue = 0;

        sim.PlaceServiceStructure(StructureType.PoliceStation);
        var cost = Construction.Cost(StructureType.PoliceStation);

        // Revenue is split pro-rata across the two commercials.
        Assert.Equal(cost, a.MonthlyRevenue + b.MonthlyRevenue);
        Assert.InRange(a.MonthlyRevenue - b.MonthlyRevenue, 0, 1);
    }

    [Fact]
    public void RetailCommercial_DoesNotReceiveConstructionFlow()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        var commZone = sim.CreateCommercialZone();
        var retail = sim.PlaceCommercialStructure(commZone.Id, StructureType.Shop, CommercialSector.Retail);
        retail.ConstructionTicks = retail.RequiredConstructionTicks;
        retail.CashBalance = 0;

        sim.PlaceServiceStructure(StructureType.PoliceStation);

        Assert.Equal(0, retail.CashBalance);
        Assert.Equal(0, retail.MonthlyRevenue);
    }
}
