using OrarScolar.Data;
using OrarScolar.Solver;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OrarScolar.Export;

public static class PdfExport
{
    // QuestPDF Community License: free for education, individuals, non-profits and companies under $1M revenue.
    static PdfExport() => QuestPDF.Settings.License = LicenseType.Community;

    /// <summary>A4 landscape, one 5×7 grid per page.</summary>
    public static byte[] Build(IEnumerable<OrarGridModel> grids) => Document.Create(doc =>
    {
        foreach (var g in grids)
            doc.Page(p =>
            {
                p.Size(PageSizes.A4.Landscape());
                p.Margin(20);
                p.DefaultTextStyle(x => x.FontSize(8));
                p.Header().PaddingBottom(8).Text(g.Title).FontSize(16).Bold();
                p.Content().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.ConstantColumn(58); for (int d = 0; d < Slots.Days; d++) c.RelativeColumn(); });
                    t.Header(h =>
                    {
                        h.Cell().Element(Cell).Text("Ora").Bold();
                        foreach (var day in Slots.DayNames) h.Cell().Element(Cell).Text(day).Bold();
                    });
                    for (int per = 0; per < Slots.Periods; per++)
                    {
                        t.Cell().Element(Cell).Text(x => { x.Line($"{per + 1}").Bold(); x.Span(BellSchedule.Current.Label(per)).FontSize(7); });
                        for (int d = 0; d < Slots.Days; d++)
                            t.Cell().Element(Cell).Text(string.Join("\n", g.Cells[d * Slots.Periods + per]));
                    }
                });
                p.Footer().AlignRight().Text(x => { x.CurrentPageNumber(); x.Span(" / "); x.TotalPages(); });
            });
    }).GeneratePdf();

    /// <summary>The school-wide timetable on one A3 landscape page (for the notice board).</summary>
    public static byte[] BuildGeneral(List<GeneralRow> rows) => Document.Create(doc =>
        doc.Page(p =>
        {
            p.Size(PageSizes.A3.Landscape());
            p.Margin(15);
            p.DefaultTextStyle(x => x.FontSize(5.5f));
            p.Header().PaddingBottom(6).Text("Orarul general al școlii").FontSize(14).Bold();
            p.Content().Table(t =>
            {
                t.ColumnsDefinition(c => { c.ConstantColumn(34); for (int s = 0; s < Slots.Count; s++) c.RelativeColumn(); });
                t.Header(h =>
                {
                    h.Cell().RowSpan(2).Element(Small).AlignMiddle().Text("Clasa").Bold();
                    foreach (var day in Slots.DayNames) h.Cell().ColumnSpan(Slots.Periods).Element(Small).AlignCenter().Text(day).Bold().FontSize(8);
                    for (int s = 0; s < Slots.Count; s++) h.Cell().Element(Small).AlignCenter().Text($"{s % Slots.Periods + 1}").Bold();
                });
                foreach (var r in rows)
                {
                    t.Cell().Element(Small).Text(r.ClassName).Bold().FontSize(7);
                    foreach (var c in r.Cells)
                        t.Cell().Element(Small).Text(c == null ? "" : $"{c.Subject}\n{c.Teacher}\n{c.Room}");
                }
            });
        })).GeneratePdf();

    static IContainer Small(IContainer c) => c.Border(0.4f).Padding(1.5f);

    static IContainer Cell(IContainer c) => c.Border(0.5f).Padding(3).MinHeight(48);
}
