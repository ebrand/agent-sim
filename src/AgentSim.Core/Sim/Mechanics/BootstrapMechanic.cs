using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Bootstrap settler burst: fires once when the first residential zone is created.
/// Adds bootstrap goods stock to the regional goods reservoir, immigrates 50 settlers
/// (60% uneducated, 40% primary), and spawns 13 houses for them.
/// Bootstrap construction is instant (special case) — settlers move into completed homes.
/// </summary>
public static class BootstrapMechanic
{
    public static void Fire(SimState state, Zone residentialZone)
    {
        // 1. Add bootstrap stock to the regional goods reservoir.
        foreach (var (good, count) in Bootstrap.BootstrapGoodsStock)
        {
            state.Region.GoodsReservoir.TryGetValue(good, out var existing);
            state.Region.GoodsReservoir[good] = existing + count;
        }

        // 2. Spawn the houses needed for 50 settlers (4 per house = 13 houses).
        const int settlerCount = Demographics.SettlerCount;
        var capacityPerHouse = Residential.Capacity(StructureType.House);
        var housesNeeded = (settlerCount + capacityPerHouse - 1) / capacityPerHouse; // ceil
        var houses = new List<Structure>();
        for (int i = 0; i < housesNeeded; i++)
        {
            var house = SpawnInstantHouse(state, residentialZone);
            houses.Add(house);
        }

        // 3. Consume bootstrap goods for the houses we just spawned.
        var houseRecipe = Residential.ConstructionRecipe(StructureType.House);
        foreach (var (good, perHouse) in houseRecipe)
        {
            var totalNeeded = perHouse * housesNeeded;
            state.Region.GoodsReservoir[good] -= totalNeeded;
            if (state.Region.GoodsReservoir[good] < 0)
            {
                throw new InvalidOperationException(
                    $"Bootstrap goods stock insufficient for {good}: needed {totalNeeded}, " +
                    $"had only {state.Region.GoodsReservoir[good] + totalNeeded}");
            }
        }

        // 4. Immigrate 50 settlers: 30 uneducated + 20 primary.
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
            ConstructionTicks = Residential.BuildDurationTicks, // instant: construction already complete
            RequiredConstructionTicks = Residential.BuildDurationTicks,
        };
        state.City.Structures[house.Id] = house;
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
            // Pull from regional reservoir
            state.Region.AgentReservoir.Decrement(tier);

            // Random working-age (between working-age start and lifespan)
            var ageDays = Demographics.WorkingAgeStartDay
                + state.Prng.NextInt(Demographics.LifespanDays - Demographics.WorkingAgeStartDay);

            var agent = new Agent
            {
                Id = state.AllocateAgentId(),
                EducationTier = tier,
                AgeDays = ageDays,
                Savings = Bootstrap.StartingSavings(tier),
            };
            state.City.Agents[agent.Id] = agent;

            // Place in the first house with vacancy
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
