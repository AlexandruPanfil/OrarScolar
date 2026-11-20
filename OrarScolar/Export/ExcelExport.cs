using ClosedXML.Excel;
using OrarScolar.Data;
using OrarScolar.Solver;

namespace OrarScolar.Export;

public static class ExcelExport
{
    /// <summary>The school-wide sheet first, then one sheet per class, teacher and room.</summary>
    public static byte[] Build(OrarView v)
    {
        using var wb = new XLWorkbook();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddGeneralSheet(wb, v, SheetName("Orar general", used));
        foreach (var tip in new[] { Tip.Clasa, Tip.Profesor, Tip.Sala })
            foreach (var (id, name) in v.Entities(tip))
            {
                var g = v.Grid(tip, id)!;
                var ws = wb.AddWorksheet(SheetName(name, used));
                ws.Cell(1, 1).Value = g.Title;
                ws.Cell(1, 1).Style.Font.Bold = true;
                ws.Cell(2, 1).Value = "Ora";
                for (int d = 0; d < Slots.Days; d++) ws.Cell(2, d + 2).Value = Slots.DayNames[d];
                ws.Row(2).Style.Font.Bold = true;
                for (int p = 0; p < Slots.Periods; p++)
                {
                    ws.Cell(p + 3, 1).Value = $"{p + 1}\n{BellSchedule.Current.Label(p)}";
                    ws.Cell(p + 3, 1).Style.Alignment.WrapText = true;
                    for (int d = 0; d < Slots.Days; d++)
                    {
                        var cell = ws.Cell(p + 3, d + 2);
                        cell.Value = string.Join("\n", g.Cells[d * Slots.Periods + p]);
                        cell.Style.Alignment.WrapText = true;
                    }
                }
                var range = ws.Range(2, 1, Slots.Periods + 2, Slots.Days + 1);
                range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Column(1).Width = 13;
                ws.Columns(2, Slots.Days + 1).Width = 22;
            }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>Classes on rows, days × lessons on columns; each cell "subject / teacher / room".</summary>
    static void AddGeneralSheet(XLWorkbook wb, OrarView v, string name)
    {
        var rows = v.General();
        var ws = wb.AddWorksheet(name);
        ws.Cell(1, 1).Value = "Orarul general al școlii";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = "Clasa";
        ws.Range(2, 1, 3, 1).Merge();
        for (int d = 0; d < Slots.Days; d++)
        {
            int c0 = 2 + d * Slots.Periods;
            ws.Cell(2, c0).Value = Slots.DayNames[d];
            ws.Range(2, c0, 2, c0 + Slots.Periods - 1).Merge();
            for (int p = 0; p < Slots.Periods; p++) ws.Cell(3, c0 + p).Value = p + 1;
        }
        var head = ws.Range(2, 1, 3, 1 + Slots.Count);
        head.Style.Font.Bold = true;
        head.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        head.Style.Fill.BackgroundColor = XLColor.LightGray;
        for (int r = 0; r < rows.Count; r++)
        {
            ws.Cell(r + 4, 1).Value = rows[r].ClassName;
            ws.Cell(r + 4, 1).Style.Font.Bold = true;
            for (int s = 0; s < Slots.Count; s++)
                if (rows[r].Cells[s] is { } c)
                {
                    var cell = ws.Cell(r + 4, s + 2);
                    cell.Value = $"{c.Subject}\n{c.Teacher}\n{c.Room}";
                    cell.Style.Alignment.WrapText = true;
                    cell.Style.Font.FontSize = 8;
                }
        }
        var all = ws.Range(2, 1, 3 + rows.Count, 1 + Slots.Count);
        all.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        all.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        all.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        for (int d = 1; d < Slots.Days; d++) // thicker line between days
            ws.Range(2, 2 + d * Slots.Periods, 3 + rows.Count, 2 + d * Slots.Periods).Style.Border.LeftBorder = XLBorderStyleValues.Medium;
        ws.Column(1).Width = 8;
        ws.Columns(2, 1 + Slots.Count).Width = 9;
        ws.SheetView.FreezeRows(3);
        ws.SheetView.FreezeColumns(1);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PaperSize = XLPaperSize.A3Paper;
        ws.PageSetup.FitToPages(1, 1);
    }

    /// <summary>Strips : \ / ? * [ ] and edge apostrophes, truncates to 31, dedups case-insensitively with " (2)", " (3)"…</summary>
    public static string SheetName(string raw, HashSet<string> used)
    {
        var clean = new string(raw.Where(ch => @":\/?*[]".IndexOf(ch) < 0).ToArray()).Trim().Trim('\'');
        if (clean.Length == 0) clean = "Foaie";
        var name = (clean.Length > 31 ? clean[..31] : clean).TrimEnd('\'');
        for (int k = 2; !used.Add(name); k++)
        {
            var suffix = $" ({k})";
            name = (clean.Length > 31 - suffix.Length ? clean[..(31 - suffix.Length)] : clean).TrimEnd('\'') + suffix;
        }
        return name;
    }
}
