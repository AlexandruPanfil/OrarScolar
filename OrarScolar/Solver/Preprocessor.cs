using System.Numerics;
using OrarScolar.Data;

namespace OrarScolar.Solver;

public sealed record PreResult(List<string> Errors, ulong[] Domains);

/// <summary>
/// Necessary-condition checks with Romanian explanations: input checks, pigeonhole, per-class Hall
/// matching (Kuhn), then optional AC-3 over ≠ (class/teacher) and sibling ordering.
/// </summary>
public static class Preprocessor
{
    public static PreResult Run(SolverInput x, bool useAc3)
    {
        var err = new List<string>(x.InputErrors);
        var dom = (ulong[])x.BaseSlots.Clone();
        if (x.N == 0) err.Add("Nu există nicio oră de planificat. Adăugați clase, profesori, materii și planul-cadru (sau generați date demo).");

        for (int c = 0; c < x.ClassCount; c++)
            if (x.ClassLessons[c].Length == 0) err.Add($"Clasa {x.ClassNames[c]} nu are nicio materie în planul-cadru.");

        // pigeonhole
        for (int c = 0; c < x.ClassCount; c++)
            if (x.ClassLessons[c].Length > Slots.Count)
                err.Add($"Clasa {x.ClassNames[c]} are {x.ClassLessons[c].Length} ore pe săptămână, dar există doar {Slots.Count} sloturi.");
        for (int t = 0; t < x.TeacherCount; t++)
        {
            int avail = Slots.Pop(~x.TeacherUnavail[t] & Slots.All);
            if (x.TeacherLessons[t].Length > avail)
                err.Add($"Profesorul {x.TeacherNames[t]} are {x.TeacherLessons[t].Length} ore, dar doar {avail} sloturi disponibile.");
        }
        for (int a = 0; a < x.AssignCount; a++)
        {
            int i = x.AFirst[a], c = x.AClass[a], s = x.ASubject[a];
            if (x.KeyMask[x.Key[i]] == 0)
                err.Add($"{x.SubjectNames[s]} – clasa {x.ClassNames[c]}: nu există nicio sală de tip {x.SubjectRoomType[s].Ro()} cu capacitate ≥ {x.ClassSize[c]}.");
            int days = Enumerable.Range(0, Slots.Days).Count(d => (x.BaseSlots[i] & Slots.DayMask(d)) != 0);
            if (x.AHours[a] > 2 * days)
                err.Add($"{x.SubjectNames[s]} – clasa {x.ClassNames[c]}: {x.AHours[a]} ore, dar profesorul {x.TeacherNames[x.ATeacher[a]]} este disponibil doar în {days} zile (maxim 2 ore/zi).");
        }
        for (int k = 0; k < x.KeyMask.Length; k++)
        {
            int rooms = Slots.Pop(x.KeyMask[k]);
            if (rooms > 0 && x.KeyLessons[k].Length > rooms * Slots.Count)
                err.Add($"Prea multe ore ({x.KeyLessons[k].Length}) pentru {rooms} săli de tip {x.SubjectRoomType[x.Subject[x.KeyLessons[k][0]]].Ro()} ({rooms} × {Slots.Count} sloturi).");
        }

        // Hall's condition per class: lessons -> available slots
        for (int c = 0; c < x.ClassCount; c++)
            if (x.ClassLessons[c].Length <= Slots.Count && HallViolation(x, x.ClassLessons[c]) is { } msg)
                err.Add($"Clasa {x.ClassNames[c]}: {msg}");

        if (err.Count == 0 && useAc3 && Ac3(x, dom) is { } wipe) err.Add(wipe);
        return new PreResult(err, dom);
    }

    /// <summary>Kuhn matching; on failure the alternating-path set S has |N(S)| &lt; |S|.</summary>
    static string? HallViolation(SolverInput x, int[] lessons)
    {
        var match = Enumerable.Repeat(-1, Slots.Count).ToArray(); // slot -> local lesson
        ulong visited = 0;
        bool Try(int u)
        {
            for (ulong m = x.BaseSlots[lessons[u]] & ~visited; m != 0; m &= m - 1)
            {
                int s = Slots.Low(m);
                if ((visited >> s & 1) != 0) continue;
                visited |= 1UL << s;
                if (match[s] < 0 || Try(match[s])) { match[s] = u; return true; }
            }
            return false;
        }
        for (int u = 0; u < lessons.Length; u++)
        {
            visited = 0;
            if (Try(u)) continue;
            var set = new List<int> { lessons[u] };
            for (ulong m = visited; m != 0; m &= m - 1) set.Add(lessons[match[Slots.Low(m)]]);
            var names = set.Select(i => x.TeacherNames[x.Teacher[i]]).Distinct().ToList();
            var who = names.Count == 1 ? $"profesorul {names[0]} are" : $"profesorii {string.Join(", ", names)} au împreună";
            return $"{who} {Slots.Pop(visited)} sloturi disponibile pentru {set.Count} ore.";
        }
        return null;
    }

    /// <summary>AC-3 over ≠ (same class / same teacher) and slot_k &lt; slot_{k+1} for siblings. Returns a wipeout message or null.</summary>
    internal static string? Ac3(SolverInput x, ulong[] dom)
    {
        int n = x.N;
        var queue = new Queue<(int, int)>();
        var inQ = new HashSet<long>();
        void Push(int i, int j) { if (inQ.Add((long)i * n + j)) queue.Enqueue((i, j)); }
        for (int i = 0; i < n; i++) foreach (var j in x.Neighbors[i]) Push(i, j);

        while (queue.Count > 0)
        {
            var (i, j) = queue.Dequeue();
            inQ.Remove((long)i * n + j);
            ulong before = dom[i], dj = dom[j];
            string kind = x.Class[i] == x.Class[j] ? "aceeași clasă" : "același profesor";
            if (x.Assign[i] == x.Assign[j] && x.SiblingIdx[j] == x.SiblingIdx[i] + 1)
                dom[i] &= (1UL << (63 - BitOperations.LeadingZeroCount(dj))) - 1; // below max(Dj)
            else if (x.Assign[i] == x.Assign[j] && x.SiblingIdx[j] == x.SiblingIdx[i] - 1)
                dom[i] &= Slots.After(Slots.Low(dj)); // above min(Dj)
            if (dom[i] == 0) kind = "ordinea orelor aceleiași materii";
            if (Slots.Pop(dj) == 1) dom[i] &= ~dj; // ≠
            if (dom[i] == before) continue;
            if (dom[i] == 0)
                return $"AC-3: lecția {x.Describe(i)} nu mai are niciun slot posibil din cauza lecției {x.Describe(j)} (constrângere: {kind}).";
            foreach (var k in x.Neighbors[i]) if (k != j) Push(k, i);
        }
        return null;
    }
}
