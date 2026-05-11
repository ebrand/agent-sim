using AgentSim.Core.Defaults;
using AgentSim.Core.Rng;
using AgentSim.Core.Sim.Mechanics;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim;

/// <summary>
/// Public API for creating and driving a simulation.
/// </summary>
public sealed class Sim
{
    public SimState State { get; }

    private Sim(SimState state)
    {
        State = state;
    }

    /// <summary>
    /// Create a new sim from configuration. The sim is dormant until the player creates the first residential zone.
    /// </summary>
    public static Sim Create(SimConfig config)
    {
        var prng = new Prng(config.Seed);

        var region = new Region
        {
            Climate = config.Climate,
            Nature = config.Nature,
        };

        // Initialize the regional reservoir to the cap with a biased distribution favoring lower tiers.
        // Alpha-1 default distribution: 40 / 30 / 20 / 10 across uneducated / primary / secondary / college.
        var totalReservoir = config.RegionalReservoirSize;
        region.AgentReservoir.Uneducated = (int)(totalReservoir * 0.40);
        region.AgentReservoir.Primary = (int)(totalReservoir * 0.30);
        region.AgentReservoir.Secondary = (int)(totalReservoir * 0.20);
        region.AgentReservoir.College = totalReservoir
            - region.AgentReservoir.Uneducated
            - region.AgentReservoir.Primary
            - region.AgentReservoir.Secondary;

        var city = new City
        {
            TreasuryBalance = config.StartingTreasury,
        };

        var state = new SimState
        {
            Region = region,
            City = city,
            Prng = prng,
        };

        return new Sim(state);
    }

    /// <summary>
    /// Create a residential zone. Triggers the bootstrap settler burst on first call.
    /// </summary>
    public Zone CreateResidentialZone(int structureCapacity = 20)
    {
        var zone = new Zone
        {
            Id = State.AllocateZoneId(),
            Type = ZoneType.Residential,
            StructureCapacity = structureCapacity,
        };
        State.City.Zones[zone.Id] = zone;

        if (!State.BootstrapFired)
        {
            BootstrapMechanic.Fire(State, zone);
            State.BootstrapFired = true;
        }

        return zone;
    }

    /// <summary>
    /// Create a commercial zone. Commercial structures can be auto-spawned within (M3+ auto-spawn TBD)
    /// or manually placed via PlaceCommercialStructure.
    /// </summary>
    public Zone CreateCommercialZone(int structureCapacity = 10)
    {
        var zone = new Zone
        {
            Id = State.AllocateZoneId(),
            Type = ZoneType.Commercial,
            StructureCapacity = structureCapacity,
        };
        State.City.Zones[zone.Id] = zone;
        return zone;
    }

    /// <summary>
    /// Manually place a commercial structure in a commercial zone. The structure begins construction
    /// (90 ticks) and is operational once construction completes.
    /// </summary>
    public Structure PlaceCommercialStructure(long commercialZoneId, StructureType type)
    {
        if (!State.City.Zones.TryGetValue(commercialZoneId, out var zone))
            throw new ArgumentException($"Zone {commercialZoneId} not found", nameof(commercialZoneId));
        if (zone.Type != ZoneType.Commercial)
            throw new ArgumentException($"Zone {commercialZoneId} is not a commercial zone", nameof(commercialZoneId));
        if (type.Category() != StructureCategory.Commercial)
            throw new ArgumentException($"{type} is not a commercial structure type", nameof(type));

        var structure = new Structure
        {
            Id = State.AllocateStructureId(),
            Type = type,
            ZoneId = zone.Id,
            ResidentialCapacity = 0,
            ConstructionTicks = 0,
            RequiredConstructionTicks = 90,
            JobSlots = Commercial.JobSlots(type).ToDictionary(kv => kv.Key, kv => kv.Value),
        };
        State.City.Structures[structure.Id] = structure;
        zone.StructureIds.Add(structure.Id);
        return structure;
    }

    /// <summary>
    /// Advance the simulation by N ticks (days).
    /// Per `time-and-pacing.md`, each tick:
    ///   1. Increment tick counter
    ///   2. Daily events (construction, hiring, aging, production, ...)
    ///   3. Continuous transactions settle (M4+)
    ///   4. Periodic settlement events on relevant days (1, 8, 15, 22, 30)
    ///   5. End-of-month emigration check (folded into day-30 settlement)
    /// </summary>
    public void Tick(int days = 1)
    {
        for (int i = 0; i < days; i++)
        {
            State.CurrentTick++;

            // Daily events
            ConstructionMechanic.AdvanceConstruction(State);
            CommercialOperationMechanic.HireForNewlyOperationalStructures(State);

            // Periodic settlements (fires on days 1, 8, 15, 22, 30)
            SettlementMechanic.RunDailySettlements(State);
        }
    }
}
