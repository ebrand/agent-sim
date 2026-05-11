using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Periodic settlement events. Per `time-and-pacing.md`:
///   Day 1:  treasury upkeep paid out, agent rent, wage installment 1 (with income tax)
///   Day 8:  licensing fees (service-only commercial → regional treasury)
///   Day 15: utilities (residential / commercial / industrial → treasury), wage installment 2
///   Day 22: sales tax (commercial → treasury)
///   Day 30: property tax, end-of-month profitability check, COL flow, end-of-month emigration check
///
/// Per-actor sub-ordering: outflows fire before inflows on a given day.
///
/// M3 adds commercial-side flows: wage payment from commercial → agents (instead of placeholder
/// $0 wages), commercial utilities, commercial property tax, commercial sales tax, monthly COL.
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
        var day = DayOfMonth(state.CurrentTick);
        switch (day)
        {
            case 1:  Day1(state); break;
            case 8:  Day8(state); break;
            case 15: Day15(state); break;
            case 22: Day22(state); break;
            case 30: Day30(state); break;
        }
    }

    /// <summary>
    /// Day 1: treasury upkeep (out), agent rent (in to treasury), wage installment 1 (employer → agent).
    /// Per-actor sub-ordering: outflows fire first.
    /// </summary>
    public static void Day1(SimState state)
    {
        // Agent outflow: rent (agent → treasury)
        foreach (var agent in state.City.Agents.Values)
        {
            PayRent(state, agent);
        }

        // Agent inflow: wage installment 1 (employer pays half wage, income tax withheld)
        foreach (var agent in state.City.Agents.Values)
        {
            PayWageInstallment(state, agent);
        }

        // Treasury outflows for treasury-funded structures (civic / healthcare / education / utility / affordable housing).
        // M3: no such structures exist — no-op.
    }

    /// <summary>Day 8: service-only commercial pays licensing fees to regional treasury. M3: not yet implemented.</summary>
    public static void Day8(SimState state) { /* M3: no-op; M4+ adds service-only commercial */ }

    /// <summary>
    /// Day 15: utilities (out from agent / commercial / industrial → treasury),
    /// wage installment 2 (in to agent).
    /// </summary>
    public static void Day15(SimState state)
    {
        // Agent outflow: utilities
        foreach (var agent in state.City.Agents.Values)
        {
            PayAgentUtilities(state, agent);
        }

        // Commercial / Industrial outflow: utilities (structure → treasury)
        foreach (var structure in state.City.Structures.Values)
        {
            if (!structure.Operational || structure.Inactive) continue;
            if (structure.Category == StructureCategory.Commercial)
            {
                PayCommercialUtilities(state, structure);
            }
            else if (Industrial.IsIndustrial(structure.Type))
            {
                PayIndustrialUtilities(state, structure);
            }
        }

        // Agent inflow: wage installment 2
        foreach (var agent in state.City.Agents.Values)
        {
            PayWageInstallment(state, agent);
        }
    }

    /// <summary>Day 22: commercial sales tax → treasury.</summary>
    public static void Day22(SimState state)
    {
        foreach (var structure in state.City.Structures.Values)
        {
            if (structure.Category == StructureCategory.Commercial && structure.Operational && !structure.Inactive)
            {
                PaySalesTax(state, structure);
            }
        }
    }

    /// <summary>
    /// Day 30: COL revenue flows from agents to commercial; property tax (commercial / industrial → treasury);
    /// end-of-month profitability check; end-of-month emigration check.
    /// </summary>
    public static void Day30(SimState state)
    {
        // COL: agents pay commercial (monthly, lumped). Per the per-actor rule, agent COL outflow is "out"
        // from the agent and "in" to commercial — fires after agent outflows are conceptually settled.
        CostOfLivingMechanic.RunMonthlyCol(state);

        // Property tax (commercial / industrial → treasury)
        foreach (var structure in state.City.Structures.Values)
        {
            if (structure.Operational && !structure.Inactive &&
                (structure.Category == StructureCategory.Commercial
                 || structure.Category == StructureCategory.IndustrialExtractor
                 || structure.Category == StructureCategory.IndustrialProcessor
                 || structure.Category == StructureCategory.IndustrialManufacturer
                 || structure.Category == StructureCategory.IndustrialStorage))
            {
                PayPropertyTax(state, structure);
            }
        }

        // End-of-month profitability check — fires after all settlements (so this month's
        // revenue and expenses are fully populated) but before the monthly reset.
        StructureProfitabilityMechanic.EndOfMonthCheck(state);

        // Reset monthly accumulators for the next month.
        foreach (var structure in state.City.Structures.Values)
        {
            structure.MonthlyRevenue = 0;
            structure.MonthlyExpenses = 0;
        }

        // End-of-month emigration check (agents whose savings went negative this month)
        EmigrationMechanic.EndOfMonthCheck(state);

        // End-of-month service-pressure emigration (worst-of services below threshold)
        ServiceEmigrationMechanic.EndOfMonthCheck(state);

        // Monthly births (after emigration so this month's emigrants don't count toward birth rate)
        BirthMechanic.RunMonthlyBirths(state);
    }

    // === Per-event implementations ===

    private static void PayRent(SimState state, Agent agent)
    {
        if (agent.ResidenceStructureId is null) return;
        if (!state.City.Structures.TryGetValue(agent.ResidenceStructureId.Value, out var residence)) return;
        if (residence.Category != StructureCategory.Residential) return;

        var rent = Residential.MonthlyRent(residence.Type);
        agent.Savings -= rent;
        state.City.TreasuryBalance += rent;
    }

    private static void PayAgentUtilities(SimState state, Agent agent)
    {
        if (agent.ResidenceStructureId is null) return;
        if (!state.City.Structures.TryGetValue(agent.ResidenceStructureId.Value, out var residence)) return;
        if (residence.Category != StructureCategory.Residential) return;

        var utility = Utilities.MonthlyResidentialUtility(residence.Type);
        agent.Savings -= utility;
        state.City.TreasuryBalance += utility;
    }

    private static void PayCommercialUtilities(SimState state, Structure structure)
    {
        var utility = Commercial.MonthlyUtility(structure.Type);
        structure.CashBalance -= utility;
        structure.MonthlyExpenses += utility;
        state.City.TreasuryBalance += utility;
    }

    private static void PayIndustrialUtilities(SimState state, Structure structure)
    {
        var utility = Industrial.MonthlyUtility(structure.Type);
        structure.CashBalance -= utility;
        structure.MonthlyExpenses += utility;
        state.City.TreasuryBalance += utility;
    }

    private static void PayWageInstallment(SimState state, Agent agent)
    {
        if (agent.CurrentWage <= 0) return;
        if (agent.EmployerStructureId is null) return;
        if (!state.City.Structures.TryGetValue(agent.EmployerStructureId.Value, out var employer)) return;

        // Wage paid in two equal installments per month. Income tax withheld.
        var installment = agent.CurrentWage / 2;
        var tax = (int)(installment * TaxRates.IncomeTax);
        var net = installment - tax;

        // Outflow from employer's cash balance
        employer.CashBalance -= installment;
        employer.MonthlyExpenses += installment;

        // Inflow to agent (net) + treasury (tax)
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
        if (tax <= 0) return;
        structure.CashBalance -= tax;
        structure.MonthlyExpenses += tax;
        state.City.TreasuryBalance += tax;
    }

    private static int StructureValueLookup(Structure structure) => structure.Category switch
    {
        StructureCategory.Commercial => Commercial.StructureValue(structure.Type),
        StructureCategory.IndustrialExtractor => Industrial.StructureValue(structure.Type),
        StructureCategory.IndustrialProcessor => Industrial.StructureValue(structure.Type),
        StructureCategory.IndustrialManufacturer => Industrial.StructureValue(structure.Type),
        StructureCategory.IndustrialStorage => Industrial.StructureValue(structure.Type),
        _ => 0,
    };
}
