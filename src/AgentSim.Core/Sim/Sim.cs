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

        // Initialize the regional reservoir with a biased distribution favoring lower tiers.
        // Alpha-1 default distribution: 40 / 30 / 20 / 10 across uneducated / primary / secondary / college.
        var totalReservoir = config.InitialReservoirSize;
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
            Config = config,
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
    /// Manually place an industrial structure under a CorporateHq's ownership. Industrial structures
    /// sit outside zones. Construction cost is deducted from the HQ's CashBalance (not the city
    /// treasury) — per M12 design: each industrial chain is owned and funded by a parent commercial
    /// venture. The structure begins 7-tick construction and is operational once it completes.
    /// </summary>
    /// <summary>
    /// Place an extractor or processor under a CorporateHq's ownership. M14: HQs own only the
    /// extractor + processor stages. Manufacturers are placed independently via PlaceManufacturer.
    /// </summary>
    public Structure PlaceIndustrialStructure(StructureType type, long ownerHqId)
    {
        if (!Industrial.IsIndustrial(type))
            throw new ArgumentException($"{type} is not an industrial structure type", nameof(type));
        if (!Industrial.IsExtractor(type) && !Industrial.IsProcessor(type))
            throw new ArgumentException(
                $"{type} is not an extractor or processor — manufacturers are placed via PlaceManufacturer", nameof(type));

        if (!State.City.Structures.TryGetValue(ownerHqId, out var hq))
            throw new ArgumentException($"HQ {ownerHqId} not found", nameof(ownerHqId));
        if (hq.Type != StructureType.CorporateHq)
            throw new ArgumentException($"Structure {ownerHqId} is not a CorporateHq", nameof(ownerHqId));
        if (hq.Industry is not IndustryType hqIndustry)
            throw new InvalidOperationException($"HQ {ownerHqId} has no Industry set");
        if (!Defaults.Industry.Allows(hqIndustry, type))
            throw new InvalidOperationException(
                $"HQ {ownerHqId} is a {hqIndustry} industry; cannot fund a {type}");

        // Charge construction cost to the HQ, not the city treasury.
        ChargeHqConstructionCost(hq, type);

        var structure = new Structure
        {
            Id = State.AllocateStructureId(),
            Type = type,
            ZoneId = 0,  // industrial sits outside zones
            ResidentialCapacity = 0,
            ConstructionTicks = 0,
            RequiredConstructionTicks = 7,
            JobSlots = Industrial.JobSlots(type).ToDictionary(kv => kv.Key, kv => kv.Value),
            InternalStorageCapacity = Industrial.InternalStorageCapacity,
            OwnerHqId = ownerHqId,
        };
        State.City.Structures[structure.Id] = structure;
        hq.OwnedStructureIds.Add(structure.Id);
        return structure;
    }

    /// <summary>
    /// Place a standalone manufacturer. M14: manufacturers are independent industrial businesses
    /// that buy processed goods from any HQ's processor and sell manufactured goods to commercial
    /// (or to the region as overflow). They have their own P&L: city treasury pays for construction;
    /// the manufacturer's own CashBalance covers ongoing operations.
    /// </summary>
    public Structure PlaceManufacturer(StructureType type)
    {
        if (!Industrial.IsManufacturer(type))
            throw new ArgumentException($"{type} is not a manufacturer type", nameof(type));

        ChargeConstructionCost(type);

        var structure = new Structure
        {
            Id = State.AllocateStructureId(),
            Type = type,
            ZoneId = 0,
            ResidentialCapacity = 0,
            ConstructionTicks = 0,
            RequiredConstructionTicks = 7,
            JobSlots = Industrial.JobSlots(type).ToDictionary(kv => kv.Key, kv => kv.Value),
            InternalStorageCapacity = Industrial.InternalStorageCapacity,
            // No OwnerHqId — manufacturer is standalone.
        };
        State.City.Structures[structure.Id] = structure;
        return structure;
    }

    /// <summary>
    /// Charge the city treasury for the construction cost of a structure type. Throws if the
    /// treasury can't cover it — placement is rejected (no overdraft for new builds). Per M11
    /// design discussion.
    /// </summary>
    private void ChargeConstructionCost(StructureType type)
    {
        var cost = Defaults.Construction.Cost(type);
        if (cost <= 0) return;
        if (State.City.TreasuryBalance < cost)
        {
            throw new InvalidOperationException(
                $"Insufficient treasury to construct {type}: cost {cost}, available {State.City.TreasuryBalance}");
        }
        State.City.TreasuryBalance -= cost;
    }

    /// <summary>
    /// Deduct construction cost from the HQ's CashBalance (industrial structures are funded by
    /// their parent HQ, not the city treasury). Throws if the HQ can't afford it.
    /// </summary>
    private static void ChargeHqConstructionCost(Structure hq, StructureType type)
    {
        var cost = Defaults.Construction.Cost(type);
        if (cost <= 0) return;
        if (hq.CashBalance < cost)
        {
            throw new InvalidOperationException(
                $"HQ {hq.Id} ({hq.Industry}) lacks cash for {type}: cost {cost}, available {hq.CashBalance}");
        }
        hq.CashBalance -= cost;
    }

    /// <summary>
    /// Place a CorporateHq inside a commercial zone for the given industry. The HQ self-funds:
    /// no deduction from the city treasury. Its starting CashBalance is 2× the cost of building
    /// out the entire vertical (see Defaults.Industry.StartingCashFor), leaving roughly half its
    /// capital in reserve after the supply chain is built. The HQ then funds its own subordinates'
    /// construction, accrues their profits each month, and pays a corporate-profit tax to the city.
    /// </summary>
    public Structure PlaceCorporateHq(long commercialZoneId, IndustryType industry, string name)
    {
        if (!State.City.Zones.TryGetValue(commercialZoneId, out var zone))
            throw new ArgumentException($"Zone {commercialZoneId} not found", nameof(commercialZoneId));
        if (zone.Type != ZoneType.Commercial)
            throw new ArgumentException($"Zone {commercialZoneId} is not a commercial zone", nameof(commercialZoneId));

        var structure = new Structure
        {
            Id = State.AllocateStructureId(),
            Type = StructureType.CorporateHq,
            ZoneId = zone.Id,
            ResidentialCapacity = 0,
            ConstructionTicks = 0,
            RequiredConstructionTicks = 7,
            JobSlots = Commercial.JobSlots(StructureType.CorporateHq).ToDictionary(kv => kv.Key, kv => kv.Value),
            Name = name,
            Industry = industry,
            CashBalance = Defaults.Industry.StartingCashFor(industry),
        };
        State.City.Structures[structure.Id] = structure;
        zone.StructureIds.Add(structure.Id);
        return structure;
    }

    /// <summary>
    /// Manually place an education structure (primary school / secondary school / college).
    /// Education structures sit outside zones and start a 90-tick construction. Once operational,
    /// they offer seats for school-aged agents to enroll.
    /// </summary>
    public Structure PlaceEducationStructure(StructureType type)
    {
        if (type != StructureType.PrimarySchool
            && type != StructureType.SecondarySchool
            && type != StructureType.College)
        {
            throw new ArgumentException($"{type} is not an education structure type", nameof(type));
        }

        ChargeConstructionCost(type);

        var structure = new Structure
        {
            Id = State.AllocateStructureId(),
            Type = type,
            ZoneId = 0,
            ResidentialCapacity = 0,
            ConstructionTicks = 0,
            RequiredConstructionTicks = 7,
            SeatCapacity = Defaults.Education.SeatCapacityFor(type),
        };
        State.City.Structures[structure.Id] = structure;
        return structure;
    }

    /// <summary>
    /// Manually place a civic / healthcare / utility structure. These sit outside zones (no zone
    /// requirement per `structures.md`). 90-tick construction. M9 scope: capacity + operational
    /// state only; no operating cost, no jobs, no goods cost yet.
    /// </summary>
    public Structure PlaceServiceStructure(StructureType type)
    {
        var category = type.Category();
        if (category != StructureCategory.Civic
            && category != StructureCategory.Healthcare
            && category != StructureCategory.Utility)
        {
            throw new ArgumentException($"{type} is not a civic/healthcare/utility structure type", nameof(type));
        }

        ChargeConstructionCost(type);

        var structure = new Structure
        {
            Id = State.AllocateStructureId(),
            Type = type,
            ZoneId = 0,
            ResidentialCapacity = 0,
            ConstructionTicks = 0,
            RequiredConstructionTicks = 7,
            ServiceCapacity = Defaults.Services.CapacityFor(type),
        };
        State.City.Structures[structure.Id] = structure;
        return structure;
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

        ChargeConstructionCost(type);

        var structure = new Structure
        {
            Id = State.AllocateStructureId(),
            Type = type,
            ZoneId = zone.Id,
            ResidentialCapacity = 0,
            ConstructionTicks = 0,
            RequiredConstructionTicks = 7,
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
            // M10: game-over halts the simulation. Further ticks are no-ops until the flag is
            // cleared (e.g. UI reset). Per `feedback-loops.md`, this fires after 6 consecutive
            // months of partial-pay (upkeep underfunded).
            if (State.City.GameOver) return;

            State.CurrentTick++;

            // Daily events
            AgingMechanic.RunDaily(State);  // agents age; deaths happen at lifespan
            ConstructionMechanic.AdvanceConstruction(State);
            CommercialOperationMechanic.HireForNewlyOperationalStructures(State);
            IndustrialProductionMechanic.RunDaily(State);
            EducationMechanic.RunDaily(State);

            // Periodic settlements (fires on days 1, 8, 15, 22, 30)
            SettlementMechanic.RunDailySettlements(State);
        }
    }
}
