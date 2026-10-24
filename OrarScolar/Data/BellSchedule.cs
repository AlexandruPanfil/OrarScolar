namespace OrarScolar.Data;

/// <summary>
/// Clock times of the 7 daily lessons. Set from the "Orar" section of appsettings.json.
/// Lessons follow each other with a short break; a lesson that would run into the big break
/// (BigBreakStart … +BigBreakMinutes, e.g. 12:00–13:00) starts when the big break ends.
/// </summary>
public sealed class BellSchedule
{
    public TimeOnly Start { get; set; } = new(8, 0);
    public int LessonMinutes { get; set; } = 45;
    public int BreakMinutes { get; set; } = 15;
    public TimeOnly BigBreakStart { get; set; } = new(12, 0);
    public int BigBreakMinutes { get; set; } = 60;

    public static BellSchedule Current { get; set; } = new();

    public (TimeOnly From, TimeOnly To) Period(int p)
    {
        var bigEnd = BigBreakStart.AddMinutes(BigBreakMinutes);
        var t = Start;
        for (int k = 0; ; k++)
        {
            if (BigBreakMinutes > 0 && t < bigEnd && t.AddMinutes(LessonMinutes) > BigBreakStart) t = bigEnd;
            if (k == p) return (t, t.AddMinutes(LessonMinutes));
            t = t.AddMinutes(LessonMinutes + BreakMinutes);
        }
    }

    public string Label(int p) { var (f, t) = Period(p); return $"{f:HH\\:mm}–{t:HH\\:mm}"; }
}
