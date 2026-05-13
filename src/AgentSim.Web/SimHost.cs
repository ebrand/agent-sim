using AgentSim.Core.Calibration;
using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Sim.Mechanics;
using AgentSim.Core.Types;

namespace AgentSim.Web;

/// <summary>
/// Hosts a single sim instance shared by all HTTP clients. Background loop ticks the sim at a
/// configurable rate when running. All access goes through the lock so concurrent reads/writes
/// never race against the tick loop.
/// </summary>
public sealed class SimHost : IDisposable
{
    private readonly object _lock = new();
    private Sim _sim;
    private string _scenarioName;
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private int _ticksPerSecond = 10;

    public SimHost()
    {
        (_sim, _scenarioName) = LoadScenario("A");
    }

    public string ScenarioName => _scenarioName;
    public bool IsRunning => _runTask is { IsCompleted: false };
    public int TicksPerSecond => _ticksPerSecond;

    public void NewSim(string scenario)
    {
        StopRun();
        lock (_lock)
        {
            (_sim, _scenarioName) = LoadScenario(scenario);
        }
    }

    public void Tick(int days)
    {
        lock (_lock)
        {
            _sim.Tick(days);
        }
    }

    public void SetSpeed(int ticksPerSecond)
    {
        _ticksPerSecond = Math.Max(1, Math.Min(120, ticksPerSecond));
    }

    public void StartRun()
    {
        if (IsRunning) return;
        _runCts = new CancellationTokenSource();
        var token = _runCts.Token;
        _runTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                lock (_lock)
                {
                    if (_sim.State.City.GameOver) break;
                    _sim.Tick(1);
                }
                var delay = Math.Max(1, 1000 / _ticksPerSecond);
                try { await Task.Delay(delay, token); }
                catch (TaskCanceledException) { break; }
            }
        }, token);
    }

    public void StopRun()
    {
        _runCts?.Cancel();
        try { _runTask?.Wait(2000); } catch { /* ignore */ }
        _runCts?.Dispose();
        _runCts = null;
        _runTask = null;
    }

    /// <summary>Build a JSON-serializable snapshot of current sim state.</summary>
    public SimSnapshot Snapshot()
    {
        lock (_lock)
        {
            var state = _sim.State;
            var snap = ServiceSatisfactionMechanic.Compute(state);

            var structures = new List<StructureDto>();
            foreach (var s in state.City.Structures.Values)
            {
                var (w, h) = Footprint.For(s.Type);
                structures.Add(new StructureDto
                {
                    Id = s.Id,
                    Type = s.Type.ToString(),
                    Category = s.Category.ToString(),
                    Sector = s.Sector?.ToString(),
                    Industry = s.Industry?.ToString(),
                    Operational = s.Operational,
                    UnderConstruction = s.UnderConstruction,
                    Inactive = s.Inactive,
                    Cash = s.CashBalance,
                    Employees = s.EmployeeIds.Count,
                    JobSlots = s.JobSlots.Values.Sum(),
                    MonthlyRevenue = s.MonthlyRevenue,
                    MonthlyExpenses = s.MonthlyExpenses,
                    X = s.X,
                    Y = s.Y,
                    W = w,
                    H = h,
                });
            }

            var sectors = new List<SectorDto>();
            var workingAge = state.City.Agents.Values
                .Where(a => a.AgeDays >= Demographics.WorkingAgeStartDay)
                .ToList();
            foreach (CommercialSector sec in Enum.GetValues<CommercialSector>())
            {
                var frac = sec switch
                {
                    CommercialSector.Food => CostOfLiving.FoodFraction,
                    CommercialSector.Retail => CostOfLiving.RetailFraction,
                    CommercialSector.Entertainment => CostOfLiving.EntertainmentFraction,
                    _ => 0.0,
                };
                var demand = (int)workingAge.Sum(a => Wages.MonthlyWage(a.EducationTier) * frac);
                var shops = state.City.Structures.Values
                    .Where(x => x.Category == StructureCategory.Commercial
                                && x.Type != StructureType.CorporateHq
                                && x.Sector == sec)
                    .ToList();
                var mfgs = state.City.Structures.Values
                    .Count(x => Industrial.IsManufacturer(x.Type) && x.ManufacturerSectors.Contains(sec));
                sectors.Add(new SectorDto
                {
                    Sector = sec.ToString(),
                    MonthlyDemand = demand,
                    ActiveShops = shops.Count(x => x.Operational && !x.Inactive),
                    TotalShops = shops.Count,
                    Mfgs = mfgs,
                });
            }

            return new SimSnapshot
            {
                Scenario = _scenarioName,
                Day = state.CurrentTick,
                Month = (state.CurrentTick + 29) / 30,
                IsFoundingPhase = FoundingPhase.IsActive(state),
                IsRunning = IsRunning,
                TicksPerSecond = _ticksPerSecond,
                City = new CityDto
                {
                    Population = state.City.Population,
                    Treasury = state.City.TreasuryBalance,
                    TotalSavings = state.City.Agents.Values.Sum(a => a.Savings),
                    Employed = state.City.Agents.Values.Count(a => a.EmployerStructureId != null),
                    UpkeepFundingFraction = state.City.UpkeepFundingFraction,
                    ConsecutiveMonthsBankrupt = state.City.ConsecutiveMonthsBankrupt,
                    GameOver = state.City.GameOver,
                },
                Region = new RegionDto
                {
                    Climate = state.Region.Climate,
                    Nature = state.Region.Nature,
                    ReservoirTotal = state.Region.AgentReservoir.Total,
                },
                Services = new ServicesDto
                {
                    Civic = snap.CivicPercent,
                    Healthcare = snap.HealthcarePercent,
                    Utility = snap.UtilityPercent,
                    Environmental = snap.EnvironmentalPercent,
                    PrimaryEducation = snap.PrimaryEducationPercent,
                    SecondaryEducation = snap.SecondaryEducationPercent,
                    CollegeEducation = snap.CollegeEducationPercent,
                },
                Sectors = sectors,
                Structures = structures,
            };
        }
    }

    public void Dispose()
    {
        StopRun();
    }

    private static (Sim sim, string name) LoadScenario(string scenario) => scenario.ToUpperInvariant() switch
    {
        "B" => (Scenarios.BuildSelfSustaining(), "B: Self-sustaining"),
        "C" => (Scenarios.BuildMidGame(), "C: Mid-game"),
        _ => (Scenarios.BuildMinimal(), "A: Minimal"),
    };
}

// === DTOs ===

public sealed class SimSnapshot
{
    public required string Scenario { get; init; }
    public required int Day { get; init; }
    public required int Month { get; init; }
    public required bool IsFoundingPhase { get; init; }
    public required bool IsRunning { get; init; }
    public required int TicksPerSecond { get; init; }
    public required CityDto City { get; init; }
    public required RegionDto Region { get; init; }
    public required ServicesDto Services { get; init; }
    public required List<SectorDto> Sectors { get; init; }
    public required List<StructureDto> Structures { get; init; }
}

public sealed class CityDto
{
    public required int Population { get; init; }
    public required long Treasury { get; init; }
    public required long TotalSavings { get; init; }
    public required int Employed { get; init; }
    public required double UpkeepFundingFraction { get; init; }
    public required int ConsecutiveMonthsBankrupt { get; init; }
    public required bool GameOver { get; init; }
}

public sealed class RegionDto
{
    public required double Climate { get; init; }
    public required double Nature { get; init; }
    public required int ReservoirTotal { get; init; }
}

public sealed class ServicesDto
{
    public required double Civic { get; init; }
    public required double Healthcare { get; init; }
    public required double Utility { get; init; }
    public required double Environmental { get; init; }
    public required double PrimaryEducation { get; init; }
    public required double SecondaryEducation { get; init; }
    public required double CollegeEducation { get; init; }
}

public sealed class SectorDto
{
    public required string Sector { get; init; }
    public required int MonthlyDemand { get; init; }
    public required int ActiveShops { get; init; }
    public required int TotalShops { get; init; }
    public required int Mfgs { get; init; }
}

public sealed class StructureDto
{
    public required long Id { get; init; }
    public required string Type { get; init; }
    public required string Category { get; init; }
    public string? Sector { get; init; }
    public string? Industry { get; init; }
    public required bool Operational { get; init; }
    public required bool UnderConstruction { get; init; }
    public required bool Inactive { get; init; }
    public required long Cash { get; init; }
    public required int Employees { get; init; }
    public required int JobSlots { get; init; }
    public required int MonthlyRevenue { get; init; }
    public required int MonthlyExpenses { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public required int W { get; init; }
    public required int H { get; init; }
}
