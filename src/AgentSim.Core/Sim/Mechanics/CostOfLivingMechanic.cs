using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// Cost-of-living spending: agents pay COL (food, clothing, household, entertainment) to
/// commercial structures monthly. Per `economy.md`: if no commercial structure exists,
/// the spending fails silently — money stays in the agent's savings.
///
/// M3 simplification: all COL is lumped together on day 30, distributed pro-rata across
/// operational commercial structures (no goods/service distinction yet; that lands in M4
/// when industrial chain is wired).
/// </summary>
public static class CostOfLivingMechanic
{
    /// <summary>Fires on day 30 (before the end-of-month emigration check).</summary>
    public static void RunMonthlyCol(SimState state)
    {
        // Identify all operational commercial structures.
        var commercials = state.City.Structures.Values
            .Where(s => s.Category == StructureCategory.Commercial
                        && s.Operational
                        && !s.Inactive)
            .ToList();

        if (commercials.Count == 0)
        {
            // No commercial → all COL spending fails silently. Money stays in agent savings.
            return;
        }

        var totalRevenue = 0;
        foreach (var agent in state.City.Agents.Values)
        {
            var col = CostOfLiving.MonthlyCol(agent.EducationTier);
            agent.Savings -= col;
            totalRevenue += col;
        }

        // Distribute total COL revenue evenly across operational commercial structures.
        // (M4 will refine this with per-good attribution and service-only vs goods-backed splits.)
        var per = totalRevenue / commercials.Count;
        var remainder = totalRevenue % commercials.Count;
        foreach (var c in commercials)
        {
            var share = per + (remainder > 0 ? 1 : 0);
            if (remainder > 0) remainder--;
            c.CashBalance += share;
            c.MonthlyRevenue += share;
        }
    }
}
