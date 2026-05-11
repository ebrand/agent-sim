namespace AgentSim.Core.Types;

/// <summary>
/// Raw materials produced by industrial extractors. Consumed by processors to make processed goods.
/// </summary>
public enum RawMaterial
{
    Wood,
    IronOre,
    Crops,
    Rock,
    Sand,
    Coal,
}

/// <summary>
/// Processed goods produced by industrial processors. Consumed by manufacturers (or by fuel storage / utility structures for fuel).
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
}
