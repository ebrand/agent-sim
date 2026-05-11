using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Types;

namespace AgentSim.Tests.Integration;

/// <summary>
/// M11: user-placeable structures cost treasury cash to construct. Placement is rejected when
/// the treasury can't cover the cost. Residential is exempt (auto-spawned in zones, not user-placed).
/// </summary>
public class ConstructionCostTests
{
    [Fact]
    public void PlaceServiceStructure_DeductsConstructionCost()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        var before = sim.State.City.TreasuryBalance;

        sim.PlaceServiceStructure(StructureType.PoliceStation);

        Assert.Equal(before - Construction.PoliceStation, sim.State.City.TreasuryBalance);
    }

    [Fact]
    public void PlaceEducationStructure_DeductsConstructionCost()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        var before = sim.State.City.TreasuryBalance;

        sim.PlaceEducationStructure(StructureType.PrimarySchool);

        Assert.Equal(before - Construction.PrimarySchool, sim.State.City.TreasuryBalance);
    }

    [Fact]
    public void PlaceCommercialStructure_DeductsConstructionCost()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        sim.CreateResidentialZone();
        var commZone = sim.CreateCommercialZone();
        var before = sim.State.City.TreasuryBalance;

        sim.PlaceCommercialStructure(commZone.Id, StructureType.Shop);

        Assert.Equal(before - Construction.Shop, sim.State.City.TreasuryBalance);
    }

    [Fact]
    public void PlaceIndustrialStructure_DeductsConstructionCost()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000_000 });
        var before = sim.State.City.TreasuryBalance;

        sim.PlaceIndustrialStructure(StructureType.ForestExtractor);

        Assert.Equal(before - Construction.ForestExtractor, sim.State.City.TreasuryBalance);
    }

    [Fact]
    public void PlaceServiceStructure_ThrowsWhenTreasuryInsufficient()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000 });

        Assert.Throws<InvalidOperationException>(() =>
            sim.PlaceServiceStructure(StructureType.Hospital));
    }

    [Fact]
    public void PlaceCommercialStructure_ThrowsWhenTreasuryInsufficient()
    {
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000 });
        var commZone = sim.CreateCommercialZone();

        Assert.Throws<InvalidOperationException>(() =>
            sim.PlaceCommercialStructure(commZone.Id, StructureType.Marketplace));
    }

    [Fact]
    public void RejectedPlacement_DoesNotMutateTreasuryOrStructures()
    {
        // If placement is rejected, no side effects: treasury unchanged, no structure created.
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 1_000 });
        var treasuryBefore = sim.State.City.TreasuryBalance;
        var structureCountBefore = sim.State.City.Structures.Count;

        Assert.Throws<InvalidOperationException>(() =>
            sim.PlaceServiceStructure(StructureType.Hospital));

        Assert.Equal(treasuryBefore, sim.State.City.TreasuryBalance);
        Assert.Equal(structureCountBefore, sim.State.City.Structures.Count);
    }

    [Fact]
    public void Residential_AutoSpawn_NoConstructionCharge()
    {
        // Bootstrap creates 13 houses for 50 settlers. Residential is exempt from construction cost
        // (auto-spawned, not user-placed). Treasury should be unchanged by the residential zone.
        var sim = Sim.Create(new SimConfig { Seed = 42, StartingTreasury = 500_000 });
        var before = sim.State.City.TreasuryBalance;

        sim.CreateResidentialZone();  // spawns 13 houses

        Assert.Equal(before, sim.State.City.TreasuryBalance);
        // And the houses exist.
        var houses = sim.State.City.Structures.Values.Count(s => s.Type == StructureType.House);
        Assert.Equal(13, houses);
    }

    [Fact]
    public void ModestFoundingCity_Affordable_FromDefaultStartingTreasury()
    {
        // Calibration target: $1.8M default treasury covers the modest 5-structure city
        // ($1.15M construction) with room to spare for early-game operations.
        var sim = Sim.Create(new SimConfig { Seed = 42 });  // default StartingTreasury
        sim.CreateResidentialZone();

        sim.PlaceServiceStructure(StructureType.PoliceStation);
        sim.PlaceServiceStructure(StructureType.Clinic);
        sim.PlaceEducationStructure(StructureType.PrimarySchool);
        sim.PlaceServiceStructure(StructureType.Generator);
        sim.PlaceServiceStructure(StructureType.Well);

        var totalConstruction = Construction.PoliceStation + Construction.Clinic
            + Construction.PrimarySchool + Construction.Generator + Construction.Well;
        Assert.Equal(1_150_000, totalConstruction);  // sanity-check on the calibration
        Assert.Equal(1_800_000 - totalConstruction, sim.State.City.TreasuryBalance);
        Assert.True(sim.State.City.TreasuryBalance > 0);
    }

    [Fact]
    public void Cost_TableIsConsistentWithUpkeepRatio()
    {
        // Sanity-check: construction is ~10× monthly upkeep for treasury-funded structures.
        // (Allow a ±20% band to accommodate rounding.)
        var pairs = new (StructureType type, int upkeep, int construction)[]
        {
            (StructureType.PoliceStation, Upkeep.PoliceStation, Construction.PoliceStation),
            (StructureType.FireStation, Upkeep.FireStation, Construction.FireStation),
            (StructureType.TownHall, Upkeep.TownHall, Construction.TownHall),
            (StructureType.Clinic, Upkeep.Clinic, Construction.Clinic),
            (StructureType.Hospital, Upkeep.Hospital, Construction.Hospital),
            (StructureType.PrimarySchool, Upkeep.PrimarySchool, Construction.PrimarySchool),
            (StructureType.SecondarySchool, Upkeep.SecondarySchool, Construction.SecondarySchool),
            (StructureType.College, Upkeep.College, Construction.College),
            (StructureType.Generator, Upkeep.Generator, Construction.Generator),
            (StructureType.Well, Upkeep.Well, Construction.Well),
            (StructureType.AffordableHousing, Upkeep.AffordableHousing, Construction.AffordableHousing),
        };

        foreach (var (type, upkeep, construction) in pairs)
        {
            var ratio = (double)construction / upkeep;
            Assert.InRange(ratio, 8.0, 12.0);
        }
    }
}
