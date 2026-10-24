using OrarScolar.Data;
using OrarScolar.Solver;
using Xunit.Abstractions;
using static OrarScolar.Tests.Fixtures;

namespace OrarScolar.Tests;

public class GeneticTests(ITestOutputHelper log)
{
    // T2a
    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void RealData_NoRegression(int seed)
    {
        var (x, _, _) = Seed(16, seed);
        var o = new PipelineOptions { Reproducible = true, Bt = new() { Seed = seed }, Ga = new() { Seed = seed, Generations = 150 } };
        var r = Pipeline.Run(x, o);
        log.WriteLine($"seed {seed}: {r.Status}, soft {r.SoftBefore} → {r.SoftAfter}, {r.Generations} gen, {r.ElapsedMs} ms, {r.StoppedBy}");
        Assert.Equal(PipelineStatus.Success, r.Status);
        Assert.Equal(0, HardChecker.Count(x, r.SlotOf, r.RoomOf));
        Assert.True(r.SoftAfter <= r.SoftBefore);
    }

    // T2b
    [Fact]
    public void GapFixture_StrictlyImproves()
    {
        var s = new School(); s.Room("R"); int c = s.Class("A");
        s.Assign(c, s.Subject("M"), s.Teacher("T1"), 2); s.Assign(c, s.Subject("R"), s.Teacher("T2"), 2);
        var x = s.Input;
        int[] slots = [0, 2, 4, 6], rooms = [0, 0, 0, 0]; // 3 class gaps on Monday
        Assert.Equal(3, Fitness.Evaluate(x, slots, rooms).B.ClassGaps);
        var r = Genetic.Run(x, slots, rooms, new GaOptions { Seed = 7 });
        log.WriteLine($"soft {r.SoftBefore} → {r.SoftAfter}");
        Assert.True(r.SoftAfter < r.SoftBefore);
        Assert.Equal(0, HardChecker.Count(x, r.SlotOf, r.RoomOf));
    }

    // T3
    [Fact]
    public void Pipeline_IsDeterministic_AndOrderIndependent()
    {
        var (data, _) = SeedGenerator.Create(8, 3);
        var o = new PipelineOptions { Reproducible = true, Ga = new() { Generations = 60 } };
        var r1 = Pipeline.Run(SolverInput.FromEntities(data), o);
        var r2 = Pipeline.Run(SolverInput.FromEntities(data), o);
        var rnd = new Random(5);
        List<T> Shuffle<T>(List<T> l) { var a = l.ToArray(); rnd.Shuffle(a); return [.. a]; }
        var shuffled = new SchoolData(Shuffle(data.Classes), Shuffle(data.Teachers), Shuffle(data.Subjects), Shuffle(data.Rooms), Shuffle(data.Assignments));
        var r3 = Pipeline.Run(SolverInput.FromEntities(shuffled), o);
        Assert.Equal(PipelineStatus.Success, r1.Status);
        Assert.Equal(r1.SlotOf, r2.SlotOf); Assert.Equal(r1.RoomOf, r2.RoomOf);
        Assert.Equal(r1.SlotOf, r3.SlotOf); Assert.Equal(r1.RoomOf, r3.RoomOf);
        Assert.Equal(r1.SoftAfter, r3.SoftAfter);
    }

    // T4b
    [Fact]
    public void InvalidInput_NeverRunsBacktracking()
    {
        var s = new School(); int c = s.Class("IX B"); s.Room("R");
        for (int k = 0; k < 4; k++) s.Assign(c, s.Subject($"S{k}"), s.Teacher($"T{k}"), 9);
        var r = Pipeline.Run(s.Input, new PipelineOptions());
        Assert.Equal(PipelineStatus.InvalidInput, r.Status);
        Assert.Equal(0, r.BtNodes);
        Assert.Contains(r.Errors, e => e.Contains("IX B"));
    }

    [Fact]
    public void EmptySchool_IsInvalidInput_NotACrash()
    {
        var r = Pipeline.Run(new School().Input, new PipelineOptions());
        Assert.Equal(PipelineStatus.InvalidInput, r.Status);
        Assert.NotEmpty(r.Errors);
    }

    // T9a
    [Fact]
    public void Repair_FixesRepairableClash()
    {
        var s = new School(); s.Room("R1"); s.Room("R2");
        int t = s.Teacher("T"), m = s.Subject("M");
        s.Assign(s.Class("A"), m, t, 1); s.Assign(s.Class("B"), m, t, 1);
        var x = s.Input;
        var ga = new Genetic(x, new GaOptions());
        var ind = new Genetic.Ind([3, 3], [0, 1]);
        Assert.Equal(1, HardChecker.Count(x, ind.S, ind.R));
        ga.Repair(ind);
        Assert.Equal(0, HardChecker.Count(x, ind.S, ind.R));
    }

    // T9b
    [Fact]
    public void Repair_LeavesUnrepairable_AndArchiveUnchanged()
    {
        var s = new School(); s.Room("R"); int c = s.Class("A");
        s.Assign(c, s.Subject("M"), s.Teacher("T1", Mask(0)), 1); s.Assign(c, s.Subject("R"), s.Teacher("T2", Mask(0)), 1);
        var x = s.Input;
        var ind = new Genetic.Ind([0, 0], [0, 0]);
        new Genetic(x, new GaOptions()).Repair(ind);
        int hard = Fitness.Evaluate(x, ind.S, ind.R).Hard;
        Assert.True(hard > 0);
        Assert.Equal(HardChecker.Count(x, ind.S, ind.R), hard);

        // only one feasible timetable exists: the GA must return it unchanged
        var s2 = new School(); s2.Room("R"); int c2 = s2.Class("A");
        s2.Assign(c2, s2.Subject("M"), s2.Teacher("T1", Mask(3)), 1); s2.Assign(c2, s2.Subject("R"), s2.Teacher("T2", Mask(4)), 1);
        var x2 = s2.Input;
        var r = Genetic.Run(x2, [3, 4], [0, 0], new GaOptions { Generations = 50 });
        Assert.Equal([3, 4], r.SlotOf); Assert.Equal([0, 0], r.RoomOf);
        Assert.Equal(r.SoftBefore, r.SoftAfter);
    }

    // T15: day crossover with D = {Wednesday}: child has 2 A + 6 B + 2 C; the drop removes 2 B hours
    // (none on A's available days), and A's teacher is only available at slots occupied by A or C → discard, clone p1.
    [Fact]
    public void Crossover_FallsBackToCloneOfP1()
    {
        var s = new School(); s.Room("R"); int c = s.Class("A");
        s.Assign(c, s.Subject("A"), s.Teacher("TA", Mask(0, 1, 7, 8)), 4);
        s.Assign(c, s.Subject("B"), s.Teacher("TB"), 4);
        s.Assign(c, s.Subject("C"), s.Teacher("TC"), 2);
        var x = s.Input;
        var p1 = new Genetic.Ind([0, 1, 7, 8, 14, 15, 28, 29, 21, 22], new int[10]);
        var p2 = new Genetic.Ind([0, 1, 14, 15, 21, 22, 28, 29, 7, 8], new int[10]);
        Assert.Equal(0, HardChecker.Count(x, p1.S, p1.R));
        for (int seed = 0; seed < 20; seed++)
        {
            var child = new Genetic(x, new GaOptions { Seed = seed }).Crossover(p1, p2, _ => 1 << 2);
            Assert.NotSame(p1, child);
            Assert.Equal(p1.S, child.S); Assert.Equal(p1.R, child.R);
            Assert.Equal(0, HardChecker.Count(x, child.S, child.R));
        }
    }

    // T16
    [Fact]
    public void DeadlineBeforeBacktrackingSucceeds_LimitReached()
    {
        var (x, _, _) = Seed(16, 1);
        var r = Pipeline.Run(x, new PipelineOptions { Deadline = TimeSpan.FromMilliseconds(1) });
        Assert.Equal(PipelineStatus.LimitReached, r.Status);
        Assert.Empty(r.SlotOf);
    }
}
