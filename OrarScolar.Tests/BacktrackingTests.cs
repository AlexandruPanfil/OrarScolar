using OrarScolar.Data;
using OrarScolar.Solver;
using Xunit.Abstractions;
using static OrarScolar.Tests.Fixtures;

namespace OrarScolar.Tests;

public class BacktrackingTests(ITestOutputHelper log)
{
    // T1
    [Theory]
    [InlineData(3, 1)] [InlineData(3, 2)] [InlineData(3, 3)]
    [InlineData(8, 1)] [InlineData(8, 2)] [InlineData(8, 3)]
    [InlineData(16, 1)] [InlineData(16, 2)] [InlineData(16, 3)]
    public void SeedData_Solved_NoHardConflicts(int classes, int seed)
    {
        var (x, _, _) = Seed(classes, seed);
        var pre = Preprocessor.Run(x, useAc3: true);
        Assert.Empty(pre.Errors);
        var r = Backtracking.Solve(x, pre.Domains, new BtOptions { Seed = seed });
        log.WriteLine($"{classes} clase, seed {seed}: {r.Status}, {r.Nodes} noduri, {r.Backtracks} reveniri, {r.ElapsedMs} ms");
        Assert.Equal(BtStatus.Success, r.Status);
        Assert.Equal(0, HardChecker.Count(x, r.SlotOf, r.RoomOf));
    }

    static School TwoClasses()
    {
        var s = new School(); s.Room("R1"); s.Room("R2"); s.Room("Lab", RoomType.Laborator);
        int a = s.Class("A"), b = s.Class("B");
        int t1 = s.Teacher("T1", Range(0, 13)), t2 = s.Teacher("T2", Range(7, 20)), t3 = s.Teacher("T3", Range(0, 6) | Range(14, 20));
        int m = s.Subject("M"), f = s.Subject("F", RoomType.Laborator), r = s.Subject("R");
        s.Assign(a, m, t1, 3); s.Assign(b, m, t1, 3);
        s.Assign(a, f, t2, 2); s.Assign(b, f, t2, 2);
        s.Assign(a, r, t3, 2); s.Assign(b, r, t3, 2);
        return s;
    }

    // T7a
    [Fact]
    public void FcNeverMoreNodesThanPlain()
    {
        var x = TwoClasses().Input;
        var fc = Backtracking.Solve(x, x.BaseSlots, FcOnly);
        var plain = Backtracking.Solve(x, x.BaseSlots, Plain);
        Assert.Equal(BtStatus.Success, fc.Status); Assert.Equal(BtStatus.Success, plain.Status);
        Assert.Equal(0, HardChecker.Count(x, fc.SlotOf, fc.RoomOf));
        Assert.Equal(0, HardChecker.Count(x, plain.SlotOf, plain.RoomOf));
        log.WriteLine($"FC {fc.Nodes}, plain {plain.Nodes}");
        Assert.True(fc.Nodes <= plain.Nodes);
    }

    // T7b: L0 (class A) takes slot 0 first; L4 (class A) can only use slot 0 → FC sees it at once.
    [Fact]
    public void FcStrictlyFewerNodes_OnEarlyForcedWipeout()
    {
        var s = new School(); s.Room("R1"); s.Room("R2");
        int a = s.Class("A"), b = s.Class("B");
        s.Assign(a, s.Subject("S0"), s.Teacher("T0", Mask(0, 1)), 1);
        for (int k = 1; k <= 3; k++) s.Assign(b, s.Subject($"S{k}"), s.Teacher($"T{k}", Mask(2, 3, 4)), 1);
        s.Assign(a, s.Subject("S4"), s.Teacher("T4", Mask(0)), 1);
        var x = s.Input;
        var fc = Backtracking.Solve(x, x.BaseSlots, FcOnly);
        var plain = Backtracking.Solve(x, x.BaseSlots, Plain);
        Assert.Equal(BtStatus.Success, fc.Status); Assert.Equal(BtStatus.Success, plain.Status);
        log.WriteLine($"FC {fc.Nodes}, plain {plain.Nodes}");
        Assert.True(fc.Nodes < plain.Nodes);
    }

    // T7c
    [Fact]
    public void ComparisonTable_AllVariantsReturnStatus()
    {
        var (x, _, _) = Seed(3, 1);
        var rows = Backtracking.Compare(x, 200_000);
        Assert.Equal(4, rows.Count);
        foreach (var r in rows)
        {
            Assert.True(Enum.IsDefined(r.Status));
            log.WriteLine($"{r.Variant,-25} {r.Status,-14} {r.Nodes,10} {r.Backtracks,10} {r.Ms,6} ms {r.Note}");
        }
    }

    // T10
    [Fact]
    public void RoomOnlyConflict_FcDetectsAfterOneNode()
    {
        var s = new School(); s.Room("Lab", RoomType.Laborator);
        int f = s.Subject("F", RoomType.Laborator);
        s.Assign(s.Class("A"), f, s.Teacher("T1", Mask(4)), 1);
        s.Assign(s.Class("B"), f, s.Teacher("T2", Mask(4)), 1);
        var x = s.Input;
        var fc = Backtracking.Solve(x, x.BaseSlots, FcOnly);
        Assert.Equal(BtStatus.Unsatisfiable, fc.Status); Assert.Equal(1, fc.Nodes);
        var plain = Backtracking.Solve(x, x.BaseSlots, Plain);
        Assert.Equal(BtStatus.Unsatisfiable, plain.Status); Assert.Equal(2, plain.Nodes);
    }

    // T11
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UndoRestoresState(bool fc)
    {
        var s = new School(); s.Room("R"); int a = s.Class("A");
        foreach (var n in new[] { "T1", "T2", "T3" }) s.Assign(a, s.Subject("M" + n), s.Teacher(n, Mask(0, 1)), 1);
        var x = s.Input;
        var bt = new Backtracking(x, x.BaseSlots, fc ? FcOnly : Plain);
        var r = bt.Run();
        Assert.Equal(BtStatus.Unsatisfiable, r.Status);
        Assert.True(r.Backtracks > 0);
        Assert.All(bt.classBusy, m => Assert.Equal(0UL, m));
        Assert.All(bt.teacherBusy, m => Assert.Equal(0UL, m));
        Assert.All(bt.freeRooms, m => Assert.Equal(x.AllRooms, m));
        Assert.All(bt.keyFull, m => Assert.Equal(0UL, m));
        Assert.All(bt.dayFull, m => Assert.Equal(0UL, m));
        Assert.All(bt.slotOf, v => Assert.Equal(-1, v));
    }
}
