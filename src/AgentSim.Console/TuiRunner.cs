using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Sim.Mechanics;
using AgentSim.Core.Types;
using Spectre.Console;

namespace AgentSim.Console;

/// <summary>
/// Live terminal dashboard for observing a sim run. Builds a scenario, ticks at a configurable
/// pace, and renders panels for city overview, commercial sectors, industrial status, and recent
/// notifications. Read-only — no player interaction in this pass.
/// </summary>
public static class TuiRunner
{
    public static int Run(Sim sim, string scenarioName, int months, int ticksPerSecond = 10)
    {
        var events = new RollingLog(capacity: 12);
        int prevPop = sim.State.City.Population;
        long prevTreasury = sim.State.City.TreasuryBalance;
        var inactivePrev = new HashSet<long>();
        foreach (var s in sim.State.City.Structures.Values)
            if (s.Inactive) inactivePrev.Add(s.Id);

        var layout = BuildLayout();

        AnsiConsole.Live(layout)
            .AutoClear(false)
            .Overflow(VerticalOverflow.Ellipsis)
            .Cropping(VerticalOverflowCropping.Bottom)
            .Start(ctx =>
            {
                int totalTicks = months * 30;
                var sleepMs = Math.Max(1, 1000 / ticksPerSecond);

                for (int t = 0; t < totalTicks; t++)
                {
                    sim.Tick(1);

                    // Detect events to log.
                    int popDelta = sim.State.City.Population - prevPop;
                    if (popDelta != 0)
                    {
                        events.Add(popDelta > 0
                            ? $"[green]+{popDelta} immigrant{(popDelta > 1 ? "s" : "")}[/] (day {sim.State.CurrentTick})"
                            : $"[red]{popDelta} emigrant{(popDelta < -1 ? "s" : "")}[/] (day {sim.State.CurrentTick})");
                        prevPop = sim.State.City.Population;
                    }

                    foreach (var s in sim.State.City.Structures.Values)
                    {
                        bool isInactiveNow = s.Inactive;
                        bool wasInactive = inactivePrev.Contains(s.Id);
                        if (isInactiveNow && !wasInactive)
                        {
                            events.Add($"[yellow]{StructureLabel(s)} went inactive[/] (day {sim.State.CurrentTick})");
                            inactivePrev.Add(s.Id);
                        }
                        else if (!isInactiveNow && wasInactive)
                        {
                            events.Add($"[cyan]{StructureLabel(s)} reactivated[/] (day {sim.State.CurrentTick})");
                            inactivePrev.Remove(s.Id);
                        }
                    }

                    if (sim.State.City.TreasuryBalance < 0 && prevTreasury >= 0)
                    {
                        events.Add($"[red]Treasury negative[/] (day {sim.State.CurrentTick})");
                    }
                    prevTreasury = sim.State.City.TreasuryBalance;

                    if (sim.State.City.GameOver)
                    {
                        events.Add($"[red bold]GAME OVER[/] (day {sim.State.CurrentTick})");
                        RenderAll(layout, sim, scenarioName, events);
                        ctx.Refresh();
                        break;
                    }

                    // Render every tick. Cheap enough for terminal.
                    RenderAll(layout, sim, scenarioName, events);
                    ctx.Refresh();
                    Thread.Sleep(sleepMs);
                }
            });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]Run complete[/] — final day {sim.State.CurrentTick}, pop {sim.State.City.Population}, treasury ${sim.State.City.TreasuryBalance:N0}");
        return 0;
    }

    // === Layout ===

    private static Layout BuildLayout()
    {
        return new Layout("root")
            .SplitRows(
                new Layout("header").Size(3),
                new Layout("body").SplitColumns(
                    new Layout("left").SplitRows(
                        new Layout("overview").Size(11),
                        new Layout("sectors")),
                    new Layout("right").SplitRows(
                        new Layout("industrial"),
                        new Layout("events").Size(14))));
    }

    private static void RenderAll(Layout layout, Sim sim, string scenarioName, RollingLog events)
    {
        layout["header"].Update(BuildHeader(sim, scenarioName));
        layout["overview"].Update(BuildOverviewPanel(sim));
        layout["sectors"].Update(BuildSectorsPanel(sim));
        layout["industrial"].Update(BuildIndustrialPanel(sim));
        layout["events"].Update(BuildEventsPanel(events));
    }

    private static Panel BuildHeader(Sim sim, string scenario)
    {
        var day = sim.State.CurrentTick;
        var month = (day + 29) / 30;
        var phase = FoundingPhase.IsActive(sim.State) ? "[yellow]founding phase[/]" : "[grey]post-founding[/]";
        var content = new Markup($"[bold]Scenario:[/] {scenario}    [bold]Day:[/] {day}    [bold]Month:[/] {month}    [bold]Phase:[/] {phase}");
        return new Panel(content).Border(BoxBorder.Rounded).BorderColor(Color.DodgerBlue1);
    }

    private static Panel BuildOverviewPanel(Sim sim)
    {
        var s = sim.State;
        var snap = ServiceSatisfactionMechanic.Compute(s);
        var worst = new[] { snap.CivicPercent, snap.HealthcarePercent, snap.UtilityPercent, snap.EnvironmentalPercent }.Min();
        var totalSavings = s.City.Agents.Values.Sum(a => a.Savings);
        var employed = s.City.Agents.Values.Count(a => a.EmployerStructureId != null);

        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow(new Markup("[bold]Population[/]"), new Markup($"{s.City.Population} ({employed} employed)"));
        grid.AddRow(new Markup("[bold]Treasury[/]"), new Markup(FormatMoney(s.City.TreasuryBalance, danger: s.City.TreasuryBalance < 0)));
        grid.AddRow(new Markup("[bold]Total agent savings[/]"), new Markup($"${totalSavings:N0}"));
        grid.AddRow(new Markup("[bold]Worst service[/]"), new Markup(FormatPercent(worst)));
        grid.AddRow(new Markup("[bold]Climate / Nature[/]"), new Markup($"{s.Region.Climate * 100:F0}% / {s.Region.Nature * 100:F0}%"));
        grid.AddRow(new Markup("[bold]Upkeep funding[/]"), new Markup(FormatPercent(s.City.UpkeepFundingFraction * 100)));
        grid.AddRow(new Markup("[bold]Bankrupt months[/]"), new Markup($"{s.City.ConsecutiveMonthsBankrupt} / {Upkeep.BankruptcyMonthsToGameOver}"));
        grid.AddRow(new Markup("[bold]Reservoir[/]"), new Markup($"{s.Region.AgentReservoir.Total:N0}"));

        return new Panel(grid).Header(" City overview ").Border(BoxBorder.Rounded);
    }

    private static Panel BuildSectorsPanel(Sim sim)
    {
        var table = new Table().Border(TableBorder.None).Expand();
        table.AddColumn("Sector");
        table.AddColumn("Demand $/mo");
        table.AddColumn("Shops");
        table.AddColumn("Mfgs");
        table.AddColumn("Avg shop cash");

        var workingAge = sim.State.City.Agents.Values
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
            int demand = (int)workingAge.Sum(a => Wages.MonthlyWage(a.EducationTier) * frac);
            var shops = sim.State.City.Structures.Values
                .Where(s => s.Category == StructureCategory.Commercial
                            && s.Type != StructureType.CorporateHq
                            && s.Sector == sec)
                .ToList();
            int activeShops = shops.Count(s => s.Operational && !s.Inactive);
            var mfgs = sim.State.City.Structures.Values
                .Count(s => Industrial.IsManufacturer(s.Type) && s.ManufacturerSectors.Contains(sec));
            long avgCash = shops.Count > 0 ? (long)shops.Average(s => s.CashBalance) : 0;

            table.AddRow(
                new Markup($"[bold]{sec}[/]"),
                new Markup($"${demand:N0}"),
                new Markup($"{activeShops}/{shops.Count}"),
                new Markup($"{mfgs}"),
                new Markup(FormatMoney(avgCash, danger: avgCash < 0)));
        }

        return new Panel(table).Header(" Commercial sectors ").Border(BoxBorder.Rounded);
    }

    private static Panel BuildIndustrialPanel(Sim sim)
    {
        var table = new Table().Border(TableBorder.None).Expand();
        table.AddColumn("Entity");
        table.AddColumn("Cash");
        table.AddColumn("Jobs");
        table.AddColumn("Status");

        // HQs
        foreach (var hq in sim.State.City.Structures.Values
            .Where(s => s.Type == StructureType.CorporateHq)
            .OrderBy(s => s.Industry.ToString()))
        {
            table.AddRow(
                new Markup($"[bold]HQ {hq.Industry}[/]"),
                new Markup(FormatMoney(hq.CashBalance, danger: hq.CashBalance < 0)),
                new Markup($"—"),
                new Markup(hq.Inactive ? "[red]inactive[/]" : "[green]active[/]"));
        }

        // Industrial sub-structures + standalone mfgs
        foreach (var s in sim.State.City.Structures.Values
            .Where(x => Industrial.IsIndustrial(x.Type))
            .OrderBy(x => x.Type.ToString()))
        {
            var jobs = $"{s.EmployeeIds.Count}/{s.JobSlots.Values.Sum()}";
            var status = s.UnderConstruction ? "[grey]building[/]"
                : s.Inactive ? "[red]inactive[/]"
                : "[green]active[/]";
            table.AddRow(
                new Markup($"{s.Type}"),
                new Markup(s.OwnerHqId is null ? FormatMoney(s.CashBalance, danger: s.CashBalance < 0) : "[grey](hq)[/]"),
                new Markup(jobs),
                new Markup(status));
        }

        return new Panel(table).Header(" Industrial ").Border(BoxBorder.Rounded);
    }

    private static Panel BuildEventsPanel(RollingLog events)
    {
        var lines = events.Items.ToList();
        var content = lines.Count == 0
            ? new Markup("[grey](no events yet)[/]")
            : new Markup(string.Join("\n", lines));
        return new Panel(content).Header(" Events ").Border(BoxBorder.Rounded);
    }

    // === Helpers ===

    private static string StructureLabel(Structure s)
    {
        if (s.Type == StructureType.CorporateHq && s.Industry is IndustryType ind) return $"HQ[{ind}]";
        if (s.Sector is CommercialSector sec) return $"{s.Type}[{sec}]";
        return s.Type.ToString();
    }

    private static string FormatMoney(long amount, bool danger = false)
    {
        var s = amount >= 0 ? $"${amount:N0}" : $"-${-amount:N0}";
        if (danger) return $"[red]{s}[/]";
        if (amount > 0) return $"[green]{s}[/]";
        return s;
    }

    private static string FormatPercent(double pct)
    {
        var clamped = Math.Max(0, Math.Min(100, pct));
        var color = clamped < 25 ? "red" : clamped < 60 ? "yellow" : "green";
        return $"[{color}]{clamped:F0}%[/]";
    }
}

internal sealed class RollingLog
{
    private readonly Queue<string> _items;
    private readonly int _capacity;
    public RollingLog(int capacity) { _capacity = capacity; _items = new Queue<string>(capacity); }
    public IEnumerable<string> Items => _items;
    public void Add(string line)
    {
        _items.Enqueue(line);
        while (_items.Count > _capacity) _items.Dequeue();
    }
}
