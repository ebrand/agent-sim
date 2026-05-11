using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Sim.Mechanics;
using AgentSim.Core.Types;

namespace AgentSim.Tests.Integration;

/// <summary>
/// M12: CorporateHq commercial structure owns vertically-integrated industrial supply chains.
/// HQ self-funds construction (zero deduction from city treasury), pays for its industrial
/// subordinates' construction from its own balance, sweeps monthly profits up from those
/// subordinates, and pays corporate-profit + externality taxes to the city treasury.
/// </summary>
public class CorporateHqTests
{
    private static (Sim sim, Structure hq) NewSimWithHq(IndustryType industry = IndustryType.Forestry)
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        var commZone = sim.CreateCommercialZone();
        var hq = sim.PlaceCorporateHq(commZone.Id, industry, $"TestCo-{industry}");
        hq.ConstructionTicks = hq.RequiredConstructionTicks;
        return (sim, hq);
    }

    [Fact]
    public void PlaceCorporateHq_StartingCash_Is2xFullChainCost()
    {
        var (sim, hq) = NewSimWithHq(IndustryType.Forestry);

        // Forestry chain: ForestExtractor + Sawmill + BldgSuppliesFactory + Storage
        // = $150k + $250k + $400k + $150k = $950k. 2× = $1.9M.
        var expected = 2 * (Construction.Cost(StructureType.ForestExtractor)
            + Construction.Cost(StructureType.Sawmill)
            + Construction.Cost(StructureType.BldgSuppliesFactory)
            + Construction.Cost(StructureType.Storage));
        Assert.Equal(expected, hq.CashBalance);
        Assert.Equal(1_900_000, hq.CashBalance);  // sanity check the actual figure
    }

    [Fact]
    public void PlaceCorporateHq_DoesNotChargeCityTreasury()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        var commZone = sim.CreateCommercialZone();
        var treasuryBefore = sim.State.City.TreasuryBalance;

        sim.PlaceCorporateHq(commZone.Id, IndustryType.Oil, "OilCo");

        Assert.Equal(treasuryBefore, sim.State.City.TreasuryBalance);
    }

    [Fact]
    public void PlaceCorporateHq_StoresNameAndIndustry()
    {
        var (sim, hq) = NewSimWithHq(IndustryType.Mining);

        Assert.Equal("TestCo-Mining", hq.Name);
        Assert.Equal(IndustryType.Mining, hq.Industry);
        Assert.Equal(StructureType.CorporateHq, hq.Type);
    }

    [Fact]
    public void PlaceIndustrial_ChargesHqNotTreasury()
    {
        var (sim, hq) = NewSimWithHq(IndustryType.Forestry);
        var hqCashBefore = hq.CashBalance;
        var treasuryBefore = sim.State.City.TreasuryBalance;

        var extractor = sim.PlaceIndustrialStructure(StructureType.ForestExtractor, hq.Id);

        Assert.Equal(hqCashBefore - Construction.Cost(StructureType.ForestExtractor), hq.CashBalance);
        Assert.Equal(treasuryBefore, sim.State.City.TreasuryBalance);
        Assert.Equal(hq.Id, extractor.OwnerHqId);
        Assert.Contains(extractor.Id, hq.OwnedStructureIds);
    }

    [Fact]
    public void PlaceIndustrial_RejectsWrongIndustryStructure()
    {
        var (sim, hq) = NewSimWithHq(IndustryType.Forestry);

        // Mine belongs to Mining, not Forestry.
        Assert.Throws<InvalidOperationException>(() =>
            sim.PlaceIndustrialStructure(StructureType.Mine, hq.Id));
    }

    [Fact]
    public void PlaceIndustrial_HouseholdFactory_AllowedAnywhere()
    {
        // HouseholdFactory is intentionally cross-industry per design.
        var (sim, fhq) = NewSimWithHq(IndustryType.Forestry);
        var (sim2, mhq) = NewSimWithHq(IndustryType.Mining);

        // Should not throw under either industry.
        sim.PlaceIndustrialStructure(StructureType.HouseholdFactory, fhq.Id);
        sim2.PlaceIndustrialStructure(StructureType.HouseholdFactory, mhq.Id);
    }

    [Fact]
    public void PlaceIndustrial_RejectsWhenHqCashInsufficient()
    {
        var (sim, hq) = NewSimWithHq(IndustryType.Oil);
        hq.CashBalance = 1_000;  // not enough to fund anything

        Assert.Throws<InvalidOperationException>(() =>
            sim.PlaceIndustrialStructure(StructureType.CoalMine, hq.Id));
    }

    [Fact]
    public void PlaceIndustrial_RejectsBogusHqId()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        Assert.Throws<ArgumentException>(() =>
            sim.PlaceIndustrialStructure(StructureType.ForestExtractor, 99999));
    }

    [Fact]
    public void PlaceIndustrial_RejectsNonHqOwner()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        var commZone = sim.CreateCommercialZone();
        var shop = sim.PlaceCommercialStructure(commZone.Id, StructureType.Shop);

        Assert.Throws<ArgumentException>(() =>
            sim.PlaceIndustrialStructure(StructureType.ForestExtractor, shop.Id));
    }

    [Fact]
    public void ProfitSweep_PositiveProfit_MovesToHq()
    {
        var (sim, hq) = NewSimWithHq(IndustryType.Forestry);
        hq.CashBalance = 10_000_000;
        var extractor = sim.PlaceIndustrialStructure(StructureType.ForestExtractor, hq.Id);
        extractor.ConstructionTicks = extractor.RequiredConstructionTicks;

        // Synthesize a profitable month: revenue > expenses on the extractor.
        extractor.MonthlyRevenue = 50_000;
        extractor.MonthlyExpenses = 20_000;
        extractor.CashBalance = 100_000;
        var hqCashBefore = hq.CashBalance;

        CorporateProfitMechanic.SweepAndTax(sim.State);

        // Profit = 30k. Corporate tax = 25% = 7.5k. Externality tax (Forestry) = 10% = 3k.
        // Total tax = 10.5k. HQ keeps 30k - 10.5k = 19.5k. Treasury gets 10.5k.
        Assert.Equal(hqCashBefore + 30_000 - 10_500, hq.CashBalance);
        Assert.Equal(100_000 - 30_000, extractor.CashBalance);  // profit moved up
        Assert.Equal(30_000, hq.MonthlyRevenue);
    }

    [Fact]
    public void ProfitSweep_NegativeProfit_NotSwept()
    {
        var (sim, hq) = NewSimWithHq(IndustryType.Mining);
        hq.CashBalance = 1_000_000;
        var mine = sim.PlaceIndustrialStructure(StructureType.Mine, hq.Id);
        mine.ConstructionTicks = mine.RequiredConstructionTicks;

        // Loss-making month.
        mine.MonthlyRevenue = 5_000;
        mine.MonthlyExpenses = 20_000;
        mine.CashBalance = 100_000;
        var hqCashBefore = hq.CashBalance;
        var mineCashBefore = mine.CashBalance;

        CorporateProfitMechanic.SweepAndTax(sim.State);

        // Nothing swept (negative profit). Both balances unchanged.
        Assert.Equal(hqCashBefore, hq.CashBalance);
        Assert.Equal(mineCashBefore, mine.CashBalance);
        Assert.Equal(0, hq.MonthlyRevenue);
    }

    [Fact]
    public void CorporateTax_GoesToCityTreasury()
    {
        var (sim, hq) = NewSimWithHq(IndustryType.Stone);
        hq.CashBalance = 10_000_000;
        var quarry = sim.PlaceIndustrialStructure(StructureType.Quarry, hq.Id);
        quarry.ConstructionTicks = quarry.RequiredConstructionTicks;
        quarry.MonthlyRevenue = 100_000;
        quarry.MonthlyExpenses = 60_000;
        quarry.CashBalance = 200_000;

        var treasuryBefore = sim.State.City.TreasuryBalance;
        CorporateProfitMechanic.SweepAndTax(sim.State);

        // Profit 40k. Corp tax 25% = 10k. Externality (Stone) 10% = 4k. Total 14k → treasury.
        Assert.Equal(treasuryBefore + 14_000, sim.State.City.TreasuryBalance);
    }

    [Fact]
    public void ExternalityTax_OilPaysMoreThanAgriculture()
    {
        // Same swept profit; Oil pays 25%, Agriculture pays 5%.
        Assert.True(TaxRates.Externality(IndustryType.Oil) > TaxRates.Externality(IndustryType.Agriculture));
        Assert.Equal(0.25, TaxRates.Externality(IndustryType.Oil));
        Assert.Equal(0.05, TaxRates.Externality(IndustryType.Agriculture));
    }

    [Fact]
    public void Hq_ExemptFromProfitabilityCheck_DoesNotGoInactive()
    {
        // An HQ with no profitable subsidiaries (just utility + tax expenses) would normally be
        // unprofitable for 2+ months. M12: HQ is exempt — failure is "out of cash," not unprofitability.
        var (sim, hq) = NewSimWithHq(IndustryType.Oil);

        // Tick several months — HQ accrues monthly losses (utility + property tax = $7,500/mo).
        sim.Tick(30 * 4);

        Assert.False(hq.Inactive, "HQ should not go inactive from unprofitability alone.");
    }

    [Fact]
    public void Hq_DoesNotReceiveCol_FromAgents()
    {
        // CorporateHq is in the Commercial category but isn't consumer-facing. Agents shouldn't
        // pay COL revenue to HQ.
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        sim.CreateResidentialZone();
        var commZone = sim.CreateCommercialZone();
        var hq = sim.PlaceCorporateHq(commZone.Id, IndustryType.Forestry, "TestCo");
        hq.ConstructionTicks = hq.RequiredConstructionTicks;
        var hqCashStart = hq.CashBalance;

        sim.Tick(30);  // monthly settlement; would normally distribute COL to commercial

        // HQ should have lost money on utility + property tax + maybe income tax flowthrough,
        // but NOT gained any COL revenue. Net change should be modestly negative.
        Assert.True(hq.CashBalance < hqCashStart,
            $"HQ should not receive COL revenue. Cash before: {hqCashStart}, after: {hq.CashBalance}");
        // Specifically: monthly COL would be ~$31k from 50 settlers × $625 avg COL. Verify the
        // HQ's cash change is much less than that (i.e., didn't receive COL).
        var cashDrop = hqCashStart - hq.CashBalance;
        Assert.True(cashDrop < 20_000,
            $"HQ should not receive COL revenue. Expected modest loss, got drop of {cashDrop}.");
    }

    [Fact]
    public void Determinism_HqSweep_SameSeedSameOutcome()
    {
        Sim BuildSim()
        {
            var sim = Sim.Create(new SimConfig { Seed = 1337, StartingTreasury = 5_000_000 });
            sim.CreateResidentialZone();
            var commZone = sim.CreateCommercialZone();
            var hq = sim.PlaceCorporateHq(commZone.Id, IndustryType.Forestry, "ACo");
            hq.ConstructionTicks = hq.RequiredConstructionTicks;
            var ext = sim.PlaceIndustrialStructure(StructureType.ForestExtractor, hq.Id);
            ext.ConstructionTicks = ext.RequiredConstructionTicks;
            ext.CashBalance = 100_000;
            foreach (var (tier, count) in ext.JobSlots) ext.FilledSlots[tier] = count;
            sim.Tick(60);
            return sim;
        }

        var a = BuildSim();
        var b = BuildSim();

        var aHq = a.State.City.Structures.Values.Single(s => s.Type == StructureType.CorporateHq);
        var bHq = b.State.City.Structures.Values.Single(s => s.Type == StructureType.CorporateHq);
        Assert.Equal(aHq.CashBalance, bHq.CashBalance);
        Assert.Equal(a.State.City.TreasuryBalance, b.State.City.TreasuryBalance);
    }
}
