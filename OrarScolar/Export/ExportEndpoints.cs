using Microsoft.EntityFrameworkCore;
using OrarScolar.Data;

namespace OrarScolar.Export;

/// <summary>Export endpoints: public GET /export/pdf?tip=&amp;id=, GET /export/pdf-general, GET /export/excel; admin-only GET /export/date.</summary>
public static class ExportEndpoints
{
    const string NoTimetable = "Nu există un orar generat";

    public static async Task<IResult> Pdf(string? tip, int? id, IDbContextFactory<AppDb> factory)
    {
        if (!OrarView.TryParseTip(tip, out var t))
            return TypedResults.BadRequest("Parametrul „tip” trebuie să fie clasa, profesor sau sala.");
        await using var db = await factory.CreateDbContextAsync();
        var v = await OrarView.LoadAsync(db);
        if (v == null) return TypedResults.NotFound(NoTimetable);
        List<OrarGridModel> grids;
        if (id is { } i)
        {
            if (v.Grid(t, i) is not { } g) return TypedResults.BadRequest($"Nu există {OrarView.TipParam(t)} cu id-ul {i}.");
            grids = [g];
        }
        else grids = v.Entities(t).Select(e => v.Grid(t, e.Id)!).ToList();
        var file = $"orar-{OrarView.TipParam(t)}{(id is { } n ? "-" + n : "")}.pdf";
        return TypedResults.File(PdfExport.Build(grids), "application/pdf", file);
    }

    public static async Task<IResult> Excel(IDbContextFactory<AppDb> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var v = await OrarView.LoadAsync(db);
        if (v == null) return TypedResults.NotFound(NoTimetable);
        return TypedResults.File(ExcelExport.Build(v), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "orar.xlsx");
    }

    /// <summary>The school-wide timetable as one A3 page.</summary>
    public static async Task<IResult> GeneralPdf(IDbContextFactory<AppDb> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var v = await OrarView.LoadAsync(db);
        if (v == null) return TypedResults.NotFound(NoTimetable);
        return TypedResults.File(PdfExport.BuildGeneral(v.General()), "application/pdf", "orar-general.pdf");
    }

    /// <summary>The school's input data as an editable workbook (also the import template). Admin only.</summary>
    public static async Task<IResult> SchoolDataXlsx(IDbContextFactory<AppDb> factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var (_, data) = await db.LoadSnapshotAsync();
        return TypedResults.File(DataWorkbook.Write(SchoolImport.ToSheets(data)), DataWorkbook.ContentType, "date-scoala.xlsx");
    }
}
