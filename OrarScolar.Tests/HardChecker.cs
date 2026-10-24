using OrarScolar.Solver;

namespace OrarScolar.Tests;

/// <summary>Naive, independent hard-constraint counter used to validate every solver output.</summary>
public static class HardChecker
{
    public static int Count(SolverInput x, int[] slotOf, int[] roomOf)
    {
        if (slotOf.Length != x.N || roomOf.Length != x.N) throw new ArgumentException("lungime greșită");
        for (int i = 0; i < x.N; i++)
        {
            if (slotOf[i] < 0 || slotOf[i] >= 35) throw new ArgumentOutOfRangeException(nameof(slotOf), $"slot {slotOf[i]}");
            if (roomOf[i] < 0 || roomOf[i] >= x.RoomCount) throw new ArgumentOutOfRangeException(nameof(roomOf), $"sala {roomOf[i]}");
        }
        int count = 0;
        for (int i = 0; i < x.N; i++)
            for (int j = i + 1; j < x.N; j++)
            {
                if (slotOf[i] != slotOf[j]) continue;
                if (x.Class[i] == x.Class[j]) count++;
                if (x.Teacher[i] == x.Teacher[j]) count++;
                if (roomOf[i] == roomOf[j]) count++;
            }
        for (int i = 0; i < x.N; i++)
        {
            if ((x.TeacherUnavail[x.Teacher[i]] >> slotOf[i] & 1) != 0) count++;
            int r = roomOf[i];
            if (x.RoomTypes[r] != x.SubjectRoomType[x.Subject[i]] || x.RoomCap[r] < x.ClassSize[x.Class[i]]) count++;
        }
        for (int a = 0; a < x.AssignCount; a++)
            for (int d = 0; d < 5; d++)
            {
                int k = 0;
                for (int i = 0; i < x.N; i++) if (x.Assign[i] == a && slotOf[i] / 7 == d) k++;
                if (k > 2) count += k - 2;
            }
        return count;
    }
}
