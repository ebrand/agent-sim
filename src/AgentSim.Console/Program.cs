using AgentSim.Console;
using AgentSim.Core.Calibration;
using AgentSim.Core.Types;

// Args:
//   (no args)             → run all scenarios with text CalibrationRunner output
//   A | B | C [months]    → run that scenario as text CalibrationRunner output
//   tui A|B|C [months] [tps]  → run as live Spectre.Console TUI dashboard
//                                tps = ticks per second (default 10)
var first = args.Length > 0 ? args[0].ToUpperInvariant() : "ALL";

if (first == "TUI")
{
    var which = args.Length > 1 ? args[1].ToUpperInvariant() : "A";
    var months = args.Length > 2 && int.TryParse(args[2], out var m) ? m : 24;
    var tps = args.Length > 3 && int.TryParse(args[3], out var t) ? t : 10;

    var (sim, name) = which switch
    {
        "A" => (Scenarios.BuildMinimal(), "A: Minimal"),
        "B" => (Scenarios.BuildSelfSustaining(), "B: Self-sustaining"),
        "C" => (Scenarios.BuildMidGame(), "C: Mid-game"),
        _ => (Scenarios.BuildMinimal(), "A: Minimal"),
    };
    return TuiRunner.Run(sim, name, months, tps);
}

// Text CalibrationRunner output (existing).
var months_ = args.Length > 1 && int.TryParse(args[1], out var mm) ? mm : 0;

void Run(string name, AgentSim.Core.Sim.Sim sim, int defaultMonths)
{
    var r = CalibrationRunner.Run(name, sim, months_ > 0 ? months_ : defaultMonths);
    CalibrationRunner.PrintReport(r);
}

switch (first)
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
    default:
        Run("A: Minimal", Scenarios.BuildMinimal(), 12);
        Run("B: Self-sustaining", Scenarios.BuildSelfSustaining(), 24);
        Run("C: Mid-game", Scenarios.BuildMidGame(), 36);
        break;
}
return 0;
