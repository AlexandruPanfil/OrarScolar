using OrarScolar.Data;
using OrarScolar.Solver;
using static OrarScolar.Tests.Fixtures;

namespace OrarScolar.Tests;

public class PreprocessorTests
{
    static List<string> Errors(School s) => Preprocessor.Run(s.Input, useAc3: true).Errors;

    // T0
    [Fact]
    public void HardChecker_SelfTest()
    {
        var s = new School();
        int a = s.Class("A"), b = s.Class("B"), c = s.Class("C");
        int t1 = s.Teacher("T1"), t2 = s.Teacher("T2"), t3 = s.Teacher("T3");
        int m = s.Subject("M");
        s.Room("R0"); s.Room("R1");
        s.Assign(a, m, t1, 1); s.Assign(b, m, t1, 1); s.Assign(c, m, t2, 1);
        int ist = s.Subject("Ist"); s.Assign(b, ist, t3, 1);
        var x = s.Input;
        Assert.Throws<ArgumentException>(() => HardChecker.Count(x, [0, 0, 1], [0, 1, 0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => HardChecker.Count(x, [0, 0, 1, 35], [0, 1, 0, 0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => HardChecker.Count(x, [0, 0, 1, 1], [0, 1, 0, -1]));
        // L0 A/T1 slot0 R0, L1 B/T1 slot0 R1 → teacher clash; L2 C/T2 slot1 R0, L3 B/T3 slot1 R0 → room clash
        Assert.Equal(2, HardChecker.Count(x, [0, 0, 1, 1], [0, 1, 0, 0]));
    }

    // T4a
    [Fact]
    public void ClassOver35Hours_NamesClass()
    {
        var s = new School(); int c = s.Class("IX B"); s.Room("R");
        for (int k = 0; k < 4; k++) s.Assign(c, s.Subject($"S{k}"), s.Teacher($"T{k}"), 9);
        Assert.Contains(Errors(s), e => e.Contains("IX B") && e.Contains("36"));
    }

    [Fact]
    public void TeacherOverAvailable_NamesTeacher()
    {
        var s = new School(); s.Room("R");
        ulong ten = Enumerable.Range(0, 5).Aggregate(0UL, (m, d) => m | Mask(d * 7, d * 7 + 1));
        int t = s.Teacher("Ionescu", ten), m = s.Subject("M");
        s.Assign(s.Class("A"), m, t, 10); s.Assign(s.Class("B"), m, t, 10);
        Assert.Contains(Errors(s), e => e.Contains("Ionescu") && e.Contains("20"));
    }

    [Fact]
    public void ChemistryWithoutLab_MentionsLaborator()
    {
        var s = new School(); s.Room("R");
        s.Assign(s.Class("A"), s.Subject("Chimie", RoomType.Laborator), s.Teacher("T"), 2);
        Assert.Contains(Errors(s), e => e.Contains("Chimie") && e.Contains("Laborator"));
    }

    [Fact]
    public void HallViolation_NamesAllThreeTeachers()
    {
        var s = new School(); s.Room("R"); int c = s.Class("IX B");
        foreach (var n in new[] { "Ionescu", "Pop", "Radu" }) s.Assign(c, s.Subject("M" + n), s.Teacher(n, Mask(3, 10)), 1);
        var err = Assert.Single(Errors(s));
        Assert.Contains("IX B", err);
        Assert.Contains("Ionescu", err); Assert.Contains("Pop", err); Assert.Contains("Radu", err);
        Assert.Contains("2 sloturi", err); Assert.Contains("3 ore", err);
    }

    [Fact]
    public void SixHoursTeacherOnTwoDays_NamesAssignment()
    {
        var s = new School(); s.Room("R");
        s.Assign(s.Class("X A"), s.Subject("Matematică"), s.Teacher("Pop", Range(0, 13)), 6);
        Assert.Contains(Errors(s), e => e.Contains("Matematică") && e.Contains("X A") && e.Contains("2 zile"));
    }

    [Fact]
    public void Over64Rooms_LimitMessage()
    {
        var s = new School();
        for (int r = 0; r < 65; r++) s.Room($"R{r}");
        s.Assign(s.Class("A"), s.Subject("M"), s.Teacher("T"), 1);
        Assert.Contains(Errors(s), e => e.Contains("65") && e.Contains("64"));
    }

    // T5
    [Fact]
    public void Ac3_NotEqual_Propagates()
    {
        var s = new School(); s.Room("R");
        int a = s.Class("A"), b = s.Class("B"), t1 = s.Teacher("T1"), t2 = s.Teacher("T2");
        s.Assign(a, s.Subject("S1"), t1, 1); s.Assign(a, s.Subject("S2"), t2, 1); s.Assign(b, s.Subject("S3"), t2, 1);
        var x = s.Input;
        int sl = 0, t = 1, u = 2;
        var dom = new[] { Mask(sl), Mask(sl, t), Mask(t, u) };
        Assert.Null(Preprocessor.Ac3(x, dom));
        Assert.Equal(Mask(sl), dom[0]); Assert.Equal(Mask(t), dom[1]); Assert.Equal(Mask(u), dom[2]);
    }

    [Fact]
    public void Ac3_SiblingOrdering()
    {
        var s = new School(); s.Room("R");
        s.Assign(s.Class("A"), s.Subject("M"), s.Teacher("T"), 3);
        var dom = Enumerable.Repeat(Range(0, 4), 3).ToArray();
        Assert.Null(Preprocessor.Ac3(s.Input, dom));
        Assert.Equal([Range(0, 2), Range(1, 3), Range(2, 4)], dom);
    }

    [Fact]
    public void Ac3_Wipeout_NamesBothLessonsAndConstraint()
    {
        var s = new School(); s.Room("R"); int a = s.Class("A");
        s.Assign(a, s.Subject("Fizică"), s.Teacher("T1"), 1); s.Assign(a, s.Subject("Chimie"), s.Teacher("T2"), 1);
        var msg = Preprocessor.Ac3(s.Input, [Mask(5), Mask(5)]);
        Assert.NotNull(msg);
        Assert.Contains("Fizică", msg); Assert.Contains("Chimie", msg); Assert.Contains("aceeași clasă", msg);
    }
}
