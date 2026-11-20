# OrarScolar

A school timetable generator built with Blazor Server (.NET 10) and SQLite. An admin enters
classes, teachers, rooms, subjects and the weekly plan ("plan-cadru"). The app generates a
timetable with no conflicts and shows it publicly per class, teacher or room, with PDF and
Excel export. A real school can load its data from an Excel file, and an experiments page
measures what each algorithm contributes. The interface is in Romanian.

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
| Admin | `/` (Acasă) → **Datele reale ale școlii** | Download the school data as Excel (`/export/date`), fill it in, upload it back |
| Admin | `/experimente` | Run the algorithms on generated schools of 3, 8 and 16 classes and get tables, a convergence chart and CSV for the report |

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

## Importing a real school (Excel)

1. Log in and open **Acasă**. Under **Datele reale ale școlii**, press **Descarcă datele (Excel)**.
   With no data you get an empty template; after **Generează date demo** you get a filled example.
2. Fill in the sheets **Clase**, **Profesori**, **Materii**, **Săli** and **Plan-cadru**. The first sheet,
   **Instrucțiuni**, explains every column. Teacher availability is written as text, for example
   `Luni; Marți 1-3; Vineri 5-7` (a day alone means the whole day).
3. Upload the file. Every error is reported with its sheet and row, and nothing is imported until the file
   is clean. After you confirm, all data and the current timetable are replaced, and the necessary-condition
   checks run at once, so you know right away whether a timetable is possible.

Names are matched without regard to case or diacritics, so `Marti` and `Marți` are the same day.

## Experiments (for the report)

`/experimente` generates schools of 3, 8 and 16 classes (fixed data seeds, no deadline, so the numbers are
reproducible) and shows:

1. the four backtracking variants under the same node limit: how many instances each solves, nodes, backtracks, time;
2. the genetic algorithm: fitness of the backtracking timetable vs after the GA, improvement in %, broken down by criterion;
3. a convergence chart (best fitness per generation) for the largest size.

Everything is also given as semicolon-separated CSV that can be pasted into Excel.

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
- exports and lesson times;
- the Excel import: text form of availability, round trip through the workbook, row-level error messages;
- a small experiments run (the GA best score never rises, the full backtracking variant solves the instance).

Every solver result is checked by an independent, naive hard-constraint checker.

## Project layout

```
OrarScolar/         Blazor Server app
  Solver/           pure C# algorithms (no EF/Blazor): Preprocessor, Backtracking, Genetic, Fitness, Pipeline, Experiments
  Data/             EF Core entities, AppDb, demo data generator, Excel import rules (SchoolImport), timetable view, lesson times
  Export/           PDF (QuestPDF) and Excel (ClosedXML) export, school data workbook (DataWorkbook)
  Components/       pages (Romanian UI)
OrarScolar.Tests/   xUnit tests
```

**Licenses:** ClosedXML is MIT. QuestPDF uses its Community License, which is free for education,
individuals and companies under $1M revenue.
