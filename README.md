# OrarScolar

A school timetable generator built with Blazor Server (.NET 10) and SQLite. An admin enters
classes, teachers, rooms, subjects and the weekly plan ("plan-cadru"). The app generates a
timetable with no conflicts and shows it publicly per class, teacher or room, with PDF and
Excel export. The interface is in Romanian.

## Installation

**Requirement:** [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Check it with `dotnet --version`.

```bash
git clone https://github.com/AlexandruPanfil/OrarScolar.git
cd OrarScolar
dotnet build OrarScolar/OrarScolar.csproj
```

The SQLite database (`orar.db`) is created on the first start. You don't need any migrations
or a database server.

## Running the app

The admin password comes from configuration (`Admin:Password`). If it is empty, nobody can log in.

PowerShell:
```powershell
$env:Admin__Password = "your-password"
dotnet run --project OrarScolar
```

Bash:
```bash
Admin__Password="your-password" dotnet run --project OrarScolar
```

Then open the URL printed in the console (by default `https://localhost:7xxx` / `http://localhost:5xxx`;
you can pick one with `--urls http://localhost:5199`).

## Accessing the app

| Who | Where | What |
|---|---|---|
| Anyone | `/` (Acasă), `/orar` | View the timetable per class, teacher or room. Export it as PDF (one or all) or Excel (whole timetable) |
| Admin | `/login` | Log in with the configured password. Logout is in the top-right corner |
| Admin | `/clase`, `/profesori`, `/sali`, `/materii`, `/plan-cadru` | Edit the input data. For teachers you also mark unavailable and preferred slots |
| Admin | `/generare` | Generate the timetable, cancel a run, compare the algorithms |

**Quick start:**
1. Log in.
2. On **Acasă**, choose a size and press **Generează date demo**.
3. Go to **Generare** and press **Generează**.
4. Open **Orar**.

Any real change to the input data invalidates the current timetable, so the app never shows a
timetable built from old data. Logins are limited to 5 attempts per minute per IP.

## How the three algorithms are used

Generation always runs the same pipeline. Each stage runs only if the previous one succeeded:

```
Preprocessing  →  Backtracking (CSP)  →  Genetic algorithm  →  saved timetable
```

| Stage | When it is used | What it does | Result |
|---|---|---|---|
| **1. Preprocessing**: pigeonhole checks, Hall matching, AC-3 | Always, first. Also on its own with **Validează datele** on Acasă | Finds data that can never fit, for example a class with more than 35 hours, a teacher with fewer free slots than hours, Chemistry with no lab, or a group of teachers who share too few slots. AC-3 also shrinks each lesson's possible slots | If a problem is found, you get a clear Romanian message and generation stops (**Date invalide**). Otherwise it passes reduced domains to backtracking |
| **2. Backtracking** (CSP): MRV, LCV, forward checking | Always, after preprocessing succeeds | Builds the **first valid timetable**: no class, teacher or room double-booking, rooms of the right type and size, teacher availability respected, at most 2 hours of a subject per day | **Succes** goes on to the GA. **Nesatisfiabil** means it was proven impossible. **Limită atinsă** means the node or time limit ran out; try another seed or a longer time limit |
| **3. Genetic algorithm**: tournament selection, day crossover, mutation, repair | Only after backtracking succeeds | **Improves the quality** of the valid timetable: fewer gaps for classes and teachers, difficult subjects in the first 4 lessons, teacher preferences, balanced days. It keeps the best valid timetable found, so it never makes things worse | The saved timetable. The page shows the score before (BT) and after (GA), broken down by criterion |

In short, preprocessing explains why data **cannot** work, backtracking finds **a** valid
timetable, and the genetic algorithm makes it a **good** one.

**Comparație algoritmi** on `/generare` runs four backtracking variants with the same node limit:
plain, +AC-3, +forward checking, and everything together. It shows status, nodes and time, which
is useful for showing how much each technique helps.

**Options on `/generare`:**
- seed, population, generations, crossover and mutation rate;
- a time limit for the whole pipeline (0 = none);
- **Reproductibil**: no time limit, so the same data and seed always give the same timetable;
- switches for AC-3, forward checking, MRV and LCV.

## Configuration

`OrarScolar/appsettings.json` (or environment variables such as `Orar__LessonMinutes=60`):

```json
"Orar": {
  "Start": "08:00",
  "LessonMinutes": 45,
  "BreakMinutes": 15,
  "BigBreakStart": "12:00",
  "BigBreakMinutes": 60
}
```

This produces the lesson times shown in the timetable and in the exports. The defaults give
08:00–11:45, a big break from 12:00 to 13:00, then 13:00–15:45.

- **Database location:** `ConnectionStrings__Orar="Data Source=path/orar.db"`
- **After changing the data model:** delete `orar.db`. It is recreated on start, and the demo data can be generated again.

## Tests

```bash
dotnet test OrarScolar.Tests
```

The suite covers:
- preprocessing messages, AC-3, forward checking vs plain search, and the undo logic;
- seed data solvability, GA non-regression, and determinism;
- staleness and atomic save on a real SQLite file;
- exports and lesson times.

Every solver result is checked by an independent, naive hard-constraint checker.

## Project layout

```
OrarScolar/         Blazor Server app
  Solver/           pure C# algorithms (no EF/Blazor): Preprocessor, Backtracking, Genetic, Fitness, Pipeline
  Data/             EF Core entities, AppDb, demo data generator, timetable view, lesson times
  Export/           PDF (QuestPDF) and Excel (ClosedXML) export
  Components/       pages (Romanian UI)
OrarScolar.Tests/   xUnit tests
```

**Licenses:** ClosedXML is MIT. QuestPDF uses its Community License, which is free for education,
individuals and companies under $1M revenue.
