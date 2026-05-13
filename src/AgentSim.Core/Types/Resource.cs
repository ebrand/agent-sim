namespace AgentSim.Core.Types;

/// <summary>
/// Natural resources extracted from the world. Per the M16 supply-chain model: there's no
/// distinct named good between the extractor and the processor — the processor just pulls from
/// the natural resource pool via its extractor. This enum identifies which natural resource an
/// extractor pulls from, used to match extractors to processors (a Sawmill only pulls from
/// Forest extractors, etc.).
/// </summary>
public enum NaturalResource
{
    Forest,
    Ore,
    ArableLand,      // farms, ranches, cotton-farms all draw from arable land
    Stone,
    Sand,
    Petroleum,
}

/// <summary>
/// Manufacturer inputs — what processors produce and manufacturers consume. Plus a few that go
/// directly to civic structures (Fuel to oil-generator; Water as a special-case via water-pump).
///
/// Renames from prior ProcessedGood enum (per the M16 diagram):
///   Lumber  → Wood
///   Steel   → Steel    (unchanged but kept)
///   Grain   → Flour
///   Textiles → (dropped — never produced)
///   Silicate → Glass
///   New: Vegetables (from Harvester), Rock (from StoneCutter)
/// </summary>
public enum MfgInput
{
    Wood,
    Pulp,
    Steel,
    Flour,
    Vegetables,
    Meat,
    Cotton,
    Rock,
    Aggregate,
    Chalk,
    Glass,
    Plastic,
    Fuel,
    Water,
}
