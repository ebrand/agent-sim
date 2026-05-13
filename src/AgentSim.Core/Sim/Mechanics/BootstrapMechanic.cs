using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Bootstrap settler burst: fires once when the first residential zone is created.
/// Immigrates 50 settlers (60% uneducated, 40% primary) and spawns 13 houses for them.
/// M16: bootstrap construction is free — M17 will wire construction-sector mfg costs.
/// </summary>
public static class BootstrapMechanic
{
    public static void Fire(SimState state, Zone residentialZone)
    {
        // Spawn the houses needed for 50 settlers (4 per house = 13 houses).
        const int settlerCount = Demographics.SettlerCount;
        var capacityPerHouse = Residential.Capacity(StructureType.House);
        var housesNeeded = (settlerCount + capacityPerHouse - 1) / capacityPerHouse; // ceil
        var houses = new List<Structure>();
        for (int i = 0; i < housesNeeded; i++)
        {
            var house = SpawnInstantHouse(state, residentialZone);
            houses.Add(house);
        }

        // Immigrate 50 settlers: 30 uneducated + 20 primary.
        var uneducatedCount = (int)Math.Round(settlerCount * Demographics.SettlerUneducatedFraction);
        var primaryCount = settlerCount - uneducatedCount;

        ImmigrateSettlers(state, EducationTier.Uneducated, uneducatedCount, houses);
        ImmigrateSettlers(state, EducationTier.Primary, primaryCount, houses);
    }

    private static Structure SpawnInstantHouse(SimState state, Zone zone)
    {
        var house = new Structure
        {
            Id = state.AllocateStructureId(),
            Type = StructureType.House,
            ZoneId = zone.Id,
            ResidentialCapacity = Residential.Capacity(StructureType.House),
            ConstructionTicks = Residential.BuildDurationTicks,
            RequiredConstructionTicks = Residential.BuildDurationTicks,
        };
        state.City.Structures[house.Id] = house;
        // Place at first free 1×1 tile within the zone.
        if (zone.Bounds is ZoneBounds zb)
        {
            var spot = state.Region.Tilemap.FindFreeSpotInZone(zone.Id, zb, 1, 1);
            if (spot is not null)
            {
                house.X = spot.Value.X;
                house.Y = spot.Value.Y;
                state.Region.Tilemap.SetStructureFootprint(house.Id, spot.Value.X, spot.Value.Y, 1, 1);
            }
        }
        zone.StructureIds.Add(house.Id);
        return house;
    }

    private static void ImmigrateSettlers(
        SimState state,
        EducationTier tier,
        int count,
        IReadOnlyList<Structure> availableHouses)
    {
        for (int i = 0; i < count; i++)
        {
            state.Region.AgentReservoir.Decrement(tier);

            var ageDays = Demographics.WorkingAgeStartDay
                + state.Prng.NextInt(Demographics.SettlerMaxAgeDays - Demographics.WorkingAgeStartDay);

            var agent = new Agent
            {
                Id = state.AllocateAgentId(),
                EducationTier = tier,
                AgeDays = ageDays,
                Savings = Bootstrap.FoundersStartingSavings(tier),
            };
            state.City.Agents[agent.Id] = agent;

            var home = availableHouses.FirstOrDefault(h => h.ResidentIds.Count < h.ResidentialCapacity);
            if (home == null)
            {
                throw new InvalidOperationException(
                    "Bootstrap: no available houses for settlers — capacity calculation is wrong.");
            }
            home.ResidentIds.Add(agent.Id);
            agent.ResidenceStructureId = home.Id;
        }
    }
}
