using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Industrial production runs each tick. M16 model:
///   - Extractor: produces raw units (single int RawUnitsInStock).
///   - Processor: pulls raw units from a matching-NaturalResource extractor; produces an MfgInput
///     into MfgInputStorage. Processor pays its own HQ (no cash flow same-HQ to upstream extractor).
///   - Manufacturer: pulls MfgInputs from any processor (paying processor's HQ at MfgInputPrice).
///     Produces a generic sector-tagged output unit into MfgOutputStock.
///
/// Overflow: there is no region spill for output. If MfgOutputStock is full, the manufacturer
/// simply doesn't produce more that tick. Commercial-sector demand drains MfgOutputStock via
/// CostOfLivingMechanic.
/// </summary>
public static class IndustrialProductionMechanic
{
    public static void RunDaily(SimState state)
    {
        ManufacturerProduce(state);
        ProcessorProduce(state);
        ExtractorProduce(state);
    }

    private static void ExtractorProduce(SimState state)
    {
        foreach (var structure in state.City.Structures.Values)
        {
            if (!Industrial.IsExtractor(structure.Type)) continue;
            if (!structure.Operational || structure.Inactive) continue;
            if (Industrial.ExtractorSource(structure.Type) is null) continue;

            var staffing = StaffingFraction(structure);
            var produced = (int)(Industrial.MaxOutputPerDay * staffing);
            if (produced <= 0) continue;

            var canStore = Math.Max(0, structure.InternalStorageCapacity - structure.RawUnitsInStock);
            var actualProduced = Math.Min(produced, canStore);
            if (actualProduced <= 0) continue;

            structure.RawUnitsInStock += actualProduced;
        }
    }

    private static void ProcessorProduce(SimState state)
    {
        foreach (var structure in state.City.Structures.Values)
        {
            if (!Industrial.IsProcessor(structure.Type)) continue;
            if (!structure.Operational || structure.Inactive) continue;
            var recipe = Industrial.ProcessorRecipe(structure.Type);
            if (recipe is null) continue;
            var (source, output) = recipe.Value;

            var staffing = StaffingFraction(structure);
            var maxProduce = (int)(Industrial.MaxOutputPerDay * staffing);
            if (maxProduce <= 0) continue;

            var currentOutput = structure.MfgInputStorage.GetValueOrDefault(output);
            var outputCapacityRemaining = Math.Max(0, structure.InternalStorageCapacity - currentOutput);
            maxProduce = Math.Min(maxProduce, outputCapacityRemaining);
            if (maxProduce <= 0) continue;

            // Pull raw units from extractors with matching NaturalResource (1:1).
            var unitsPulled = PullRawUnits(state, source, maxProduce);
            if (unitsPulled <= 0) continue;

            structure.MfgInputStorage[output] = currentOutput + unitsPulled;
        }
    }

    private static void ManufacturerProduce(SimState state)
    {
        foreach (var structure in state.City.Structures.Values)
        {
            if (!Industrial.IsManufacturer(structure.Type)) continue;
            if (!structure.Operational || structure.Inactive) continue;
            var recipe = Industrial.ManufacturerRecipe(structure.Type);
            if (recipe is null) continue;
            ProduceOne(state, structure, recipe.Value);
        }
    }

    private static void ProduceOne(SimState state, Structure mfg, Industrial.MfgRecipe recipe)
    {
        var staffing = StaffingFraction(mfg);
        var maxByStaffing = (int)(Industrial.MaxOutputPerDay * staffing);
        if (maxByStaffing <= 0) return;

        var maxByCapacity = Math.Max(0, mfg.InternalStorageCapacity - mfg.MfgOutputStock);

        int maxByInputs = int.MaxValue;
        foreach (var (input, units) in recipe.Inputs)
        {
            var available = AvailableMfgInputAcrossProcessors(state, input);
            var canProduce = available / units;
            if (canProduce < maxByInputs) maxByInputs = canProduce;
        }

        var actualOutput = Math.Min(Math.Min(maxByStaffing, maxByCapacity), maxByInputs);
        if (actualOutput <= 0) return;

        foreach (var (input, units) in recipe.Inputs)
        {
            var needed = actualOutput * units;
            var price = Industrial.MfgInputPrice(input);
            PullMfgInput(state, mfg, input, needed, price);
        }

        mfg.MfgOutputStock += actualOutput;
    }

    private static int AvailableMfgInputAcrossProcessors(SimState state, MfgInput input)
    {
        int total = 0;
        foreach (var p in state.City.Structures.Values)
        {
            if (!Industrial.IsProcessor(p.Type)) continue;
            if (!p.Operational || p.Inactive) continue;
            total += p.MfgInputStorage.GetValueOrDefault(input);
        }
        return total;
    }

    /// <summary>
    /// Credit revenue to the structure's owning HQ if any (consolidated industrial chain).
    /// Falls back to the structure itself for standalone (manufacturer) or orphan industrial.
    /// </summary>
    internal static void CreditRevenueToHqOrSelf(SimState state, Structure structure, int amount)
    {
        if (structure.OwnerHqId is long hqId && state.City.Structures.TryGetValue(hqId, out var hq))
        {
            hq.CashBalance += amount;
            hq.MonthlyRevenue += amount;
        }
        else
        {
            structure.CashBalance += amount;
            structure.MonthlyRevenue += amount;
        }
    }

    internal static void ChargeExpenseToHqOrSelf(SimState state, Structure structure, int amount)
    {
        if (structure.OwnerHqId is long hqId && state.City.Structures.TryGetValue(hqId, out var hq))
        {
            hq.CashBalance -= amount;
            hq.MonthlyExpenses += amount;
        }
        else
        {
            structure.CashBalance -= amount;
            structure.MonthlyExpenses += amount;
        }
    }

    // === Helpers ===

    private static double StaffingFraction(Structure s)
    {
        var totalSlots = s.JobSlots.Values.Sum();
        if (totalSlots == 0) return 0;
        var filled = s.FilledSlots.Values.Sum();
        return (double)filled / totalSlots;
    }

    /// <summary>
    /// Pull up to `unitsRequested` raw units from any extractor with the matching NaturalResource.
    /// No cash flow — same-HQ goods xfer for the consolidated industrial chain.
    /// </summary>
    private static int PullRawUnits(SimState state, NaturalResource source, int unitsRequested)
    {
        var unitsPulled = 0;
        foreach (var extractor in state.City.Structures.Values)
        {
            if (!Industrial.IsExtractor(extractor.Type)) continue;
            if (!extractor.Operational || extractor.Inactive) continue;
            if (Industrial.ExtractorSource(extractor.Type) != source) continue;
            if (unitsPulled >= unitsRequested) break;

            var available = extractor.RawUnitsInStock;
            if (available <= 0) continue;

            var qty = Math.Min(available, unitsRequested - unitsPulled);
            extractor.RawUnitsInStock = available - qty;
            extractor.MonthlySalesUnits += qty;
            unitsPulled += qty;
        }
        return unitsPulled;
    }

    /// <summary>
    /// Pull MfgInput units from any processor's buffer. The manufacturer is standalone; it pays
    /// the processor's owning HQ (or the processor itself for orphan processors).
    /// </summary>
    private static int PullMfgInput(SimState state, Structure buyer, MfgInput input, int unitsRequested, int unitPrice)
    {
        var unitsPulled = 0;
        foreach (var processor in state.City.Structures.Values)
        {
            if (!Industrial.IsProcessor(processor.Type)) continue;
            if (!processor.Operational || processor.Inactive) continue;
            if (unitsPulled >= unitsRequested) break;

            var available = processor.MfgInputStorage.GetValueOrDefault(input);
            if (available <= 0) continue;

            var qty = Math.Min(available, unitsRequested - unitsPulled);
            var total = qty * unitPrice;

            buyer.CashBalance -= total;
            buyer.MonthlyExpenses += total;
            CreditRevenueToHqOrSelf(state, processor, total);

            processor.MfgInputStorage[input] = available - qty;
            processor.MonthlySalesUnits += qty;
            unitsPulled += qty;
        }
        return unitsPulled;
    }
}
