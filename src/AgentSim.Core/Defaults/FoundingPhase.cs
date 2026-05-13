using AgentSim.Core.Types;

namespace AgentSim.Core.Defaults;

/// <summary>
/// Founding-economy phase: first 12 months of sim get subsidies to break the bootstrap
/// chicken-and-egg (industry needs customers, customers need wages, treasury needs taxes).
///
/// Active subsidies during the founding phase:
///   - Commercial monthly utility: 50% discount
///   - Property tax (all structures): 50% discount
///   - Treasury upkeep: 50% discount on civic facility upkeep
///   - Employer wage subsidy: treasury pays 25% of gross wages for commercial/industrial workers
///   - Corporate profit tax: waived (CorporateProfit + Externality both skipped)
///
/// All subsidies snap off at month 13 (no ramp — keep it sharp and observable).
/// </summary>
public static class FoundingPhase
{
    public const int DurationMonths = 12;

    public static bool IsActive(SimState state) =>
        state.Config.FoundingPhaseEnabled && state.CurrentTick / 30 < DurationMonths;

    public const double CommercialUtilityFactor = 0.5;
    public const double PropertyTaxFactor = 0.5;
    public const double TreasuryUpkeepFactor = 0.5;
    public const double WageSubsidyFraction = 0.25;  // treasury pays this share of gross wages

    /// <summary>During the founding phase, imports come without the upcharge — so shops can
    /// survive on regional supply while population grows enough to support local industry.</summary>
    public static double EffectiveImportUpcharge(SimState state) =>
        IsActive(state) ? 0.0 : TaxRates.ImportUpcharge;
}
