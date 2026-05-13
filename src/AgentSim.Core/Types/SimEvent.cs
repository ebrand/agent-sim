namespace AgentSim.Core.Types;

/// <summary>
/// A discrete event the player should know about. Events are written to SimState.EventLog as the
/// sim ticks; the UI surfaces them as toasts (recent / critical) and in a scrollable log panel.
/// </summary>
public sealed record SimEvent(int Tick, SimEventSeverity Severity, string Category, string Message);

public enum SimEventSeverity
{
    Info,
    Warning,
    Critical,
}
