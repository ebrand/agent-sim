using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Per-industry supply chain definition and HQ economics. An IndustryType maps to a fixed set of
/// industrial structure types it can fund + own. HQs are seeded with 2× the full-chain construction
/// cost so the player has runway to build out the whole vertical before earning anything back.
/// </summary>
public static class Industry
{
    /// <summary>
    /// Industries' constituent structure types: extractor + processor only. Per M14: manufacturers
    /// are standalone industrial businesses (not HQ-owned) that buy processed goods from any
    /// available HQ chain. Storage has been removed; manufacturers sell directly to commercial
    /// or overflow to the region.
    /// </summary>
    public static StructureType[] Chain(IndustryType industry) => industry switch
    {
        IndustryType.Forestry => new[]
        {
            StructureType.ForestExtractor,
            StructureType.Sawmill,
        },
        IndustryType.Mining => new[]
        {
            StructureType.Mine,
            StructureType.Smelter,
        },
        IndustryType.Oil => new[]
        {
            StructureType.CoalMine,
            StructureType.FuelRefinery,
        },
        IndustryType.Stone => new[]
        {
            StructureType.Quarry,
            StructureType.AggregatePlant,
        },
        IndustryType.Glass => new[]
        {
            StructureType.SandPit,
            StructureType.SilicatePlant,
        },
        IndustryType.Agriculture => new[]
        {
            StructureType.Farm,
            StructureType.Mill,
        },
        _ => Array.Empty<StructureType>(),
    };

    /// <summary>
    /// Whether the given structure type may be funded / owned by an HQ of this industry.
    /// Per M14, HQs own only their extractor + processor. Manufacturers and storage are placed
    /// independently (not under HQ ownership).
    /// </summary>
    public static bool Allows(IndustryType industry, StructureType type)
    {
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
