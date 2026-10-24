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

    static IContainer Cell(IContainer c) => c.Border(0.5f).Padding(3).MinHeight(48);
}
