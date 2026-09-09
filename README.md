# Timetable Alert

A Windows system tray application that puts a red warning on screen before each lesson starts,
for anyone who loses track of the time during an online learning course.

Two warnings per lesson:

- **7 minutes before** — a red banner naming the lesson, on screen for 5 seconds.
- **60 seconds before** — the same banner with a live countdown, held until the lesson starts.

The banner appears on **every monitor**, always on top, and is **click-through**: it never takes
focus and mouse clicks pass straight through to whatever is underneath, so it interrupts you
visually without interrupting what you are doing.

## Using it

Everything is on the tray icon's menu:

| Menu item | What it does |
|---|---|
| **Load timetable…** | Pick a timetable JSON file. The path is remembered, and the next lesson's banner is previewed. |
| **Reload timetable** | Re-read the current file after editing it, and preview the next lesson's banner. |
| **Today's lessons** | List what is on today. |
| **Test overlay** | Show a sample banner, to check the overlay without waiting for a lesson. |
| **About** | Version and whose timetable is loaded. |
| **Exit** | Quit. |

After a successful **Load** or **Reload**, the next lesson's banner is shown for 5 seconds
however far off that lesson is, so you can see exactly how the warning will look without
waiting for one. Far-out lessons name the time rather than counting down to it — "starts at
09:00 on Monday" rather than "starts in 4260 minutes". Starting the app at logon does *not*
preview, so there is no banner every time the machine boots.

The tray tooltip always shows the next lesson.

On first run, if no timetable has been chosen yet, the app loads `timetable.sample.json` from
its own folder. The installer registers the app to start with Windows.

## Installing

The MSI on the [releases page](../../releases) installs for the current user only, into
`%LOCALAPPDATA%\Programs\TimetableAlert`, so it never asks for administrator rights. If you
have v0.1.3 or earlier installed — those went into Program Files for all users — uninstall it
before installing this one; a per-user installer has no way to remove a per-machine one.

## Timetable format

```json
{
  "student": "Sam",
  "alerts": {
    "firstWarningMinutes": 7,
    "firstWarningSeconds": 5,
    "secondWarningSeconds": 60
  },
  "lessons": [
    { "day": "Monday",  "start": "08:30", "end": "09:00", "subject": "Assembly",     "teacher": null },
    { "day": "Tuesday", "start": "09:00", "end": "10:00", "subject": "Maths Higher", "teacher": "Bailey Bravo" }
  ]
}
```

- `day` — full or three-letter name, any case: `Monday`, `mon`, `THURSDAY`.
- `start`, `end` — 24-hour local time, `HH:mm`. `end` is optional and used only for display.
- `teacher` — optional.
- `student` — optional; shown in the About box.
- `alerts` — optional; the values above are the defaults. Change these to move the warnings
  without rebuilding the app. The early warning must be further ahead than the countdown.

The whole file is validated on load, and anything wrong is reported naming the entry
(`lessons[4]: 'Frugday' is not a day of the week.`) rather than being silently dropped.

`timetable.sample.json` in this repository is a made-up week — the student and teachers are
fictional — laid out like a real one, and is a good starting point to edit.

## Building

Requires the .NET 10 SDK on Windows.

```bash
dotnet build TimetableAlert.csproj -c Release
dotnet test TimetableAlert.Tests/TimetableAlert.Tests.csproj
```

Build the app project, not the solution: the solution also contains the WiX v3 installer
project, which the `dotnet` CLI cannot load. The MSI is built separately with full MSBuild
(`msbuild TimetableAlert.Installer/TimetableAlert.Installer.wixproj /p:Configuration=Release
/p:Platform=x64`), which is what the release workflow does.

Releases are cut by `./scripts/make-release`, which tags the next patch version and pushes it;
GitHub Actions then builds the WiX MSI and attaches it to a GitHub Release.

## Licence

GPL-3.0. See [LICENSE](LICENSE).
