using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Monthly auto-spawn for zoned structures (Cities-Skylines-style). Residential zones auto-spawn
/// houses to absorb immigration demand; commercial zones auto-spawn sector-tagged shops to provide
/// retail capacity. Both are free of construction cost — the player pays for zoning intent, not
/// individual buildings. Player still places civic/utility/restoration/HQ structures manually
/// (those go through the M11/M17 paid-construction path).
/// </summary>
public static class ZoneAutoSpawnMechanic
{
    /// <summary>Spawn budget per zone per month (limits how fast a zone develops).</summary>
    public const int MaxSpawnsPerZonePerMonth = 1;

    public static void RunMonthly(SimState state)
    {
        foreach (var zone in state.City.Zones.Values)
        {
            if (zone.StructureIds.Count >= zone.StructureCapacity) continue;

            switch (zone.Type)
            {
                case ZoneType.Residential:
                    TrySpawnResidential(state, zone);
                    break;
                case ZoneType.Commercial:
                    TrySpawnCommercial(state, zone);
                    break;
            }
        }
    }

    private static void TrySpawnResidential(SimState state, Zone zone)
    {
        // Spawn a House when there's demand pressure: pop near current housing capacity AND the
        // reservoir has agents waiting.
        var totalHousing = state.City.Structures.Values
            .Where(s => s.Category == StructureCategory.Residential && s.Operational && !s.Inactive)
            .Sum(s => s.ResidentialCapacity);
        var pop = state.City.Population;
        var vacancy = totalHousing - pop;
        if (vacancy >= 4) return;  // enough housing
        if (state.Region.AgentReservoir.Total <= 0) return;

        SpawnFree(state, zone, StructureType.House, ResidentialOptions(state, StructureType.House));
    }

    private static void TrySpawnCommercial(SimState state, Zone zone)
    {
        if (zone.Sector is not CommercialSector sector) return;

        // Count existing sector shops in zone. Inactive shops indicate oversupply — don't spawn.
        int operationalSectorShops = 0, anySectorShops = 0;
        foreach (var s in state.City.Structures.Values)
        {
            if (s.Category != StructureCategory.Commercial) continue;
            if (s.Type == StructureType.CorporateHq) continue;
            if (s.Sector != sector) continue;
            if (s.ZoneId != zone.Id) continue;
            anySectorShops++;
            if (s.Operational && !s.Inactive) operationalSectorShops++;
        }
        if (operationalSectorShops < anySectorShops) return;

        // Demand-based spawn with 1.5× safety margin: shops need 50% more demand than
        // break-even to be allowed. Prevents oversupply that splits demand too thin.
        var sectorDemand = ComputeSectorMonthlyDemand(state, sector);
        var shopBreakEven = ComputeShopBreakEven(StructureType.Shop);
        if (shopBreakEven <= 0) return;
        const double SafetyMargin = 1.5;
        var allowedShops = (int)(sectorDemand / (shopBreakEven * SafetyMargin));
        if (anySectorShops == 0) allowedShops = Math.Max(1, allowedShops);  // seed one
        if (anySectorShops >= allowedShops) return;

        SpawnFree(state, zone, StructureType.Shop, CommercialOptions(state, StructureType.Shop, sector));
    }

    /// <summary>Total monthly dollar demand for a sector across all working-age agents.</summary>
    private static int ComputeSectorMonthlyDemand(SimState state, CommercialSector sector)
    {
        var frac = sector switch
        {
            CommercialSector.Food => CostOfLiving.FoodFraction,
            CommercialSector.Retail => CostOfLiving.RetailFraction,
            CommercialSector.Entertainment => CostOfLiving.EntertainmentFraction,
            _ => 0.0,
        };
        if (frac <= 0) return 0;

        int total = 0;
        foreach (var a in state.City.Agents.Values)
        {
            if (a.AgeDays < Demographics.WorkingAgeStartDay) continue;
            total += (int)(Wages.MonthlyWage(a.EducationTier) * frac);
        }
        return total;
    }

    /// <summary>Break-even monthly revenue for a commercial structure type: the revenue at which
    /// gross margin (revenue × (1 - GoodsCostFraction - SalesTax)) covers fixed costs.</summary>
    private static int ComputeShopBreakEven(StructureType type)
    {
        var slots = Commercial.JobSlots(type);
        var wagesTotal = slots.Sum(kv => Wages.MonthlyWage(kv.Key) * kv.Value);
        var utility = Commercial.MonthlyUtility(type);
        var propertyTax = (int)(Commercial.StructureValue(type) * TaxRates.PropertyTaxMonthly);
        var fixedCost = wagesTotal + utility + propertyTax;

        var netMarginFrac = 1.0 - CostOfLivingMechanic.CommercialGoodsCostFraction - TaxRates.SalesTax;
        if (netMarginFrac <= 0) return int.MaxValue;
        return (int)(fixedCost / netMarginFrac);
    }

    private readonly record struct SpawnInit(
        int ResidentialCapacity,
        int RequiredConstructionTicks,
        Dictionary<EducationTier, int>? JobSlots,
        CommercialSector? Sector);

    private static SpawnInit ResidentialOptions(SimState state, StructureType type) => new(
        ResidentialCapacity: Residential.Capacity(type),
        RequiredConstructionTicks: state.Config.InstantConstruction ? 0 : Residential.BuildDurationTicks,
        JobSlots: null,
        Sector: null);

    private static SpawnInit CommercialOptions(SimState state, StructureType type, CommercialSector sector) => new(
        ResidentialCapacity: 0,
        RequiredConstructionTicks: state.Config.InstantConstruction ? 0 : 7,
        JobSlots: Commercial.JobSlots(type).ToDictionary(kv => kv.Key, kv => kv.Value),
        Sector: sector);

    private static void SpawnFree(SimState state, Zone zone, StructureType type, SpawnInit init)
    {
        var (w, h) = Footprint.For(type);
        if (zone.Bounds is not ZoneBounds zb) return;
        var spot = state.Region.Tilemap.FindFreeSpotInZone(zone.Id, zb, w, h);
        if (spot is null) return;  // zone full at this size — caller will retry later

        var structure = new Structure
        {
            Id = state.AllocateStructureId(),
            Type = type,
            ZoneId = zone.Id,
            ResidentialCapacity = init.ResidentialCapacity,
            ConstructionTicks = 0,
            RequiredConstructionTicks = init.RequiredConstructionTicks,
            JobSlots = init.JobSlots ?? new Dictionary<EducationTier, int>(),
            Sector = init.Sector,
            X = spot.Value.X,
            Y = spot.Value.Y,
        };
        state.City.Structures[structure.Id] = structure;
        state.Region.Tilemap.SetStructureFootprint(structure.Id, spot.Value.X, spot.Value.Y, w, h);
        zone.StructureIds.Add(structure.Id);
    }
}
