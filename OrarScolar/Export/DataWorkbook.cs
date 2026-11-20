using ClosedXML.Excel;
using OrarScolar.Data;

namespace OrarScolar.Export;

/// <summary>Excel side of <see cref="SchoolImport"/>: sheets of strings ⇄ .xlsx.</summary>
public static class DataWorkbook
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    const string InstructionsSheet = "Instrucțiuni";

    public static byte[] Write(Dictionary<string, List<string[]>> sheets)
    {
        using var wb = new XLWorkbook();
        var info = wb.AddWorksheet(InstructionsSheet);
        info.Cell(1, 1).Value = "Datele școlii – Orar Școlar";
        info.Cell(1, 1).Style.Font.Bold = true;
        for (int i = 0; i < SchoolImport.Instructions.Length; i++)
        {
            info.Cell(i + 3, 1).Value = $"{i + 1}. {SchoolImport.Instructions[i]}";
            info.Cell(i + 3, 1).Style.Alignment.WrapText = true;
        }
        info.Column(1).Width = 110;

        foreach (var name in SchoolImport.SheetOrder)
        {
            var ws = wb.AddWorksheet(name);
            var rows = sheets[name];
            for (int r = 0; r < rows.Count; r++)
                for (int c = 0; c < rows[r].Length; c++)
                {
                    var cell = ws.Cell(r + 1, c + 1);
                    var v = rows[r][c];
                    if (r > 0 && int.TryParse(v, out var n)) cell.Value = n;
                    else cell.Value = v;
                }
            ws.Row(1).Style.Font.Bold = true;
            ws.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;
            ws.SheetView.FreezeRows(1);
            for (int c = 1; c <= SchoolImport.Headers[name].Length; c++)
                ws.Column(c).Width = name == SchoolImport.SProfesori && c > 1 ? 40 : 24;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>Every sheet as rows of displayed cell text. Error is set when the file is not a readable .xlsx.</summary>
    public static (Dictionary<string, List<string[]>>? Sheets, string? Error) Read(Stream s)
    {
        try
        {
            using var wb = new XLWorkbook(s);
            var result = new Dictionary<string, List<string[]>>();
            foreach (var ws in wb.Worksheets)
            {
                var rows = new List<string[]>();
                int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
                int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
                if (lastRow > 5000 || lastCol > 50) return (null, $"Foaia „{ws.Name}” este prea mare ({lastRow} rânduri × {lastCol} coloane).");
                for (int r = 1; r <= lastRow; r++)
                {
                    var row = new string[lastCol];
                    for (int c = 1; c <= lastCol; c++) row[c - 1] = ws.Cell(r, c).GetFormattedString();
                    rows.Add(row);
                }
                result[ws.Name] = rows;
            }
            return (result, null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return (null, "Fișierul nu a putut fi citit. Folosiți un fișier Excel (.xlsx) descărcat din aplicație.");
        }
    }
}
