# Timetable Alert

A Windows system tray application that puts a red warning on screen before each lesson starts,
for anyone who loses track of the time during an online learning course.

Two warnings per lesson:

- **7 minutes before** — a red banner naming the lesson, on screen for 5 seconds.
- **60 seconds before** — the same banner with a live countdown, held until the lesson starts.

The banner appears on **every monitor**, always on top, and is **click-through**: it never takes
focus and mouse clicks pass straight through to whatever is underneath, so it interrupts you
visually without interrupting what you are doing.

The one exception is the **✕ in the top-right corner**, which dismisses the banner from every
monitor at once. Clicking it does not steal focus either — you can swat the banner away without
losing your place in whatever you were typing. Dismissing the 7-minute warning still leaves the
60-second countdown to come; dismissing the countdown is final for that lesson.

## Using it

Everything is on the tray icon's menu:

| Menu item | What it does |
|---|---|
| **Load timetable…** | Pick a timetable JSON file. The path is remembered, and the next lesson's banner is previewed. |
| **Reload timetable** | Re-read the current file after editing it, and preview the next lesson's banner. |
| **Refresh from calendar** | Download this week from the school calendar now, without waiting for the cached copy to expire. |
| **Timetable source…** | Set the calendar feed address, and optionally the parent login used for teacher names. |
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

- `day` — full or three-letter name, any case: `Monday`, `mon`, `THURSDAY`. A lesson given a `day`
  repeats every week, which is what you want in a file you maintain by hand.
- `date` — `yyyy-MM-dd`, an alternative to `day`. A lesson given a `date` happens once, on that
  day, and never again. This is what a downloaded week uses, so a one-off — an orientation, a
  mentor session — does not come back next Tuesday. Give one or the other; if you give both they
  have to agree.
- `start`, `end` — 24-hour local time, `HH:mm`. `end` is optional and used only for display.
- `teacher` — optional.
- `student` — optional; shown in the About box.
- `coversFrom`, `coversUntil`, `fetchedAt` — written by the app on a downloaded week to record
  which week it speaks for and when it was fetched. Not needed in a hand-written file.
- `alerts` — optional; the values above are the defaults. Change these to move the warnings
  without rebuilding the app. The early warning must be further ahead than the countdown.

The whole file is validated on load, and anything wrong is reported naming the entry
(`lessons[4]: 'Frugday' is not a day of the week.`) rather than being silently dropped.

`timetable.sample.json` in this repository is a made-up week — the student and teachers are
fictional — laid out like a real one, and is a good starting point to edit.

## Getting the timetable from the school calendar

Timetables drift from week to week, so the app can rebuild its own from the school's live Canvas
calendar instead of being edited by hand.

Open **Timetable source…** on the tray menu and paste the **calendar feed address** — the `.ics`
URL behind the *Calendar Feed* button at the bottom right of the Canvas calendar page. That is all
that is needed: the address carries its own token, so downloading lessons never asks for a
password. **Test** checks it and says how many lessons it found.

From then on the app fetches the current week itself. It keeps the last copy in
`%APPDATA%\TimetableAlert\timetable.cache.json` and only downloads again when that copy has
expired, which means either of:

- it is more than **12 hours** old, or
- the week it covers no longer includes today.

So a normal logon uses the cached copy and touches the network at most twice a day. If a download
fails, the app carries on with the cached copy and says so in the tray tooltip, rather than going
quiet. A cached week never outlives itself: because every downloaded lesson carries its real date,
last week's timetable cannot go on firing this week's alerts.

> **Treat the feed address like a password.** Anyone holding it can read the calendar. It is stored
> in Windows Credential Manager, not in any settings file.

### Teacher names

The calendar feed does not carry teacher names. To fill them in, add the **parent** Canvas
username and password in the same dialog and press **Refresh teacher names**. They are stored in
Credential Manager and used only for that button — never for the ordinary weekly download.

A course with exactly one teacher, ignoring the operations accounts every course carries, is
matched automatically. Year-group courses — assembly, social room, personal development — list the
whole year team, so those stay as they are and can be set by hand in
`%APPDATA%\TimetableAlert\settings.json`; a refresh will not overwrite them.

Student logins will not work here: they go through Google, and only the parent account has a
Canvas password.

### Doing it from the command line instead

`scripts/fetch-timetable.py` does the same job outside the app, writing a timetable JSON you can
load with **Load timetable…**. It is useful for generating a file on another machine, or for
seeing what the tidying rules make of a title.

```bash
cp scripts/timetable-source.example.json scripts/timetable-source.json   # then add your feed URL
./scripts/fetch-timetable.py                 # this week   -> timetable.json
./scripts/fetch-timetable.py --week next     # next week
```

`scripts/timetable-source.json` is gitignored and should stay that way, for the same reason as
above. Needs Python 3.9+ and no third-party packages.

### How a calendar title becomes a subject

`Y10 Maths Higher Core Luna [10MaHLun]` becomes `Maths Higher`: the trailing course code comes off,
then the year-group prefix, then any trailing "drop words" — teaching-set names like `Luna` and
`Ceres`, and filler like `Core`. Titles the rules cannot tidy are mapped outright. Both lists live
in `settings.json` under `feed`, and in `scripts/timetable-source.example.json` for the script.

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
