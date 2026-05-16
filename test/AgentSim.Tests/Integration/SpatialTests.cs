using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Types;

namespace AgentSim.Tests.Integration;

/// <summary>
/// Phase A spatial layer: structures have positions, zones have bounds, the tilemap tracks
/// footprints. Auto-pick fills free tiles inside zones for residential / commercial structures
/// and anywhere on the map for civic / industrial / restoration.
/// </summary>
public class SpatialTests
{
    [Fact]
    public void NewSim_HasEmptyTilemap()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var tm = sim.State.Region.Tilemap;
        Assert.Equal(Tilemap.MapSize, tm.Size);
        Assert.Null(tm.StructureAt(0, 0));
        Assert.Null(tm.ZoneAt(0, 0));
    }

    [Fact]
    public void CreateResidentialZone_MarksTilemap_WithZoneArea()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        var zone = sim.CreateResidentialZone(new ZoneBounds(10, 20, 8, 8));

        Assert.Equal(new ZoneBounds(10, 20, 8, 8), zone.Bounds);
        Assert.Equal(zone.Id, sim.State.Region.Tilemap.ZoneAt(10, 20));
        Assert.Equal(zone.Id, sim.State.Region.Tilemap.ZoneAt(17, 27));
        Assert.Null(sim.State.Region.Tilemap.ZoneAt(9, 20));  // just outside
    }

    [Fact]
    public void Bootstrap_PlacesHousesAtTilePositions()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42 });
        sim.CreateResidentialZone();

        var houses = sim.State.City.Structures.Values
            .Where(s => s.Type == StructureType.House)
            .ToList();

        Assert.Equal(13, houses.Count);
        foreach (var h in houses)
        {
            Assert.True(h.X >= 0 && h.Y >= 0, $"House {h.Id} has invalid position ({h.X},{h.Y})");
            Assert.Equal(h.Id, sim.State.Region.Tilemap.StructureAt(h.X, h.Y));
        }

        // All houses should have unique positions.
        var positions = houses.Select(h => (h.X, h.Y)).ToHashSet();
        Assert.Equal(houses.Count, positions.Count);
    }

    [Fact]
    public void PlaceServiceStructure_AutoPicks_AndMarksTilemap()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 200_000 });
        var police = sim.PlaceServiceStructure(StructureType.PoliceStation);

        Assert.True(police.X >= 0 && police.Y >= 0);
        var (w, h) = Footprint.For(StructureType.PoliceStation);
        Assert.Equal(police.Id, sim.State.Region.Tilemap.StructureAt(police.X, police.Y));
        Assert.Equal(police.Id, sim.State.Region.Tilemap.StructureAt(police.X + w - 1, police.Y + h - 1));
    }

    [Fact]
    public void PlaceCommercialStructure_AutoPicksInZone()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 2_000_000 });
        var zone = sim.CreateCommercialZone(CommercialSector.Retail, new ZoneBounds(40, 40, 10, 10));
        var shop = sim.PlaceCommercialStructure(zone.Id, StructureType.Shop, CommercialSector.Retail);

        Assert.True(shop.X >= 40 && shop.X <= 47, $"Shop X={shop.X} should be within zone bounds.");
        Assert.True(shop.Y >= 40 && shop.Y <= 47);
        Assert.Equal(shop.Id, sim.State.Region.Tilemap.StructureAt(shop.X, shop.Y));
    }

    [Fact]
    public void TwoStructuresDoNotOverlap()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 500_000 });
        var a = sim.PlaceServiceStructure(StructureType.PoliceStation);
        var b = sim.PlaceServiceStructure(StructureType.Clinic);

        // Different positions.
        Assert.NotEqual((a.X, a.Y), (b.X, b.Y));

        // No tile is claimed by both.
        var (aw, ah) = Footprint.For(StructureType.PoliceStation);
        var (bw, bh) = Footprint.For(StructureType.Clinic);
        for (int dy = 0; dy < bh; dy++)
        for (int dx = 0; dx < bw; dx++)
        {
            Assert.Equal(b.Id, sim.State.Region.Tilemap.StructureAt(b.X + dx, b.Y + dy));
        }
        for (int dy = 0; dy < ah; dy++)
        for (int dx = 0; dx < aw; dx++)
        {
            Assert.Equal(a.Id, sim.State.Region.Tilemap.StructureAt(a.X + dx, a.Y + dy));
        }
    }

    [Theory]
    [InlineData(StructureType.House)]
    [InlineData(StructureType.Shop)]
    [InlineData(StructureType.Apartment)]
    [InlineData(StructureType.PoliceStation)]
    [InlineData(StructureType.PaperMill)]
    [InlineData(StructureType.Hospital)]
    public void Footprint_IsUniform10x10(StructureType type)
    {
        // All structures use a uniform 10×10 footprint (city-block scale).
        var (w, h) = Footprint.For(type);
        Assert.Equal(10, w);
        Assert.Equal(10, h);
    }

    [Fact]
    public void Tilemap_IsAreaFree_DetectsOverlap()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 200_000 });
        var police = sim.PlaceServiceStructure(StructureType.PoliceStation);
        var (w, h) = Footprint.For(StructureType.PoliceStation);

        Assert.False(sim.State.Region.Tilemap.IsAreaFree(police.X, police.Y, w, h));
        Assert.True(sim.State.Region.Tilemap.IsAreaFree(police.X + w, police.Y, w, h));
    }
}
