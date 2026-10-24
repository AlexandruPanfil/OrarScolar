using OrarScolar.Data;
using OrarScolar.Solver;
using static OrarScolar.Tests.Fixtures;

namespace OrarScolar.Tests;

// T6
public class FitnessTests
{
    [Fact]
    public void ExactBreakdown()
    {
        var s = new School(); s.Room("R"); int c = s.Class("A");
        s.Assign(c, s.Subject("M", difficult: true), s.Teacher("T1", preferred: Mask(0)), 2);
        s.Assign(c, s.Subject("R"), s.Teacher("T2"), 2);
        var x = s.Input;
        // M at d0p0, d0p5; R at d0p2, d1p0
        var (hard, soft, b) = Fitness.Evaluate(x, [0, 5, 2, 7], [0, 0, 0, 0]);
        Assert.Equal(0, hard);
        Assert.Equal(new Breakdown(ClassGaps: 3, TeacherGaps: 4, LateDifficult: 1, OutsidePreferred: 1, Imbalance: 5), b);
        Assert.Equal(30 + 12 + 5 + 2 + 5, soft);
    }

    public static TheoryData<int> Broken() => [0, 1, 2];

    [Theory, MemberData(nameof(Broken))]
    public void HardMatchesChecker_OnBrokenFixtures(int which)
    {
        var s = new School(); s.Room("R", cap: 20); s.Room("Lab", RoomType.Laborator);
        int a = s.Class("A", 25), b = s.Class("B", 15);
        int t1 = s.Teacher("T1", Range(0, 20)), t2 = s.Teacher("T2");
        s.Assign(a, s.Subject("M"), t1, 3); s.Assign(b, s.Subject("F", RoomType.Laborator), t1, 2); s.Assign(b, s.Subject("R"), t2, 2);
        var x = s.Input;
        (int[] slots, int[] rooms) = which switch
        {
            0 => (new[] { 0, 7, 30, 0, 8, 1, 2 }, new[] { 0, 0, 0, 1, 1, 0, 0 }),    // teacher clash at 0, unavailable at 30, capacity 20 < 25
            1 => (new[] { 0, 1, 2, 3, 4, 5, 6 }, new[] { 1, 1, 1, 0, 0, 1, 1 }),    // wrong room types, >2/day for M, room clash-free
            _ => (new int[7], new int[7]),    // everything at slot 0
        };
        Assert.True(HardChecker.Count(x, slots, rooms) > 0);
        Assert.Equal(HardChecker.Count(x, slots, rooms), Fitness.Evaluate(x, slots, rooms).Hard);
    }
}
