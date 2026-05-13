using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Types;

namespace AgentSim.Tests.Integration;

/// <summary>
/// M14c/M16: Agriculture diversification — an Agriculture HQ can fund Farm/Mill (flour),
/// Ranch/Slaughterhouse (meat), and CottonFarm/Ginnery (cotton). Manufacturers downstream
/// consume the resulting MfgInputs.
/// </summary>
public class AgricultureDiversificationTests
{
    private static (Sim sim, Structure hq) NewAgricultureSim()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 10_000_000 });
        var commZone = sim.CreateCommercialZone();
        var hq = sim.PlaceCorporateHq(commZone.Id, IndustryType.Agriculture, "AgriCo");
        hq.ConstructionTicks = hq.RequiredConstructionTicks;
        hq.CashBalance = 5_000_000;
        return (sim, hq);
    }

    private static Structure FastBuild(Sim sim, StructureType type, long hqId)
    {
        var s = sim.PlaceIndustrialStructure(type, hqId);
        s.ConstructionTicks = s.RequiredConstructionTicks;
        foreach (var (tier, count) in s.JobSlots) s.FilledSlots[tier] = count;
        return s;
    }

    [Fact]
    public void AgricultureHq_CanFundFarmAndMill()
    {
        var (sim, hq) = NewAgricultureSim();
        var farm = FastBuild(sim, StructureType.Farm, hq.Id);
        var mill = FastBuild(sim, StructureType.Mill, hq.Id);

        Assert.Equal(hq.Id, farm.OwnerHqId);
        Assert.Equal(hq.Id, mill.OwnerHqId);
    }

    [Fact]
    public void AgricultureHq_CanDiversify_Ranch_CottonFarm_Slaughterhouse_Ginnery()
    {
        var (sim, hq) = NewAgricultureSim();

        FastBuild(sim, StructureType.Farm, hq.Id);
        FastBuild(sim, StructureType.Mill, hq.Id);
        FastBuild(sim, StructureType.Ranch, hq.Id);
        FastBuild(sim, StructureType.Slaughterhouse, hq.Id);
        FastBuild(sim, StructureType.CottonFarm, hq.Id);
        FastBuild(sim, StructureType.Ginnery, hq.Id);

        Assert.Equal(6, hq.OwnedStructureIds.Count);
    }

    [Fact]
    public void ForestryHq_RejectsRanch()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 10_000_000 });
        var commZone = sim.CreateCommercialZone();
        var hq = sim.PlaceCorporateHq(commZone.Id, IndustryType.Forestry, "ForestCo");
        hq.CashBalance = 5_000_000;

        Assert.Throws<InvalidOperationException>(() =>
            sim.PlaceIndustrialStructure(StructureType.Ranch, hq.Id));
    }

    [Fact]
    public void Ranch_ProducesRawUnits()
    {
        var (sim, hq) = NewAgricultureSim();
        var ranch = FastBuild(sim, StructureType.Ranch, hq.Id);

        sim.Tick(1);

        Assert.True(ranch.RawUnitsInStock > 0,
            $"Ranch should produce raw units. Got {ranch.RawUnitsInStock}.");
    }

    [Fact]
    public void Slaughterhouse_ProducesMeat_FromRanchUnits()
    {
        var (sim, hq) = NewAgricultureSim();
        FastBuild(sim, StructureType.Ranch, hq.Id);
        var slaughter = FastBuild(sim, StructureType.Slaughterhouse, hq.Id);

        sim.Tick(5);

        var meat = slaughter.MfgInputStorage.GetValueOrDefault(MfgInput.Meat);
        Assert.True(meat > 0, $"Slaughterhouse should produce Meat. Got {meat}.");
    }

    [Fact]
    public void CottonFarm_ProducesRawUnits()
    {
        var (sim, hq) = NewAgricultureSim();
        var cottonFarm = FastBuild(sim, StructureType.CottonFarm, hq.Id);

        sim.Tick(1);

        Assert.True(cottonFarm.RawUnitsInStock > 0,
            $"CottonFarm should produce raw units. Got {cottonFarm.RawUnitsInStock}.");
    }

    [Fact]
    public void Ginnery_ProducesCotton_FromCottonFarm()
    {
        var (sim, hq) = NewAgricultureSim();
        FastBuild(sim, StructureType.CottonFarm, hq.Id);
        var ginnery = FastBuild(sim, StructureType.Ginnery, hq.Id);

        sim.Tick(5);

        var cotton = ginnery.MfgInputStorage.GetValueOrDefault(MfgInput.Cotton);
        Assert.True(cotton > 0, $"Ginnery should produce Cotton. Got {cotton}.");
    }

    [Fact]
    public void ClothingFactory_RequiresCottonAndPlastic()
    {
        var (sim, agHq) = NewAgricultureSim();
        FastBuild(sim, StructureType.CottonFarm, agHq.Id);
        FastBuild(sim, StructureType.Ginnery, agHq.Id);

        var oilHq = sim.PlaceCorporateHq(sim.State.City.Zones.Values.First(z => z.Type == ZoneType.Commercial).Id,
            IndustryType.Oil, "OilCo");
        oilHq.ConstructionTicks = oilHq.RequiredConstructionTicks;
        oilHq.CashBalance = 5_000_000;
        FastBuild(sim, StructureType.OilWell, oilHq.Id);
        FastBuild(sim, StructureType.PlasticPlant, oilHq.Id);

        sim.State.City.TreasuryBalance += 2_000_000;
        var clothing = sim.PlaceManufacturer(StructureType.ClothingFactory);
        clothing.ConstructionTicks = clothing.RequiredConstructionTicks;
        foreach (var (tier, count) in clothing.JobSlots) clothing.FilledSlots[tier] = count;

        sim.Tick(30);

        Assert.True(clothing.MfgOutputStock > 0,
            $"ClothingFactory should produce with both Cotton and Plastic chains in place. Got {clothing.MfgOutputStock}.");
    }

    [Fact]
    public void FoodPackingPlant_RequiresFlourMeatPlasticGlass()
    {
        var (sim, agHq) = NewAgricultureSim();
        FastBuild(sim, StructureType.Farm, agHq.Id);
        FastBuild(sim, StructureType.Mill, agHq.Id);
        FastBuild(sim, StructureType.Ranch, agHq.Id);
        FastBuild(sim, StructureType.Slaughterhouse, agHq.Id);

        var oilHq = sim.PlaceCorporateHq(sim.State.City.Zones.Values.First(z => z.Type == ZoneType.Commercial).Id,
            IndustryType.Oil, "OilCo");
        oilHq.ConstructionTicks = oilHq.RequiredConstructionTicks;
        oilHq.CashBalance = 5_000_000;
        FastBuild(sim, StructureType.OilWell, oilHq.Id);
        FastBuild(sim, StructureType.PlasticPlant, oilHq.Id);

        var glassHq = sim.PlaceCorporateHq(sim.State.City.Zones.Values.First(z => z.Type == ZoneType.Commercial).Id,
            IndustryType.Glass, "GlassCo");
        glassHq.ConstructionTicks = glassHq.RequiredConstructionTicks;
        glassHq.CashBalance = 5_000_000;
        FastBuild(sim, StructureType.SandPit, glassHq.Id);
        FastBuild(sim, StructureType.SilicatePlant, glassHq.Id);

        sim.State.City.TreasuryBalance += 2_000_000;
        var packer = sim.PlaceManufacturer(StructureType.FoodPackingPlant);
        packer.ConstructionTicks = packer.RequiredConstructionTicks;
        foreach (var (tier, count) in packer.JobSlots) packer.FilledSlots[tier] = count;

        sim.Tick(30);

        Assert.True(packer.MfgOutputStock > 0,
            $"FoodPackingPlant should produce with full input chain. Got {packer.MfgOutputStock}.");
    }

    [Fact]
    public void AgricultureHq_StartingCash_CoversFullChain()
    {
        var (sim, hq) = NewAgricultureSim();
        var fresh = sim.PlaceCorporateHq(sim.State.City.Zones.Values.First().Id,
            IndustryType.Agriculture, "AgriCo2");
        var expected = 2 * (Construction.Cost(StructureType.Farm)
            + Construction.Cost(StructureType.Mill)
            + Construction.Cost(StructureType.Ranch)
            + Construction.Cost(StructureType.Slaughterhouse)
            + Construction.Cost(StructureType.CottonFarm)
            + Construction.Cost(StructureType.Ginnery));
        Assert.Equal(expected, fresh.CashBalance);
    }
}
