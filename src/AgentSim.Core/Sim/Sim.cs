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

        // Initialize the regional reservoir to the cap, biased toward uneducated/primary
        // since real cities historically have more low-tier than high-tier population.
        // For alpha-1 we initialize the reservoir at the cap with a simple distribution.
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
    /// Player-driven action: create a residential zone in the city. Triggers the bootstrap settler burst on first call.
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
    /// Advance the simulation by N ticks (days).
    /// Per `time-and-pacing.md`, each tick:
    ///   1. Increment tick counter
    ///   2. Daily events (construction, aging, production, etc.)
    ///   3. Continuous transactions settle (M3+)
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

            // Periodic settlements (fires on days 1, 8, 15, 22, 30)
            SettlementMechanic.RunDailySettlements(State);
        }
    }
}
