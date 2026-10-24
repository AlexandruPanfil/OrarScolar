namespace OrarScolar.Solver;

public static class Weights
{
    public const int ClassGap = 10, TeacherGap = 3, LateDifficult = 5, OutsidePreferred = 2, Imbalance = 1;
}

/// <summary>Raw counts of each soft term (unweighted).</summary>
public sealed record Breakdown(int ClassGaps, int TeacherGaps, int LateDifficult, int OutsidePreferred, int Imbalance)
{
    public int Soft => ClassGaps * Weights.ClassGap + TeacherGaps * Weights.TeacherGap + LateDifficult * Weights.LateDifficult
        + OutsidePreferred * Weights.OutsidePreferred + Imbalance * Weights.Imbalance;
}

public static class Fitness
{
    /// <summary>
    /// Hard = class/teacher/room double-bookings (pairs) + unavailable slots + wrong room type/capacity + hours over 2/day.
    /// Soft = weighted gaps, late difficult lessons, lessons outside preferences, daily load imbalance.
    /// </summary>
    public static (int Hard, int Soft, Breakdown B) Evaluate(SolverInput x, int[] slotOf, int[] roomOf)
    {
        var classCnt = new int[x.ClassCount * Slots.Count];
        var teacherCnt = new int[x.TeacherCount * Slots.Count];
        var roomCnt = new int[x.RoomCount * Slots.Count];
        var dayCnt = new int[x.AssignCount * Slots.Days];
        var classMask = new ulong[x.ClassCount];
        var teacherMask = new ulong[x.TeacherCount];
        int hard = 0, late = 0, outside = 0;
        for (int i = 0; i < x.N; i++)
        {
            int s = slotOf[i], r = roomOf[i], c = x.Class[i], t = x.Teacher[i];
            hard += classCnt[c * Slots.Count + s]++ + teacherCnt[t * Slots.Count + s]++ + roomCnt[r * Slots.Count + s]++; // adds pairs
            if (++dayCnt[x.Assign[i] * Slots.Days + s / Slots.Periods] > 2) hard++;
            if ((x.TeacherUnavail[t] >> s & 1) != 0) hard++;
            if (x.RoomTypes[r] != x.SubjectRoomType[x.Subject[i]] || x.RoomCap[r] < x.ClassSize[c]) hard++;
            classMask[c] |= 1UL << s; teacherMask[t] |= 1UL << s;
            if (x.Difficult[i] && s % Slots.Periods >= 4) late++;
            if (x.TeacherPref[t] != 0 && (x.TeacherPref[t] >> s & 1) == 0) outside++;
        }
        int classGaps = classMask.Sum(Gaps), teacherGaps = teacherMask.Sum(Gaps);
        double imbalance = 0;
        for (int c = 0; c < x.ClassCount; c++)
        {
            double avg = x.ClassLessons[c].Length / (double)Slots.Days;
            for (int d = 0; d < Slots.Days; d++)
            {
                int h = 0;
                for (int s = d * Slots.Periods; s < (d + 1) * Slots.Periods; s++) h += classCnt[c * Slots.Count + s];
                imbalance += Math.Abs(h - avg);
            }
        }
        var b = new Breakdown(classGaps, teacherGaps, late, outside, (int)Math.Round(imbalance));
        return (hard, b.Soft, b);
    }

    /// <summary>Per day: (high − low + 1) − popcount.</summary>
    public static int Gaps(ulong mask)
    {
        int g = 0;
        for (int d = 0; d < Slots.Days; d++)
        {
            ulong day = mask >> (d * Slots.Periods) & 0x7F;
            if (day == 0) continue;
            int low = Slots.Low(day), high = 63 - System.Numerics.BitOperations.LeadingZeroCount(day);
            g += high - low + 1 - Slots.Pop(day);
        }
        return g;
    }
}
