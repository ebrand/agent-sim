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
    /// Create a residential zone with explicit spatial bounds. Triggers the bootstrap settler
    /// burst on first call.
    /// </summary>
    public Zone CreateResidentialZone(ZoneBounds bounds, int structureCapacity = 20)
        => CreateZone(ZoneType.Residential, sector: null, bounds, structureCapacity);

    /// <summary>Legacy: auto-allocate bounds when none provided. Picks a 16×16 area.</summary>
    public Zone CreateResidentialZone(int structureCapacity = 20)
        => CreateZone(ZoneType.Residential, sector: null, bounds: null, structureCapacity);

    /// <summary>
    /// Create a commercial zone tagged with a sector + explicit bounds. Auto-spawned shops in
    /// this zone serve the given sector; manually placed structures ignore the sector tag.
    /// </summary>
    public Zone CreateCommercialZone(CommercialSector sector, ZoneBounds bounds, int structureCapacity = 10)
        => CreateZone(ZoneType.Commercial, sector, bounds, structureCapacity);

    /// <summary>Legacy: auto-allocate bounds.</summary>
    public Zone CreateCommercialZone(CommercialSector sector, int structureCapacity = 10)
        => CreateZone(ZoneType.Commercial, sector, bounds: null, structureCapacity);

    /// <summary>Legacy: sector-less commercial zone with auto bounds.</summary>
    public Zone CreateCommercialZone(int structureCapacity = 10)
        => CreateZone(ZoneType.Commercial, sector: null, bounds: null, structureCapacity);

    /// <summary>Shared zone-creation path. If bounds is null, auto-allocates a 16×16 area on the
    /// first free spot. Marks the tilemap so future placements can validate against zone.</summary>
    private Zone CreateZone(ZoneType type, CommercialSector? sector, ZoneBounds? bounds, int structureCapacity)
    {
        var effectiveBounds = bounds ?? AllocateZoneBounds(width: 16, height: 16);
        var zone = new Zone
        {
            Id = State.AllocateZoneId(),
            Type = type,
            StructureCapacity = structureCapacity,
            Sector = sector,
            Bounds = effectiveBounds,
        };
        State.City.Zones[zone.Id] = zone;
        State.Region.Tilemap.SetZoneArea(zone.Id, effectiveBounds.X, effectiveBounds.Y,
            effectiveBounds.Width, effectiveBounds.Height);

        if (type == ZoneType.Residential && !State.BootstrapFired)
        {
            BootstrapMechanic.Fire(State, zone);
            State.BootstrapFired = true;
        }

        State.LogEvent(SimEventSeverity.Info, "Zone",
            $"Zoned {type}{(sector is { } s ? $" ({s})" : "")} #{zone.Id} at ({effectiveBounds.X},{effectiveBounds.Y}) {effectiveBounds.Width}×{effectiveBounds.Height}");
        return zone;
    }

    /// <summary>Find a free area for a new zone of the given size. Falls back to (0,0) if scan
    /// fails (shouldn't happen on a 256×256 map).</summary>
    private ZoneBounds AllocateZoneBounds(int width, int height)
    {
        // Scan map for an area not yet zoned and not occupied by structures.
        var tm = State.Region.Tilemap;
        for (int y = 0; y <= Tilemap.MapSize - height; y++)
        for (int x = 0; x <= Tilemap.MapSize - width; x++)
        {
            if (IsAreaUnzoned(tm, x, y, width, height)) return new ZoneBounds(x, y, width, height);
        }
        return new ZoneBounds(0, 0, width, height);
    }

    private static bool IsAreaUnzoned(Tilemap tm, int x, int y, int w, int h)
    {
        for (int dy = 0; dy < h; dy++)
        for (int dx = 0; dx < w; dx++)
        {
            if (tm.ZoneAt(x + dx, y + dy) is not null) return false;
            if (tm.StructureAt(x + dx, y + dy) is not null) return false;
        }
        return true;
    }

    /// <summary>Set the structure's position (X, Y) and mark the tilemap. Throws if the spot is
    /// invalid (out of bounds, overlapping, or — for zoned structures — not in the right zone).
    /// If x or y is null, auto-picks: zoned structures fit within their zone, non-zoned anywhere.</summary>
    private void PlaceSpatial(Structure structure, int? x = null, int? y = null)
    {
        var (w, h) = Footprint.For(structure.Type);
        var tm = State.Region.Tilemap;

        (int X, int Y) pos;
        if (x.HasValue && y.HasValue)
        {
            if (!tm.IsAreaFree(x.Value, y.Value, w, h))
                throw new InvalidOperationException(
                    $"Cannot place {structure.Type} at ({x.Value},{y.Value}): tiles not free.");
            if (Footprint.IsZoned(structure.Type) && structure.ZoneId != 0
                && !tm.AreaInZone(x.Value, y.Value, w, h, structure.ZoneId))
                throw new InvalidOperationException(
                    $"{structure.Type} at ({x.Value},{y.Value}) is not entirely within zone {structure.ZoneId}.");
            pos = (x.Value, y.Value);
        }
        else
        {
            (int X, int Y)? spot;
            if (Footprint.IsZoned(structure.Type) && structure.ZoneId != 0
                && State.City.Zones.TryGetValue(structure.ZoneId, out var zone)
                && zone.Bounds is ZoneBounds zb)
            {
                spot = tm.FindFreeSpotInZone(zone.Id, zb, w, h);
            }
            else
            {
                spot = tm.FindFreeSpotAnywhere(w, h);
            }
            if (spot is null)
                throw new InvalidOperationException(
                    $"No free tile for {structure.Type} (footprint {w}×{h}).");
            pos = spot.Value;
        }

        structure.X = pos.X;
        structure.Y = pos.Y;
        tm.SetStructureFootprint(structure.Id, pos.X, pos.Y, w, h);
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
    public Structure PlaceIndustrialStructure(StructureType type, long ownerHqId, int? x = null, int? y = null)
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
        PlaceSpatial(structure, x, y);
        hq.OwnedStructureIds.Add(structure.Id);
        State.LogEvent(SimEventSeverity.Info, "Placement",
            $"Built {type} #{structure.Id} (HQ #{ownerHqId}) at ({structure.X},{structure.Y})");
        return structure;
    }

    /// <summary>
    /// Place a standalone manufacturer. M14+: manufacturers are independent industrial businesses
    /// that buy MfgInputs from any HQ's processor and sell sector-tagged units to commercial.
    /// M16: the manufacturer's serviced sectors and per-unit price are taken from its recipe defaults
    /// and stored on the structure (so the player or sim can override them later).
    /// </summary>
    public Structure PlaceManufacturer(StructureType type, int? x = null, int? y = null)
    {
        if (!Industrial.IsManufacturer(type))
            throw new ArgumentException($"{type} is not a manufacturer type", nameof(type));

        var recipe = Industrial.ManufacturerRecipe(type)
            ?? throw new InvalidOperationException($"No manufacturer recipe for {type}");

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
            MfgUnitPrice = recipe.UnitPrice,
        };
        foreach (var sector in recipe.Sectors) structure.ManufacturerSectors.Add(sector);
        State.City.Structures[structure.Id] = structure;
        PlaceSpatial(structure, x, y);
        State.LogEvent(SimEventSeverity.Info, "Placement",
            $"Built {type} #{structure.Id} at ({structure.X},{structure.Y})");
        return structure;
    }

    /// <summary>
    /// Charge the city treasury for the construction cost of a structure type. Throws if the
    /// treasury can't cover it — placement is rejected (no overdraft for new builds). Per M11
    /// design discussion. M17: after deduction, route the cost through the construction-goods chain.
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
        ConstructionGoodsMechanic.Route(State, cost);
    }

    /// <summary>
    /// Deduct construction cost from the HQ's CashBalance (industrial structures are funded by
    /// their parent HQ, not the city treasury). Throws if the HQ can't afford it. M17: after
    /// deduction, route the cost through the construction-goods chain (same as treasury-funded).
    /// </summary>
    private void ChargeHqConstructionCost(Structure hq, StructureType type)
    {
        var cost = Defaults.Construction.Cost(type);
        if (cost <= 0) return;
        if (hq.CashBalance < cost)
        {
            throw new InvalidOperationException(
                $"HQ {hq.Id} ({hq.Industry}) lacks cash for {type}: cost {cost}, available {hq.CashBalance}");
        }
        hq.CashBalance -= cost;
        ConstructionGoodsMechanic.Route(State, cost);
    }

    /// <summary>
    /// Place a residential structure in a residential zone. M-cal: the player can expand housing
    /// capacity by building Houses/Apartments/Townhouses/etc. Construction cost flows through the
    /// standard treasury path (and the M17 construction-goods chain). The structure is operational
    /// once construction completes; immigration will fill it as housing becomes available.
    /// </summary>
    public Structure PlaceResidentialStructure(long residentialZoneId, StructureType type, int? x = null, int? y = null)
    {
        if (!State.City.Zones.TryGetValue(residentialZoneId, out var zone))
            throw new ArgumentException($"Zone {residentialZoneId} not found", nameof(residentialZoneId));
        if (zone.Type != ZoneType.Residential)
            throw new ArgumentException($"Zone {residentialZoneId} is not a residential zone", nameof(residentialZoneId));
        if (type.Category() != StructureCategory.Residential)
            throw new ArgumentException($"{type} is not a residential structure type", nameof(type));

        ChargeConstructionCost(type);

        var structure = new Structure
        {
            Id = State.AllocateStructureId(),
            Type = type,
            ZoneId = zone.Id,
            ResidentialCapacity = Defaults.Residential.Capacity(type),
            ConstructionTicks = 0,
            RequiredConstructionTicks = Defaults.Residential.BuildDurationTicks,
        };
        State.City.Structures[structure.Id] = structure;
        PlaceSpatial(structure, x, y);
        zone.StructureIds.Add(structure.Id);
        State.LogEvent(SimEventSeverity.Info, "Placement",
            $"Built {type} #{structure.Id} in zone #{zone.Id} at ({structure.X},{structure.Y})");
        return structure;
    }

    /// <summary>
    /// Place a restoration structure (Park / ReforestationSite / WetlandRestoration).
    /// M15: restoration structures sit outside zones and restore climate / nature per day.
    /// </summary>
    public Structure PlaceRestorationStructure(StructureType type, int? x = null, int? y = null)
    {
        if (type != StructureType.Park
            && type != StructureType.ReforestationSite
            && type != StructureType.WetlandRestoration)
        {
            throw new ArgumentException($"{type} is not a restoration structure type", nameof(type));
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
        };
        State.City.Structures[structure.Id] = structure;
        PlaceSpatial(structure, x, y);
        State.LogEvent(SimEventSeverity.Info, "Placement",
            $"Built {type} #{structure.Id} at ({structure.X},{structure.Y})");
        return structure;
    }

    /// <summary>
    /// Place a CorporateHq inside a commercial zone for the given industry. The HQ self-funds:
    /// no deduction from the city treasury. Its starting CashBalance is 2× the cost of building
    /// out the entire vertical (see Defaults.Industry.StartingCashFor), leaving roughly half its
    /// capital in reserve after the supply chain is built. The HQ then funds its own subordinates'
    /// construction, accrues their profits each month, and pays a corporate-profit tax to the city.
    /// </summary>
    public Structure PlaceCorporateHq(long commercialZoneId, IndustryType industry, string name, int? x = null, int? y = null)
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
        PlaceSpatial(structure, x, y);
        zone.StructureIds.Add(structure.Id);
        State.LogEvent(SimEventSeverity.Info, "Placement",
            $"Built CorporateHq #{structure.Id} ({industry}, \"{name}\") in zone #{zone.Id} at ({structure.X},{structure.Y})");
        return structure;
    }

    /// <summary>
    /// Manually place an education structure (primary school / secondary school / college).
    /// Education structures sit outside zones and start a 90-tick construction. Once operational,
    /// they offer seats for school-aged agents to enroll.
    /// </summary>
    public Structure PlaceEducationStructure(StructureType type, int? x = null, int? y = null)
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
            JobSlots = Defaults.CivicEmployment.JobSlots(type).ToDictionary(kv => kv.Key, kv => kv.Value),
        };
        State.City.Structures[structure.Id] = structure;
        PlaceSpatial(structure, x, y);
        State.LogEvent(SimEventSeverity.Info, "Placement",
            $"Built {type} #{structure.Id} at ({structure.X},{structure.Y})");
        return structure;
    }

    /// <summary>
    /// Manually place a civic / healthcare / utility structure. These sit outside zones (no zone
    /// requirement per `structures.md`). 90-tick construction. M9 scope: capacity + operational
    /// state only; no operating cost, no jobs, no goods cost yet.
    /// </summary>
    public Structure PlaceServiceStructure(StructureType type, int? x = null, int? y = null)
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
            JobSlots = Defaults.CivicEmployment.JobSlots(type).ToDictionary(kv => kv.Key, kv => kv.Value),
        };
        State.City.Structures[structure.Id] = structure;
        PlaceSpatial(structure, x, y);
        State.LogEvent(SimEventSeverity.Info, "Placement",
            $"Built {type} #{structure.Id} at ({structure.X},{structure.Y})");
        return structure;
    }

    /// <summary>
    /// Manually place a commercial structure in a commercial zone. M16: each commercial structure
    /// belongs to a single CommercialSector — agents' sector COL flows only to commercials of the
    /// matching sector. CorporateHq is placed separately via PlaceCorporateHq and has no sector.
    /// </summary>
    public Structure PlaceCommercialStructure(long commercialZoneId, StructureType type, CommercialSector sector, int? x = null, int? y = null)
    {
        if (!State.City.Zones.TryGetValue(commercialZoneId, out var zone))
            throw new ArgumentException($"Zone {commercialZoneId} not found", nameof(commercialZoneId));
        if (zone.Type != ZoneType.Commercial)
            throw new ArgumentException($"Zone {commercialZoneId} is not a commercial zone", nameof(commercialZoneId));
        if (type.Category() != StructureCategory.Commercial)
            throw new ArgumentException($"{type} is not a commercial structure type", nameof(type));
        if (type == StructureType.CorporateHq)
            throw new ArgumentException("Use PlaceCorporateHq for HQ placement", nameof(type));

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
            Sector = sector,
        };
        State.City.Structures[structure.Id] = structure;
        PlaceSpatial(structure, x, y);
        zone.StructureIds.Add(structure.Id);
        State.LogEvent(SimEventSeverity.Info, "Placement",
            $"Built {type} #{structure.Id} ({sector}) in zone #{zone.Id} at ({structure.X},{structure.Y})");
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
            EnvironmentalDegradationMechanic.RunDaily(State);

            // Periodic settlements (fires on days 1, 8, 15, 22, 30)
            SettlementMechanic.RunDailySettlements(State);
        }
    }
}
