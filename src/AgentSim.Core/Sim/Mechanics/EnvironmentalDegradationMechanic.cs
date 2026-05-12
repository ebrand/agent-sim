using AgentSim.Core.Defaults;
using AgentSim.Core.Types;

namespace AgentSim.Core.Sim.Mechanics;

/// <summary>
/// M15: daily environmental degradation from industrial production + restoration from
/// Park / ReforestationSite / WetlandRestoration structures.
///
/// Per `feedback-loops.md`:
///   "Industrial structures degrade climate and/or nature each tick at a rate scaled by their
///    current operation level. Climate and nature in turn additively modify immigration and
///    emigration rates."
///
/// Implementation note: we approximate "current operation level" by the structure's expected
/// daily output (MaxOutputPerDay × StaffingFraction). This is an upper bound — the structure
/// might not actually produce that much if inputs are short. For M15 simplicity we don't
/// retroactively reconcile; the degradation tracks intent rather than realized output.
/// </summary>
public static class EnvironmentalDegradationMechanic
{
    public static void RunDaily(SimState state)
    {
        double climateDelta = 0;
        double natureDelta = 0;

        foreach (var s in state.City.Structures.Values)
        {
            if (!s.Operational || s.Inactive) continue;

            if (Industrial.IsIndustrial(s.Type))
            {
                var dailyOutput = ApproximateDailyOutput(s);
                if (dailyOutput <= 0) continue;
                climateDelta -= Defaults.Environment.ClimateImpactPerUnit(s.Type) * dailyOutput;
                natureDelta -= Defaults.Environment.NatureImpactPerUnit(s.Type) * dailyOutput;
            }
            else if (s.Category == StructureCategory.Restoration)
            {
                climateDelta += Defaults.Environment.ClimateRestorationPerDay(s.Type);
                natureDelta += Defaults.Environment.NatureRestorationPerDay(s.Type);
            }
        }

        state.Region.Climate = Math.Clamp(
            state.Region.Climate + climateDelta,
            Defaults.Environment.EnvironmentFloor,
            Defaults.Environment.EnvironmentCeiling);
        state.Region.Nature = Math.Clamp(
            state.Region.Nature + natureDelta,
            Defaults.Environment.EnvironmentFloor,
            Defaults.Environment.EnvironmentCeiling);
    }

    private static int ApproximateDailyOutput(Structure s)
    {
        var totalSlots = s.JobSlots.Values.Sum();
        if (totalSlots == 0) return 0;
        var filled = s.FilledSlots.Values.Sum();
        var staffingFraction = (double)filled / totalSlots;
        return (int)(Industrial.MaxOutputPerDay * staffingFraction);
    }
}
