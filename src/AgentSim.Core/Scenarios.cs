using AgentSim.Core.Defaults;
using AgentSim.Core.Types;
using SimClass = AgentSim.Core.Sim.Sim;
using SimConfig = AgentSim.Core.Types.SimConfig;

namespace AgentSim.Core.Calibration;

/// <summary>
/// Calibration scenarios. Per the founding-economy design: early-game cities run industry-free on
/// imports, growing pop and commercial. Only when pop is large enough does industry become viable
/// (post-founding-phase, when import upcharge kicks in).
///
/// A: industry-free founding city — survive 12 months on imports + founding subsidies.
/// B: industry-free maturing city — 24 months of growth across more sector zones.
/// C: industrialized city — B's economy + late-stage industrial chain feeding Entertainment.
/// </summary>
public static class Scenarios
{
    /// <summary>Empty map — no zones, no structures. Player must zone residential and place
    /// a Generator + Well to trigger the initial settler bootstrap, then provide services
    /// (police, education, healthcare) as the city grows. Default starting scenario.</summary>
    public static SimClass BuildEmpty()
    {
        return SimClass.Create(new SimConfig
        {
            Seed = 42,
            StartingTreasury = 1_800_000,
            GateBootstrapOnUtilities = true,
            InstantConstruction = true,
        });
    }

    public static SimClass BuildMinimal()
    {
        var sim = SimClass.Create(new SimConfig { Seed = 42, StartingTreasury = 1_800_000 });
        sim.CreateResidentialZone(structureCapacity: 30);  // room for auto-spawn housing
        sim.CreateCommercialZone(CommercialSector.Food, structureCapacity: 5);

        sim.PlaceServiceStructure(StructureType.PoliceStation);
        sim.PlaceServiceStructure(StructureType.Clinic);
        sim.PlaceEducationStructure(StructureType.PrimarySchool);
        sim.PlaceServiceStructure(StructureType.Generator);
        sim.PlaceServiceStructure(StructureType.Well);

        return sim;
    }

    public static SimClass BuildSelfSustaining()
    {
        var sim = BuildMinimal();
        var resZone = sim.State.City.Zones.Values.First(z => z.Type == ZoneType.Residential);

        sim.CreateCommercialZone(CommercialSector.Retail, structureCapacity: 5);
        sim.CreateCommercialZone(CommercialSector.Entertainment, structureCapacity: 5);

        var park = sim.PlaceRestorationStructure(StructureType.Park);
        park.ConstructionTicks = park.RequiredConstructionTicks;

        var apt = sim.PlaceResidentialStructure(resZone.Id, StructureType.Apartment);
        apt.ConstructionTicks = apt.RequiredConstructionTicks;

        // Industrial chain — player industrializes before founding phase ends (m12). Without
        // local mfg supply, post-phase import upcharges would bleed shops. With it, Entertainment
        // shops have an in-city source.
        var hqZone = sim.CreateCommercialZone();  // sectorless zone for HQ placement
        var hq = sim.PlaceCorporateHq(hqZone.Id, IndustryType.Forestry, "ForestCo");
        sim.PlaceIndustrialStructure(StructureType.ForestExtractor, hq.Id);
        sim.PlaceIndustrialStructure(StructureType.PulpMill, hq.Id);
        sim.PlaceManufacturer(StructureType.PaperMill);

        return sim;
    }

    public static SimClass BuildMidGame()
    {
        var sim = BuildSelfSustaining();

        sim.State.City.TreasuryBalance += 1_500_000;

        // Mid-game: secondary school + 2nd residential zone for further pop growth. Hospital
        // omitted — its $30k/mo upkeep needs a much larger industrial base to fund. Player
        // would add hospital only after multiple industries are paying corp tax.
        sim.PlaceEducationStructure(StructureType.SecondarySchool);

        var resZone2 = sim.CreateResidentialZone(structureCapacity: 30);
        var apt2 = sim.PlaceResidentialStructure(resZone2.Id, StructureType.Apartment);
        apt2.ConstructionTicks = apt2.RequiredConstructionTicks;

        return sim;
    }
}
