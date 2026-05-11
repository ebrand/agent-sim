using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Monthly treasury upkeep for treasury-funded structures (civic / healthcare / education /
/// utility / affordable housing). Per `economy.md` and `feedback-loops.md`:
///   - Fires on day 1, before any other settlement event (treasury outflow before treasury inflows).
///   - Each treasury-funded operational, non-inactive structure costs a fixed monthly amount.
///   - Under-construction structures pay nothing yet (construction cost is a separate concern; M11+).
///   - Inactive structures pay nothing.
///   - Full pay when `treasury >= total_upkeep`. Otherwise partial-pay: deduct
///     `max(0, treasury) / 6` so the remaining treasury stretches at least 6 months of slow
///     decline. Service capacity scales by the funded fraction this month.
///   - End-of-month bankruptcy check: if this month was partial-pay, the consecutive-months counter
///     increments. 6 consecutive partial-pay months → game-over.
/// </summary>
public static class TreasuryUpkeepMechanic
{
    /// <summary>Months that partial-pay can persist before game-over fires.</summary>
    public const int PartialPayStretchMonths = 6;

    public static void PayMonthlyUpkeep(SimState state)
    {
        var totalUpkeep = ComputeTotalUpkeep(state);
        if (totalUpkeep <= 0)
        {
            state.City.UpkeepFundingFraction = 1.0;
            return;
        }

        if (state.City.TreasuryBalance >= totalUpkeep)
        {
            // Full pay.
            state.City.TreasuryBalance -= totalUpkeep;
            state.City.UpkeepFundingFraction = 1.0;
            return;
        }

        // Partial pay: spend a sixth of available (non-negative) treasury this month, so the
        // remaining cushion stretches ~6 months of declining payments before depletion.
        var available = Math.Max(0, state.City.TreasuryBalance);
        var paid = available / PartialPayStretchMonths;
        state.City.TreasuryBalance -= paid;
        state.City.UpkeepFundingFraction = (double)paid / totalUpkeep;
    }

    /// <summary>Sum of monthly upkeep across all operational, non-inactive, treasury-funded structures.</summary>
    public static int ComputeTotalUpkeep(SimState state)
    {
        int total = 0;
        foreach (var s in state.City.Structures.Values)
        {
            if (!s.Operational || s.Inactive) continue;
            total += Upkeep.MonthlyCost(s.Type);
        }
        return total;
    }

    /// <summary>
    /// End-of-month bankruptcy clock. If THIS month was a partial-pay month (or zero-funded),
    /// increment ConsecutiveMonthsBankrupt; else reset to 0. At ≥ 6, set GameOver (which halts
    /// subsequent Tick calls).
    /// </summary>
    public static void RunEndOfMonthBankruptcyCheck(SimState state)
    {
        if (state.City.UpkeepFundingFraction < 1.0)
        {
            state.City.ConsecutiveMonthsBankrupt++;
        }
        else
        {
            state.City.ConsecutiveMonthsBankrupt = 0;
        }

        if (state.City.ConsecutiveMonthsBankrupt >= Upkeep.BankruptcyMonthsToGameOver)
        {
            state.City.GameOver = true;
        }
    }
}
