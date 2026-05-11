using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Industrial production runs each tick. Walks the chain backwards (downstream pulls from
/// upstream), producing output and transferring money along the way:
///
///   manufacturer ← processor ← extractor
///   storage ← manufacturer
///
/// Each structure's daily output = MaxOutputPerDay × (jobs_filled / total_slots), limited by:
///   - Input availability (for processors/manufacturers)
///   - Output buffer space (internal storage capacity)
///
/// Money flows accompany goods movement:
///   - Processor pays extractor at raw material price
///   - Manufacturer pays processor at processed good price
///   - Storage pays manufacturer at 80% of manufactured good price (20% storage margin)
///   - Storage sells overflow to regional treasury at full manufactured price (regional treasury is functionally infinite)
/// </summary>
public static class IndustrialProductionMechanic
{
    /// <summary>Runs each tick. Order: storage pull from manufacturer, manufacturer pull from processor, processor pull from extractor, extractor produces.</summary>
    public static void RunDaily(SimState state)
    {
        // Process downstream first (pull-based).
        StorageDrawFromManufacturers(state);
        ManufacturerProduce(state);
        ProcessorProduce(state);
        ExtractorProduce(state);
        StorageSellOverflowToRegion(state);
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
            var (input, inputUnits, output) = recipe.Value;

            var staffing = StaffingFraction(structure);
            var maxOutput = (int)(Industrial.MaxOutputPerDay * staffing);
            if (maxOutput <= 0) continue;

            // Limited by output capacity in own buffer
            var currentOutput = structure.ManufacturedStorage.GetValueOrDefault(output);
            var outputCapacityRemaining = Math.Max(0, structure.InternalStorageCapacity - currentOutput);
            maxOutput = Math.Min(maxOutput, outputCapacityRemaining);
            if (maxOutput <= 0) continue;

            // We need `inputUnits` × `maxOutput` processed goods to produce maxOutput units.
            var inputNeeded = inputUnits * maxOutput;
            var processedPrice = Industrial.ProcessedGoodPrice(input);
            var inputPulled = PullProcessedGood(state, structure, input, inputNeeded, processedPrice);

            var actualOutput = inputPulled / inputUnits;  // floor: only whole units produced
            if (actualOutput <= 0) continue;

            structure.ManufacturedStorage[output] = currentOutput + actualOutput;
        }
    }

    // === Storage: pull manufactured goods from manufacturers (pays 80% of mfg price) ===
    private static void StorageDrawFromManufacturers(SimState state)
    {
        var storages = state.City.Structures.Values
            .Where(s => Industrial.IsStorage(s.Type) && s.Type == StructureType.Storage
                        && s.Operational && !s.Inactive)
            .ToList();
        if (storages.Count == 0) return;

        // M13: same-HQ goods transfer — no cash exchange. Goods just move from manufacturer to
        // storage (both owned by the same parent company; the transfer is bookkeeping).
        foreach (var storage in storages)
        {
            foreach (var manufacturer in state.City.Structures.Values
                         .Where(s => Industrial.IsManufacturer(s.Type)
                                     && s.Operational && !s.Inactive))
            {
                var keysSnapshot = manufacturer.ManufacturedStorage.Keys.ToList();
                foreach (var good in keysSnapshot)
                {
                    var available = manufacturer.ManufacturedStorage[good];
                    if (available <= 0) continue;

                    var stored = storage.ManufacturedStorage.GetValueOrDefault(good);
                    var canAccept = Math.Max(0, storage.InternalStorageCapacity - stored);
                    var qty = Math.Min(available, canAccept);
                    if (qty <= 0) continue;

                    manufacturer.ManufacturedStorage[good] = available - qty;
                    storage.ManufacturedStorage[good] = stored + qty;
                }
            }
        }
    }

    // === Storage sells overflow to regional treasury at full price ===
    // M13: revenue accrues to the owning HQ, not the storage itself.
    private static void StorageSellOverflowToRegion(SimState state)
    {
        foreach (var storage in state.City.Structures.Values)
        {
            if (storage.Type != StructureType.Storage) continue;
            if (!storage.Operational || storage.Inactive) continue;

            var keysSnapshot = storage.ManufacturedStorage.Keys.ToList();
            foreach (var good in keysSnapshot)
            {
                var qty = storage.ManufacturedStorage[good];
                if (qty <= 0) continue;

                var unitPrice = Industrial.ManufacturedGoodPrice(good);
                var sellPrice = unitPrice * qty;

                CreditRevenueToHqOrSelf(state, storage, sellPrice);
                storage.ManufacturedStorage[good] = 0;
                // Regional treasury is functionally infinite — goods accumulate, no balance tracked.
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
    /// Pull up to `unitsRequested` processed-good units from any processor's buffer. M13: goods
    /// only — no cash transfer between same-HQ industrial structures.
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
            processor.ProcessedStorage[good] = available - qty;
            unitsPulled += qty;
        }
        return unitsPulled;
    }
}
