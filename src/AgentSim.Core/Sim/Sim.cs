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
        // Default 80×80 fits ~64 ten-tile structures with breathing room — enough for the
        // 13-house settler burst plus subsequent auto-spawn growth.
        var effectiveBounds = bounds ?? AllocateZoneBounds(width: 80, height: 80);
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

        // Bootstrap: legacy mode (Config.GateBootstrapOnUtilities=false) auto-fires on first
        // residential zone — keeps existing tests and pre-built scenarios working without
        // requiring explicit Generator/Well placement first. Gated mode (Empty scenario) waits
        // for utilities to be placed; see TryFireBootstrap() called from Tick().
        if (!State.Config.GateBootstrapOnUtilities
            && type == ZoneType.Residential
            && !State.BootstrapFired)
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

    /// <summary>True when a padded box around (x, y, w, h) contains no zoned tiles and no
    /// structures. The padding leaves breathing room between auto-allocated zones so they
    /// don't end up directly abutting — important now that the distribution network only
    /// auto-spreads through touching footprints.</summary>
    private const int AutoZonePadding = 6;
    private static bool IsAreaUnzoned(Tilemap tm, int x, int y, int w, int h)
    {
        for (int dy = -AutoZonePadding; dy < h + AutoZonePadding; dy++)
        for (int dx = -AutoZonePadding; dx < w + AutoZonePadding; dx++)
        {
            int tx = x + dx, ty = y + dy;
            if (!tm.InBounds(tx, ty)) continue;
            if (tm.ZoneAt(tx, ty) is not null) return false;
            if (tm.StructureAt(tx, ty) is not null) return false;
        }
        return true;
    }

    /// <summary>Mark a corridor cell as residentially zoned. Cell is identified by its
    /// road edge, along-index, perp-index (0 = front row adjacent to road), and side
    /// (+1/-1 = left/right of FromNode→ToNode direction). Idempotent. Only PerpCell=0
    /// cells drive auto-spawn; deeper rows are visual / informational.</summary>
    public void ZoneCorridorCellResidential(long edgeId, int alongCell, int perpCell, int side)
    {
        State.City.ZonedResidentialCells.Add((edgeId, alongCell, perpCell, side));
    }

    /// <summary>Remove a corridor cell from the residential-zoned set. No-op if not zoned.</summary>
    public void UnzoneCorridorCell(long edgeId, int alongCell, int perpCell, int side)
    {
        State.City.ZonedResidentialCells.Remove((edgeId, alongCell, perpCell, side));
    }

    public bool IsCorridorCellZonedResidential(long edgeId, int alongCell, int perpCell, int side)
        => State.City.ZonedResidentialCells.Contains((edgeId, alongCell, perpCell, side));

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

        // M-LV: recompute on every placement so the heatmap reflects the new structure
        // immediately. Under-construction structures still contribute nothing (filtered
        // inside the mechanic), but operational ones will start influencing right away.
        Mechanics.LandValueMechanic.RunMonthly(State);

        // Recompute utility coverage so the UI / mechanics see which structures are now
        // served by the new layout.
        Mechanics.UtilityCoverageMechanic.Compute(State);
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
            RequiredConstructionTicks = State.Config.InstantConstruction ? 0 : 7,
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
            RequiredConstructionTicks = State.Config.InstantConstruction ? 0 : 7,
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
    /// <summary>Place a residential structure WITHOUT requiring a zone — for manual
    /// corridor-snap placement before corridor-based zoning is implemented. ZoneId is set
    /// to 0, which PlaceSpatial treats as "skip zone check".</summary>
    public Structure PlaceResidentialStructureFreeform(StructureType type, int x, int y)
    {
        if (type.Category() != StructureCategory.Residential)
            throw new ArgumentException($"{type} is not a residential structure type", nameof(type));

        ChargeConstructionCost(type);
        var structure = new Structure
        {
            Id = State.AllocateStructureId(),
            Type = type,
            ZoneId = 0,
            ResidentialCapacity = Defaults.Residential.Capacity(type),
            ConstructionTicks = 0,
            RequiredConstructionTicks = State.Config.InstantConstruction ? 0 : Defaults.Residential.BuildDurationTicks,
        };
        State.City.Structures[structure.Id] = structure;
        PlaceSpatial(structure, x, y);
        State.LogEvent(SimEventSeverity.Info, "Placement",
            $"Built {type} #{structure.Id} (freeform) at ({structure.X},{structure.Y})");
        return structure;
    }

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
            RequiredConstructionTicks = State.Config.InstantConstruction ? 0 : Defaults.Residential.BuildDurationTicks,
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
            RequiredConstructionTicks = State.Config.InstantConstruction ? 0 : 7,
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
            RequiredConstructionTicks = State.Config.InstantConstruction ? 0 : 7,
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
            RequiredConstructionTicks = State.Config.InstantConstruction ? 0 : 7,
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
            RequiredConstructionTicks = State.Config.InstantConstruction ? 0 : 7,
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
            RequiredConstructionTicks = State.Config.InstantConstruction ? 0 : 7,
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
    /// Remove a structure from the city. Clears the tilemap footprint, drops references from
    /// agents (employer / residence / enrollment), removes from the parent zone's structure
    /// list, removes from the owning HQ's owned list, and recomputes land value. Industrial
    /// chains owned by a demolished HQ are NOT cascaded — they stay but are orphaned (their
    /// OwnerHqId now points at a deleted structure; mechanics that look it up should handle
    /// that gracefully). No construction-cost refund.
    /// </summary>
    public void RemoveStructure(long structureId)
    {
        if (!State.City.Structures.TryGetValue(structureId, out var s))
            throw new ArgumentException($"Structure {structureId} not found", nameof(structureId));

        // Clear tilemap footprint.
        if (s.X >= 0 && s.Y >= 0)
        {
            var (w, h) = Footprint.For(s.Type);
            State.Region.Tilemap.ClearStructureFootprint(s.X, s.Y, w, h);
        }

        // Drop agent references — agents will find new jobs / residences via normal mechanics.
        foreach (var agent in State.City.Agents.Values)
        {
            if (agent.EmployerStructureId == structureId) agent.EmployerStructureId = null;
            if (agent.ResidenceStructureId == structureId) agent.ResidenceStructureId = null;
            if (agent.EnrolledStructureId == structureId) agent.EnrolledStructureId = null;
        }

        // Remove from parent zone.
        if (s.ZoneId != 0 && State.City.Zones.TryGetValue(s.ZoneId, out var zone))
        {
            zone.StructureIds.Remove(structureId);
        }

        // Remove from owning HQ.
        if (s.OwnerHqId is long ownerId
            && State.City.Structures.TryGetValue(ownerId, out var hq))
        {
            hq.OwnedStructureIds.Remove(structureId);
        }

        State.City.Structures.Remove(structureId);

        // Remove any network edges that referenced this structure.
        var staleEdges = new List<long>();
        foreach (var e in State.NetworkEdges.Values)
        {
            if (e.SourceStructureId == structureId || e.TargetStructureId == structureId)
                staleEdges.Add(e.Id);
        }
        foreach (var id in staleEdges) State.NetworkEdges.Remove(id);

        Mechanics.LandValueMechanic.RunMonthly(State);
        Mechanics.UtilityCoverageMechanic.Compute(State);
        State.LogEvent(SimEventSeverity.Info, "Demolition",
            $"Removed {s.Type} #{s.Id} at ({s.X},{s.Y})");
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

            // Bootstrap gate: fire the initial settler burst when the player has zoned
            // residential AND placed both a Generator and a Well. Under-construction counts —
            // the houses themselves take 7 days to build, so utilities will be operational by
            // the time the city actually needs them.
            TryFireBootstrap();

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

    /// <summary>Public wrapper for the bootstrap gate. Scenarios call this after placing the
    /// initial zone + utilities so the settler burst fires immediately (without needing to
    /// Tick). Returns true if bootstrap fired this call.</summary>
    public void CheckBootstrap() => TryFireBootstrap();

    /// <summary>Maximum Manhattan/Euclidean distance between a network edge's endpoint
    /// structures (measured center-to-center). Edges further than this are rejected.</summary>
    public const float MaxEdgeLengthTiles = 30f;

    /// <summary>Create a player-drawn distribution edge between two distributors of the same
    /// kind (ElectricityDistribution↔ElectricityDistribution, or WaterDistribution↔WaterDistribution).
    /// The network as a whole is energized when ANY distributor in the connected component is
    /// 8-neighbor adjacent to a matching producer (Generator / Well). Edges are conceptually
    /// undirected; we just store endpoints in (A, B) order for de-duping.</summary>
    public NetworkEdge ConnectEdge(long aId, long bId, NetworkKind kind)
    {
        if (aId == bId)
            throw new InvalidOperationException("Cannot connect a distributor to itself.");
        if (!State.City.Structures.TryGetValue(aId, out var a))
            throw new ArgumentException($"Structure {aId} not found", nameof(aId));
        if (!State.City.Structures.TryGetValue(bId, out var b))
            throw new ArgumentException($"Structure {bId} not found", nameof(bId));

        var expectedType = kind == NetworkKind.Power
            ? StructureType.ElectricityDistribution
            : StructureType.WaterDistribution;
        if (a.Type != expectedType || b.Type != expectedType)
            throw new InvalidOperationException(
                $"Both endpoints must be {expectedType} for a {kind} edge.");

        // Length check (center-to-center).
        var (aw, ah) = Defaults.Footprint.For(a.Type);
        var (bw, bh) = Defaults.Footprint.For(b.Type);
        float aCx = a.X + aw / 2f;
        float aCy = a.Y + ah / 2f;
        float bCx = b.X + bw / 2f;
        float bCy = b.Y + bh / 2f;
        float dist = MathF.Sqrt((aCx - bCx) * (aCx - bCx) + (aCy - bCy) * (aCy - bCy));
        if (dist > MaxEdgeLengthTiles)
            throw new InvalidOperationException(
                $"Edge would be {dist:F1} tiles long; maximum is {MaxEdgeLengthTiles}.");

        // Reject duplicate (in either direction since edges are conceptually undirected).
        foreach (var e in State.NetworkEdges.Values)
        {
            if (e.Kind != kind) continue;
            bool same = (e.SourceStructureId == aId && e.TargetStructureId == bId)
                      || (e.SourceStructureId == bId && e.TargetStructureId == aId);
            if (same) throw new InvalidOperationException("Edge already exists.");
        }

        var edge = new NetworkEdge
        {
            Id = State.AllocateEdgeId(),
            SourceStructureId = aId,
            TargetStructureId = bId,
            Kind = kind,
        };
        State.NetworkEdges[edge.Id] = edge;
        Mechanics.UtilityCoverageMechanic.Compute(State);
        State.LogEvent(SimEventSeverity.Info, "Network",
            $"Connected {kind} distributors: #{aId} ↔ #{bId} ({dist:F1} tiles)");
        return edge;
    }

    /// <summary>Place a freeform road segment between two float-precision points (in tile
    /// coordinates). Validates that both endpoints fall within the map, the segment has
    /// non-zero length, and the segment is no longer than <see cref="MaxRoadLengthTiles"/>.
    /// Returns the new Road.</summary>
    // Scales with map size — allows a full-map diagonal road, with margin.
    public const float MaxRoadLengthTiles = Tilemap.MapSize * 2f;
    public const float NodeSnapRadiusTiles = 1.0f;

    /// <summary>Player draws a "road" from start to end. The sim creates / reuses nodes at
    /// each endpoint, finds intersections with existing edges, creates intersection nodes,
    /// splits the existing edges at those points, and writes new edges from start to end
    /// split at each crossing. Returns the list of new edges generated by this call.</summary>
    public IReadOnlyList<RoadEdge> PlaceRoad(Point2 start, Point2 end,
                                             int lanesForward = 1, int lanesBackward = 1)
    {
        if (!InsideMap(start) || !InsideMap(end))
            throw new InvalidOperationException("Road endpoints must be inside the 256-tile map.");
        if (lanesForward < 0 || lanesBackward < 0)
            throw new InvalidOperationException("Lane counts cannot be negative.");
        if (lanesForward + lanesBackward < 1)
            throw new InvalidOperationException("Road must have at least one lane total.");
        float dx = end.X - start.X, dy = end.Y - start.Y;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.5f)
            throw new InvalidOperationException("Road segment is too short.");
        if (len > MaxRoadLengthTiles)
            throw new InvalidOperationException($"Road segment exceeds {MaxRoadLengthTiles}-tile max.");

        // 1. Find (or create) endpoint nodes; reuse if the player clicked very close to an
        //    existing node so the graph stays connected instead of growing parallel dupes.
        long startNodeId = FindOrCreateNode(start);
        long endNodeId = FindOrCreateNode(end);

        // 1b. T-intersection handling: if either endpoint lies on an existing edge's interior,
        //     split that edge at the endpoint node. (TryLineIntersect's strict-interior test
        //     excludes endpoint-on-segment cases, so we handle them here explicitly.)
        SplitEdgesContainingPoint(startNodeId, start);
        SplitEdgesContainingPoint(endNodeId, end);

        // 2. For every existing edge, check if it intersects the new segment in its interior.
        //    Each intersection gets a fresh node and splits both edges.
        var crossings = new List<(float t, long nodeId)>();  // t = parameter along new segment
        // Iterate over a snapshot so we can mutate during the loop.
        foreach (var existingId in new List<long>(State.RoadEdges.Keys))
        {
            if (!State.RoadEdges.TryGetValue(existingId, out var existing)) continue;
            var a = State.RoadNodes[existing.FromNodeId].Position;
            var b = State.RoadNodes[existing.ToNodeId].Position;
            if (!TryLineIntersect(start, end, a, b, out var t, out var u)) continue;

            // Build (or reuse) a node at the intersection point.
            float ix = start.X + t * (end.X - start.X);
            float iy = start.Y + t * (end.Y - start.Y);
            long crossNodeId = FindOrCreateNode(new Point2(ix, iy));
            crossings.Add((t, crossNodeId));

            // Split the existing edge into two at the crossing node.
            SplitEdgeAt(existing, crossNodeId);
        }

        // 3. Sort crossings by their position along the new segment and emit edges in order.
        crossings.Sort((a, b) => a.t.CompareTo(b.t));
        var nodeChain = new List<long> { startNodeId };
        foreach (var (_, nid) in crossings)
            if (nodeChain[nodeChain.Count - 1] != nid) nodeChain.Add(nid);
        if (nodeChain[nodeChain.Count - 1] != endNodeId) nodeChain.Add(endNodeId);

        var newEdges = new List<RoadEdge>();
        for (int i = 0; i + 1 < nodeChain.Count; i++)
        {
            if (nodeChain[i] == nodeChain[i + 1]) continue;
            var edge = new RoadEdge
            {
                Id = State.AllocateRoadEdgeId(),
                FromNodeId = nodeChain[i],
                ToNodeId = nodeChain[i + 1],
                LanesForward = lanesForward,
                LanesBackward = lanesBackward,
            };
            State.RoadEdges[edge.Id] = edge;
            newEdges.Add(edge);
        }

        State.LogEvent(SimEventSeverity.Info, "Road",
            $"Drew {lanesForward}+{lanesBackward}-lane road: {newEdges.Count} edge(s), {crossings.Count} new junction(s)");
        return newEdges;
    }

    /// <summary>Place a curved road (quadratic Bezier) from start to end with the given
    /// control point. Returns the single edge that was created. V1: no auto-split for
    /// curves — if the curve crosses an existing edge, both edges coexist visually but
    /// the graph stays disconnected at the crossing. Caller is expected to have already
    /// snapped <paramref name="start"/> to an existing node (per the curve-UX contract).</summary>
    public RoadEdge PlaceCurvedRoad(Point2 start, Point2 control, Point2 end,
                                     int lanesForward = 1, int lanesBackward = 1)
    {
        if (!InsideMap(start) || !InsideMap(end))
            throw new InvalidOperationException("Road endpoints must be inside the map.");
        if (lanesForward < 0 || lanesBackward < 0)
            throw new InvalidOperationException("Lane counts cannot be negative.");
        if (lanesForward + lanesBackward < 1)
            throw new InvalidOperationException("Road must have at least one lane total.");

        // Reuse existing nodes if the player clicked very close to one (matches straight-
        // road behavior — keeps the graph connected at endpoints).
        long startNodeId = FindOrCreateNode(start);
        long endNodeId = FindOrCreateNode(end);
        if (startNodeId == endNodeId)
            throw new InvalidOperationException("Curve endpoints resolve to the same node.");

        var edge = new RoadEdge
        {
            Id = State.AllocateRoadEdgeId(),
            FromNodeId = startNodeId,
            ToNodeId = endNodeId,
            LanesForward = lanesForward,
            LanesBackward = lanesBackward,
            ControlPoint = control,
        };
        State.RoadEdges[edge.Id] = edge;

        State.LogEvent(SimEventSeverity.Info, "Road",
            $"Drew curved {lanesForward}+{lanesBackward}-lane road (edge #{edge.Id})");
        return edge;
    }

    /// <summary>Move a road node to a new position. All edges referencing this node by id
    /// automatically reflect the new geometry on the next render pass.</summary>
    public void MoveRoadNode(long nodeId, Point2 newPosition)
    {
        if (!State.RoadNodes.TryGetValue(nodeId, out var node))
            throw new ArgumentException($"Road node {nodeId} not found", nameof(nodeId));
        if (!InsideMap(newPosition))
            throw new InvalidOperationException("New position is outside the map.");
        node.Position = newPosition;
    }

    /// <summary>Snap to an existing node within <see cref="NodeSnapRadiusTiles"/>, or create
    /// a new one at <paramref name="p"/>. Returns the chosen node's id.</summary>
    private long FindOrCreateNode(Point2 p)
    {
        float sqRadius = NodeSnapRadiusTiles * NodeSnapRadiusTiles;
        foreach (var n in State.RoadNodes.Values)
        {
            float dx = n.Position.X - p.X;
            float dy = n.Position.Y - p.Y;
            if (dx * dx + dy * dy <= sqRadius) return n.Id;
        }
        var node = new RoadNode { Id = State.AllocateRoadNodeId(), Position = p };
        State.RoadNodes[node.Id] = node;
        return node.Id;
    }

    /// <summary>Split every existing edge whose interior contains <paramref name="p"/> at
    /// the node <paramref name="nodeId"/>. Skips edges that already use this node as an
    /// endpoint, and skips edges where the point is close to an existing endpoint (so we
    /// don't make a zero-length sliver).</summary>
    private const float EdgeSplitToleranceTiles = 0.5f;
    private void SplitEdgesContainingPoint(long nodeId, Point2 p)
    {
        foreach (var edgeId in new List<long>(State.RoadEdges.Keys))
        {
            if (!State.RoadEdges.TryGetValue(edgeId, out var edge)) continue;
            if (edge.FromNodeId == nodeId || edge.ToNodeId == nodeId) continue;
            var a = State.RoadNodes[edge.FromNodeId].Position;
            var b = State.RoadNodes[edge.ToNodeId].Position;
            if (PointSegmentDistance(p, a, b) > EdgeSplitToleranceTiles) continue;
            // Ensure the split is in the interior, not near an existing endpoint.
            if (Dist2D(p, a) <= EdgeSplitToleranceTiles) continue;
            if (Dist2D(p, b) <= EdgeSplitToleranceTiles) continue;
            SplitEdgeAt(edge, nodeId);
        }
    }

    private static float PointSegmentDistance(Point2 p, Point2 a, Point2 b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-6f) return Dist2D(p, a);
        float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
        t = MathF.Max(0f, MathF.Min(1f, t));
        float px = a.X + t * dx, py = a.Y + t * dy;
        return MathF.Sqrt((p.X - px) * (p.X - px) + (p.Y - py) * (p.Y - py));
    }

    private static float Dist2D(Point2 a, Point2 b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Replace <paramref name="edge"/> with two edges sharing <paramref name="splitNodeId"/>
    /// in the middle. Lane configuration carries forward to both halves.</summary>
    private void SplitEdgeAt(RoadEdge edge, long splitNodeId)
    {
        if (edge.FromNodeId == splitNodeId || edge.ToNodeId == splitNodeId) return;
        State.RoadEdges.Remove(edge.Id);
        var left = new RoadEdge
        {
            Id = State.AllocateRoadEdgeId(),
            FromNodeId = edge.FromNodeId,
            ToNodeId = splitNodeId,
            LanesForward = edge.LanesForward,
            LanesBackward = edge.LanesBackward,
        };
        var right = new RoadEdge
        {
            Id = State.AllocateRoadEdgeId(),
            FromNodeId = splitNodeId,
            ToNodeId = edge.ToNodeId,
            LanesForward = edge.LanesForward,
            LanesBackward = edge.LanesBackward,
        };
        State.RoadEdges[left.Id] = left;
        State.RoadEdges[right.Id] = right;
    }

    /// <summary>2D line-line intersection. Returns true and the parametric position along
    /// each segment when they cross strictly in their interiors (small inset rules out
    /// endpoint touches as crossings).</summary>
    private static bool TryLineIntersect(Point2 a1, Point2 a2, Point2 b1, Point2 b2,
                                         out float t, out float u)
    {
        t = u = 0;
        float x1 = a1.X, y1 = a1.Y, x2 = a2.X, y2 = a2.Y;
        float x3 = b1.X, y3 = b1.Y, x4 = b2.X, y4 = b2.Y;
        float den = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
        if (MathF.Abs(den) < 1e-6f) return false;
        t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / den;
        u = -((x1 - x2) * (y1 - y3) - (y1 - y2) * (x1 - x3)) / den;
        return t > 0.02f && t < 0.98f && u > 0.02f && u < 0.98f;
    }

    private static bool InsideMap(Point2 p) =>
        p.X >= 0 && p.Y >= 0 && p.X <= Tilemap.MapSize && p.Y <= Tilemap.MapSize;

    /// <summary>Remove a distribution edge. Triggers a coverage recompute.</summary>
    public void RemoveEdge(long edgeId)
    {
        if (!State.NetworkEdges.Remove(edgeId)) return;
        Mechanics.UtilityCoverageMechanic.Compute(State);
        State.LogEvent(SimEventSeverity.Info, "Network", $"Disconnected edge #{edgeId}");
    }

    private void TryFireBootstrap()
    {
        if (State.BootstrapFired) return;
        if (!State.Config.GateBootstrapOnUtilities) return;  // legacy: CreateZone handles it

        Zone? firstResZone = null;
        bool hasGenerator = false;
        bool hasWell = false;

        foreach (var z in State.City.Zones.Values)
        {
            if (z.Type == ZoneType.Residential) { firstResZone = z; break; }
        }
        if (firstResZone is null) return;

        foreach (var s in State.City.Structures.Values)
        {
            if (s.Type == StructureType.Generator) hasGenerator = true;
            else if (s.Type == StructureType.Well) hasWell = true;
            if (hasGenerator && hasWell) break;
        }
        if (!hasGenerator || !hasWell) return;

        BootstrapMechanic.Fire(State, firstResZone);
        State.BootstrapFired = true;
        State.LogEvent(SimEventSeverity.Info, "Bootstrap",
            "City founded — residential zone has power + water access. Settlers arriving.");
    }
}
