using Microsoft.EntityFrameworkCore;
using OrarScolar.Data;

namespace OrarScolar.Tests;

// T12: staleness + atomic save on a real SQLite file.
public class DataTests : IDisposable
{
    readonly string path = Path.Combine(Path.GetTempPath(), $"orar-test-{Guid.NewGuid():N}.db");

    AppDb Ctx() => new(new DbContextOptionsBuilder<AppDb>().UseSqlite($"Data Source={path};Pooling=False").Options);

    public void Dispose() { try { File.Delete(path); } catch (IOException) { } }

    async Task<long> Setup()
    {
        await using var db = Ctx();
        await AppDb.InitializeAsync(db);
        db.Teachers.Add(new Teacher { Name = "Pop" });
        await db.SaveChangesAsync();
        var v = await Version(db);
        Assert.True(await db.SaveTimetableAsync(NewTt(), v, default));
        return v;
    }

    static Timetable NewTt() => new() { CreatedAt = DateTime.UtcNow, Entries = [new TimetableEntry { AssignmentId = 1, Slot = 0, RoomId = 1 }] };
    static Task<long> Version(AppDb db) => db.Meta.AsNoTracking().Select(m => m.DataVersion).SingleAsync();

    [Fact]
    public async Task RealEdit_InvalidatesTimetable_AndBumpsVersion()
    {
        var v = await Setup();
        await using var db = Ctx();
        var t = await db.Teachers.SingleAsync();
        db.Entry(t).CurrentValues.SetValues(new Teacher { Id = t.Id, Name = "Popescu" });
        await db.SaveChangesAsync();
        Assert.Equal(0, await db.Timetables.CountAsync());
        Assert.Equal(0, await db.TimetableEntries.CountAsync());
        Assert.Equal(v + 1, await Version(db));
    }

    [Fact]
    public async Task NoOpEdit_KeepsTimetable()
    {
        var v = await Setup();
        await using var db = Ctx();
        var t = await db.Teachers.SingleAsync();
        db.Entry(t).CurrentValues.SetValues(new Teacher { Id = t.Id, Name = "Pop" });
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.Timetables.CountAsync());
        Assert.Equal(v, await Version(db));
    }

    [Fact]
    public async Task StaleSave_IsRejected_OldTimetableIntact()
    {
        var v = await Setup();
        await using (var db = Ctx()) { db.Teachers.Add(new Teacher { Name = "Ionescu" }); await db.SaveChangesAsync(); }
        await using (var db = Ctx())
        {
            // the edit deleted the timetable; save a fresh one at the new version, then try a stale save
            Assert.True(await db.SaveTimetableAsync(NewTt(), v + 1, default));
            var id = await db.Timetables.Select(x => x.Id).SingleAsync();
            Assert.False(await db.SaveTimetableAsync(NewTt(), v, default));
            await using var db2 = Ctx();
            Assert.Equal(id, await db2.Timetables.Select(x => x.Id).SingleAsync());
        }
    }

    [Fact]
    public async Task CommitThenEditOnSecondContext_EndsInvalidated()
    {
        long vStart;
        await using (var db = Ctx()) { await AppDb.InitializeAsync(db); db.Teachers.Add(new Teacher { Name = "Pop" }); await db.SaveChangesAsync(); }
        await using var gen = Ctx();
        (vStart, _) = await gen.LoadSnapshotAsync();
        Assert.True(await gen.SaveTimetableAsync(NewTt(), vStart, default));
        await using (var edit = Ctx()) { (await edit.Teachers.SingleAsync()).Name = "Alt"; await edit.SaveChangesAsync(); }
        await using var check = Ctx();
        Assert.Equal(0, await check.Timetables.CountAsync());
        Assert.Equal(vStart + 1, await Version(check));
    }

    [Fact]
    public async Task SaveWithDeletedReference_ReturnsFriendlyError()
    {
        await Setup();
        await using var db = Ctx();
        var (error, _) = await db.UpsertAsync(0, new Assignment { ClassId = 99, SubjectId = 99, TeacherId = 99, HoursPerWeek = 2 });
        Assert.NotNull(error);
        Assert.Equal(0, await db.Assignments.CountAsync());
    }

    [Fact]
    public async Task TwoSequentialEdits_NeverReuseVersion()
    {
        var v = await Setup();
        await using (var db = Ctx()) { (await db.Teachers.SingleAsync()).Name = "A"; await db.SaveChangesAsync(); }
        await using (var db = Ctx()) { (await db.Teachers.SingleAsync()).Name = "B"; await db.SaveChangesAsync(); }
        await using var c = Ctx();
        Assert.Equal(v + 2, await Version(c));
        Assert.False(await c.SaveTimetableAsync(NewTt(), v, default));
        Assert.False(await c.SaveTimetableAsync(NewTt(), v + 1, default));
        Assert.True(await c.SaveTimetableAsync(NewTt(), v + 2, default));
    }
}
