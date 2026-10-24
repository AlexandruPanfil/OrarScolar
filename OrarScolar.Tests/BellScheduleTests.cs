using OrarScolar.Data;

namespace OrarScolar.Tests;

public class BellScheduleTests
{
    static string[] Labels(BellSchedule b) => Enumerable.Range(0, 7).Select(b.Label).ToArray();

    [Fact]
    public void Default_45min_15break_BigBreak12to13()
    {
        Assert.Equal(["08:00–08:45", "09:00–09:45", "10:00–10:45", "11:00–11:45", "13:00–13:45", "14:00–14:45", "15:00–15:45"],
            Labels(new BellSchedule()));
    }

    [Fact]
    public void HourLessons_NeverOverlapBigBreak()
    {
        var b = new BellSchedule { LessonMinutes = 60, BreakMinutes = 15 };
        Assert.Equal(["08:00–09:00", "09:15–10:15", "10:30–11:30", "13:00–14:00", "14:15–15:15", "15:30–16:30", "16:45–17:45"],
            Labels(b));
    }
}
