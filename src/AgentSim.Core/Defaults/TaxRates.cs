namespace AgentSim.Core.Defaults;

public static class TaxRates
{
    /// <summary>Flat income tax rate (5% per `economy.md`).</summary>
    public const double IncomeTax = 0.05;

    /// <summary>Flat sales tax on commercial revenue (3% per `economy.md`).</summary>
    public const double SalesTax = 0.03;

    /// <summary>Property tax as % of structure value per month (0.5% per `economy.md`).</summary>
    public const double PropertyTaxMonthly = 0.005;

    /// <summary>Import upcharge over local price (25% per `economy.md`).</summary>
    public const double ImportUpcharge = 0.25;

    /// <summary>
    /// Corporate profit tax — applied to the amount swept from industrial subsidiaries to a
    /// CorporateHq each month. Replaces sales tax for HQs (no double taxation on the same revenue).
    /// M12: 25%.
    /// </summary>
    public const double CorporateProfit = 0.25;

    /// <summary>
    /// Externality tax — per-industry environmental tax applied to the same swept profit.
    /// Heavier-impact industries (Oil, Mining) pay more; lighter footprints (Agriculture) pay
    /// less. The city/region uses this revenue conceptually for ecological mitigation. Applied
    /// alongside corporate profit tax, so Oil pays 25% + 25% = 50% total on profit.
    /// </summary>
    public static double Externality(Types.IndustryType industry) => industry switch
    {
        Types.IndustryType.Agriculture => 0.05,  // light footprint
        Types.IndustryType.Forestry => 0.10,     // deforestation
        Types.IndustryType.Stone => 0.10,        // quarry impact
        Types.IndustryType.Glass => 0.12,        // energy + silica
        Types.IndustryType.Mining => 0.20,       // heavy land/water disruption
        Types.IndustryType.Oil => 0.25,          // highest — fossil extraction + emissions
        _ => 0.0,
    };
}
