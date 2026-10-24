using OrarScolar.Data;
using OrarScolar.Solver;

namespace OrarScolar.Tests;

/// <summary>Tiny hand-built schools.</summary>
public sealed class School
{
    public readonly List<SchoolClass> Classes = [];
    public readonly List<Teacher> Teachers = [];
    public readonly List<Subject> Subjects = [];
    public readonly List<Room> Rooms = [];
    public readonly List<Assignment> Assignments = [];

    public int Class(string name, int size = 25) { Classes.Add(new() { Id = Classes.Count + 1, Name = name, Grade = 9, StudentCount = size }); return Classes.Count; }
    /// <param name="available">slot mask the teacher can teach; default all</param>
    public int Teacher(string name, ulong available = Slots.All, ulong preferred = 0)
    { Teachers.Add(new() { Id = Teachers.Count + 1, Name = name, UnavailableMask = (long)(~available & Slots.All), PreferredMask = (long)preferred }); return Teachers.Count; }
    public int Subject(string name, RoomType type = RoomType.Normal, bool difficult = false)
    { Subjects.Add(new() { Id = Subjects.Count + 1, Name = name, RoomType = type, IsDifficult = difficult }); return Subjects.Count; }
    public int Room(string name, RoomType type = RoomType.Normal, int cap = 30)
    { Rooms.Add(new() { Id = Rooms.Count + 1, Name = name, RoomType = type, Capacity = cap }); return Rooms.Count; }
    public int Assign(int c, int s, int t, int h)
    { Assignments.Add(new() { Id = Assignments.Count + 1, ClassId = c, SubjectId = s, TeacherId = t, HoursPerWeek = h }); return Assignments.Count; }

    public SchoolData Data => new(Classes, Teachers, Subjects, Rooms, Assignments);
    public SolverInput Input => SolverInput.FromEntities(Data);
}

public static class Fixtures
{
    public static ulong Mask(params int[] slots) => slots.Aggregate(0UL, (m, s) => m | 1UL << s);
    public static ulong Range(int from, int to) => Enumerable.Range(from, to - from + 1).Aggregate(0UL, (m, s) => m | 1UL << s);

    public static (SolverInput X, int[] SlotOf, int[] RoomOf) Seed(int classes, int seed)
    {
        var (data, witness) = SeedGenerator.Create(classes, seed);
        var x = SolverInput.FromEntities(data);
        var (s, r) = x.FromEntries(witness);
        return (x, s, r);
    }

    public static BtOptions Plain => new() { UseAc3 = false, UseForwardChecking = false, UseMrv = false, UseLcv = false, MaxRestarts = 0 };
    public static BtOptions FcOnly => Plain with { UseForwardChecking = true };
}
