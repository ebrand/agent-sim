namespace AgentSim.Core.Types;

/// <summary>
/// Raw materials produced by industrial extractors. Consumed by processors to make processed goods.
///
/// Wood = raw timber (logs) from ForestExtractor.
/// IronOre = mined ore from Mine.
/// Crops = harvested plant matter from Farm.
/// Rock = quarried stone.
/// Sand = pit sand.
/// Coal = mined coal.
/// Petroleum = crude oil from OilWell (M14b).
/// </summary>
public enum RawMaterial
{
    Wood,
    IronOre,
    Crops,
    Rock,
    Sand,
    Coal,
    Petroleum,
}

/// <summary>
/// Processed goods produced by industrial processors. Consumed by manufacturers (or directly by
/// commercial / utilities). M14b additions: Plastic (from Petroleum), Pulp (from Wood),
/// Glass (kept as Silicate, semantically equivalent).
/// </summary>
public enum ProcessedGood
{
    Lumber,
    Steel,
    Grain,
    Textiles,
    Aggregate,
    Silicate,
    Fuel,
    Plastic,
    Pulp,
}
