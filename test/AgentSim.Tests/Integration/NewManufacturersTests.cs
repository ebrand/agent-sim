using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Types;

namespace AgentSim.Tests.Integration;

/// <summary>
/// Brewery (Flour + Glass → Entertainment), ElectronicsFactory (Steel + Glass + Pulp → Retail),
/// PharmaPlant (Plastic + Chalk → Food). Each has a unique input set vs. existing manufacturers.
/// </summary>
public class NewManufacturersTests
{
    [Theory]
    [InlineData(StructureType.Brewery, CommercialSector.Entertainment, 90)]
    [InlineData(StructureType.ElectronicsFactory, CommercialSector.Retail, 250)]
    [InlineData(StructureType.PharmaPlant, CommercialSector.Food, 200)]
    public void NewMfg_RecipeIsDefined_AndSectorAndPriceMatch(
        StructureType type, CommercialSector expectedSector, int expectedPrice)
    {
        var recipe = Industrial.ManufacturerRecipe(type);
        Assert.NotNull(recipe);
        Assert.Contains(expectedSector, recipe.Value.Sectors);
        Assert.Equal(expectedPrice, recipe.Value.UnitPrice);
        Assert.True(recipe.Value.Inputs.Count > 0);
    }

    [Fact]
    public void Brewery_ConsumesFlourAndGlass()
    {
        var recipe = Industrial.ManufacturerRecipe(StructureType.Brewery);
        Assert.NotNull(recipe);
        Assert.Contains(recipe.Value.Inputs, i => i.Input == MfgInput.Flour);
        Assert.Contains(recipe.Value.Inputs, i => i.Input == MfgInput.Glass);
    }

    [Fact]
    public void ElectronicsFactory_ConsumesSteelGlassPulp()
    {
        var recipe = Industrial.ManufacturerRecipe(StructureType.ElectronicsFactory);
        Assert.NotNull(recipe);
        var inputs = recipe.Value.Inputs.Select(i => i.Input).ToHashSet();
        Assert.Contains(MfgInput.Steel, inputs);
        Assert.Contains(MfgInput.Glass, inputs);
        Assert.Contains(MfgInput.Pulp, inputs);
    }

    [Fact]
    public void PharmaPlant_ConsumesPlasticAndChalk()
    {
        var recipe = Industrial.ManufacturerRecipe(StructureType.PharmaPlant);
        Assert.NotNull(recipe);
        var inputs = recipe.Value.Inputs.Select(i => i.Input).ToHashSet();
        Assert.Contains(MfgInput.Plastic, inputs);
        Assert.Contains(MfgInput.Chalk, inputs);
    }

    [Theory]
    [InlineData(StructureType.Brewery)]
    [InlineData(StructureType.ElectronicsFactory)]
    [InlineData(StructureType.PharmaPlant)]
    public void NewMfg_CanBePlacedAsManufacturer(StructureType type)
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });

        var mfg = sim.PlaceManufacturer(type);

        Assert.Equal(StructureCategory.IndustrialManufacturer, mfg.Category);
        Assert.True(mfg.MfgUnitPrice > 0);
        Assert.NotEmpty(mfg.ManufacturerSectors);
    }

    [Fact]
    public void Brewery_ProducesWhenInputsAvailable()
    {
        // Brewery needs Flour (from Mill, Agriculture industry) + Glass (from SilicatePlant, Glass industry).
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 5_000_000 });
        var commZone = sim.CreateCommercialZone();

        // Agriculture chain for Flour
        var agHq = sim.PlaceCorporateHq(commZone.Id, IndustryType.Agriculture, "AgriCo");
        agHq.ConstructionTicks = agHq.RequiredConstructionTicks;
        agHq.CashBalance = 5_000_000;
        FastBuild(sim, StructureType.Farm, agHq.Id);
        FastBuild(sim, StructureType.Mill, agHq.Id);

        // Glass chain
        var glassHq = sim.PlaceCorporateHq(commZone.Id, IndustryType.Glass, "GlassCo");
        glassHq.ConstructionTicks = glassHq.RequiredConstructionTicks;
        glassHq.CashBalance = 5_000_000;
        FastBuild(sim, StructureType.SandPit, glassHq.Id);
        FastBuild(sim, StructureType.SilicatePlant, glassHq.Id);

        var brewery = sim.PlaceManufacturer(StructureType.Brewery);
        brewery.ConstructionTicks = brewery.RequiredConstructionTicks;
        foreach (var (tier, count) in brewery.JobSlots) brewery.FilledSlots[tier] = count;

        sim.Tick(30);

        Assert.True(brewery.MfgOutputStock > 0,
            $"Brewery should produce output units. Got {brewery.MfgOutputStock}.");
    }

    private static Structure FastBuild(Sim sim, StructureType type, long hqId)
    {
        var s = sim.PlaceIndustrialStructure(type, hqId);
        s.ConstructionTicks = s.RequiredConstructionTicks;
        foreach (var (tier, count) in s.JobSlots) s.FilledSlots[tier] = count;
        return s;
    }
}
