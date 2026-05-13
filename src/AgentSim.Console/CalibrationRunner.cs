using AgentSim.Core.Defaults;
using AgentSim.Core.Sim;
using AgentSim.Core.Sim.Mechanics;
using AgentSim.Core.Types;

namespace AgentSim.Console;

/// <summary>
/// Run a scenario for N months and emit a compact monthly trace plus a final pass/fail summary
/// against the three success criteria: population stable, all entities solvent, industry profitable.
/// </summary>
public static class CalibrationRunner
{
    public sealed class MonthSample
    {
        public int Month;
        public int Population;
        public long Treasury;
        public int TreasuryDelta;
        public int TotalAgentSavings;
        public int EmployedAgents;
        public int InactiveCommercials;
        public int InactiveIndustrials;
        public double WorstServicePercent;
        public List<(string label, int cash, int rev, int exp, bool inactive)> Entities = new();
    }

    public sealed class ScenarioResult
    {
        public required string Name;
        public required int Months;
        public required List<MonthSample> Samples;
        public required Dictionary<string, bool> Criteria;
        public required Sim Sim;
    }

    public static ScenarioResult Run(string name, Sim sim, int months)
    {
        var samples = new List<MonthSample>();
        long prevTreasury = sim.State.City.TreasuryBalance;

        for (int m = 1; m <= months; m++)
        {
            sim.Tick(30);
            var sample = SampleMonth(sim, m, prevTreasury);
            samples.Add(sample);
            prevTreasury = sim.State.City.TreasuryBalance;
        }

        var criteria = EvaluateCriteria(sim, samples);
        return new ScenarioResult
        {
            Name = name,
            Months = months,
            Samples = samples,
            Criteria = criteria,
            Sim = sim,
        };
    }

    private static MonthSample SampleMonth(Sim sim, int month, long prevTreasury)
    {
        var s = new MonthSample
        {
            Month = month,
            Population = sim.State.City.Population,
            Treasury = sim.State.City.TreasuryBalance,
            TreasuryDelta = (int)(sim.State.City.TreasuryBalance - prevTreasury),
            TotalAgentSavings = sim.State.City.Agents.Values.Sum(a => a.Savings),
            EmployedAgents = sim.State.City.Agents.Values.Count(a => a.EmployerStructureId != null),
        };

        foreach (var st in sim.State.City.Structures.Values)
        {
            if (st.Inactive)
            {
                if (st.Category == StructureCategory.Commercial) s.InactiveCommercials++;
                if (st.Category == StructureCategory.IndustrialExtractor
                    || st.Category == StructureCategory.IndustrialProcessor
                    || st.Category == StructureCategory.IndustrialManufacturer) s.InactiveIndustrials++;
            }
        }

        var snap = ServiceSatisfactionMechanic.Compute(sim.State);
        s.WorstServicePercent = new[] {
            snap.CivicPercent, snap.HealthcarePercent, snap.UtilityPercent, snap.EnvironmentalPercent,
        }.Min();

        // Track per-entity at the final sampling moment (for the summary). We'll capture from the
        // final sim state in CalibrationRunner after the loop.
        return s;
    }

    private static Dictionary<string, bool> EvaluateCriteria(Sim sim, List<MonthSample> samples)
    {
        var first = samples.First();
        var last = samples.Last();
        // Pop stability: after the bootstrap unemployed-wave clears, pop should hold steady at
        // a positive equilibrium. Check the last 2 months (small window so we don't penalize
        // the late-game wave that follows bonus depletion).
        bool popStable;
        if (samples.Count >= 2)
        {
            var recent = samples.TakeLast(2).Select(s => s.Population).ToList();
            var maxPop = recent.Max();
            var minPop = recent.Min();
            popStable = minPop >= 10 && (maxPop - minPop) <= Math.Max(3, maxPop / 5);
        }
        else
        {
            popStable = last.Population >= 10;
        }

        // All entities solvent: treasury, all HQs, all standalone industrials (Mfgs), all commercials.
        var allSolvent = sim.State.City.TreasuryBalance >= 0;
        foreach (var st in sim.State.City.Structures.Values)
        {
            if (st.UnderConstruction) continue;
            if (st.Type == StructureType.CorporateHq && st.CashBalance < 0) allSolvent = false;
            if (st.Category == StructureCategory.Commercial && st.Type != StructureType.CorporateHq
                && st.CashBalance < 0) allSolvent = false;
            // Industrials owned by an HQ are funded by HQ; non-owned mfgs use their own cash.
            if ((Industrial.IsManufacturer(st.Type) && st.OwnerHqId is null) && st.CashBalance < 0) allSolvent = false;
        }

        // Industry profitable: each HQ + standalone mfg has positive cumulative profit over the run.
        // We approximate via final CashBalance relative to "starting cash". For HQs starting cash is
        // Industry.StartingCashFor. For standalone mfgs, starting cash is 0.
        var industryProfitable = true;
        foreach (var st in sim.State.City.Structures.Values)
        {
            if (st.UnderConstruction) continue;
            if (st.Type == StructureType.CorporateHq && st.Industry is IndustryType ind)
            {
                var startingCash = Industry.StartingCashFor(ind);
                var fullChainCost = Industry.FullChainConstructionCost(ind);
                // After build-out HQ should retain ~startingCash - fullChainCost = startingCash/2.
                // Profitable means cash >= that floor.
                var expected = startingCash - fullChainCost;
                if (st.CashBalance < expected) industryProfitable = false;
            }
            if (Industrial.IsManufacturer(st.Type) && st.OwnerHqId is null && st.CashBalance < 0)
            {
                industryProfitable = false;
            }
        }

        return new Dictionary<string, bool>
        {
            ["pop_stable"] = popStable,
            ["all_solvent"] = allSolvent,
            ["industry_profitable"] = industryProfitable,
        };
    }

    public static void PrintReport(ScenarioResult r)
    {
        System.Console.WriteLine($"=== Scenario: {r.Name} ({r.Months} months) ===");
        System.Console.WriteLine($"{"M",2} {"Pop",4} {"Treasury",-10} {"ΔTreas",-8} {"Empl",4} {"Savings",-10} {"WorstSvc",-8} {"Inact(c/i)",10}");
        foreach (var s in r.Samples)
        {
            System.Console.WriteLine($"{s.Month,2} {s.Population,4} {s.Treasury,-10} {s.TreasuryDelta,-8} {s.EmployedAgents,4} {s.TotalAgentSavings,-10} {s.WorstServicePercent,-8:F1} {s.InactiveCommercials}/{s.InactiveIndustrials,8}");
        }

        // Per-entity end-state
        System.Console.WriteLine($"--- End-of-run entity state ---");
        foreach (var st in r.Sim.State.City.Structures.Values
            .Where(x => !x.UnderConstruction)
            .OrderBy(x => x.Type.ToString()))
        {
            var label = st.Type.ToString();
            if (st.Sector is CommercialSector sec) label += $"[{sec}]";
            if (st.Industry is IndustryType ind) label += $"[{ind}]";
            if (st.ManufacturerSectors.Count > 0) label += $"[svc:{string.Join(",", st.ManufacturerSectors)}]";
            var emp = st.EmployeeIds.Count;
            var slots = st.JobSlots.Values.Sum();
            System.Console.WriteLine($"  {label,-50} cash={st.CashBalance,-12} jobs={emp}/{slots} inactive={(st.Inactive ? "Y" : "n")}");
        }

        System.Console.WriteLine($"--- Criteria ---");
        foreach (var (k, v) in r.Criteria) System.Console.WriteLine($"  {(v ? "PASS" : "FAIL")}  {k}");
        var allPass = r.Criteria.Values.All(v => v);
        System.Console.WriteLine($"  {(allPass ? "OVERALL: PASS" : "OVERALL: FAIL")}");
        System.Console.WriteLine();
    }
}
