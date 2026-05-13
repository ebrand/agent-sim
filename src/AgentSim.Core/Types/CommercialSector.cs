namespace AgentSim.Core.Types;

/// <summary>
/// Commercial sectors a manufacturer can service and a commercial structure can belong to.
/// Per the M16 sector-based model: there are no individual products; each manufacturer outputs
/// a generic unit tagged with the sector(s) it services, and each commercial structure is in
/// exactly one sector. Agents pay cost-of-living into sector buckets (food/retail/entertainment)
/// or save it (disposable).
/// </summary>
public enum CommercialSector
{
    Food,
    Retail,
    Entertainment,
    Construction,
}
