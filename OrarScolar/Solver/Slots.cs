using System.Diagnostics;
using System.Numerics;

namespace OrarScolar.Solver;

/// <summary>slot = day*7 + period; day 0..4 (Luni..Vineri), period 0..6 (UI shows 1..7).</summary>
public static class Slots
{
    public const int Days = 5, Periods = 7, Count = 35;
    public const ulong All = (1UL << Count) - 1;
    public static readonly string[] DayNames = ["Luni", "Marți", "Miercuri", "Joi", "Vineri"];

    public static ulong Bit(int s) => 1UL << s;
    /// <summary>Bits strictly above s; After(-1) = All.</summary>
    public static ulong After(int s) => s < 0 ? All : All & ~((2UL << s) - 1);
    public static ulong DayMask(int d) => 0x7FUL << (d * Periods);
    public static int Pop(ulong m) => BitOperations.PopCount(m);
    public static int Low(ulong m) => BitOperations.TrailingZeroCount(m);
    public static string Name(int s) => $"{DayNames[s / Periods]} ora {s % Periods + 1}";
}

/// <summary>One wall-clock deadline for all stages. End = 0 means off.</summary>
public readonly record struct Deadline(long End)
{
    public static Deadline None => new(0);
    public static Deadline In(TimeSpan t) => t <= TimeSpan.Zero ? None : new(Stopwatch.GetTimestamp() + (long)(t.TotalSeconds * Stopwatch.Frequency));
    public bool Passed => End != 0 && Stopwatch.GetTimestamp() >= End;
}
