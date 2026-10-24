using System.Diagnostics;

namespace OrarScolar.Solver;

public enum PipelineStatus { InvalidInput, Success, Unsatisfiable, LimitReached, Cancelled }

public sealed record PipelineOptions
{
    public BtOptions Bt { get; init; } = new();
    public GaOptions Ga { get; init; } = new();
    /// <summary>One deadline for all stages; zero = off.</summary>
    public TimeSpan Deadline { get; init; } = TimeSpan.FromSeconds(60);
    /// <summary>Deadline off: same input + seed + budgets give the same timetable.</summary>
    public bool Reproducible { get; init; }
}

public sealed record PipelineResult(
    PipelineStatus Status, List<string> Errors, int[] SlotOf, int[] RoomOf,
    long BtNodes, long BtBacktracks, long BtMs, long ElapsedMs,
    int SoftBefore, int SoftAfter, Breakdown? Before, Breakdown? After, int Generations, string StoppedBy);

/// <summary>Preprocess → backtracking → genetic, under one deadline.</summary>
public static class Pipeline
{
    public static PipelineResult Run(SolverInput x, PipelineOptions o, CancellationToken ct = default, Action<string>? progress = null)
    {
        var sw = Stopwatch.StartNew();
        var deadline = o.Reproducible ? Deadline.None : Deadline.In(o.Deadline);
        PipelineResult Fail(PipelineStatus st, List<string> errors, BtResult? bt = null) =>
            new(st, errors, [], [], bt?.Nodes ?? 0, bt?.Backtracks ?? 0, bt?.ElapsedMs ?? 0, sw.ElapsedMilliseconds, 0, 0, null, null, 0, "");

        progress?.Report("Preprocesare…");
        var pre = Preprocessor.Run(x, o.Bt.UseAc3);
        if (pre.Errors.Count > 0) return Fail(PipelineStatus.InvalidInput, pre.Errors);
        if (ct.IsCancellationRequested) return Fail(PipelineStatus.Cancelled, []);

        var bt = Backtracking.Solve(x, pre.Domains, o.Bt, ct, deadline, progress);
        if (bt.Status != BtStatus.Success)
        {
            var st = bt.Status switch
            {
                BtStatus.Cancelled => PipelineStatus.Cancelled,
                BtStatus.Unsatisfiable => PipelineStatus.Unsatisfiable,
                _ => PipelineStatus.LimitReached,
            };
            return Fail(st, bt.Hardest.Select(h => "Lecție dificilă: " + h).ToList(), bt);
        }

        var ga = Genetic.Run(x, bt.SlotOf, bt.RoomOf, o.Ga, ct, deadline, progress);
        if (ga.StoppedBy == GaStop.Cancelled) return Fail(PipelineStatus.Cancelled, [], bt);
        return new(PipelineStatus.Success, [], ga.SlotOf, ga.RoomOf, bt.Nodes, bt.Backtracks, bt.ElapsedMs, sw.ElapsedMilliseconds,
            ga.SoftBefore, ga.SoftAfter, ga.Before, ga.After, ga.Generations, ga.StoppedBy.ToString());
    }

    static void Report(this Action<string> p, string s) => p(s);
}
