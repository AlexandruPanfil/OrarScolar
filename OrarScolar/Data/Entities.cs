using System.ComponentModel.DataAnnotations;

namespace OrarScolar.Data;

public enum RoomType { Normal, Laborator, Informatica, SalaSport }

public static class RoomTypeNames
{
    public static string Ro(this RoomType t) => t switch
    {
        RoomType.Laborator => "Laborator",
        RoomType.Informatica => "Informatică",
        RoomType.SalaSport => "Sală de sport",
        _ => "Normală",
    };
}

public class SchoolClass
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Numele este obligatoriu"), MaxLength(50)]
    public string Name { get; set; } = "";
    [Range(5, 12, ErrorMessage = "Clasa trebuie să fie între 5 și 12")]
    public int Grade { get; set; } = 5;
    [Range(1, 40, ErrorMessage = "Numărul de elevi trebuie să fie între 1 și 40")]
    public int StudentCount { get; set; } = 25;
}

public class Teacher
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Numele este obligatoriu"), MaxLength(80)]
    public string Name { get; set; } = "";
    public long UnavailableMask { get; set; }
    public long PreferredMask { get; set; }
}

public class Subject
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Numele este obligatoriu"), MaxLength(80)]
    public string Name { get; set; } = "";
    public bool IsDifficult { get; set; }
    public RoomType RoomType { get; set; }
}

public class Room
{
    public int Id { get; set; }
    [Required(ErrorMessage = "Numele este obligatoriu"), MaxLength(50)]
    public string Name { get; set; } = "";
    [Range(1, 200, ErrorMessage = "Capacitatea trebuie să fie între 1 și 200")]
    public int Capacity { get; set; } = 30;
    public RoomType RoomType { get; set; }
}

public class Assignment
{
    public int Id { get; set; }
    public int ClassId { get; set; }
    public int SubjectId { get; set; }
    public int TeacherId { get; set; }
    [Range(1, 10, ErrorMessage = "Orele pe săptămână trebuie să fie între 1 și 10")]
    public int HoursPerWeek { get; set; } = 1;
}

public class Meta
{
    public int Id { get; set; }
    public long DataVersion { get; set; }
}

public class Timetable
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public int Seed { get; set; }
    public string OptionsJson { get; set; } = "";
    public long DataVersion { get; set; }
    public int FitnessBefore { get; set; }
    public int FitnessAfter { get; set; }
    public long BtNodes { get; set; }
    public long ElapsedMs { get; set; }
    public string StoppedBy { get; set; } = "";
    public List<TimetableEntry> Entries { get; set; } = [];
}

public class TimetableEntry
{
    public int Id { get; set; }
    public int TimetableId { get; set; }
    public int AssignmentId { get; set; }
    public int Slot { get; set; }
    public int RoomId { get; set; }
}
