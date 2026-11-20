using Microsoft.EntityFrameworkCore;
using OrarScolar.Solver;

namespace OrarScolar.Data;

public enum Tip { Clasa, Profesor, Sala }

/// <summary>A 5×7 grid for one class/teacher/room. Cells[slot] = lines to show (empty = free).</summary>
public sealed record OrarGridModel(string Title, string[][] Cells);

/// <summary>One cell of the school-wide timetable: short texts for the grid, Full for a tooltip.</summary>
public sealed record GeneralCell(string Subject, string Teacher, string Room, string Full);

/// <summary>One class of the school-wide timetable; Cells[slot] is null when the class is free.</summary>
public sealed record GeneralRow(string ClassName, GeneralCell?[] Cells);

/// <summary>Read side of the saved timetable, shared by the /orar page and the exports.</summary>
public sealed class OrarView
{
    public required Timetable Timetable { get; init; }
    public required Dictionary<int, Assignment> Assignments { get; init; }
    public required Dictionary<int, SchoolClass> Classes { get; init; }
    public required Dictionary<int, Teacher> Teachers { get; init; }
    public required Dictionary<int, Subject> Subjects { get; init; }
    public required Dictionary<int, Room> Rooms { get; init; }

    public static bool TryParseTip(string? s, out Tip tip)
    {
        tip = Tip.Clasa;
        switch (s?.ToLowerInvariant())
        {
            case "clasa": tip = Tip.Clasa; return true;
            case "profesor": tip = Tip.Profesor; return true;
            case "sala": tip = Tip.Sala; return true;
            default: return false;
        }
    }

    public static string TipParam(Tip t) => t switch { Tip.Profesor => "profesor", Tip.Sala => "sala", _ => "clasa" };
    public static string TipLabel(Tip t) => t switch { Tip.Profesor => "Profesori", Tip.Sala => "Săli", _ => "Clase" };

    /// <summary>Null when no timetable has been generated.</summary>
    public static async Task<OrarView?> LoadAsync(AppDb db)
    {
        await using var tx = await db.BeginReadAsync(); // timetable + names from the same snapshot
        var t = await db.Timetables.AsNoTracking().Include(x => x.Entries).FirstOrDefaultAsync();
        if (t == null) return null;
        var view = new OrarView
        {
            Timetable = t,
            Assignments = await db.Assignments.AsNoTracking().ToDictionaryAsync(x => x.Id),
            Classes = await db.Classes.AsNoTracking().ToDictionaryAsync(x => x.Id),
            Teachers = await db.Teachers.AsNoTracking().ToDictionaryAsync(x => x.Id),
            Subjects = await db.Subjects.AsNoTracking().ToDictionaryAsync(x => x.Id),
            Rooms = await db.Rooms.AsNoTracking().ToDictionaryAsync(x => x.Id),
        };
        await tx.CommitAsync();
        return view;
    }

    public List<(int Id, string Name)> Entities(Tip tip) => tip switch
    {
        Tip.Profesor => Teachers.Values.OrderBy(x => x.Name).Select(x => (x.Id, x.Name)).ToList(),
        Tip.Sala => Rooms.Values.OrderBy(x => x.Name).Select(x => (x.Id, x.Name)).ToList(),
        _ => Classes.Values.OrderBy(x => x.Grade).ThenBy(x => x.Name).Select(x => (x.Id, x.Name)).ToList(),
    };

    /// <summary>Null when the id does not exist for this type.</summary>
    public OrarGridModel? Grid(Tip tip, int id)
    {
        string? title = tip switch
        {
            Tip.Profesor => Teachers.GetValueOrDefault(id)?.Name is { } n ? $"Profesor: {n}" : null,
            Tip.Sala => Rooms.GetValueOrDefault(id)?.Name is { } n ? $"Sala: {n}" : null,
            _ => Classes.GetValueOrDefault(id)?.Name is { } n ? $"Clasa: {n}" : null,
        };
        if (title == null) return null;
        var cells = new List<string>[Slots.Count];
        for (int s = 0; s < Slots.Count; s++) cells[s] = [];
        foreach (var e in Timetable.Entries)
        {
            if (!Assignments.TryGetValue(e.AssignmentId, out var a) || e.Slot is < 0 or >= Slots.Count) continue;
            string subj = Subjects.GetValueOrDefault(a.SubjectId)?.Name ?? "?";
            string cls = Classes.GetValueOrDefault(a.ClassId)?.Name ?? "?";
            string tch = Teachers.GetValueOrDefault(a.TeacherId)?.Name ?? "?";
            string room = Rooms.GetValueOrDefault(e.RoomId)?.Name ?? "?";
            var lines = tip switch
            {
                Tip.Profesor when a.TeacherId == id => new[] { subj, cls, room },
                Tip.Sala when e.RoomId == id => [subj, cls, tch],
                Tip.Clasa when a.ClassId == id => [subj, tch, room],
                _ => null,
            };
            if (lines != null) cells[e.Slot].AddRange(lines);
        }
        return new OrarGridModel(title, cells.Select(c => c.ToArray()).ToArray());
    }

    /// <summary>
    /// School-wide timetable ("orarul general" from the notice board): one row per class, one column per
    /// slot, with short subject names, teacher surnames and short room names.
    /// </summary>
    public List<GeneralRow> General()
    {
        var subj = UniqueShort(Subjects.Values.OrderBy(x => x.Id).Select(x => (x.Id, x.Name)), Abbrev);
        var tch = UniqueShort(Teachers.Values.OrderBy(x => x.Id).Select(x => (x.Id, x.Name)), Surname);
        var rows = Classes.Values.OrderBy(x => x.Grade).ThenBy(x => x.Name)
            .Select(c => new GeneralRow(c.Name, new GeneralCell?[Slots.Count])).ToList();
        var rowOf = Classes.Values.OrderBy(x => x.Grade).ThenBy(x => x.Name).Select((c, i) => (c.Id, i)).ToDictionary(p => p.Id, p => p.i);
        foreach (var e in Timetable.Entries)
        {
            if (!Assignments.TryGetValue(e.AssignmentId, out var a) || e.Slot is < 0 or >= Slots.Count || !rowOf.TryGetValue(a.ClassId, out var r)) continue;
            string subject = Subjects.GetValueOrDefault(a.SubjectId)?.Name ?? "?";
            string teacher = Teachers.GetValueOrDefault(a.TeacherId)?.Name ?? "?";
            string room = Rooms.GetValueOrDefault(e.RoomId)?.Name ?? "?";
            rows[r].Cells[e.Slot] = new GeneralCell(subj.GetValueOrDefault(a.SubjectId, subject), tch.GetValueOrDefault(a.TeacherId, teacher),
                ShortRoom(room), $"{subject} – {teacher} – {room}");
        }
        return rows;
    }

    /// <summary>Short form per id; where two short forms collide, those ids keep the full name.</summary>
    static Dictionary<int, string> UniqueShort(IEnumerable<(int Id, string Name)> items, Func<string, string> shorten)
    {
        var list = items.Select(x => (x.Id, x.Name, Short: shorten(x.Name))).ToList();
        var clash = list.GroupBy(x => x.Short, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return list.ToDictionary(x => x.Id, x => clash.Contains(x.Short) ? x.Name : x.Short);
    }

    static readonly HashSet<string> Filler = ["si", "de", "a", "in", "pentru"];

    /// <summary>"Matematică" → "Mate.", "Limba română" → "Rom.", "Educație fizică" → "Ed. fiz.", "Istorie" → "Istorie".</summary>
    public static string Abbrev(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !Filler.Contains(SchoolImport.Key(w))).ToList();
        if (words.Count == 0) return name;
        static string Cut(string w, int n) => w.Length <= n + 1 ? w : w[..n] + ".";
        if (words.Count >= 2 && SchoolImport.Key(words[0]) is "limba" or "limbă")
            return char.ToUpper(words[1][0]) + Cut(words[1], 3)[1..];
        if (words.Count == 1) return words[0].Length <= 7 ? words[0] : words[0][..4] + ".";
        return $"{Cut(words[0], 2)} {Cut(words[1], 3)}";
    }

    /// <summary>"Popescu Ana" → "Popescu" (family name first, as in Moldovan school lists).</summary>
    public static string Surname(string name) => name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;

    /// <summary>"Sala 101" → "101", "Laborator 2" → "Lab 2", "Informatică 1" → "Inf 1", "Sala de sport 1" → "Sport 1".</summary>
    public static string ShortRoom(string name)
    {
        var k = SchoolImport.Key(name);
        foreach (var (prefix, repl) in new[] { ("sala de sport", "Sport"), ("sala", ""), ("laborator", "Lab"), ("informatica", "Inf") })
            if (k.StartsWith(prefix + " ") && name.Length > prefix.Length)
            {
                var rest = name[(prefix.Length + 1)..].Trim();
                return rest.Length == 0 ? name : (repl.Length == 0 ? rest : $"{repl} {rest}");
            }
        return name;
    }
}
