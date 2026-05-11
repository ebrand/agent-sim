namespace AgentSim.Core.Types;

/// <summary>
/// The vertical industry that a CorporateHq owns. Each industry maps to a specific supply chain
/// of industrial structure types (extractor → processor → manufacturer → storage). An HQ can
/// only fund/own structures inside its declared industry. Per M12 design discussion.
/// </summary>
public enum IndustryType
{
    Forestry,
    Mining,
    Oil,
    Stone,
    Glass,
    Agriculture,
}
