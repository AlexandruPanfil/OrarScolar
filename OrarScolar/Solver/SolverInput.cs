using OrarScolar.Data;

namespace OrarScolar.Solver;

/// <summary>
/// Array view of the school. Lesson i = one teaching hour; an assignment with H hours gives H siblings
/// (consecutive indices), so hour completeness holds by construction. Everything is ordered by Id
/// (rooms by capacity, then Id, so the lowest free bit of a key is the best-fit room).
/// </summary>
public sealed class SolverInput
{
    public int ClassCount, TeacherCount, RoomCount, AssignCount, N;
    public string[] ClassNames = [], TeacherNames = [], SubjectNames = [], RoomNames = [];
    public int[] ClassIds = [], TeacherIds = [], RoomIds = [], AssignIds = [];
    public int[] ClassSize = [], RoomCap = [];
    public RoomType[] RoomTypes = [], SubjectRoomType = [];
    public ulong[] TeacherUnavail = [], TeacherPref = [];

    // assignments
    public int[] AClass = [], ATeacher = [], ASubject = [], AHours = [], AFirst = [];

    // lessons
    public int[] Class = [], Teacher = [], Assign = [], Subject = [], SiblingIdx = [], Key = [];
    public bool[] Difficult = [];
    public ulong[] BaseSlots = [];

    // room keys: distinct admissible-room masks
    public ulong[] KeyMask = [];
    public int[][] KeyLessons = [], RoomKeys = [];

    public int[][] Neighbors = [], ClassLessons = [], TeacherLessons = [];
    public ulong AllRooms;
    public List<string> InputErrors = [];

    public string Describe(int i) =>
        $"{SubjectNames[Subject[i]]} – clasa {ClassNames[Class[i]]} (ora {SiblingIdx[i] + 1}, prof. {TeacherNames[Teacher[i]]})";

    public static SolverInput FromEntities(SchoolData d)
    {
        var x = new SolverInput();
        var classes = d.Classes.OrderBy(c => c.Id).ToList();
        var teachers = d.Teachers.OrderBy(t => t.Id).ToList();
        var subjects = d.Subjects.OrderBy(s => s.Id).ToList();
        var rooms = d.Rooms.OrderBy(r => r.Capacity).ThenBy(r => r.Id).ToList();
        if (rooms.Count > 64)
        {
            x.InputErrors.Add($"Sunt {rooms.Count} săli; maximul suportat este 64.");
            rooms = rooms.Take(64).ToList();
        }

        x.ClassCount = classes.Count; x.TeacherCount = teachers.Count; x.RoomCount = rooms.Count;
        x.ClassNames = classes.Select(c => c.Name).ToArray();
        x.ClassIds = classes.Select(c => c.Id).ToArray();
        x.ClassSize = classes.Select(c => c.StudentCount).ToArray();
        x.TeacherNames = teachers.Select(t => t.Name).ToArray();
        x.TeacherIds = teachers.Select(t => t.Id).ToArray();
        x.TeacherUnavail = teachers.Select(t => (ulong)t.UnavailableMask & Slots.All).ToArray();
        x.TeacherPref = teachers.Select(t => (ulong)t.PreferredMask & Slots.All).ToArray();
        x.SubjectNames = subjects.Select(s => s.Name).ToArray();
        x.SubjectRoomType = subjects.Select(s => s.RoomType).ToArray();
        var subjDifficult = subjects.Select(s => s.IsDifficult).ToArray();
        x.RoomNames = rooms.Select(r => r.Name).ToArray();
        x.RoomIds = rooms.Select(r => r.Id).ToArray();
        x.RoomCap = rooms.Select(r => r.Capacity).ToArray();
        x.RoomTypes = rooms.Select(r => r.RoomType).ToArray();
        x.AllRooms = x.RoomCount == 64 ? ulong.MaxValue : (1UL << x.RoomCount) - 1;

        var ci = Index(x.ClassIds); var ti = Index(x.TeacherIds); var si = Index(subjects.Select(s => s.Id).ToArray());
        var aC = new List<int>(); var aT = new List<int>(); var aS = new List<int>(); var aH = new List<int>(); var aId = new List<int>();
        foreach (var a in d.Assignments.OrderBy(a => a.Id))
        {
            if (!ci.TryGetValue(a.ClassId, out var c) || !ti.TryGetValue(a.TeacherId, out var t) || !si.TryGetValue(a.SubjectId, out var s))
            { x.InputErrors.Add($"Asignarea #{a.Id} are referințe invalide (clasă, profesor sau materie inexistentă)."); continue; }
            if (a.HoursPerWeek is < 1 or > 10)
            { x.InputErrors.Add($"{x.SubjectNames[s]} – clasa {x.ClassNames[c]}: {a.HoursPerWeek} ore (permis 1..10)."); continue; }
            aC.Add(c); aT.Add(t); aS.Add(s); aH.Add(a.HoursPerWeek); aId.Add(a.Id);
        }
        x.AssignCount = aC.Count;
        x.AClass = [.. aC]; x.ATeacher = [.. aT]; x.ASubject = [.. aS]; x.AHours = [.. aH]; x.AssignIds = [.. aId];
        x.AFirst = new int[x.AssignCount];

        var L = new List<(int a, int k)>();
        for (int a = 0; a < x.AssignCount; a++) { x.AFirst[a] = L.Count; for (int k = 0; k < x.AHours[a]; k++) L.Add((a, k)); }
        int n = x.N = L.Count;
        x.Class = new int[n]; x.Teacher = new int[n]; x.Assign = new int[n]; x.Subject = new int[n];
        x.SiblingIdx = new int[n]; x.Key = new int[n]; x.Difficult = new bool[n]; x.BaseSlots = new ulong[n];

        var keyOf = new Dictionary<ulong, int>(); var keyMasks = new List<ulong>();
        for (int i = 0; i < n; i++)
        {
            var (a, k) = L[i];
            int c = x.AClass[a], t = x.ATeacher[a], s = x.ASubject[a];
            x.Class[i] = c; x.Teacher[i] = t; x.Assign[i] = a; x.Subject[i] = s; x.SiblingIdx[i] = k;
            x.Difficult[i] = subjDifficult[s];
            x.BaseSlots[i] = ~x.TeacherUnavail[t] & Slots.All;
            ulong mask = 0;
            for (int r = 0; r < x.RoomCount; r++)
                if (x.RoomTypes[r] == x.SubjectRoomType[s] && x.RoomCap[r] >= x.ClassSize[c]) mask |= 1UL << r;
            if (!keyOf.TryGetValue(mask, out var key)) { key = keyMasks.Count; keyOf[mask] = key; keyMasks.Add(mask); }
            x.Key[i] = key;
        }
        x.KeyMask = [.. keyMasks];
        x.KeyLessons = Enumerable.Range(0, keyMasks.Count).Select(k => Enumerable.Range(0, n).Where(i => x.Key[i] == k).ToArray()).ToArray();
        x.RoomKeys = Enumerable.Range(0, x.RoomCount).Select(r => Enumerable.Range(0, keyMasks.Count).Where(k => (keyMasks[k] >> r & 1) != 0).ToArray()).ToArray();
        x.ClassLessons = Enumerable.Range(0, x.ClassCount).Select(c => Enumerable.Range(0, n).Where(i => x.Class[i] == c).ToArray()).ToArray();
        x.TeacherLessons = Enumerable.Range(0, x.TeacherCount).Select(t => Enumerable.Range(0, n).Where(i => x.Teacher[i] == t).ToArray()).ToArray();
        x.Neighbors = Enumerable.Range(0, n).Select(i => x.ClassLessons[x.Class[i]].Union(x.TeacherLessons[x.Teacher[i]]).Where(j => j != i).OrderBy(j => j).ToArray()).ToArray();
        return x;
    }

    static Dictionary<int, int> Index(int[] ids) => ids.Select((id, i) => (id, i)).ToDictionary(p => p.id, p => p.i);

    /// <summary>Maps (AssignmentId, Slot, RoomId) entries to lesson arrays; siblings sorted by slot. Unknown entries → -1.</summary>
    public (int[] SlotOf, int[] RoomOf) FromEntries(IEnumerable<TimetableEntry> entries)
    {
        var slotOf = Enumerable.Repeat(-1, N).ToArray(); var roomOf = Enumerable.Repeat(-1, N).ToArray();
        var ai = Index(AssignIds); var ri = Index(RoomIds);
        foreach (var g in entries.GroupBy(e => e.AssignmentId))
        {
            if (!ai.TryGetValue(g.Key, out var a)) continue;
            int k = 0;
            foreach (var e in g.OrderBy(e => e.Slot).Take(AHours[a]))
            {
                slotOf[AFirst[a] + k] = e.Slot;
                roomOf[AFirst[a] + k] = ri.GetValueOrDefault(e.RoomId, -1);
                k++;
            }
        }
        return (slotOf, roomOf);
    }

    public List<TimetableEntry> ToEntries(int[] slotOf, int[] roomOf) =>
        Enumerable.Range(0, N).Select(i => new TimetableEntry { AssignmentId = AssignIds[Assign[i]], Slot = slotOf[i], RoomId = RoomIds[roomOf[i]] }).ToList();
}
