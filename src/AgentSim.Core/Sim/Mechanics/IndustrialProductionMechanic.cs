using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Industrial production runs each tick. Walks the chain backwards (downstream pulls from upstream):
///
///   manufacturer ← processor ← extractor
///
/// Each structure's daily output = MaxOutputPerDay × (jobs_filled / total_slots), limited by:
///   - Input availability
///   - Output buffer space (internal storage capacity)
///
/// M13 consolidated HQ chain (extractor + processor under one HQ): no cash flow between same-HQ
/// subs; goods xfer only. Revenue routes to the HQ via CreditRevenueToHqOrSelf.
///
/// M14: Manufacturer is a standalone industrial entity (not HQ-owned). When a Mfg pulls processed
/// goods from a processor, it pays the processor's HQ at the processed-good price — real cash
/// transaction between two independent economic entities. Storage has been removed; manufacturers
/// sell directly to commercial (via CostOfLivingMechanic) or to the region as overflow.
/// </summary>
public static class IndustrialProductionMechanic
{
    /// <summary>Runs each tick. Order matters: pull downstream first.</summary>
    public static void RunDaily(SimState state)
    {
        ManufacturerProduce(state);
        ProcessorProduce(state);
        ExtractorProduce(state);
        ProcessorSellOverflowToRegion(state);
        ManufacturerSellOverflowToRegion(state);
    }

    // === Extractor: produce raw material into own buffer ===
    private static void ExtractorProduce(SimState state)
    {
        foreach (var structure in state.City.Structures.Values)
        {
            if (!Industrial.IsExtractor(structure.Type)) continue;
            if (!structure.Operational || structure.Inactive) continue;
            var output = Industrial.ExtractorOutput(structure.Type);
            if (output is null) continue;

            var staffing = StaffingFraction(structure);
            var produced = (int)(Industrial.MaxOutputPerDay * staffing);
            if (produced <= 0) continue;

            // Limited by remaining internal capacity
            var currentStored = structure.RawStorage.GetValueOrDefault(output.Value);
            var canStore = Math.Max(0, structure.InternalStorageCapacity - currentStored);
            var actualProduced = Math.Min(produced, canStore);
            if (actualProduced <= 0) continue;

            structure.RawStorage[output.Value] = currentStored + actualProduced;
        }
    }

    // === Processor: pull raw materials from any extractor's buffer, produce processed good ===
    private static void ProcessorProduce(SimState state)
    {
        foreach (var structure in state.City.Structures.Values)
        {
            if (!Industrial.IsProcessor(structure.Type)) continue;
            if (!structure.Operational || structure.Inactive) continue;
            var recipe = Industrial.ProcessorRecipe(structure.Type);
            if (recipe is null) continue;
            var (input, output) = recipe.Value;

            var staffing = StaffingFraction(structure);
            var maxProduce = (int)(Industrial.MaxOutputPerDay * staffing);
            if (maxProduce <= 0) continue;

            // Limited by output capacity in processor's own buffer
            var currentOutput = structure.ProcessedStorage.GetValueOrDefault(output);
            var outputCapacityRemaining = Math.Max(0, structure.InternalStorageCapacity - currentOutput);
            maxProduce = Math.Min(maxProduce, outputCapacityRemaining);
            if (maxProduce <= 0) continue;

            // Pull raw materials from extractors (1:1 ratio for processors)
            var rawPrice = Industrial.RawMaterialPrice(input);
            var unitsPulled = PullRawMaterial(state, structure, input, maxProduce, rawPrice);
            if (unitsPulled <= 0) continue;

            structure.ProcessedStorage[output] = currentOutput + unitsPulled;
        }
    }

    // === Manufacturer: pull processed goods from processors, produce manufactured good ===
    private static void ManufacturerProduce(SimState state)
    {
        foreach (var structure in state.City.Structures.Values)
        {
            if (!Industrial.IsManufacturer(structure.Type)) continue;
            if (!structure.Operational || structure.Inactive) continue;
            var recipe = Industrial.ManufacturerRecipe(structure.Type);
            if (recipe is null) continue;
            var (inputs, output) = recipe.Value;

            var staffing = StaffingFraction(structure);
            var maxByStaffing = (int)(Industrial.MaxOutputPerDay * staffing);
            if (maxByStaffing <= 0) continue;

            var currentOutput = structure.ManufacturedStorage.GetValueOrDefault(output);
            var outputCapacityRemaining = Math.Max(0, structure.InternalStorageCapacity - currentOutput);
            var maxByCapacity = outputCapacityRemaining;

            // M14b multi-input: bottleneck = whichever input has the least available stock relative
            // to its consumption rate per output unit. Sum across all operational processors.
            int maxByInputs = int.MaxValue;
            foreach (var (input, units) in inputs)
            {
                var available = AvailableProcessedAcrossProcessors(state, input);
                var canProduce = available / units;
                if (canProduce < maxByInputs) maxByInputs = canProduce;
            }

            var actualOutput = Math.Min(Math.Min(maxByStaffing, maxByCapacity), maxByInputs);
            if (actualOutput <= 0) continue;

            // Pull exactly what's needed for actualOutput units. Each pull does a real cross-entity
            // cash transaction (Mfg pays processor's HQ at the processed-good price).
            foreach (var (input, units) in inputs)
            {
                var needed = actualOutput * units;
                var price = Industrial.ProcessedGoodPrice(input);
                PullProcessedGood(state, structure, input, needed, price);
            }

            structure.ManufacturedStorage[output] = currentOutput + actualOutput;
        }
    }

    /// <summary>Sum the available stock of a processed good across all operational processors.</summary>
    private static int AvailableProcessedAcrossProcessors(SimState state, ProcessedGood good)
    {
        int total = 0;
        foreach (var p in state.City.Structures.Values)
        {
            if (!Industrial.IsProcessor(p.Type)) continue;
            if (!p.Operational || p.Inactive) continue;
            total += p.ProcessedStorage.GetValueOrDefault(good);
        }
        return total;
    }

    // === Processor sells overflow to regional treasury at full processed price ===
    // M14: if processors fill up their buffers (no manufacturer pulls), they sell direct to the
    // region. Revenue routes to the processor's owning HQ.
    private static void ProcessorSellOverflowToRegion(SimState state)
    {
        foreach (var processor in state.City.Structures.Values)
        {
            if (!Industrial.IsProcessor(processor.Type)) continue;
            if (!processor.Operational || processor.Inactive) continue;

            var keysSnapshot = processor.ProcessedStorage.Keys.ToList();
            foreach (var good in keysSnapshot)
            {
                var qty = processor.ProcessedStorage[good];
                if (qty <= 0) continue;

                // Only spill to region when the buffer is nearly full — otherwise let manufacturers
                // have first crack via the daily ManufacturerProduce pull.
                if (qty < processor.InternalStorageCapacity) continue;

                var unitPrice = Industrial.ProcessedGoodPrice(good);
                var sellPrice = unitPrice * qty;

                CreditRevenueToHqOrSelf(state, processor, sellPrice);
                processor.ProcessedStorage[good] = 0;
                state.Region.ProcessedGoodsReservoir.TryGetValue(good, out var existing);
                state.Region.ProcessedGoodsReservoir[good] = existing + qty;
            }
        }
    }

    // === Manufacturer sells overflow to regional treasury at full manufactured price ===
    // M14: manufacturers spill their own buffer to the region when full. Manufacturers are
    // standalone (no HQ ownership), so revenue accrues to their own CashBalance.
    private static void ManufacturerSellOverflowToRegion(SimState state)
    {
        foreach (var manufacturer in state.City.Structures.Values)
        {
            if (!Industrial.IsManufacturer(manufacturer.Type)) continue;
            if (!manufacturer.Operational || manufacturer.Inactive) continue;

            var keysSnapshot = manufacturer.ManufacturedStorage.Keys.ToList();
            foreach (var good in keysSnapshot)
            {
                var qty = manufacturer.ManufacturedStorage[good];
                if (qty <= 0) continue;
                if (qty < manufacturer.InternalStorageCapacity) continue;  // only spill at full

                var unitPrice = Industrial.ManufacturedGoodPrice(good);
                var sellPrice = unitPrice * qty;

                manufacturer.CashBalance += sellPrice;
                manufacturer.MonthlyRevenue += sellPrice;
                manufacturer.ManufacturedStorage[good] = 0;
                state.Region.GoodsReservoir.TryGetValue(good, out var existing);
                state.Region.GoodsReservoir[good] = existing + qty;
            }
        }
    }

    /// <summary>
    /// Credit revenue to the structure's owning HQ if it has one (M13 consolidated industrial
    /// model). Falls back to the structure itself for legacy / orphan industrial structures.
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

    /// <summary>
    /// Charge an expense to the structure's owning HQ if it has one, else to the structure itself.
    /// M13 consolidated industrial model.
    /// </summary>
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
    /// Pull up to `unitsRequested` raw material units from any extractor's buffer. M13: goods only —
    /// no cash transfer between same-HQ industrial structures. The unitPrice parameter is retained
    /// for signature compatibility but unused.
    /// </summary>
    private static int PullRawMaterial(SimState state, Structure buyer, RawMaterial material, int unitsRequested, int unitPrice)
    {
        var unitsPulled = 0;
        foreach (var extractor in state.City.Structures.Values)
        {
            if (!Industrial.IsExtractor(extractor.Type)) continue;
            if (!extractor.Operational || extractor.Inactive) continue;
            if (unitsPulled >= unitsRequested) break;

            var available = extractor.RawStorage.GetValueOrDefault(material);
            if (available <= 0) continue;

            var qty = Math.Min(available, unitsRequested - unitsPulled);
            extractor.RawStorage[material] = available - qty;
            unitsPulled += qty;
        }
        return unitsPulled;
    }

    /// <summary>
    /// Pull processed-good units from any processor's buffer. M14: a standalone Manufacturer pays
    /// the processor's owning HQ at unitPrice per unit — a real inter-entity cash transaction.
    /// (For legacy orphan/non-HQ processors, money still flows to the processor itself.)
    /// </summary>
    private static int PullProcessedGood(SimState state, Structure buyer, ProcessedGood good, int unitsRequested, int unitPrice)
    {
        var unitsPulled = 0;
        foreach (var processor in state.City.Structures.Values)
        {
            if (!Industrial.IsProcessor(processor.Type)) continue;
            if (!processor.Operational || processor.Inactive) continue;
            if (unitsPulled >= unitsRequested) break;

            var available = processor.ProcessedStorage.GetValueOrDefault(good);
            if (available <= 0) continue;

            var qty = Math.Min(available, unitsRequested - unitsPulled);
            var total = qty * unitPrice;

            // Money: buyer (manufacturer) pays the processor's HQ. Buyer is standalone — its own
            // CashBalance is decremented. Processor's HQ collects revenue.
            buyer.CashBalance -= total;
            buyer.MonthlyExpenses += total;
            CreditRevenueToHqOrSelf(state, processor, total);

            // Goods: processor ships to buyer's input pool (no buffer on buyer side for inputs;
            // the production caller used the pulled units immediately).
            processor.ProcessedStorage[good] = available - qty;
            unitsPulled += qty;
        }
        return unitsPulled;
    }
}
