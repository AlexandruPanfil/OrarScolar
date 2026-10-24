using Microsoft.EntityFrameworkCore;

namespace OrarScolar.Data;

public record SchoolData(List<SchoolClass> Classes, List<Teacher> Teachers, List<Subject> Subjects, List<Room> Rooms, List<Assignment> Assignments);

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<SchoolClass> Classes => Set<SchoolClass>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<Meta> Meta => Set<Meta>();
    public DbSet<Timetable> Timetables => Set<Timetable>();
    public DbSet<TimetableEntry> TimetableEntries => Set<TimetableEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<SchoolClass>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Teacher>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Subject>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Room>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Assignment>().HasIndex(x => new { x.ClassId, x.SubjectId }).IsUnique();
        b.Entity<Assignment>().HasOne<SchoolClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Assignment>().HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Assignment>().HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Meta>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<Timetable>().HasMany(x => x.Entries).WithOne().HasForeignKey(x => x.TimetableId).OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>One generation (or demo reset) at a time, process-wide.</summary>
    public static readonly SemaphoreSlim GenerationGate = new(1, 1);

    /// <summary>Add (id 0) or update a tracked entity via SetValues, so no-op saves change nothing.</summary>
    public async Task<(string? Error, bool Invalidated)> UpsertAsync<T>(int id, T form) where T : class
    {
        bool had = await Timetables.AnyAsync();
        if (id == 0) Add(form);
        else if (await FindAsync<T>(id) is { } e) Entry(e).CurrentValues.SetValues(form);
        else return ("Înregistrarea nu mai există.", false);
        return await SaveFriendlyAsync(had);
    }

    public async Task<(string? Error, bool Invalidated)> DeleteAsync<T>(int id) where T : class
    {
        bool had = await Timetables.AnyAsync();
        if (await FindAsync<T>(id) is { } e) Remove(e);
        return await SaveFriendlyAsync(had);
    }

    async Task<(string?, bool)> SaveFriendlyAsync(bool had)
    {
        try { await SaveChangesAsync(); }
        catch (DbUpdateException ex)
        {
            ChangeTracker.Clear();
            return (ex.InnerException?.Message.Contains("UNIQUE") == true
                ? "Există deja o înregistrare cu acest nume (sau această materie e deja în planul clasei)."
                : "Salvarea a eșuat: o înregistrare referită nu mai există. Reîncărcați pagina.", false);
        }
        return (null, had && !await Timetables.AnyAsync());
    }

    public static async Task InitializeAsync(AppDb db)
    {
        await db.Database.EnsureCreatedAsync();
        if (!await db.Meta.AnyAsync()) { db.Meta.Add(new Meta { Id = 1 }); await db.SaveChangesAsync(); }
    }

    bool HasInputChanges()
    {
        ChangeTracker.DetectChanges();
        return ChangeTracker.Entries().Any(e =>
            e.Entity is SchoolClass or Teacher or Subject or Room or Assignment &&
            (e.State is EntityState.Added or EntityState.Deleted ||
             e.State == EntityState.Modified && e.Properties.Any(p => p.IsModified)));
    }

    // Any real input edit: bump DataVersion first (takes the SQLite write lock), drop the timetable, save, commit.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        if (!HasInputChanges()) return base.SaveChanges(acceptAllChangesOnSuccess);
        using var tx = Database.CurrentTransaction == null ? Database.BeginTransaction() : null;
        Database.ExecuteSqlRaw("UPDATE Meta SET DataVersion = DataVersion + 1 WHERE Id = 1");
        Timetables.ExecuteDelete();
        var n = base.SaveChanges(acceptAllChangesOnSuccess);
        tx?.Commit();
        return n;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        if (!HasInputChanges()) return await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
        await using var tx = Database.CurrentTransaction == null ? await Database.BeginTransactionAsync(ct) : null;
        await Database.ExecuteSqlRawAsync("UPDATE Meta SET DataVersion = DataVersion + 1 WHERE Id = 1", ct);
        await Timetables.ExecuteDeleteAsync(ct);
        var n = await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
        if (tx != null) await tx.CommitAsync(ct);
        return n;
    }

    /// <summary>
    /// Deferred (read) transaction: a consistent snapshot that only takes SQLite's shared lock,
    /// so many concurrent viewers never block each other.
    /// </summary>
    public async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginReadAsync(CancellationToken ct = default)
    {
        await Database.OpenConnectionAsync(ct);
        var conn = (Microsoft.Data.Sqlite.SqliteConnection)Database.GetDbConnection();
        return await Database.UseTransactionAsync(conn.BeginTransaction(deferred: true), ct)
            ?? throw new InvalidOperationException("Tranzacția nu a putut fi pornită.");
    }

    /// <summary>Consistent snapshot: the version and the input it belongs to, read in one transaction.</summary>
    public async Task<(long Version, SchoolData Data)> LoadSnapshotAsync(CancellationToken ct = default)
    {
        await using var tx = await BeginReadAsync(ct);
        var v = await Meta.Where(m => m.Id == 1).Select(m => m.DataVersion).SingleAsync(ct);
        var data = new SchoolData(
            await Classes.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
            await Teachers.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
            await Subjects.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
            await Rooms.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct),
            await Assignments.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct));
        await tx.CommitAsync(ct);
        return (v, data);
    }

    /// <summary>Atomic generation save. False if the input changed since <paramref name="vStart"/>.</summary>
    public async Task<bool> SaveTimetableAsync(Timetable t, long vStart, CancellationToken ct)
    {
        await using var tx = await Database.BeginTransactionAsync(ct);
        var ok = await Database.ExecuteSqlRawAsync(
            "UPDATE Meta SET DataVersion = DataVersion WHERE Id = 1 AND DataVersion = {0}", [vStart], ct);
        if (ok == 0) { await tx.RollbackAsync(CancellationToken.None); return false; }
        await Timetables.ExecuteDeleteAsync(ct);
        t.DataVersion = vStart;
        Timetables.Add(t);
        await SaveChangesAsync(ct);
        ct.ThrowIfCancellationRequested();
        await tx.CommitAsync(CancellationToken.None); // commit = completion boundary
        return true;
    }

    /// <summary>Replaces all input data (demo reset). Goes through SaveChanges, so DataVersion is bumped.</summary>
    public async Task ReplaceAllAsync(SchoolData d)
    {
        await using var tx = await Database.BeginTransactionAsync();
        await Assignments.ExecuteDeleteAsync();
        await Classes.ExecuteDeleteAsync();
        await Teachers.ExecuteDeleteAsync();
        await Subjects.ExecuteDeleteAsync();
        await Rooms.ExecuteDeleteAsync();
        ChangeTracker.Clear();
        Classes.AddRange(d.Classes); Teachers.AddRange(d.Teachers); Subjects.AddRange(d.Subjects);
        Rooms.AddRange(d.Rooms); Assignments.AddRange(d.Assignments);
        await SaveChangesAsync();
        await tx.CommitAsync();
    }
}
