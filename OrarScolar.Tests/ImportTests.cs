using OrarScolar.Data;
using OrarScolar.Export;
using OrarScolar.Solver;

namespace OrarScolar.Tests;

// T15: Excel import of real school data, experiments for the report.
public class ImportTests
{
    [Theory]
    [InlineData("Luni", "Luni")]
    [InlineData("Marți 1-3", "Marți 1-3")]
    [InlineData("mi 1,2,7; V 5-7", "Miercuri 1-2,7; Vineri 5-7")]
    [InlineData("Vineri: 7", "Vineri 7")]
    [InlineData("Marţi *", "Marți")]
    [InlineData("", "")]
    public void SlotText_ParsesAndFormats(string text, string formatted)
    {
        Assert.True(SlotText.TryParse(text, out var mask, out var error), error);
        Assert.Equal(formatted, SlotText.Format(mask));
    }

    [Theory]
    [InlineData("Sâmbătă")]
    [InlineData("Luni 0-3")]
    [InlineData("Joi 3-2")]
    [InlineData("Luni 8")]
    public void SlotText_RejectsInvalid(string text) => Assert.False(SlotText.TryParse(text, out _, out _));

    [Fact]
    public void SlotText_RoundTrips_RandomMasks()
    {
        var rnd = new Random(7);
        for (int k = 0; k < 1000; k++)
        {
            long m = (long)((ulong)rnd.NextInt64() & Slots.All);
            Assert.True(SlotText.TryParse(SlotText.Format(m), out var back, out _));
            Assert.Equal(m, back);
        }
    }

    static string Signature(SchoolData d)
    {
        var c = d.Classes.ToDictionary(x => x.Id, x => x.Name);
        var s = d.Subjects.ToDictionary(x => x.Id, x => x.Name);
        var t = d.Teachers.ToDictionary(x => x.Id, x => x.Name);
        return string.Join("\n",
            d.Classes.Select(x => $"C|{x.Name}|{x.Grade}|{x.StudentCount}")
            .Concat(d.Teachers.Select(x => $"T|{x.Name}|{x.UnavailableMask}|{x.PreferredMask}"))
            .Concat(d.Subjects.Select(x => $"S|{x.Name}|{x.IsDifficult}|{x.RoomType}"))
            .Concat(d.Rooms.Select(x => $"R|{x.Name}|{x.Capacity}|{x.RoomType}"))
            .Concat(d.Assignments.Select(a => $"A|{c[a.ClassId]}|{s[a.SubjectId]}|{t[a.TeacherId]}|{a.HoursPerWeek}"))
            .OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Sheets_RoundTrip_AndImportedDataIsSolvable()
    {
        var (data, _) = SeedGenerator.Create(8, 5);
        var (back, errors) = SchoolImport.Parse(SchoolImport.ToSheets(data));
        Assert.Empty(errors);
        Assert.Equal(Signature(data), Signature(back!));
        var r = Pipeline.Run(SolverInput.FromEntities(back!), new PipelineOptions { Reproducible = true, Ga = new GaOptions { Generations = 20 } });
        Assert.Equal(PipelineStatus.Success, r.Status);
    }

    [Fact]
    public void Workbook_RoundTrips_ThroughExcel()
    {
        var (data, _) = SeedGenerator.Create(3, 2);
        var bytes = DataWorkbook.Write(SchoolImport.ToSheets(data));
        using var ms = new MemoryStream(bytes);
        var (sheets, readError) = DataWorkbook.Read(ms);
        Assert.Null(readError);
        var (back, errors) = SchoolImport.Parse(sheets!);
        Assert.Empty(errors);
        Assert.Equal(Signature(data), Signature(back!));
    }

    [Fact]
    public void Workbook_NotAnXlsx_GivesMessage()
    {
        using var ms = new MemoryStream("nu este excel"u8.ToArray());
        var (sheets, error) = DataWorkbook.Read(ms);
        Assert.Null(sheets);
        Assert.NotNull(error);
    }

    [Fact]
    public void Parse_ReportsRowLevelErrors_WithoutCascading()
    {
        var (data, _) = SeedGenerator.Create(3, 1);
        var sh = SchoolImport.ToSheets(data);
        sh[SchoolImport.SClase][1][1] = "15";                       // grade out of range
        sh[SchoolImport.SMaterii][1][2] = "Bucătărie";              // unknown room type
        sh[SchoolImport.SPlan][1][2] = "Profesor Inexistent";       // unknown teacher
        sh[SchoolImport.SPlan].Add(sh[SchoolImport.SPlan][2]);      // duplicate (class, subject)
        sh[SchoolImport.SSali].Add([sh[SchoolImport.SSali][1][0], "30", "Normală"]); // duplicate room
        var (back, errors) = SchoolImport.Parse(sh);
        Assert.Null(back);
        Assert.Equal(5, errors.Count);
        Assert.Contains(errors, e => e.Contains("„Clase”, rândul 2") && e.Contains("anul de studiu"));
        Assert.Contains(errors, e => e.Contains("Bucătărie"));
        Assert.Contains(errors, e => e.Contains("Profesor Inexistent"));
        Assert.Contains(errors, e => e.Contains("de două ori în planul clasei"));
        Assert.Contains(errors, e => e.Contains("„Săli”") && e.Contains("de două ori"));
    }

    [Fact]
    public void Parse_IsCaseAndDiacriticInsensitive_AndNeedsAllSheets()
    {
        var (data, _) = SeedGenerator.Create(3, 1);
        var loose = SchoolImport.ToSheets(data).ToDictionary(kv => kv.Key.ToUpperInvariant().Replace("Ă", "A"), kv => kv.Value);
        loose["PLAN-CADRU"][1][0] = loose["PLAN-CADRU"][1][0].ToLowerInvariant();
        loose["PLAN-CADRU"][1][3] = "4.0";
        Assert.Empty(SchoolImport.Parse(loose).Errors);

        var (_, missing) = SchoolImport.Parse(new Dictionary<string, List<string[]>> { ["Clase"] = loose["CLASE"] });
        Assert.Equal(4, missing.Count);
    }

    [Fact]
    public void Experiments_SmallRun_IsConsistent()
    {
        var r = Experiments.Run(new ExperimentOptions { Sizes = [3], Runs = 1, Ga = new GaOptions { Generations = 30 } });
        Assert.Equal(4, r.Bt.Count);
        Assert.Equal(1, r.Bt.Single(b => b.Variant.StartsWith("AC-3")).Solved);
        var g = Assert.Single(r.Ga);
        Assert.True(g.AvgSoftAfter <= g.AvgSoftBefore);
        Assert.Equal(g.AvgGenerations + 1, r.Convergence.Count);
        Assert.True(r.Convergence.Zip(r.Convergence.Skip(1)).All(p => p.Second <= p.First)); // best-so-far never rises
        Assert.Contains("Algoritm genetic", Experiments.ToCsv(r));
    }
}
