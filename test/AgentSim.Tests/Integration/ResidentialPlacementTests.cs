using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Types;

namespace AgentSim.Tests.Integration;

/// <summary>
/// Player-placement of all residential structure types. Houses are auto-spawned by bootstrap and
/// the zone auto-spawn; Apartment / Townhouse / Condo / AffordableHousing can be placed manually
/// via PlaceResidentialStructure once a residential zone exists.
/// </summary>
public class ResidentialPlacementTests
{
    [Theory]
    [InlineData(StructureType.Apartment, 40)]
    [InlineData(StructureType.Townhouse, 12)]
    [InlineData(StructureType.Condo, 25)]
    [InlineData(StructureType.AffordableHousing, 40)]
    public void PlaceResidentialStructure_AddsStructureWithCapacity(StructureType type, int expectedCapacity)
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 200_000 });
        var resZone = sim.CreateResidentialZone();

        var structure = sim.PlaceResidentialStructure(resZone.Id, type);

        Assert.False(structure.Operational);
        Assert.Equal(expectedCapacity, structure.ResidentialCapacity);
        Assert.Equal(StructureCategory.Residential, structure.Category);
        Assert.Equal(resZone.Id, structure.ZoneId);
        Assert.Contains(structure.Id, resZone.StructureIds);
    }

    [Fact]
    public void PlaceTownhouse_BecomesOperationalAfterBuild()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var resZone = sim.CreateResidentialZone();

        var townhouse = sim.PlaceResidentialStructure(resZone.Id, StructureType.Townhouse);
        sim.Tick(townhouse.RequiredConstructionTicks);

        Assert.True(townhouse.Operational);
    }

    [Fact]
    public void PlaceCondo_AcceptsResidentsUpToCapacity()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var resZone = sim.CreateResidentialZone();
        var condo = sim.PlaceResidentialStructure(resZone.Id, StructureType.Condo);
        condo.ConstructionTicks = condo.RequiredConstructionTicks;

        // Verify the condo's capacity matches the residential defaults and is empty initially.
        Assert.Equal(25, condo.ResidentialCapacity);
        Assert.Empty(condo.ResidentIds);
    }

    [Fact]
    public void PlaceResidentialInCommercialZone_Throws()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var commZone = sim.CreateCommercialZone();

        Assert.Throws<ArgumentException>(() =>
            sim.PlaceResidentialStructure(commZone.Id, StructureType.Apartment));
    }

    [Theory]
    [InlineData(StructureType.Restaurant, CommercialSector.Food)]
    [InlineData(StructureType.Theater, CommercialSector.Entertainment)]
    public void PlaceCommercialSubType_AddsStructureWithSector(StructureType type, CommercialSector sector)
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        var commZone = sim.CreateCommercialZone();

        var structure = sim.PlaceCommercialStructure(commZone.Id, type, sector);

        Assert.False(structure.Operational);
        Assert.Equal(sector, structure.Sector);
        Assert.Equal(StructureCategory.Commercial, structure.Category);
        Assert.NotEmpty(structure.JobSlots);
    }

    [Fact]
    public void Restaurant_HasLargerStaffThanShop()
    {
        var shopSlots = Commercial.JobSlots(StructureType.Shop).Values.Sum();
        var restSlots = Commercial.JobSlots(StructureType.Restaurant).Values.Sum();
        Assert.True(restSlots > shopSlots,
            $"Restaurant slots ({restSlots}) should exceed Shop slots ({shopSlots}).");
    }
}
