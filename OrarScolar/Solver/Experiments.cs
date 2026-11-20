using System.Diagnostics;
using System.Globalization;
using System.Text;
using OrarScolar.Data;

namespace OrarScolar.Solver;

public sealed record ExperimentOptions
{
    /// <summary>Numbers of classes of the generated schools (demo generator).</summary>
    public int[] Sizes { get; init; } = [3, 8, 16];
    /// <summary>Instances per size (different demo data seeds).</summary>
    public int Runs { get; init; } = 3;
    /// <summary>Equal node limit for every backtracking variant.</summary>
    public long NodeCap { get; init; } = 200_000;
    public GaOptions Ga { get; init; } = new();
}

/// <summary>One backtracking variant on one size, aggregated over the runs.</summary>
public sealed record BtRow(int Size, string Variant, int Solved, int Runs, double AvgNodes, double AvgBacktracks, double AvgMs);

/// <summary>Whole pipeline on one size, aggregated over the runs.</summary>
public sealed record GaRow(int Size, int Lessons, int Runs, double AvgSoftBefore, double AvgSoftAfter, double AvgImprovementPct,
    double AvgBtMs, double AvgGaMs, double AvgGenerations, Breakdown? AvgBefore, Breakdown? AvgAfter);

public sealed record ExperimentResult(List<BtRow> Bt, List<GaRow> Ga, int ConvergenceSize, List<int> Convergence);

/// <summary>
/// Experimental evaluation for the project report: on generated schools of several sizes, measures
/// (1) what each constraint-propagation technique contributes to backtracking (equal node limit) and
/// (2) how much the genetic algorithm improves the first valid timetable. Everything is reproducible:
/// fixed data seeds, no deadline.
/// </summary>
public static class Experiments
{
    public static ExperimentResult Run(ExperimentOptions o, CancellationToken ct = default, Action<string>? progress = null)
    {
        var bt = new List<BtRow>();
        var ga = new List<GaRow>();
        List<int> convergence = [];
        int convSize = 0;
        foreach (var size in o.Sizes)
        {
            var btAcc = new Dictionary<string, (int Solved, long Nodes, long Back, long Ms)>();
            var order = new List<string>();
            var gaRuns = new List<(int N, PipelineResult R, long GaMs)>();
            for (int run = 0; run < o.Runs; run++)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Invoke($"{size} clase – instanța {run + 1}/{o.Runs}: backtracking…");
                var (data, _) = SeedGenerator.Create(size, 42 + 1000 * run);
                var x = SolverInput.FromEntities(data);

                foreach (var row in Backtracking.Compare(x, o.NodeCap, ct))
                {
                    if (!btAcc.ContainsKey(row.Variant)) { btAcc[row.Variant] = default; order.Add(row.Variant); }
                    var a = btAcc[row.Variant];
                    btAcc[row.Variant] = (a.Solved + (row.Status == BtStatus.Success ? 1 : 0), a.Nodes + row.Nodes, a.Back + row.Backtracks, a.Ms + row.Ms);
                }

                progress?.Invoke($"{size} clase – instanța {run + 1}/{o.Runs}: algoritm genetic…");
                var history = new List<int>();
                var sw = Stopwatch.StartNew();
                var pre = Preprocessor.Run(x, useAc3: true);
                if (pre.Errors.Count > 0) continue;
                var b = Backtracking.Solve(x, pre.Domains, new BtOptions { Seed = run + 1 }, ct);
                if (b.Status != BtStatus.Success) { ct.ThrowIfCancellationRequested(); continue; }
                long btMs = sw.ElapsedMilliseconds;
                var g = Genetic.Run(x, b.SlotOf, b.RoomOf, o.Ga with { Seed = run + 1 }, ct, Deadline.None, null, history);
                if (g.StoppedBy == GaStop.Cancelled) ct.ThrowIfCancellationRequested();
                long gaMs = sw.ElapsedMilliseconds - btMs;
                var r = new PipelineResult(PipelineStatus.Success, [], g.SlotOf, g.RoomOf, b.Nodes, b.Backtracks, btMs, sw.ElapsedMilliseconds,
                    g.SoftBefore, g.SoftAfter, g.Before, g.After, g.Generations, g.StoppedBy.ToString());
                gaRuns.Add((x.N, r, gaMs));
                if (run == 0 && size >= convSize) { convSize = size; convergence = history; }
            }

            foreach (var v in order)
            {
                var a = btAcc[v];
                bt.Add(new BtRow(size, v, a.Solved, o.Runs, a.Nodes / (double)o.Runs, a.Back / (double)o.Runs, a.Ms / (double)o.Runs));
            }
            if (gaRuns.Count > 0)
            {
                double Avg(Func<(int N, PipelineResult R, long GaMs), double> f) => gaRuns.Average(f);
                ga.Add(new GaRow(size, gaRuns[0].N, gaRuns.Count,
                    Avg(r => r.R.SoftBefore), Avg(r => r.R.SoftAfter),
                    Avg(r => r.R.SoftBefore == 0 ? 0 : 100.0 * (r.R.SoftBefore - r.R.SoftAfter) / r.R.SoftBefore),
                    Avg(r => r.R.BtMs), Avg(r => r.GaMs), Avg(r => r.R.Generations),
                    AvgBreakdown(gaRuns.Select(r => r.R.Before!)), AvgBreakdown(gaRuns.Select(r => r.R.After!))));
            }
        }
        return new ExperimentResult(bt, ga, convSize, convergence);
    }

    static Breakdown AvgBreakdown(IEnumerable<Breakdown> bs)
    {
        var l = bs.ToList();
        int A(Func<Breakdown, int> f) => (int)Math.Round(l.Average(f));
        return new Breakdown(A(b => b.ClassGaps), A(b => b.TeacherGaps), A(b => b.LateDifficult), A(b => b.OutsidePreferred), A(b => b.Imbalance));
    }

    /// <summary>Semicolon-separated tables (opens directly in Excel with Romanian settings).</summary>
    public static string ToCsv(ExperimentResult r)
    {
        var ci = CultureInfo.GetCultureInfo("ro-RO");
        string F(double v) => v.ToString("0.##", ci);
        var sb = new StringBuilder();
        sb.AppendLine("Backtracking (limită egală de noduri)");
        sb.AppendLine("Clase;Variantă;Rezolvate;Instanțe;Noduri (medie);Reveniri (medie);Timp ms (medie)");
        foreach (var b in r.Bt) sb.AppendLine($"{b.Size};{b.Variant};{b.Solved};{b.Runs};{F(b.AvgNodes)};{F(b.AvgBacktracks)};{F(b.AvgMs)}");
        sb.AppendLine();
        sb.AppendLine("Algoritm genetic");
        sb.AppendLine("Clase;Lecții;Instanțe;Fitness BT;Fitness GA;Îmbunătățire %;Timp BT ms;Timp GA ms;Generații;Ferestre clasă înainte;după;Ferestre profesor înainte;după;Dificile după ora 4 înainte;după;În afara preferințelor înainte;după;Dezechilibru înainte;după");
        foreach (var g in r.Ga)
        {
            var (b, a) = (g.AvgBefore!, g.AvgAfter!);
            sb.AppendLine($"{g.Size};{g.Lessons};{g.Runs};{F(g.AvgSoftBefore)};{F(g.AvgSoftAfter)};{F(g.AvgImprovementPct)};{F(g.AvgBtMs)};{F(g.AvgGaMs)};{F(g.AvgGenerations)};" +
                $"{b.ClassGaps};{a.ClassGaps};{b.TeacherGaps};{a.TeacherGaps};{b.LateDifficult};{a.LateDifficult};{b.OutsidePreferred};{a.OutsidePreferred};{b.Imbalance};{a.Imbalance}");
        }
        sb.AppendLine();
        sb.AppendLine($"Convergența GA ({r.ConvergenceSize} clase, instanța 1)");
        sb.AppendLine("Generația;Cel mai bun fitness");
        for (int i = 0; i < r.Convergence.Count; i++) sb.AppendLine($"{i};{r.Convergence[i]}");
        return sb.ToString();
    }
}
