using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OrarScolar.Data;
using OrarScolar.Export;

namespace OrarScolar.Tests;

// T14
public class ExportTests : IDisposable
{
    readonly string path = Path.Combine(Path.GetTempPath(), $"orar-export-{Guid.NewGuid():N}.db");
    public void Dispose() { try { File.Delete(path); } catch (IOException) { } }

    [Fact]
    public void SheetNames_AreDistinct_AndAtMost31()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] raw = ["IX A", "ix a", "IX: A", "IX/A?", new string('x', 40), new string('X', 40), new string('x', 35) + "[1]", "[]", "'Ionescu", "Lab'", "'", new string('y', 30) + "'z"];
        var names = raw.Select(r => ExcelExport.SheetName(r, used)).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(names, n => { Assert.InRange(n.Length, 1, 31); Assert.DoesNotContain(n, c => @":\/?*[]".Contains(c)); Assert.False(n.StartsWith('\'') || n.EndsWith('\'')); });
        // ClosedXML itself must accept every produced name
        using var wb = new ClosedXML.Excel.XLWorkbook();
        foreach (var n in names) wb.AddWorksheet(n);
    }

    [Fact]
    public async Task Excel_WithoutTimetable_Returns404()
    {
        var factory = new PooledDbContextFactory<AppDb>(new DbContextOptionsBuilder<AppDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = await factory.CreateDbContextAsync()) await AppDb.InitializeAsync(db);
        var result = await ExportEndpoints.Excel(factory);
        Assert.Equal(404, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
        var pdf = await ExportEndpoints.Pdf("clasa", null, factory);
        Assert.Equal(404, Assert.IsAssignableFrom<IStatusCodeHttpResult>(pdf).StatusCode);
        var bad = await ExportEndpoints.Pdf("elev", null, factory);
        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(bad).StatusCode);
    }

    [Fact]
    public async Task Exports_ProduceFiles_ForAGeneratedTimetable()
    {
        var factory = new PooledDbContextFactory<AppDb>(new DbContextOptionsBuilder<AppDb>().UseSqlite($"Data Source={path};Pooling=False").Options);
        await using (var db = await factory.CreateDbContextAsync())
        {
            await AppDb.InitializeAsync(db);
            var (data, witness) = SeedGenerator.Create(3, 1);
            await db.ReplaceAllAsync(data);
            var (v, _) = await db.LoadSnapshotAsync();
            Assert.True(await db.SaveTimetableAsync(new Timetable { CreatedAt = DateTime.UtcNow, Entries = witness }, v, default));
        }
        var xlsx = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult>(await ExportEndpoints.Excel(factory));
        Assert.True(xlsx.FileContents.Length > 1000);
        var pdf = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult>(await ExportEndpoints.Pdf("profesor", null, factory));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf.FileContents.Span[..4]));
        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "orar-test-export.pdf"), pdf.FileContents.ToArray());
    }
}
