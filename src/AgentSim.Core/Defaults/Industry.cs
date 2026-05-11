using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Per-industry supply chain definition and HQ economics. An IndustryType maps to a fixed set of
/// industrial structure types it can fund + own. HQs are seeded with 2× the full-chain construction
/// cost so the player has runway to build out the whole vertical before earning anything back.
/// </summary>
public static class Industry
{
    /// <summary>Industries' constituent structure types in chain order (extractor → ... → storage).</summary>
    public static StructureType[] Chain(IndustryType industry) => industry switch
    {
        IndustryType.Forestry => new[]
        {
            StructureType.ForestExtractor,
            StructureType.Sawmill,
            StructureType.BldgSuppliesFactory,
            StructureType.Storage,
        },
        IndustryType.Mining => new[]
        {
            StructureType.Mine,
            StructureType.Smelter,
            StructureType.MetalGoodsFactory,
            StructureType.Storage,
        },
        IndustryType.Oil => new[]
        {
            StructureType.CoalMine,
            StructureType.FuelRefinery,
            StructureType.FuelStorage,
        },
        IndustryType.Stone => new[]
        {
            StructureType.Quarry,
            StructureType.AggregatePlant,
            StructureType.ConcretePlant,
            StructureType.Storage,
        },
        IndustryType.Glass => new[]
        {
            StructureType.SandPit,
            StructureType.SilicatePlant,
            StructureType.GlassWorks,
            StructureType.Storage,
        },
        IndustryType.Agriculture => new[]
        {
            StructureType.Farm,
            StructureType.Mill,
            StructureType.FoodPackingPlant,
            StructureType.ClothingFactory,
            StructureType.Storage,
        },
        _ => Array.Empty<StructureType>(),
    };

    /// <summary>Whether the given structure type may be funded / owned by an HQ of this industry.</summary>
    public static bool Allows(IndustryType industry, StructureType type)
    {
        // HouseholdFactory is intentionally cross-industry. It assembles outputs from multiple
        // verticals (lumber + metal + glass goods) into household goods, so it doesn't fit any
        // single supply-chain. M12 lets any HQ own one; a future milestone may introduce a
        // dedicated "Assembly" or "ConsumerGoods" industry type.
        if (type == StructureType.HouseholdFactory) return true;

        foreach (var t in Chain(industry))
        {
            if (t == type) return true;
        }
        return false;
    }

    /// <summary>Sum of construction costs across one full instance of every chain stage.</summary>
    public static int FullChainConstructionCost(IndustryType industry)
    {
        int sum = 0;
        foreach (var t in Chain(industry))
        {
            sum += Construction.Cost(t);
        }
        return sum;
    }

    /// <summary>
    /// Starting bank balance for a new CorporateHq: 2× the full-chain construction cost. This lets
    /// the company afford the entire vertical buildout with roughly half its capital still in
    /// reserve for early-month operating losses before profit ramps up.
    /// </summary>
    public static int StartingCashFor(IndustryType industry) => 2 * FullChainConstructionCost(industry);
}
