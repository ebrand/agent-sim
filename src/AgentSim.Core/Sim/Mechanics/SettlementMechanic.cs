using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Periodic settlement events. Per `time-and-pacing.md`:
///   Day 1:  treasury upkeep paid out, agent rent, wage installment 1 (with income tax)
///   Day 8:  licensing fees (service-only commercial → Region.Treasury)
///   Day 15: utilities (residential / commercial / industrial → treasury), wage installment 2
///   Day 22: sales tax (commercial → treasury)
///   Day 30: property tax, end-of-month profitability check, end-of-month emigration check
///
/// Per-actor sub-ordering: outflows fire before inflows on a given day.
///
/// M2 implements the agent-side flows for days 1, 15, 30. Other settlements (treasury upkeep,
/// licensing, sales tax, property tax, profitability check) are no-ops in M2 because no
/// treasury-funded structures, commercial structures, or industrial structures exist yet.
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
    /// Day 1: treasury upkeep (out), agent rent (in to treasury), wage installment 1 (employer → agent, income tax withheld).
    /// Per-actor sub-ordering: outflows fire first.
    /// </summary>
    public static void Day1(SimState state)
    {
        // Agent outflow: rent. Per "outflows before inflows", rent fires before wage 1.
        foreach (var agent in state.City.Agents.Values)
        {
            PayRent(state, agent);
        }

        // Agent inflow: wage installment 1 (half of monthly wage, income tax withheld).
        foreach (var agent in state.City.Agents.Values)
        {
            PayWageInstallment(state, agent);
        }

        // Treasury outflows for treasury-funded structures (civic / healthcare / education / utility / affordable housing subsidy).
        // M2: no such structures exist — no-op.
    }

    /// <summary>Day 8: service-only commercial pays licensing fees to Region.Treasury. M2: no commercial — no-op.</summary>
    public static void Day8(SimState state) { /* no-op in M2 */ }

    /// <summary>
    /// Day 15: utilities (out from agent / commercial / industrial → treasury),
    /// wage installment 2 (in to agent, income tax withheld).
    /// </summary>
    public static void Day15(SimState state)
    {
        // Outflow: utilities.
        foreach (var agent in state.City.Agents.Values)
        {
            PayUtilities(state, agent);
        }

        // Inflow: wage installment 2.
        foreach (var agent in state.City.Agents.Values)
        {
            PayWageInstallment(state, agent);
        }
    }

    /// <summary>Day 22: commercial sales tax → treasury. M2: no commercial — no-op.</summary>
    public static void Day22(SimState state) { /* no-op in M2 */ }

    /// <summary>
    /// Day 30: property tax (commercial / industrial → treasury), end-of-month profitability check,
    /// end-of-month emigration check.
    /// M2: no commercial / industrial — only the emigration check runs.
    /// </summary>
    public static void Day30(SimState state)
    {
        // Property tax: M2 no-op.
        // Profitability check: M2 no-op.

        // End-of-month emigration check (agents). Runs after all other day-30 settlements.
        EmigrationMechanic.EndOfMonthCheck(state);
    }

    private static void PayRent(SimState state, Agent agent)
    {
        if (agent.ResidenceStructureId is null) return;
        if (!state.City.Structures.TryGetValue(agent.ResidenceStructureId.Value, out var residence)) return;
        if (residence.Category != StructureCategory.Residential) return;

        var rent = Residential.MonthlyRent(residence.Type);
        agent.Savings -= rent;
        state.City.TreasuryBalance += rent;
    }

    private static void PayUtilities(SimState state, Agent agent)
    {
        if (agent.ResidenceStructureId is null) return;
        if (!state.City.Structures.TryGetValue(agent.ResidenceStructureId.Value, out var residence)) return;
        if (residence.Category != StructureCategory.Residential) return;

        var utility = Utilities.MonthlyResidentialUtility(residence.Type);
        agent.Savings -= utility;
        state.City.TreasuryBalance += utility;
    }

    private static void PayWageInstallment(SimState state, Agent agent)
    {
        if (agent.CurrentWage <= 0) return;
        // Wage paid in two equal installments (day 1, day 15). Income tax withheld with each.
        var installment = agent.CurrentWage / 2;
        var tax = (int)(installment * TaxRates.IncomeTax);
        var net = installment - tax;
        agent.Savings += net;
        state.City.TreasuryBalance += tax;
    }
}
