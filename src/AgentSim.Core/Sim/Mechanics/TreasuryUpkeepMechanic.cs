using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Monthly treasury upkeep for treasury-funded structures (civic / healthcare / education /
/// utility / affordable housing). Per `economy.md` and `time-and-pacing.md`:
///   - Fires on day 1, before any other settlement event (treasury outflow before treasury inflows).
///   - Each treasury-funded operational, non-inactive structure costs a fixed monthly amount.
///   - Under-construction structures pay nothing yet (per design, construction is a separate one-time cost).
///   - Inactive structures pay nothing.
///   - Overdraft is allowed — treasury may go negative. When negative, the service-satisfaction
///     calculation zeros out treasury-funded service capacity (services collapse).
/// </summary>
public static class TreasuryUpkeepMechanic
{
    public static void PayMonthlyUpkeep(SimState state)
    {
        var total = ComputeTotalUpkeep(state);
        if (total <= 0) return;
        state.City.TreasuryBalance -= total;
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
    /// End-of-month bankruptcy clock. If treasury &lt; 0, increment ConsecutiveMonthsBankrupt;
    /// else reset to 0. At ≥ 6, set GameOver. Idempotent — calling repeatedly within a month
    /// has no effect because the caller fires it once per month-end.
    /// </summary>
    public static void RunEndOfMonthBankruptcyCheck(SimState state)
    {
        if (state.City.TreasuryBalance < 0)
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
