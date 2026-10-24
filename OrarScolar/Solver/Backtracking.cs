using System.Diagnostics;

namespace OrarScolar.Solver;

public enum BtStatus { Success, Unsatisfiable, LimitReached, Cancelled }

public sealed record BtOptions
{
    public bool UseAc3 { get; init; } = true;
    public bool UseForwardChecking { get; init; } = true;
    public bool UseMrv { get; init; } = true;
    public bool UseLcv { get; init; } = true;
    public long MaxNodes { get; init; } = 2_000_000;
    public int MaxRestarts { get; init; } = 5;
    public int Seed { get; init; } = 1;
}

public sealed record BtResult(BtStatus Status, int[] SlotOf, int[] RoomOf, long Nodes, long Backtracks, long ElapsedMs, List<string> Hardest);

/// <summary>
/// Chronological backtracking over bitmasks: MRV (+degree), LCV, forward checking with room keys and
/// counting, restarts. A node = one tentative (lesson, slot, room) assignment, counted before any check.
/// </summary>
public sealed class Backtracking
{
    readonly SolverInput x;
    readonly ulong[] dom;
    readonly BtOptions o;
    readonly CancellationToken ct;
    readonly Deadline deadline;
    readonly Action<string>? progress;

    // search state (internal for the undo test)
    internal readonly ulong[] classBusy, teacherBusy, freeRooms, keyFull, dayFull;
    internal readonly int[] slotOf, roomOf;
    readonly int[] assigned;       // per assignment: number of assigned siblings (always the lowest ones)
    readonly int[] dayCount;       // per assignment*5 + day
    readonly int[] classLeft, teacherLeft, tieRank, wipeouts;
    readonly List<int> flipped = []; // keys flipped full, as a stack
    int done;
    long nodes, attemptNodes, budget, backtracks;
    bool limitHit, stopped;
    BtStatus stopStatus;
    readonly Stopwatch sw = new();
    long lastReport;

    public Backtracking(SolverInput x, ulong[] domains, BtOptions o, CancellationToken ct = default, Deadline deadline = default, Action<string>? progress = null)
    {
        this.x = x; dom = domains; this.o = o; this.ct = ct; this.deadline = deadline; this.progress = progress;
        classBusy = new ulong[x.ClassCount]; teacherBusy = new ulong[x.TeacherCount];
        freeRooms = new ulong[Slots.Count]; keyFull = new ulong[x.KeyMask.Length]; dayFull = new ulong[x.AssignCount];
        slotOf = new int[x.N]; roomOf = new int[x.N];
        assigned = new int[x.AssignCount]; dayCount = new int[x.AssignCount * Slots.Days];
        classLeft = new int[x.ClassCount]; teacherLeft = new int[x.TeacherCount];
        tieRank = new int[x.N]; wipeouts = new int[x.N];
        Reset();
    }

    public sealed record CompareRow(string Variant, BtStatus Status, long Nodes, long Backtracks, long Ms, string Note);

    /// <summary>"Comparație algoritmi": four variants under an equal node cap, no restarts.</summary>
    public static List<CompareRow> Compare(SolverInput x, long cap, CancellationToken ct = default)
    {
        var plain = new BtOptions { UseAc3 = false, UseForwardChecking = false, UseMrv = false, UseLcv = false, MaxNodes = cap, MaxRestarts = 0 };
        (string, BtOptions)[] variants =
        [
            ("Backtracking simplu", plain),
            ("+ AC-3", plain with { UseAc3 = true }),
            ("+ Forward checking", plain with { UseForwardChecking = true }),
            ("AC-3 + FC + MRV + LCV", plain with { UseAc3 = true, UseForwardChecking = true, UseMrv = true, UseLcv = true }),
        ];
        var rows = new List<CompareRow>();
        foreach (var (name, o) in variants)
        {
            ct.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            var pre = Preprocessor.Run(x, o.UseAc3);
            if (pre.Errors.Count > 0) { rows.Add(new(name, BtStatus.Unsatisfiable, 0, 0, sw.ElapsedMilliseconds, pre.Errors[0])); continue; }
            var r = Solve(x, pre.Domains, o, ct);
            rows.Add(new(name, r.Status, r.Nodes, r.Backtracks, sw.ElapsedMilliseconds, ""));
        }
        return rows;
    }

    public static BtResult Solve(SolverInput x, ulong[] domains, BtOptions o, CancellationToken ct = default, Deadline deadline = default, Action<string>? progress = null)
        => new Backtracking(x, domains, o, ct, deadline, progress).Run();

    void Reset()
    {
        Array.Clear(classBusy); Array.Clear(teacherBusy); Array.Clear(dayFull); Array.Clear(assigned); Array.Clear(dayCount);
        Array.Fill(freeRooms, x.AllRooms);
        for (int k = 0; k < keyFull.Length; k++) keyFull[k] = x.KeyMask[k] == 0 ? Slots.All : 0;
        Array.Fill(slotOf, -1); Array.Fill(roomOf, -1);
        for (int c = 0; c < x.ClassCount; c++) classLeft[c] = x.ClassLessons[c].Length;
        for (int t = 0; t < x.TeacherCount; t++) teacherLeft[t] = x.TeacherLessons[t].Length;
        done = 0;
    }

    public BtResult Run()
    {
        sw.Restart();
        int attempts = o.MaxRestarts + 1;
        budget = Math.Max(1, o.MaxNodes / attempts);
        var status = BtStatus.LimitReached;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            Reset();
            for (int i = 0; i < x.N; i++) tieRank[i] = i;
            if (attempt > 0) new Random(o.Seed + attempt).Shuffle(tieRank);
            attemptNodes = 0; limitHit = false;
            if (CheckStop()) { status = stopStatus; break; }
            bool ok = Search();
            if (ok) { status = BtStatus.Success; break; }
            if (stopped) { status = stopStatus; break; }
            if (!limitHit) { status = BtStatus.Unsatisfiable; break; }
        }
        var hardest = Enumerable.Range(0, x.N).Where(i => wipeouts[i] > 0).OrderByDescending(i => wipeouts[i]).ThenBy(i => i)
            .Take(3).Select(i => $"{x.Describe(i)}: {wipeouts[i]} eșecuri").ToList();
        bool success = status == BtStatus.Success;
        return new BtResult(status, success ? (int[])slotOf.Clone() : [], success ? (int[])roomOf.Clone() : [],
            nodes, backtracks, sw.ElapsedMilliseconds, success ? [] : hardest);
    }

    int Prev(int a) => assigned[a] == 0 ? -1 : slotOf[x.AFirst[a] + assigned[a] - 1];

    /// <summary>Effective domain, O(1).</summary>
    internal ulong Eff(int i)
    {
        int a = x.Assign[i];
        return dom[i] & ~classBusy[x.Class[i]] & ~teacherBusy[x.Teacher[i]] & ~keyFull[x.Key[i]] & ~dayFull[a] & Slots.After(Prev(a));
    }

    ulong Domain(int i) => o.UseForwardChecking ? Eff(i) : dom[i] & Slots.After(Prev(x.Assign[i]));

    bool Search()
    {
        if (done == x.N) return true;
        int v = SelectVariable();
        int a = x.Assign[v];
        ulong d = Domain(v);
        Span<int> values = stackalloc int[Slots.Count];
        int nv = OrderValues(v, d, values);

        for (int vi = 0; vi < nv; vi++)
        {
            int s = values[vi];
            if (attemptNodes >= budget) { limitHit = true; return false; }
            nodes++; attemptNodes++;
            if ((nodes & 1023) == 0 && CheckStop()) return false;

            ulong bit = 1UL << s;
            ulong free = freeRooms[s] & x.KeyMask[x.Key[v]];
            if (!o.UseForwardChecking &&
                (free == 0 || ((classBusy[x.Class[v]] | teacherBusy[x.Teacher[v]] | dayFull[a]) & bit) != 0))
                continue; // plain: consistency check against assigned lessons failed
            int r = Slots.Low(free); // best-fit: rooms are ordered by capacity
            int mark = flipped.Count;
            Assign(v, s, r);
            if (o.UseForwardChecking && !ForwardCheck(v, s, mark)) { Undo(v, mark); continue; }
            if (Search()) return true;
            Undo(v, mark);
            if (stopped || limitHit) return false;
        }
        wipeouts[v]++;
        backtracks++;
        return false;
    }

    bool CheckStop()
    {
        if (ct.IsCancellationRequested) { stopped = true; stopStatus = BtStatus.Cancelled; return true; }
        if (deadline.Passed) { stopped = true; stopStatus = BtStatus.LimitReached; return true; }
        if (progress != null && sw.ElapsedMilliseconds - lastReport >= 200)
        {
            lastReport = sw.ElapsedMilliseconds;
            progress($"Backtracking: {nodes:N0} noduri, {done}/{x.N} lecții plasate");
        }
        return false;
    }

    int SelectVariable()
    {
        int best = -1, bestSize = int.MaxValue, bestDeg = -1;
        for (int a = 0; a < x.AssignCount; a++)
        {
            if (assigned[a] == x.AHours[a]) continue;
            int i = x.AFirst[a] + assigned[a]; // only the lowest unassigned sibling is eligible
            if (!o.UseMrv) { if (best < 0 || tieRank[i] < tieRank[best]) best = i; continue; } // static order, reshuffled on restarts
            int size = Slots.Pop(Domain(i)), deg = x.Neighbors[i].Length;
            if (size < bestSize || size == bestSize && (deg > bestDeg || deg == bestDeg && tieRank[i] < tieRank[best]))
            { best = i; bestSize = size; bestDeg = deg; }
        }
        return best;
    }

    int OrderValues(int v, ulong d, Span<int> values)
    {
        int n = 0;
        for (ulong m = d; m != 0; m &= m - 1) values[n++] = Slots.Low(m);
        if (!o.UseLcv || n < 2) return n;
        // LCV: ascending count of unassigned class/teacher neighbors that still contain s (ties by slot)
        Span<int> cost = stackalloc int[Slots.Count];
        foreach (var j in x.Neighbors[v])
        {
            if (slotOf[j] >= 0) continue;
            ulong dj = Domain(j) & d;
            for (; dj != 0; dj &= dj - 1) cost[Slots.Low(dj)]++;
        }
        var arr = values[..n].ToArray();
        var c = cost.ToArray();
        Array.Sort(arr, (p, q) => c[p] != c[q] ? c[p].CompareTo(c[q]) : p.CompareTo(q));
        arr.CopyTo(values);
        return n;
    }

    void Assign(int i, int s, int r)
    {
        ulong bit = 1UL << s;
        int a = x.Assign[i], c = x.Class[i], t = x.Teacher[i];
        classBusy[c] |= bit; teacherBusy[t] |= bit;
        freeRooms[s] &= ~(1UL << r);
        foreach (var k in x.RoomKeys[r])
            if ((freeRooms[s] & x.KeyMask[k]) == 0 && (keyFull[k] & bit) == 0) { keyFull[k] |= bit; flipped.Add(k); }
        int d = s / Slots.Periods;
        if (++dayCount[a * Slots.Days + d] == 2) dayFull[a] |= Slots.DayMask(d);
        slotOf[i] = s; roomOf[i] = r; assigned[a]++;
        classLeft[c]--; teacherLeft[t]--; done++;
    }

    void Undo(int i, int mark)
    {
        int s = slotOf[i], r = roomOf[i];
        ulong bit = 1UL << s;
        int a = x.Assign[i], c = x.Class[i], t = x.Teacher[i];
        classBusy[c] &= ~bit; teacherBusy[t] &= ~bit;
        freeRooms[s] |= 1UL << r;
        for (int f = flipped.Count - 1; f >= mark; f--) keyFull[flipped[f]] &= ~bit;
        flipped.RemoveRange(mark, flipped.Count - mark);
        int d = s / Slots.Periods;
        if (dayCount[a * Slots.Days + d]-- == 2) dayFull[a] &= ~Slots.DayMask(d);
        slotOf[i] = -1; roomOf[i] = -1; assigned[a]--;
        classLeft[c]++; teacherLeft[t]++; done--;
    }

    bool ForwardCheck(int v, int s, int mark)
    {
        foreach (var j in x.Neighbors[v])
            if (slotOf[j] < 0 && Eff(j) == 0) { wipeouts[j]++; return false; }
        for (int f = mark; f < flipped.Count; f++)
            foreach (var j in x.KeyLessons[flipped[f]])
                if (slotOf[j] < 0 && Eff(j) == 0) { wipeouts[j]++; return false; }
        return Counting(x.ClassLessons[x.Class[v]], classLeft[x.Class[v]])
            && Counting(x.TeacherLessons[x.Teacher[v]], teacherLeft[x.Teacher[v]]);
    }

    /// <summary>Unassigned count must fit in the union of their effective domains.</summary>
    bool Counting(int[] lessons, int left)
    {
        if (left == 0) return true;
        ulong union = 0;
        foreach (var j in lessons) if (slotOf[j] < 0) union |= Eff(j);
        return Slots.Pop(union) >= left;
    }
}
