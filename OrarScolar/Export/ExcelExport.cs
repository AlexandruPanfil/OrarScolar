using ClosedXML.Excel;
using OrarScolar.Data;
using OrarScolar.Solver;

namespace OrarScolar.Export;

public static class ExcelExport
{
    /// <summary>One sheet per class, teacher and room.</summary>
    public static byte[] Build(OrarView v)
    {
        using var wb = new XLWorkbook();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
