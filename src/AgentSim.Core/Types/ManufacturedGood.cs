namespace AgentSim.Core.Types;

/// <summary>
/// The seven manufactured goods sold by industrial storage to commercial / regional.
/// Bldg supplies, concrete, metal goods, and glass goods are consumed during construction.
/// </summary>
public enum ManufacturedGood
{
    Household,
    BldgSupplies,
    MetalGoods,
    Food,
    Clothing,
    Concrete,
    GlassGoods,
    Paper,
    Books,        // M14e: from Printer consuming Paper
}
