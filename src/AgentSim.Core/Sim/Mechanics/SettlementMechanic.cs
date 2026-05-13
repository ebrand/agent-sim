using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Monthly settlement. Per the simplified-accounting model (Option A): every money flow happens
/// on day 30. Days 1, 8, 15, 22 are no-op economically. This eliminates intra-month transient
/// states, the "outflows-before-inflows" sub-ordering rule, and wage-installment splitting.
///
/// Day 30 order:
///   1. Treasury upkeep (sets UpkeepFundingFraction; treasury outflow)
///   2. Agent rent + utilities (agent → treasury)
///   3. Structure utilities + property tax (commercial / industrial → treasury)
///   4. COL spending (agent → commercial → storage/region/imports); updates commercial revenue
///   5. Sales tax (commercial → treasury; depends on revenue from step 4)
///   6. Wages (employer → agent net of income tax; tax → treasury)
///   7. Profitability check + monthly accumulator reset
///   8. Insolvency emigration (agents whose Savings went negative net)
///   9. Service-pressure emigration (worst-of services below threshold)
///   10. Births
///   11. Bankruptcy clock + game-over check
///
/// Industrial chain (extractor → processor → manufacturer → storage) continues to flow daily —
/// that's a goods/production cycle, not a settlement event, and its cash transfers are between
/// industrial structures only (not agents or treasury).
/// </summary>
public static class SettlementMechanic
{
    /// <summary>Returns 1..30 for the day-of-month given a 1-indexed tick (tick 1 = day 1).</summary>
    public static int DayOfMonth(int currentTick)
    {
        if (currentTick <= 0) return 0;
        var d = currentTick % 30;
        return d == 0 ? 30 : d;
    }

    public static void RunDailySettlements(SimState state)
    {
        if (DayOfMonth(state.CurrentTick) == 30)
        {
            RunMonthlySettlement(state);
        }
    }

    public static void RunMonthlySettlement(SimState state)
    {
        // 1. Treasury outflow: monthly upkeep for treasury-funded structures. Sets
        //    UpkeepFundingFraction, which the satisfaction calc consumes in step 9.
        TreasuryUpkeepMechanic.PayMonthlyUpkeep(state);

        // 2. Agent outflows: rent + utilities → treasury.
        foreach (var agent in state.City.Agents.Values)
        {
            PayRent(state, agent);
            PayAgentUtilities(state, agent);
        }

        // 3. Structure outflows: utilities + property tax (commercial / industrial → treasury).
        foreach (var structure in state.City.Structures.Values)
        {
            if (!structure.Operational || structure.Inactive) continue;
            if (structure.Category == StructureCategory.Commercial)
            {
                PayCommercialUtilities(state, structure);
                PayPropertyTax(state, structure);
            }
            else if (Industrial.IsIndustrial(structure.Type))
            {
                PayIndustrialUtilities(state, structure);
                PayPropertyTax(state, structure);
            }
        }

        // 4. COL: agents pay commercial (agent outflow, commercial inflow). Commercial then pays
        //    storage/region/imports for the goods backing this consumption. Updates MonthlyRevenue.
        CostOfLivingMechanic.RunMonthlyCol(state);

        // 4b. Variable industrial workforce: lay off excess workers BEFORE wages are paid this
        //     month, so over-supplied industrial chains don't burn full-staff wages on idle capacity.
        //     Uses MonthlySalesUnits accumulated during the month (including this tick's COL pulls).
        ProductionStaffingMechanic.RunMonthly(state);

        // 5. Sales tax (commercial → treasury). Uses MonthlyRevenue accumulated in step 4.
        //    M12: CorporateHq is exempt from sales tax — its swept-up profit is taxed separately
        //    via the corporate-profit tax in CorporateProfitMechanic to avoid double taxation.
        foreach (var structure in state.City.Structures.Values)
        {
            if (structure.Category == StructureCategory.Commercial
                && structure.Operational
                && !structure.Inactive
                && structure.Type != StructureType.CorporateHq)
            {
                PaySalesTax(state, structure);
            }
        }

        // 6. Wages: employer → agent (net of income tax); income tax → treasury. Single full
        //    payment (no more installment splitting).
        foreach (var agent in state.City.Agents.Values)
        {
            PayFullWage(state, agent);
        }

        // 7. End-of-month profitability check (uses fully-populated MonthlyRevenue / MonthlyExpenses).
        //    CorporateHq is exempt (per M12) — its failure mode is running out of cash, not
        //    accumulated unprofitable months.
        StructureProfitabilityMechanic.EndOfMonthCheck(state);

        // 7b. M12: sweep industrial profits to parent HQ and pay corporate-profit tax to treasury.
        //     Runs AFTER the profitability check (which reads raw R/E) and BEFORE the reset.
        CorporateProfitMechanic.SweepAndTax(state);

        // Reset monthly accumulators for next month.
        foreach (var structure in state.City.Structures.Values)
        {
            structure.MonthlyRevenue = 0;
            structure.MonthlyExpenses = 0;
        }

        // 8-9. Emigration checks (insolvency then service-pressure).
        EmigrationMechanic.EndOfMonthCheck(state);
        ServiceEmigrationMechanic.EndOfMonthCheck(state);

        // 10. Monthly births (after emigration so this month's emigrants don't count toward birth rate).
        BirthMechanic.RunMonthlyBirths(state);

        // 10b. M-cal: monthly immigration. Vacant labor demand attracts agents from the reservoir,
        //     bounded by available housing. New immigrants get per-tier StartingSavings.
        if (state.Config.ImmigrationEnabled)
        {
            ImmigrationMechanic.RunMonthlyImmigration(state);
        }

        // 10c. Zone auto-spawn: residential / commercial zones grow buildings to match demand.
        ZoneAutoSpawnMechanic.RunMonthly(state);

        // 11. Bankruptcy clock + game-over check (uses UpkeepFundingFraction from step 1).
        TreasuryUpkeepMechanic.RunEndOfMonthBankruptcyCheck(state);
    }

    // === Per-event implementations ===

    private static void PayRent(SimState state, Agent agent)
    {
        if (agent.ResidenceStructureId is null) return;
        if (!state.City.Structures.TryGetValue(agent.ResidenceStructureId.Value, out var residence)) return;
        if (residence.Category != StructureCategory.Residential) return;

        var rent = Rent.RentForAgent(agent, residence);
        agent.Savings -= rent;
        state.City.TreasuryBalance += rent;
    }

    private static void PayAgentUtilities(SimState state, Agent agent)
    {
        if (agent.ResidenceStructureId is null) return;
        if (!state.City.Structures.TryGetValue(agent.ResidenceStructureId.Value, out var residence)) return;
        if (residence.Category != StructureCategory.Residential) return;

        var utility = Rent.UtilityForAgent(agent, residence);
        agent.Savings -= utility;
        state.City.TreasuryBalance += utility;
    }

    private static void PayCommercialUtilities(SimState state, Structure structure)
    {
        var utility = Commercial.MonthlyUtility(structure.Type);
        if (FoundingPhase.IsActive(state))
        {
            utility = (int)(utility * FoundingPhase.CommercialUtilityFactor);
        }
        structure.CashBalance -= utility;
        structure.MonthlyExpenses += utility;
        state.City.TreasuryBalance += utility;
    }

    private static void PayIndustrialUtilities(SimState state, Structure structure)
    {
        // M13: consolidated industrial model — utility cost charged to the owning HQ (the chain
        // is one company's books). Treasury still collects the utility either way.
        var utility = Industrial.MonthlyUtility(structure.Type);
        IndustrialProductionMechanic.ChargeExpenseToHqOrSelf(state, structure, utility);
        state.City.TreasuryBalance += utility;
    }

    /// <summary>
    /// Pay the agent's full monthly wage in a single transaction. Income tax withheld at source
    /// and forwarded to the treasury.
    /// </summary>
    private static void PayFullWage(SimState state, Agent agent)
    {
        if (agent.CurrentWage <= 0) return;
        if (agent.EmployerStructureId is null) return;
        if (!state.City.Structures.TryGetValue(agent.EmployerStructureId.Value, out var employer)) return;

        var gross = agent.CurrentWage;
        var tax = (int)(gross * TaxRates.IncomeTax);
        var net = gross - tax;

        // Payer dispatch: civic/healthcare/education/utility employers are paid from the treasury;
        // industrial employers route to their owning HQ; commercial pay themselves.
        if (CivicEmployment.IsTreasuryEmployer(employer.Category))
        {
            state.City.TreasuryBalance -= gross;
        }
        else
        {
            // Founding phase: treasury subsidizes a share of commercial/industrial wages so early
            // employers don't bleed dry while pop/demand ramp up. Treasury pays the subsidized
            // portion; employer pays the rest.
            int subsidy = FoundingPhase.IsActive(state)
                ? (int)(gross * FoundingPhase.WageSubsidyFraction)
                : 0;
            var employerShare = gross - subsidy;
            state.City.TreasuryBalance -= subsidy;
            IndustrialProductionMechanic.ChargeExpenseToHqOrSelf(state, employer, employerShare);
        }

        agent.Savings += net;
        state.City.TreasuryBalance += tax;
    }

    private static void PaySalesTax(SimState state, Structure structure)
    {
        var tax = (int)(structure.MonthlyRevenue * TaxRates.SalesTax);
        if (tax <= 0) return;
        structure.CashBalance -= tax;
        structure.MonthlyExpenses += tax;
        state.City.TreasuryBalance += tax;
    }

    private static void PayPropertyTax(SimState state, Structure structure)
    {
        var value = StructureValueLookup(structure);
        var tax = (int)(value * TaxRates.PropertyTaxMonthly);
        if (FoundingPhase.IsActive(state))
        {
            tax = (int)(tax * FoundingPhase.PropertyTaxFactor);
        }
        if (tax <= 0) return;
        // M13: industrial structures' property tax charges the owning HQ (if any).
        IndustrialProductionMechanic.ChargeExpenseToHqOrSelf(state, structure, tax);
        state.City.TreasuryBalance += tax;
    }

    private static int StructureValueLookup(Structure structure) => structure.Category switch
    {
        StructureCategory.Commercial => Commercial.StructureValue(structure.Type),
        StructureCategory.IndustrialExtractor => Industrial.StructureValue(structure.Type),
        StructureCategory.IndustrialProcessor => Industrial.StructureValue(structure.Type),
        StructureCategory.IndustrialManufacturer => Industrial.StructureValue(structure.Type),
        _ => 0,
    };
}
