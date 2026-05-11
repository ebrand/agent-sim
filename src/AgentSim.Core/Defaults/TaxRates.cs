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
}
