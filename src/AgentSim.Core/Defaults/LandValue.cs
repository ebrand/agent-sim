using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Per-tile land-value contributions per structure type. Each operational structure stamps
/// its <see cref="Stamp"/> onto nearby tiles using a linear-falloff radial influence.
///
/// Positive stamps boost land value (amenities: commercial, services, education, healthcare,
/// utilities, restoration). Negative stamps suppress it (industrial: pollution / disamenity).
/// Residential structures contribute nothing.
///
/// Weights are placeholders for first-pass calibration — expect to retune once the heatmap
/// and downstream mechanics (rent, COL, property tax) are wired up.
/// </summary>
public static class LandValue
{
    public readonly record struct Stamp(int Radius, double Weight);

    public static Stamp StampFor(StructureType type)
    {
        // Landmark overrides — broader, stronger influence.
        switch (type)
        {
            case StructureType.Hospital: return new Stamp(18, 1.2);
            case StructureType.College: return new Stamp(18, 1.2);
            case StructureType.TownHall: return new Stamp(18, 1.0);
            case StructureType.SecondarySchool: return new Stamp(14, 0.8);
            case StructureType.Park: return new Stamp(6, 1.0);
            case StructureType.Marketplace: return new Stamp(10, 0.9);
            case StructureType.Theater: return new Stamp(10, 0.9);
            case StructureType.CorporateHq: return new Stamp(8, 0.3);
        }

        return type.Category() switch
        {
            StructureCategory.Commercial => new Stamp(8, 0.6),
            StructureCategory.Civic => new Stamp(12, 0.6),
            StructureCategory.Healthcare => new Stamp(12, 0.7),
            StructureCategory.Education => new Stamp(10, 0.6),
            StructureCategory.Utility => new Stamp(8, 0.3),
            StructureCategory.Restoration => new Stamp(8, 0.7),
            StructureCategory.IndustrialExtractor => new Stamp(12, -0.8),
            StructureCategory.IndustrialProcessor => new Stamp(14, -1.0),
            StructureCategory.IndustrialManufacturer => new Stamp(10, -0.6),
            _ => default,  // Residential and anything else: no stamp.
        };
    }

    /// <summary>Monthly property tax per unit of land value, per occupied tile.
    /// Treasury inflow = sum(LV) * this rate over tiles with a structure on them.</summary>
    public const double PropertyTaxPerLvPerTile = 6.0;

    /// <summary>Linear rent multiplier per unit of LV under the residence footprint
    /// (averaged). E.g., LV=1.0 -> rent * (1 + 0.20).</summary>
    public const double RentLvFactor = 0.20;

    /// <summary>Linear commercial revenue multiplier per unit of LV at the structure's tiles.
    /// Used as a weight in the COL distribution: higher-LV commercials capture more sector dollars.</summary>
    public const double CommercialLvFactor = 0.35;
}
