using OrarScolar.Data;
using OrarScolar.Export;
using OrarScolar.Solver;

namespace OrarScolar.Tests;

// T16: school-wide timetable ("Toate clasele").
public class GeneralTests
{
    [Theory]
    [InlineData("Matematică", "Mate.")]
    [InlineData("Limba română", "Rom.")]
    [InlineData("Limba engleză", "Eng.")]
    [InlineData("Educație fizică", "Ed. fiz.")]
    [InlineData("Consiliere și orientare", "Co. ori.")]
    [InlineData("Istorie", "Istorie")]
    [InlineData("Fizică", "Fizică")]
    public void Abbrev_ShortensSubjects(string name, string expected) => Assert.Equal(expected, OrarView.Abbrev(name));

    [Theory]
    [InlineData("Sala 101", "101")]
    [InlineData("Laborator 2", "Lab 2")]
    [InlineData("Informatică 1", "Inf 1")]
    [InlineData("Sala de sport 1", "Sport 1")]
    [InlineData("Aula", "Aula")]
    public void ShortRoom_ShortensRooms(string name, string expected) => Assert.Equal(expected, OrarView.ShortRoom(name));

    static OrarView View(int classes, int seed)
    {
        var (d, witness) = SeedGenerator.Create(classes, seed);
        return new OrarView
        {
            Timetable = new Timetable { CreatedAt = DateTime.UtcNow, Entries = witness },
            Assignments = d.Assignments.ToDictionary(x => x.Id),
            Classes = d.Classes.ToDictionary(x => x.Id),
            Teachers = d.Teachers.ToDictionary(x => x.Id),
            Subjects = d.Subjects.ToDictionary(x => x.Id),
            Rooms = d.Rooms.ToDictionary(x => x.Id),
        };
    }

    [Fact]
    public void General_HasEveryClassAndEveryLesson()
    {
        var v = View(16, 3);
        var rows = v.General();
        Assert.Equal(v.Classes.Count, rows.Count);
        Assert.Equal(v.Timetable.Entries.Count, rows.Sum(r => r.Cells.Count(c => c != null)));
        foreach (var r in rows)
        {
            int id = v.Classes.Values.Single(c => c.Name == r.ClassName).Id;
            Assert.Equal(v.Assignments.Values.Where(a => a.ClassId == id).Sum(a => a.HoursPerWeek), r.Cells.Count(c => c != null));
        }
        // short subject names stay distinguishable
        var shorts = v.Subjects.Values.Select(s => rows.SelectMany(r => r.Cells).FirstOrDefault(c => c != null && c.Full.StartsWith(s.Name + " –"))?.Subject)
            .Where(x => x != null).ToList();
        Assert.Equal(shorts.Count, shorts.Distinct().Count());
    }

    [Fact]
    public void General_Exports_ExcelSheetFirst_AndPdf()
    {
        var v = View(16, 3);
        using var ms = new MemoryStream(ExcelExport.Build(v));
        using var wb = new ClosedXML.Excel.XLWorkbook(ms);
        var ws = wb.Worksheets.First();
        Assert.Equal("Orar general", ws.Name);
        Assert.Equal("Luni", ws.Cell(2, 2).GetFormattedString());
        Assert.Equal(v.Classes.Values.OrderBy(c => c.Grade).ThenBy(c => c.Name).First().Name, ws.Cell(4, 1).GetFormattedString());

        var pdf = PdfExport.BuildGeneral(v.General());
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
