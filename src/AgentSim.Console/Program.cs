using AgentSim.Console;
using AgentSim.Core.Types;

// Calibration runner. Args:
//   (no args)  → run all three scenarios
//   "A" / "B" / "C" → run that scenario only
//   "A" 12 → run scenario A for 12 months (default per-scenario months otherwise)
var which = args.Length > 0 ? args[0].ToUpperInvariant() : "ALL";
var months = args.Length > 1 && int.TryParse(args[1], out var m) ? m : 0;

void Run(string name, AgentSim.Core.Sim.Sim sim, int defaultMonths)
{
    var r = CalibrationRunner.Run(name, sim, months > 0 ? months : defaultMonths);
    CalibrationRunner.PrintReport(r);
}

switch (which)
{
    case "A":
        Run("A: Minimal", Scenarios.BuildMinimal(), 12);
        break;
    case "B":
        Run("B: Self-sustaining", Scenarios.BuildSelfSustaining(), 24);
        break;
    case "C":
        Run("C: Mid-game", Scenarios.BuildMidGame(), 36);
        break;
    case "ALL":
    default:
        Run("A: Minimal", Scenarios.BuildMinimal(), 12);
        Run("B: Self-sustaining", Scenarios.BuildSelfSustaining(), 24);
        Run("C: Mid-game", Scenarios.BuildMidGame(), 36);
        break;
}
