using Microsoft.EntityFrameworkCore;
using OrarScolar.Solver;

namespace OrarScolar.Data;

public enum Tip { Clasa, Profesor, Sala }

/// <summary>A 5×7 grid for one class/teacher/room. Cells[slot] = lines to show (empty = free).</summary>
public sealed record OrarGridModel(string Title, string[][] Cells);

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
}
