using System.Globalization;
using System.Text;
using OrarScolar.Solver;

namespace OrarScolar.Data;

/// <summary>
/// Converts the school's input data to and from plain tables (sheet name → rows of cells), so a real
/// school can fill in an Excel file instead of typing everything in the UI. Row 0 of every sheet is the
/// header. The Excel side lives in Export/DataWorkbook.cs; this class has no dependency on it.
/// </summary>
public static class SchoolImport
{
    public const string SClase = "Clase", SProfesori = "Profesori", SMaterii = "Materii", SSali = "Săli", SPlan = "Plan-cadru";
    public static readonly string[] SheetOrder = [SClase, SProfesori, SMaterii, SSali, SPlan];

    public static readonly Dictionary<string, string[]> Headers = new()
    {
        [SClase] = ["Nume", "Anul de studiu (5-12)", "Număr elevi"],
        [SProfesori] = ["Nume", "Indisponibil", "Preferat"],
        [SMaterii] = ["Nume", "Dificilă (da/nu)", "Tip sală"],
        [SSali] = ["Nume", "Capacitate", "Tip sală"],
        [SPlan] = ["Clasa", "Materia", "Profesor", "Ore pe săptămână"],
    };

    /// <summary>Lines of the "Instrucțiuni" sheet.</summary>
    public static readonly string[] Instructions =
    [
        "Completați câte o foaie pentru clase, profesori, materii, săli și planul-cadru. Primul rând al fiecărei foi este antetul și nu se modifică.",
        "Tip sală: Normală, Laborator, Informatică sau Sală de sport (se acceptă și fără diacritice).",
        "Dificilă: da / nu. Materiile dificile sunt plasate, pe cât posibil, în primele 4 lecții.",
        "Indisponibil / Preferat: zile și lecții, separate prin „;”. Exemple: „Luni” (toată ziua), „Marți 1-3”, „Miercuri 1,2,7; Vineri 5-7”. Abrevieri: L, Ma, Mi, J, V. Gol = fără restricții.",
        "Plan-cadru: o linie pentru fiecare materie a fiecărei clase, cu profesorul care o predă și numărul de ore pe săptămână (1-10). Numele trebuie să fie identice cu cele din celelalte foi.",
        "La import, toate datele existente și orarul curent sunt înlocuite.",
    ];

    // ---------- export ----------

    public static Dictionary<string, List<string[]>> ToSheets(SchoolData d)
    {
        var cls = d.Classes.ToDictionary(c => c.Id, c => c.Name);
        var sub = d.Subjects.ToDictionary(s => s.Id, s => s.Name);
        var tea = d.Teachers.ToDictionary(t => t.Id, t => t.Name);
        var sheets = SheetOrder.ToDictionary(s => s, s => new List<string[]> { Headers[s] });
        foreach (var c in d.Classes.OrderBy(c => c.Grade).ThenBy(c => c.Name))
            sheets[SClase].Add([c.Name, Num(c.Grade), Num(c.StudentCount)]);
        foreach (var t in d.Teachers.OrderBy(t => t.Name))
            sheets[SProfesori].Add([t.Name, SlotText.Format(t.UnavailableMask), SlotText.Format(t.PreferredMask)]);
        foreach (var s in d.Subjects.OrderBy(s => s.Id))
            sheets[SMaterii].Add([s.Name, s.IsDifficult ? "da" : "nu", s.RoomType.Ro()]);
        foreach (var r in d.Rooms.OrderBy(r => r.RoomType).ThenBy(r => r.Name))
            sheets[SSali].Add([r.Name, Num(r.Capacity), r.RoomType.Ro()]);
        var classOrder = d.Classes.OrderBy(c => c.Grade).ThenBy(c => c.Name).Select((c, i) => (c.Id, i)).ToDictionary(p => p.Id, p => p.i);
        foreach (var a in d.Assignments.OrderBy(a => classOrder.GetValueOrDefault(a.ClassId)).ThenBy(a => a.SubjectId))
            if (cls.TryGetValue(a.ClassId, out var cn) && sub.TryGetValue(a.SubjectId, out var sn) && tea.TryGetValue(a.TeacherId, out var tn))
                sheets[SPlan].Add([cn, sn, tn, Num(a.HoursPerWeek)]);
        return sheets;
    }

    static string Num(int v) => v.ToString(CultureInfo.InvariantCulture);

    // ---------- import ----------

    const int MaxErrors = 40;

    /// <summary>Builds the school data from the sheets. Data is null when there is at least one error.</summary>
    public static (SchoolData? Data, List<string> Errors) Parse(IReadOnlyDictionary<string, List<string[]>> raw)
    {
        var err = new List<string>();
        var sheets = new Dictionary<string, List<string[]>>();
        foreach (var name in SheetOrder)
        {
            var found = raw.FirstOrDefault(kv => Key(kv.Key) == Key(name));
            if (found.Value == null) err.Add($"Lipsește foaia „{name}”.");
            else sheets[name] = found.Value;
        }
        if (err.Count > 0) return (null, err);

        // rows after the header, skipping fully empty rows; (excel row number, cells)
        IEnumerable<(int Row, string[] Cells)> Rows(string sheet) =>
            sheets[sheet].Select((r, i) => (Row: i + 1, Cells: r.Select(c => (c ?? "").Trim()).ToArray()))
                .Skip(1).Where(r => r.Cells.Any(c => c.Length > 0));
        string Cell(string[] r, int k) => k < r.Length ? r[k] : "";
        string At(string sheet, int row) => $"Foaia „{sheet}”, rândul {row}";

        var classes = new List<SchoolClass>();
        var classByName = new Dictionary<string, SchoolClass>();
        foreach (var (row, r) in Rows(SClase))
        {
            var name = Cell(r, 0);
            if (!CheckName(name, 50, At(SClase, row), err) || !Unique(classByName, name, At(SClase, row), "clasa", err)) continue;
            var c = new SchoolClass { Id = -row, Name = name };
            classByName[Key(name)] = c; // registered even if invalid, so the plan sheet does not repeat the error
            if (Int(Cell(r, 1), 5, 12, At(SClase, row), "anul de studiu", err) is { } g) c.Grade = g; else continue;
            if (Int(Cell(r, 2), 1, 40, At(SClase, row), "numărul de elevi", err) is { } n) c.StudentCount = n; else continue;
            c.Id = classes.Count + 1; classes.Add(c);
        }

        var teachers = new List<Teacher>();
        var teacherByName = new Dictionary<string, Teacher>();
        foreach (var (row, r) in Rows(SProfesori))
        {
            var name = Cell(r, 0);
            if (!CheckName(name, 80, At(SProfesori, row), err) || !Unique(teacherByName, name, At(SProfesori, row), "profesorul", err)) continue;
            var t = new Teacher { Id = -row, Name = name };
            teacherByName[Key(name)] = t;
            if (!SlotText.TryParse(Cell(r, 1), out var un, out var e1)) { err.Add($"{At(SProfesori, row)}: indisponibil – {e1}"); continue; }
            if (!SlotText.TryParse(Cell(r, 2), out var pr, out var e2)) { err.Add($"{At(SProfesori, row)}: preferat – {e2}"); continue; }
            t.Id = teachers.Count + 1; t.UnavailableMask = un; t.PreferredMask = pr;
            teachers.Add(t);
        }

        var subjects = new List<Subject>();
        var subjectByName = new Dictionary<string, Subject>();
        foreach (var (row, r) in Rows(SMaterii))
        {
            var name = Cell(r, 0);
            if (!CheckName(name, 80, At(SMaterii, row), err) || !Unique(subjectByName, name, At(SMaterii, row), "materia", err)) continue;
            var s = new Subject { Id = -row, Name = name };
            subjectByName[Key(name)] = s;
            bool? diff = YesNo(Cell(r, 1));
            if (diff == null) { err.Add($"{At(SMaterii, row)}: „dificilă” trebuie să fie da sau nu (este „{Cell(r, 1)}”)."); continue; }
            if (RoomTypeOf(Cell(r, 2), At(SMaterii, row), err) is not { } type) continue;
            s.Id = subjects.Count + 1; s.IsDifficult = diff.Value; s.RoomType = type;
            subjects.Add(s);
        }

        var rooms = new List<Room>();
        var roomByName = new Dictionary<string, Room>();
        foreach (var (row, r) in Rows(SSali))
        {
            var name = Cell(r, 0);
            if (!CheckName(name, 50, At(SSali, row), err) || !Unique(roomByName, name, At(SSali, row), "sala", err)) continue;
            roomByName[Key(name)] = new Room { Name = name };
            if (Int(Cell(r, 1), 1, 200, At(SSali, row), "capacitatea", err) is not { } cap) continue;
            if (RoomTypeOf(Cell(r, 2), At(SSali, row), err) is not { } type) continue;
            rooms.Add(new Room { Id = rooms.Count + 1, Name = name, Capacity = cap, RoomType = type });
        }

        var assignments = new List<Assignment>();
        var pairs = new HashSet<(int, int)>();
        foreach (var (row, r) in Rows(SPlan))
        {
            var at = At(SPlan, row);
            string cn = Cell(r, 0), sn = Cell(r, 1), tn = Cell(r, 2);
            bool ok = true;
            if (!classByName.TryGetValue(Key(cn), out var c)) { err.Add($"{at}: clasa „{cn}” nu există în foaia „{SClase}”."); ok = false; }
            if (!subjectByName.TryGetValue(Key(sn), out var s)) { err.Add($"{at}: materia „{sn}” nu există în foaia „{SMaterii}”."); ok = false; }
            if (!teacherByName.TryGetValue(Key(tn), out var t)) { err.Add($"{at}: profesorul „{tn}” nu există în foaia „{SProfesori}”."); ok = false; }
            int? hours = Int(Cell(r, 3), 1, 10, at, "numărul de ore", err);
            if (hours == null) ok = false;
            else if (ok && !pairs.Add((c!.Id, s!.Id))) { err.Add($"{at}: materia „{sn}” apare de două ori în planul clasei {cn}."); ok = false; }
            if (ok) assignments.Add(new Assignment { Id = assignments.Count + 1, ClassId = c!.Id, SubjectId = s!.Id, TeacherId = t!.Id, HoursPerWeek = hours!.Value });
        }

        if (err.Count == 0 && classes.Count == 0) err.Add($"Foaia „{SClase}” nu conține nicio clasă.");
        if (err.Count == 0 && assignments.Count == 0) err.Add($"Foaia „{SPlan}” nu conține nicio linie.");
        if (err.Count > MaxErrors) err = [.. err.Take(MaxErrors), $"… și încă {err.Count - MaxErrors} erori."];
        return err.Count > 0 ? (null, err) : (new SchoolData(classes, teachers, subjects, rooms, assignments), err);
    }

    static bool CheckName(string name, int max, string at, List<string> err)
    {
        if (name.Length == 0) { err.Add($"{at}: numele lipsește."); return false; }
        if (name.Length > max) { err.Add($"{at}: numele „{name}” depășește {max} caractere."); return false; }
        return true;
    }

    static bool Unique<T>(Dictionary<string, T> seen, string name, string at, string what, List<string> err)
    {
        if (!seen.ContainsKey(Key(name))) return true;
        err.Add($"{at}: {what} „{name}” apare de două ori.");
        return false;
    }

    static int? Int(string s, int min, int max, string at, string what, List<string> err)
    {
        // Excel may hand back "25.0" for a numeric cell
        if (double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v == Math.Floor(v) && v >= min && v <= max)
            return (int)v;
        err.Add($"{at}: {what} trebuie să fie un număr întreg între {min} și {max} (este „{s}”).");
        return null;
    }

    static bool? YesNo(string s) => Key(s) switch
    {
        "da" or "d" or "x" or "1" or "true" or "yes" => true,
        "nu" or "n" or "" or "0" or "false" or "no" => false,
        _ => null,
    };

    static RoomType? RoomTypeOf(string s, string at, List<string> err)
    {
        var k = Key(s).Replace(" ", "");
        RoomType? t = k switch
        {
            "" or "normala" or "normal" or "clasa" or "obisnuita" => RoomType.Normal,
            "laborator" or "lab" => RoomType.Laborator,
            "informatica" or "salainformatica" or "saladeinformatica" => RoomType.Informatica,
            "saladesport" or "salasport" or "sport" => RoomType.SalaSport,
            _ => null,
        };
        if (t == null) err.Add($"{at}: tipul de sală „{s}” nu este recunoscut (Normală, Laborator, Informatică, Sală de sport).");
        return t;
    }

    /// <summary>Case-, space- and diacritic-insensitive key (ș/ş, ț/ţ, ă, â, î).</summary>
    public static string Key(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch switch { 'ş' or 'Ş' => 's', 'ţ' or 'Ţ' => 't', _ => char.ToLowerInvariant(ch) });
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>
/// Text form of a slot mask, e.g. "Luni; Marți 1-3; Vineri 2,5-7". A day without lessons listed means
/// the whole day. Bit = day*7 + (lesson-1).
/// </summary>
public static class SlotText
{
    static readonly string[][] DayAliases =
    [
        ["luni", "l", "lu"],
        ["marti", "ma", "mar"],
        ["miercuri", "mi", "mie"],
        ["joi", "j", "jo"],
        ["vineri", "v", "vi", "vin"],
    ];

    public static string Format(long mask)
    {
        var parts = new List<string>();
        for (int d = 0; d < Slots.Days; d++)
        {
            int day = (int)((ulong)mask >> (d * Slots.Periods) & 0x7F);
            if (day == 0) continue;
            if (day == 0x7F) { parts.Add(Slots.DayNames[d]); continue; }
            var runs = new List<string>();
            for (int p = 0; p < Slots.Periods;)
            {
                if ((day >> p & 1) == 0) { p++; continue; }
                int q = p;
                while (q + 1 < Slots.Periods && (day >> (q + 1) & 1) != 0) q++;
                runs.Add(q == p ? $"{p + 1}" : $"{p + 1}-{q + 1}");
                p = q + 1;
            }
            parts.Add($"{Slots.DayNames[d]} {string.Join(",", runs)}");
        }
        return string.Join("; ", parts);
    }

    public static bool TryParse(string? text, out long mask, out string? error)
    {
        mask = 0; error = null;
        foreach (var raw in (text ?? "").Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = SchoolImport.Key(raw).Replace(":", " ");
            int split = 0;
            while (split < part.Length && char.IsLetter(part[split])) split++;
            var dayWord = part[..split];
            int d = Array.FindIndex(DayAliases, a => a.Contains(dayWord));
            if (d < 0) { error = $"ziua „{raw}” nu este recunoscută (Luni, Marți, Miercuri, Joi, Vineri)."; return false; }
            var rest = part[split..].Replace(" ", "");
            if (rest.Length == 0 || rest == "*") { mask |= (long)Slots.DayMask(d); continue; }
            foreach (var item in rest.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var ends = item.Split('-');
                if (ends.Length > 2 || !int.TryParse(ends[0], out var a) || !int.TryParse(ends[^1], out var b) || a < 1 || b > Slots.Periods || a > b)
                { error = $"„{raw}”: lecțiile trebuie să fie numere între 1 și {Slots.Periods} (ex.: 1-3,7)."; return false; }
                for (int p = a; p <= b; p++) mask |= 1L << (d * Slots.Periods + p - 1);
            }
        }
        return true;
    }
}
