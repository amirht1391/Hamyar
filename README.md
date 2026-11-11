# Hamyar (همیار)

**English** · [فارسی](README.fa.md)

A calm, minimal Windows desktop app for students: a day **planner** with chained alarms, your own **alarm tables**, a **study** tab with exact spaced-review schedules, **flashcards**, a weekly **timetable**, **prayer times**, **exams**, and a full-screen **focus mode**.
Persian / English, dark / light, four accent colours, glass (Mica / Acrylic) look. Data is stored locally in SQLite - nothing is sent anywhere.

![platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue) ![.NET](https://img.shields.io/badge/.NET-8-purple) ![version](https://img.shields.io/badge/version-4.0.0-green) ![license](https://img.shields.io/badge/license-MIT-lightgrey)

## Highlights
- **Planner** - a day as a chain of rows (title, start, minutes, notes). Change one time and every row follows. Saved plans with a date, rows after midnight show the next day's date, per-row mute, **insert a row anywhere** (as an extra row that moves nothing, or in the chain), **colour groups** (palette + colour selector), export to a Word file (A5, one page for up to 35 rows).
- **Alarms** - your own alarm tables, drag alarms between tables, repeat (once / daily / working days / weekly), sounds, and tasks (open a file, app or link, run a command, show a message).
- **Study** - topics with exact review timelines that tighten as the exam gets closer.
- **Flashcards** - five Leitner boxes, quick edit, drag and drop, 3D flip cards, CSV import/export.
- **Timetable, Prayer times, Exams, Focus sessions** (timer, checklist, notes, drawing board, calculator).
- **Keyboard shortcuts** - built-in (Ctrl+1 ... Ctrl+9 for the pages, F1 help, ...) and **your own**: bind any key combination to a file, program or website (Settings -> Keyboard shortcuts).
- **Guided tour** - on the first launch a cursor walks through every page and explains it. Replay it any time from Help or with `Ctrl+Shift+H`.
- Backups (automatic daily + manual), safe mode, drag-to-reorder tabs, smooth animations.

## Keyboard shortcuts (defaults)
| Keys | Action |
|---|---|
| Ctrl+1 ... Ctrl+9 | Home, Planner, Alarms, Study, Flashcards, Timetable, Prayer, Exams, Settings |
| F1 | Help |
| Ctrl+Shift+S | Start a focus session |
| Ctrl+Shift+H | Guided tour |

All of them can be changed in Settings. Plain letters never trigger a shortcut (Ctrl or Alt is required, F-keys work alone).

## Build
Requirements: Windows 10/11, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). The fonts are already in the repo (`Fonts/`, Vazirmatn) and are embedded into the exe.
```powershell
git clone <your-repo-url>
cd Hamyar
dotnet restore
dotnet build -c Release
dotnet run -c Release
```
Publish a single exe:
```powershell
.\publish.ps1                  # small, needs the .NET 8 Desktop Runtime
.\publish.ps1 -SelfContained   # ~70 MB, runs anywhere
```
Output: `publish\Hamyar.exe`.

## Data and logs
`%AppData%\TaskAlarm` - `data.db`, `backups\`, `log.txt` (the folder keeps its old name on purpose, so updates never lose data).
If a start never finishes, the next start is in **safe mode** (no glass, system font); turn it off in Settings.

## Project layout
| File | Role |
|---|---|
| `App.xaml(.cs)` | styles, font loading, crash log |
| `MainWindow*.cs / .xaml` | the main window, one file per page |
| `Hotkeys.cs` | shortcuts (built-in + custom) and their settings card |
| `Tour.cs` | the guided first-run tour |
| `PlanGroups.cs`, `ColorPickerPanel.cs` | planner colour groups and the colour selector |
| `PlanDocx.cs` | Word export (hand-written .docx, no extra package) |
| `Db.cs`, `Cfg.cs`, `Models.cs` | SQLite and settings |
| `Loc.cs` | Persian / English strings |
| `Fonts/` | Vazirmatn (all weights) + `OFL.txt` |
| `ReviewEngine.cs`, `Study.cs`, `Scheduler.cs` | spaced review and alarms |

## Font and licence
The app uses **Vazirmatn** in several weights (Regular for text, SemiBold / Bold / ExtraBold for titles). The font files in `Fonts/` are licensed under the [SIL Open Font License 1.1](Fonts/OFL.txt). The Word export uses **Arial**, so it looks the same on every computer.
The source code is released under the [MIT License](LICENSE).

## Changelog
See [CHANGELOG.md](CHANGELOG.md).
