using OrarScolar.Solver;

namespace OrarScolar.Data;

/// <summary>
/// Witness-first demo data: builds a valid timetable first, then derives teachers, rooms and
/// unavailability from it, so the generated instance is solvable by construction.
/// </summary>
public static class SeedGenerator
{
    sealed record SubjDef(string Name, bool Difficult, RoomType Type, int[] Hours); // hours for grades V..XII

    static readonly SubjDef[] Plan =
    [
        new("Limba română", true, RoomType.Normal, [5, 5, 4, 4, 4, 4, 4, 4]),
        new("Matematică", true, RoomType.Normal, [4, 4, 4, 4, 4, 4, 4, 4]),
        new("Limba engleză", false, RoomType.Normal, [2, 2, 2, 2, 2, 2, 2, 2]),
        new("Limba franceză", false, RoomType.Normal, [2, 2, 2, 2, 2, 2, 2, 2]),
        new("Istorie", false, RoomType.Normal, [1, 1, 2, 2, 2, 2, 2, 2]),
        new("Geografie", false, RoomType.Normal, [1, 1, 1, 2, 2, 2, 1, 1]),
        new("Biologie", false, RoomType.Normal, [1, 2, 2, 2, 2, 2, 2, 2]),
        new("Fizică", true, RoomType.Laborator, [0, 2, 2, 2, 3, 3, 3, 3]),
        new("Chimie", true, RoomType.Laborator, [0, 0, 2, 2, 2, 2, 2, 2]),
        new("Informatică", false, RoomType.Informatica, [1, 1, 1, 1, 2, 2, 2, 2]),
        new("Religie", false, RoomType.Normal, [1, 1, 1, 1, 1, 1, 1, 1]),
        new("Educație fizică", false, RoomType.SalaSport, [2, 2, 2, 2, 2, 2, 2, 2]),
        new("Educație plastică", false, RoomType.Normal, [1, 1, 1, 1, 1, 1, 0, 0]),
        new("Educație muzicală", false, RoomType.Normal, [1, 1, 1, 1, 1, 1, 0, 0]),
        new("Educație tehnologică", false, RoomType.Normal, [1, 1, 1, 1, 0, 0, 0, 0]),
        new("Educație socială", false, RoomType.Normal, [1, 1, 1, 1, 0, 0, 0, 0]),
        new("Consiliere și orientare", false, RoomType.Normal, [1, 1, 1, 1, 1, 1, 1, 1]),
        new("Logică și filosofie", false, RoomType.Normal, [0, 0, 0, 0, 1, 1, 2, 2]),
        new("Economie", false, RoomType.Normal, [0, 0, 0, 0, 0, 1, 1, 1]),
        new("Opțional", false, RoomType.Normal, [0, 0, 0, 0, 0, 0, 0, 0]), // fills up to the target load
    ];
    static readonly int[] TargetLoad = [29, 29, 30, 30, 31, 31, 32, 32];
    static readonly string[] Roman = ["V", "VI", "VII", "VIII", "IX", "X", "XI", "XII"];
    static readonly string[] Surnames =
    [
        "Popescu", "Ionescu", "Popa", "Pop", "Radu", "Dumitru", "Stan", "Stoica", "Gheorghe", "Matei",
        "Ciobanu", "Rusu", "Munteanu", "Constantin", "Mihai", "Florea", "Ene", "Serban", "Lazăr", "Dinu",
        "Tudor", "Moldovan", "Barbu", "Nistor", "Ilie", "Marin", "Toma", "Dobre", "Cristea", "Preda",
        "Neagu", "Vasile", "Diaconu", "Oprea", "Sandu", "Voicu", "Iordache", "Mocanu", "Lungu", "Petrescu",
    ];
    static readonly string[] FirstNames = ["Ana", "Mihai", "Elena", "Andrei", "Ioana", "Dan", "Maria", "Radu", "Irina", "Vlad"];
    const int TeacherCap = 22, TeacherAvg = 18;

    public static (SchoolData Data, List<TimetableEntry> Witness) Create(int classCount = 16, int seed = 42)
    {
        for (int attempt = 0; attempt < 20; attempt++)
            if (TryCreate(classCount, seed + attempt) is { } r) return r;
        throw new InvalidOperationException("Generarea datelor demo a eșuat după 20 de încercări.");
    }

    static (SchoolData, List<TimetableEntry>)? TryCreate(int n, int seed)
    {
        var rnd = new Random(seed);

        // 1. structure
        var classes = new List<SchoolClass>();
        var perGrade = new int[8];
        for (int c = 0; c < n; c++)
        {
            int g = c * 8 / n;
            classes.Add(new SchoolClass { Id = c + 1, Grade = g + 5, Name = $"{Roman[g]} {(char)('A' + perGrade[g]++)}", StudentCount = rnd.Next(24, 31) });
        }
        var hours = new int[n][];
        for (int c = 0; c < n; c++)
        {
            int g = classes[c].Grade - 5;
            hours[c] = Plan.Select(p => p.Hours[g]).ToArray();
            hours[c][^1] = Math.Max(0, TargetLoad[g] - hours[c].Sum());
        }
        var used = Enumerable.Range(0, Plan.Length).Where(s => hours.Any(h => h[s] > 0)).ToArray();

        var rooms = new List<Room>();
        void AddRooms(int full, RoomType type, int cap, string prefix, int startNo)
        {
            int count = n == 16 ? full : Math.Max(1, (int)Math.Ceiling(full * n / 16.0));
            if (type == RoomType.Normal) count = Math.Max(count, n);
            for (int k = 0; k < count; k++)
                rooms.Add(new Room { Id = rooms.Count + 1, Name = $"{prefix} {startNo + k}", Capacity = cap, RoomType = type });
        }
        AddRooms(18, RoomType.Normal, 32, "Sala", 101);
        AddRooms(3, RoomType.Laborator, 30, "Laborator", 1);
        AddRooms(2, RoomType.Informatica, 30, "Informatică", 1);
        AddRooms(2, RoomType.SalaSport, 60, "Sala de sport", 1);
        var typeCap = Enum.GetValues<RoomType>().ToDictionary(t => t, t => rooms.Count(r => r.RoomType == t));
        var typeUse = Enum.GetValues<RoomType>().ToDictionary(t => t, _ => new int[Slots.Count]);

        // 3. witness class grids: balanced daily load, staggered start, rotated round-robin, ≤2/day
        var grid = new int[n][];
        for (int c = 0; c < n; c++)
        {
            grid[c] = Enumerable.Repeat(-1, Slots.Count).ToArray();
            int load0 = hours[c].Sum();
            if (load0 > Slots.Count) return null;
            var classSlots = new List<int>();
            for (int d = 0; d < Slots.Days; d++)
            {
                int len = load0 / Slots.Days + ((d + c) % Slots.Days < load0 % Slots.Days ? 1 : 0);
                int start = Math.Min(c % 2, Slots.Periods - len);
                for (int p = start; p < start + len; p++) classSlots.Add(d * Slots.Periods + p);
            }
            classSlots = classSlots.OrderBy(s => s % Slots.Periods).ThenBy(s => s / Slots.Periods).ToList(); // period-major spreads days
            // rotated by class index; if the greedy gets stuck, retry this class with a shuffled order
            bool placed = false;
            for (int attempt = 0; attempt < 50 && !placed; attempt++)
            {
                var order = used.Skip(c % used.Length).Concat(used.Take(c % used.Length)).ToArray();
                if (attempt > 0) rnd.Shuffle(order);
                placed = FillClass(c, order, classSlots);
                if (!placed)
                    for (int q = 0; q < Slots.Count; q++)
                        if (grid[c][q] >= 0) { typeUse[Plan[grid[c][q]].Type][q]--; grid[c][q] = -1; }
            }
            if (!placed) return null;
        }

        bool FillClass(int c, int[] order, List<int> classSlots)
        {
            int ptr = 0;
            foreach (var s in order)
                for (int h = 0; h < hours[c][s]; h++)
                {
                    int found = -1;
                    for (int k = 0; k < classSlots.Count && found < 0; k++)
                    {
                        int slot = classSlots[(ptr + k) % classSlots.Count];
                        if (grid[c][slot] < 0 && DayCount(grid[c], s, slot / Slots.Periods) < 2 && typeUse[Plan[s].Type][slot] < typeCap[Plan[s].Type])
                        { found = slot; ptr = (ptr + k + 1) % classSlots.Count; }
                    }
                    if (found < 0) return false;
                    grid[c][found] = s; typeUse[Plan[s].Type][found]++;
                }
            return true;
        }

        // 2. teacher pool: ceil(total/18) per subject; for the real size adjust to exactly 35
        var total = used.ToDictionary(s => s, s => hours.Sum(h => h[s]));
        var pool = used.SelectMany(s => Enumerable.Repeat(0, (int)Math.Ceiling(total[s] / (double)TeacherAvg)).Select(_ => new List<int> { s })).ToList();
        if (n == 16)
        {
            while (pool.Count > 35)
            {
                var single = pool.Where(p => p.Count == 1 && pool.Count(q => q.Contains(p[0])) == 1).OrderBy(p => total[p[0]]).ToList();
                if (single.Count < 2 || total[single[0][0]] + total[single[1][0]] > TeacherAvg) break;
                single[0].AddRange(single[1]); pool.Remove(single[1]);
            }
            while (pool.Count < 35)
            {
                var s = used.OrderByDescending(s => total[s] / (double)pool.Count(p => p.Contains(s))).First();
                pool.Add([s]);
            }
        }

        // 4. teacher per (class, subject): no witness overlap, load ≤ 22; otherwise swap slots and retry
        var busy = new ulong[pool.Count]; var load = new int[pool.Count];
        var teacherOf = new int[n][];
        int failures = 0;
        for (int c = 0; c < n; c++)
        {
            teacherOf[c] = Enumerable.Repeat(-1, Plan.Length).ToArray();
            foreach (var s in used)
            {
                if (hours[c][s] == 0) continue;
                int t;
                while ((t = PickTeacher(c, s)) < 0)
                    if (++failures > 50 || !TrySwap(c, s)) return null;
                teacherOf[c][s] = t; busy[t] |= Mask(c, s); load[t] += hours[c][s];
            }
        }

        int PickTeacher(int c, int s)
        {
            ulong m = Mask(c, s); int best = -1;
            for (int t = 0; t < pool.Count; t++)
                if (pool[t].Contains(s) && (busy[t] & m) == 0 && load[t] + hours[c][s] <= TeacherCap && (best < 0 || load[t] < load[best])) best = t;
            return best;
        }

        // move one clashing hour of s to a slot the least-loaded candidate teacher has free;
        // revalidate every lesson in both swapped slots (teacher overlap, 2/day, room type)
        bool TrySwap(int c, int s)
        {
            ulong mine = Mask(c, s);
            var slots = Enumerable.Range(0, Slots.Count).ToArray();
            rnd.Shuffle(slots);
            foreach (var t in Enumerable.Range(0, pool.Count).Where(t => pool[t].Contains(s) && load[t] + hours[c][s] <= TeacherCap).OrderBy(t => load[t]))
            {
                int a = Slots.Low(mine & busy[t]);
                foreach (var b in slots)
                    if ((mine >> b & 1) == 0 && (busy[t] >> b & 1) == 0 && Swap(c, a, b)) return true;
            }
            return false;
        }

        bool Swap(int c, int a, int b)
        {
            int s = grid[c][a], s2 = grid[c][b];
            grid[c][a] = s2; grid[c][b] = s;
            bool same = s2 >= 0 && Plan[s2].Type == Plan[s].Type;
            bool ok = DayCount(grid[c], s, b / Slots.Periods) <= 2
                && (same || typeUse[Plan[s].Type][b] + 1 <= typeCap[Plan[s].Type]);
            int t2 = s2 >= 0 ? teacherOf[c][s2] : -1;
            if (s2 >= 0)
                ok = ok && DayCount(grid[c], s2, a / Slots.Periods) <= 2
                    && (same || typeUse[Plan[s2].Type][a] + 1 <= typeCap[Plan[s2].Type])
                    && (t2 < 0 || (busy[t2] >> a & 1) == 0);
            if (!ok) { grid[c][a] = s; grid[c][b] = s2; return false; }
            typeUse[Plan[s].Type][a]--; typeUse[Plan[s].Type][b]++;
            if (s2 >= 0)
            {
                typeUse[Plan[s2].Type][b]--; typeUse[Plan[s2].Type][a]++;
                if (t2 >= 0) busy[t2] = busy[t2] & ~(1UL << b) | 1UL << a;
            }
            return true;
        }

        ulong Mask(int c, int s)
        {
            ulong m = 0;
            for (int q = 0; q < Slots.Count; q++) if (grid[c][q] == s) m |= 1UL << q;
            return m;
        }

        // 5. rooms: best fit per slot by type/capacity
        var entries = new List<(int c, int s, int slot, int room)>();
        var sortedRooms = rooms.OrderBy(r => r.Capacity).ThenBy(r => r.Id).ToList();
        for (int q = 0; q < Slots.Count; q++)
        {
            var taken = new HashSet<int>();
            for (int c = 0; c < n; c++)
            {
                int s = grid[c][q];
                if (s < 0) continue;
                var room = sortedRooms.FirstOrDefault(r => r.RoomType == Plan[s].Type && r.Capacity >= classes[c].StudentCount && !taken.Contains(r.Id));
                if (room == null) return null;
                taken.Add(room.Id);
                entries.Add((c, s, q, room.Id));
            }
        }

        // 6. derived data
        var subjects = used.Select((s, k) => new Subject { Id = k + 1, Name = Plan[s].Name, IsDifficult = Plan[s].Difficult, RoomType = Plan[s].Type }).ToList();
        var subjectId = used.Select((s, k) => (s, k)).ToDictionary(p => p.s, p => p.k + 1);
        var teachers = new List<Teacher>();
        for (int t = 0; t < pool.Count; t++)
        {
            var halves = new List<ulong>();
            for (int d = 0; d < Slots.Days; d++)
            {
                ulong morning = 0xFUL << (d * Slots.Periods), afternoon = 0x70UL << (d * Slots.Periods);
                if ((busy[t] & morning) == 0) halves.Add(morning);
                if ((busy[t] & afternoon) == 0) halves.Add(afternoon);
            }
            ulong unavailable = 0;
            int want = rnd.Next(1, 3);
            for (int k = 0; k < want && halves.Count > 0; k++) { int pick = rnd.Next(halves.Count); unavailable |= halves[pick]; halves.RemoveAt(pick); }
            ulong preferred = busy[t];
            for (int k = 0; k < 5; k++) preferred |= 1UL << rnd.Next(Slots.Count);
            preferred &= ~unavailable;
            string name = t < Surnames.Length ? $"{Surnames[t]} {FirstNames[t % FirstNames.Length]}" : $"Profesor {t + 1}";
            teachers.Add(new Teacher { Id = t + 1, Name = name, UnavailableMask = (long)unavailable, PreferredMask = (long)preferred });
        }

        var assignments = new List<Assignment>();
        var assignId = new Dictionary<(int, int), int>();
        for (int c = 0; c < n; c++)
            foreach (var s in used)
                if (hours[c][s] > 0)
                {
                    var a = new Assignment { Id = assignments.Count + 1, ClassId = c + 1, SubjectId = subjectId[s], TeacherId = teacherOf[c][s] + 1, HoursPerWeek = hours[c][s] };
                    assignments.Add(a); assignId[(c, s)] = a.Id;
                }

        var witness = entries.Select(e => new TimetableEntry { AssignmentId = assignId[(e.c, e.s)], Slot = e.slot, RoomId = e.room }).ToList();
        return (new SchoolData(classes, teachers, subjects, rooms, assignments), witness);
    }

    static int DayCount(int[] row, int s, int d)
    {
        int k = 0;
        for (int p = 0; p < Slots.Periods; p++) if (row[d * Slots.Periods + p] == s) k++;
        return k;
    }
}
