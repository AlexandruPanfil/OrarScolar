using System.Diagnostics;

namespace OrarScolar.Solver;

public sealed record GaOptions
{
    public int Population { get; init; } = 60;
    public int Generations { get; init; } = 500;
    public int Tournament { get; init; } = 3;
    public double Crossover { get; init; } = 0.8;
    public double Mutation { get; init; } = 0.3;
    public int Elitism { get; init; } = 2;
    public int Stagnation { get; init; } = 100;
    public int Seed { get; init; } = 1;
}

public enum GaStop { Generations, Stagnation, Deadline, Cancelled }

public sealed record GaResult(int[] SlotOf, int[] RoomOf, int SoftBefore, int SoftAfter, Breakdown Before, Breakdown After, int Generations, GaStop StoppedBy);

/// <summary>
/// Genetic optimization of a feasible timetable. Lexicographic (Hard, Soft); the archive keeps the best
/// Hard == 0 individual (seeded with the BT solution) and is the output, so the result never regresses.
/// </summary>
public sealed class Genetic
{
    internal sealed class Ind(int[] s, int[] r)
    {
        public int[] S = s, R = r;
        public int Hard, Soft;
        public Ind Clone() => new((int[])S.Clone(), (int[])R.Clone()) { Hard = Hard, Soft = Soft };
        public bool Better(Ind o) => Hard != o.Hard ? Hard < o.Hard : Soft < o.Soft;
    }

    readonly SolverInput x;
    readonly GaOptions o;
    readonly Random rnd;
    readonly CancellationToken ct;
    readonly Deadline deadline;
    bool stop;
    GaStop stopReason;

    internal Genetic(SolverInput x, GaOptions o, CancellationToken ct = default, Deadline deadline = default)
    {
        this.x = x; this.o = o; this.ct = ct; this.deadline = deadline; rnd = new Random(o.Seed);
    }

    public static GaResult Run(SolverInput x, int[] slotOf, int[] roomOf, GaOptions o, CancellationToken ct = default, Deadline deadline = default, Action<string>? progress = null)
        => new Genetic(x, o, ct, deadline).Run(slotOf, roomOf, progress);

    bool Stopped()
    {
        if (stop) return true;
        if (ct.IsCancellationRequested) { stop = true; stopReason = GaStop.Cancelled; }
        else if (deadline.Passed) { stop = true; stopReason = GaStop.Deadline; }
        return stop;
    }

    GaResult Run(int[] slotOf, int[] roomOf, Action<string>? progress)
    {
        var sw = Stopwatch.StartNew();
        long lastReport = 0;
        var seed = new Ind((int[])slotOf.Clone(), (int[])roomOf.Clone());
        Canonical(seed); Evaluate(seed);
        var before = Fitness.Evaluate(x, seed.S, seed.R).B;
        var archive = seed.Clone();

        var pop = new List<Ind> { seed };
        while (pop.Count < o.Population && !Stopped())
        {
            var ind = seed.Clone();
            for (int k = 0; k < 10; k++) Mutate(ind);
            Repair(ind); Evaluate(ind);
            pop.Add(ind);
            if (ind.Hard == 0 && ind.Soft < archive.Soft) archive = ind.Clone();
        }

        int gen = 0, sinceImprovement = 0;
        GaStop reason = GaStop.Generations;
        for (; gen < o.Generations; gen++)
        {
            if (Stopped()) break;
            if (sinceImprovement >= o.Stagnation) { reason = GaStop.Stagnation; break; }
            pop.Sort((a, b) => a.Hard != b.Hard ? a.Hard.CompareTo(b.Hard) : a.Soft.CompareTo(b.Soft));
            var next = pop.Take(o.Elitism).Select(p => p.Clone()).ToList();
            bool improved = false;
            while (next.Count < o.Population && !Stopped())
            {
                Ind p1 = Tournament(pop), p2 = Tournament(pop);
                var child = rnd.NextDouble() < o.Crossover ? Crossover(p1, p2) : p1.Clone();
                if (rnd.NextDouble() < o.Mutation) { int m = rnd.Next(1, 3); for (int k = 0; k < m; k++) Mutate(child); }
                Repair(child); Evaluate(child);
                next.Add(child);
                if (child.Hard == 0 && child.Soft < archive.Soft) { archive = child.Clone(); improved = true; }
            }
            pop = next;
            sinceImprovement = improved ? 0 : sinceImprovement + 1;
            if (progress != null && sw.ElapsedMilliseconds - lastReport >= 200)
            {
                lastReport = sw.ElapsedMilliseconds;
                progress($"Genetic: generația {gen + 1}/{o.Generations}, cel mai bun soft = {archive.Soft}");
            }
        }
        if (stop) reason = stopReason;
        var after = Fitness.Evaluate(x, archive.S, archive.R).B;
        return new GaResult(archive.S, archive.R, before.Soft, after.Soft, before, after, gen, reason);
    }

    void Evaluate(Ind ind) { var (h, s, _) = Fitness.Evaluate(x, ind.S, ind.R); ind.Hard = h; ind.Soft = s; }

    Ind Tournament(List<Ind> pop)
    {
        var best = pop[rnd.Next(pop.Count)];
        for (int k = 1; k < o.Tournament; k++) { var c = pop[rnd.Next(pop.Count)]; if (c.Better(best)) best = c; }
        return best;
    }

    /// <summary>Siblings sorted by slot (rooms travel with their slot).</summary>
    internal void Canonical(Ind ind)
    {
        for (int a = 0; a < x.AssignCount; a++)
        {
            int f = x.AFirst[a], h = x.AHours[a];
            if (h < 2) continue;
            Array.Sort(ind.S, ind.R, f, h);
        }
    }

    /// <summary>
    /// Per class c, a random day subset D: lessons on days in D come from p1, the others from p2.
    /// Phase 1 drops extra hours (random); phase 2 fills missing hours into class-free, teacher-available
    /// slots respecting 2/day (teacher-free preferred). If a missing hour has no such slot, the child is
    /// discarded and p1 is cloned. The child inherits its parents' class validity.
    /// </summary>
    internal Ind Crossover(Ind p1, Ind p2, Func<int, int>? daysOf = null)
    {
        var child = new Ind(new int[x.N], Enumerable.Repeat(-1, x.N).ToArray());
        var teacherBusy = new ulong[x.TeacherCount];
        for (int c = 0; c < x.ClassCount; c++)
        {
            int D = daysOf?.Invoke(c) ?? rnd.Next(32);
            var lessons = x.ClassLessons[c];
            var byAssign = new Dictionary<int, List<(int s, int r)>>();
            foreach (var i in lessons) byAssign.TryAdd(x.Assign[i], []);
            foreach (var (p, fromP1) in new[] { (p1, true), (p2, false) })
                foreach (var i in lessons)
                {
                    int s = p.S[i];
                    if ((D >> (s / Slots.Periods) & 1) == 1 == fromP1) byAssign[x.Assign[i]].Add((s, p.R[i]));
                }
            ulong classUsed = 0;
            foreach (var (a, list) in byAssign) // phase 1: drop extras
            {
                while (list.Count > x.AHours[a]) list.RemoveAt(rnd.Next(list.Count));
                foreach (var e in list) classUsed |= 1UL << e.s;
            }
            foreach (var (a, list) in byAssign) // phase 2: fill missing
                while (list.Count < x.AHours[a])
                {
                    int t = x.ATeacher[a];
                    ulong dayFull = 0;
                    for (int d = 0; d < Slots.Days; d++)
                        if (list.Count(e => e.s / Slots.Periods == d) >= 2) dayFull |= Slots.DayMask(d);
                    ulong cand = x.BaseSlots[x.AFirst[a]] & ~classUsed & ~dayFull & Slots.All;
                    if (cand == 0) return p1.Clone();
                    ulong pref = cand & ~teacherBusy[t];
                    ulong pick = pref != 0 ? pref : cand;
                    int s = NthBit(pick, rnd.Next(Slots.Pop(pick)));
                    list.Add((s, -1)); classUsed |= 1UL << s;
                }
            foreach (var (a, list) in byAssign)
            {
                list.Sort((u, v) => u.s.CompareTo(v.s));
                for (int k = 0; k < list.Count; k++)
                {
                    int i = x.AFirst[a] + k;
                    child.S[i] = list[k].s; child.R[i] = list[k].r;
                    teacherBusy[x.Teacher[i]] |= 1UL << list[k].s;
                }
            }
        }
        // rooms for filled lessons: best-fit free, else the smallest admissible (repair resolves clashes)
        var roomUsed = new ulong[Slots.Count];
        for (int i = 0; i < x.N; i++) if (child.R[i] >= 0) roomUsed[child.S[i]] |= 1UL << child.R[i];
        for (int i = 0; i < x.N; i++)
            if (child.R[i] < 0)
            {
                ulong key = x.KeyMask[x.Key[i]], free = key & ~roomUsed[child.S[i]];
                child.R[i] = Slots.Low(free != 0 ? free : key);
                roomUsed[child.S[i]] |= 1UL << child.R[i];
            }
        return child;
    }

    static int NthBit(ulong m, int n) { for (; n > 0; n--) m &= m - 1; return Slots.Low(m); }

    /// <summary>50%: move a lesson to an empty slot of its class; 50%: swap two lessons of the same class from different assignments.</summary>
    internal void Mutate(Ind ind)
    {
        int i = rnd.Next(x.N), c = x.Class[i];
        var cls = x.ClassLessons[c];
        if (rnd.Next(2) == 0)
        {
            ulong used = 0;
            foreach (var j in cls) used |= 1UL << ind.S[j];
            ulong empty = Slots.All & ~used;
            if (empty == 0) return;
            ind.S[i] = NthBit(empty, rnd.Next(Slots.Pop(empty)));
            FixRoom(ind, i);
        }
        else
        {
            var others = cls.Where(j => x.Assign[j] != x.Assign[i]).ToArray();
            if (others.Length == 0) return;
            int j = others[rnd.Next(others.Length)];
            (ind.S[i], ind.S[j]) = (ind.S[j], ind.S[i]);
            FixRoom(ind, i); FixRoom(ind, j);
        }
        Canonical(ind);
    }

    /// <summary>Keep the room if free and admissible at the new slot, else best-fit.</summary>
    void FixRoom(Ind ind, int i)
    {
        ulong used = 0;
        for (int j = 0; j < x.N; j++) if (j != i && ind.S[j] == ind.S[i]) used |= 1UL << ind.R[j];
        ulong key = x.KeyMask[x.Key[i]], free = key & ~used;
        if ((free >> ind.R[i] & 1) != 0) return;
        ind.R[i] = Slots.Low(free != 0 ? free : key);
    }

    /// <summary>
    /// For each violating lesson (seeded order): move it to the first valid (slot, best-fit room);
    /// else swap with a same-class lesson if both stay valid; else leave it (counted in Hard).
    /// </summary>
    internal void Repair(Ind ind)
    {
        int S = Slots.Count;
        var classCnt = new int[x.ClassCount * S]; var teacherCnt = new int[x.TeacherCount * S];
        var roomCnt = new int[x.RoomCount * S]; var dayCnt = new int[x.AssignCount * Slots.Days];
        void Add(int i, int d)
        {
            int s = ind.S[i];
            classCnt[x.Class[i] * S + s] += d; teacherCnt[x.Teacher[i] * S + s] += d;
            roomCnt[ind.R[i] * S + s] += d; dayCnt[x.Assign[i] * Slots.Days + s / Slots.Periods] += d;
        }
        for (int i = 0; i < x.N; i++) Add(i, 1);

        bool Admissible(int i, int r) => (x.KeyMask[x.Key[i]] >> r & 1) != 0;
        bool Violating(int i)
        {
            int s = ind.S[i];
            return classCnt[x.Class[i] * S + s] > 1 || teacherCnt[x.Teacher[i] * S + s] > 1 || roomCnt[ind.R[i] * S + s] > 1
                || dayCnt[x.Assign[i] * Slots.Days + s / Slots.Periods] > 2
                || (x.TeacherUnavail[x.Teacher[i]] >> s & 1) != 0 || !Admissible(i, ind.R[i]);
        }
        // room for lesson i (not counted) at slot s if (s) is valid for it; -1 otherwise
        int ValidRoom(int i, int s)
        {
            if (classCnt[x.Class[i] * S + s] > 0 || teacherCnt[x.Teacher[i] * S + s] > 0
                || dayCnt[x.Assign[i] * Slots.Days + s / Slots.Periods] >= 2 || (x.TeacherUnavail[x.Teacher[i]] >> s & 1) != 0) return -1;
            for (ulong m = x.KeyMask[x.Key[i]]; m != 0; m &= m - 1)
                if (roomCnt[Slots.Low(m) * S + s] == 0) return Slots.Low(m);
            return -1;
        }

        var order = Enumerable.Range(0, x.N).ToArray();
        rnd.Shuffle(order);
        foreach (var i in order)
        {
            if (Stopped()) break;
            if (!Violating(i)) continue;
            Add(i, -1);
            bool moved = false, swapped = false;
            for (int s = 0; s < S && !moved; s++)
            {
                int r = ValidRoom(i, s);
                if (r >= 0) { ind.S[i] = s; ind.R[i] = r; moved = true; }
            }
            if (!moved)
                foreach (var j in x.ClassLessons[x.Class[i]])
                {
                    if (x.Assign[j] == x.Assign[i]) continue;
                    int si = ind.S[i], ri = ind.R[i], sj = ind.S[j], rj = ind.R[j];
                    Add(j, -1);
                    int nri = ValidRoom(i, sj);
                    if (nri >= 0)
                    {
                        ind.S[i] = sj; ind.R[i] = nri; Add(i, 1);
                        int nrj = ValidRoom(j, si);
                        if (nrj >= 0) { ind.S[j] = si; ind.R[j] = nrj; Add(j, 1); swapped = true; break; }
                        Add(i, -1); ind.S[i] = si; ind.R[i] = ri;
                    }
                    ind.S[j] = sj; ind.R[j] = rj; Add(j, 1);
                }
            if (!swapped) Add(i, 1); // moved, or left as is (counted in Hard)
        }
        Canonical(ind);
    }
}
