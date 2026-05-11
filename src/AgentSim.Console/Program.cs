using AgentSim.Core.Sim;
using AgentSim.Core.Types;

// Minimal CLI runner — exists so the solution has a runnable entry point during alpha-1.
// Real UI lives in `ui-and-player.md`; this is just for smoke-testing.

var sim = Sim.Create(new SimConfig { Seed = 42 });
Console.WriteLine($"Sim created. Reservoir total: {sim.State.Region.AgentReservoir.Total}");

sim.CreateResidentialZone();
Console.WriteLine($"After residential zone: city population = {sim.State.City.Population}, structures = {sim.State.City.Structures.Count}");

sim.Tick(30);
Console.WriteLine($"After 30 ticks: current tick = {sim.State.CurrentTick}");
