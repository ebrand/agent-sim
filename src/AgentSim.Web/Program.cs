using AgentSim.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<SimHost>();

var app = builder.Build();

// Static frontend lives in wwwroot/
app.UseDefaultFiles();
app.UseStaticFiles();

// === API ===

app.MapPost("/api/sim/new", (string? scenario, SimHost host) =>
{
    host.NewSim(scenario ?? "A");
    return Results.Ok(host.Snapshot());
});

app.MapGet("/api/sim/state", (SimHost host) => Results.Ok(host.Snapshot()));

app.MapPost("/api/sim/tick", (int? days, SimHost host) =>
{
    host.Tick(days ?? 1);
    return Results.Ok(host.Snapshot());
});

app.MapPost("/api/sim/run/start", (SimHost host) =>
{
    host.StartRun();
    return Results.Ok(host.Snapshot());
});

app.MapPost("/api/sim/run/stop", (SimHost host) =>
{
    host.StopRun();
    return Results.Ok(host.Snapshot());
});

app.MapPost("/api/sim/run/speed", (int ticksPerSecond, SimHost host) =>
{
    host.SetSpeed(ticksPerSecond);
    return Results.Ok(host.Snapshot());
});

app.Run();
