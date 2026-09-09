# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A Windows system tray application (.NET 10 / WPF, [H.NotifyIcon.Wpf](https://github.com/HardcodetNet/H.NotifyIcon))
that shows a red, click-through, always-on-top banner before each lesson in a JSON timetable:
once 7 minutes ahead for 5 seconds, then again 60 seconds ahead with a countdown held until the
lesson starts. Structure and release tooling are modelled on `../sample-csharp-tray-app`.

See [README.md](README.md) for the timetable JSON format.

## Build & Test Commands

```bash
# Build the app (NOT the .sln - see the note below)
dotnet build TimetableAlert.csproj -c Release

# Run tests (xUnit)
dotnet test TimetableAlert.Tests/TimetableAlert.Tests.csproj

# Publish
dotnet publish TimetableAlert.csproj -c Release -o bin/Release/net10.0-windows/publish

# Create a release (increments patch version, tags, and pushes)
./scripts/make-release
```

These need the .NET 10 SDK on **Windows** — the WPF project targets `net10.0-windows`.

**Do not `dotnet build TimetableAlert.sln`.** The solution includes the WiX v3 `.wixproj`, which
the `dotnet` CLI cannot load — it fails with "The WiX Toolset v3.11 (or newer) build tools must
be installed". Build `TimetableAlert.csproj` (which pulls in Core via `ProjectReference`) and let
full MSBuild build the installer. The solution is for Visual Studio, which handles both.

## Architecture

- **TimetableAlert.Core** (`net10.0`, no UI) — the testable half. `TimetableLoader` parses and
  validates the JSON into a `Timetable`; `AlertSchedule` turns that into alerts. Every
  `AlertSchedule` method takes "now" as a parameter rather than reading the clock, which is what
  makes the firing rules testable.
  - `Feed/` — downloading a week from the school's Canvas calendar. `IcsParser` and
    `SubjectNaming` are pure and unit tested; `TimetableFeed.Build` is separate from
    `TimetableFeed.FetchAsync` for the same reason, and takes an explicit `TimeZoneInfo` so tests
    do not move with the clock of whatever runs them. `CanvasClient` is the only thing that logs
    in, and only teacher names depend on it.
  - `Diagnostics/Log` is the whole logging story: a locked, rolling text file under
    `%APPDATA%\TimetableAlert\logs`, written to by both halves of the app. It lives in Core so
    the feed can log too. `Log.Redact` cuts a URL back to its host because the feed address is a
    bearer token in full — the token is in the *path*, so stripping the query would not be enough.
  - `TimetableFreshness.IsStale` holds the re-download rule; `TimetableWriter` renders a timetable
    back out in the format `TimetableLoader` reads, which is what makes the cache file work.
- **TimetableAlert** (`net10.0-windows`, WPF `WinExe`) — `App.xaml` sets
  `ShutdownMode="OnExplicitShutdown"`; `MainWindow.xaml` is a zero-size invisible window hosting
  the `TaskbarIcon` and owning an `AlertService`.
  - `Services/AlertService` — a one-second `DispatcherTimer` that asks `AlertSchedule` what is
    due, drives the banner, and updates the countdown and tray tooltip. `PreviewNextAlert`
    shows the next lesson's banner on demand however far off it is; `MainWindow` calls it after
    a successful Load or Reload (once the confirmation dialog is dismissed, so the banner is not
    hidden behind it), but deliberately not on the startup auto-load.
  - `Services/AppSettings` — remembers the timetable path and the feed's naming rules in
    `%APPDATA%\TimetableAlert\settings.json`.
  - `Services/TimetableSource` — cache-or-download, and the only caller of `CanvasClient`.
    `Services/TimetableCache` holds the last downloaded week next to the settings.
  - `Services/CredentialStore` — `advapi32` P/Invoke for Windows Credential Manager. The calendar
    feed URL lives there as well as the parent login: the URL carries its own token, so it is a
    password in all but name.
  - `Overlay/` — `OverlayManager` keeps one `OverlayWindow` per monitor and shows them together.
  - `App.xaml.cs` points the log at `%APPDATA%\TimetableAlert\logs` before anything else runs,
    and hooks the unhandled-exception and power-mode events into it; **View logs** on the tray
    menu opens that file in Notepad.
- **TimetableAlert.Tests** — xUnit over Core.
- **TimetableAlert.Installer** — WiX v3 MSI: a **per-user** install (`InstallScope="perUser"`)
  into `%LOCALAPPDATA%\Programs\TimetableAlert` so it never triggers UAC, plus Start
  Menu/Desktop shortcuts, auto-start via `HKCU\...\Run`, and the sample timetable alongside
  the exe. Installing into the profile means ICE38/ICE64/ICE91 fire on every component; they
  are suppressed in the `.wixproj`, which is where to look if validation starts complaining.

## Things worth knowing before changing this code

- **`Directory.Build.props` is strict**: `TreatWarningsAsErrors`, `AnalysisLevel=latest-All`,
  nullable enabled. Consequently P/Invoke uses source-generated `[LibraryImport]` in
  `Overlay/NativeMethods.cs`, and JSON goes through source-generated `JsonSerializerContext`s
  rather than reflection. The test project deliberately relaxes to `latest-Recommended`.
  Two rules bite specifically here, and both are build **errors**:
  - **CA5392** — every P/Invoke needs `[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]`.
    Add it alongside any new `[LibraryImport]`.
  - **WFO0003** — because `UseWindowsForms` is on, the WinForms analyzer rejects high-DPI
    settings in `app.manifest`. Do not put a `<dpiAware>`/`<dpiAwareness>` block there; WPF on
    .NET is PerMonitorV2 by default, so it is not needed anyway.
- **Lessons are either recurring or dated, and both must keep working.** A `Lesson` with a `Date`
  happens once; one without recurs weekly on its `Day`. `AlertSchedule` keeps two maps and merges
  them per date. Downloaded weeks are entirely dated, hand-written files entirely recurring — which
  is why `LessonsOn` has both a `DayOfWeek` and a `DateOnly` overload, and why anything asking
  about a real day must use the latter or it will find nothing in a downloaded week.
- **Downloading needs no password.** The `.ics` feed URL is itself the bearer token. Credentials
  are only ever used to look up teacher names, which the feed does not carry. The feed also beats
  the REST API on content: the API returns duplicate parent/section copies of some events and
  misses personal entries.
- **The alert firing rule is a window, not a threshold crossing** (`AlertSchedule.Evaluate`):
  fire the early warning while the lesson is 60–420 seconds away, the countdown while it is
  0–60 seconds away, each once per lesson per day. This is what makes a machine waking from
  sleep mid-run-up show the warning that is currently relevant instead of a burst of stale ones.
  Change it and the sleep/resume tests in `AlertScheduleTests` will tell you.
- **The overlay is positioned in physical pixels** via `SetWindowPos`, not through WPF's `Left`
  and `Top`, so it lands correctly on mixed-DPI multi-monitor desktops. `OverlayWindow.PositionOn`
  runs twice because moving a window between monitors of different DPI re-lays-out its content.
- **Click-through is answered by hand, not by `WS_EX_TRANSPARENT`.** That style would swallow
  the dismiss button along with everything else, so `OverlayWindow` ORs in only
  `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` (losing either makes the banner steal focus or show up in
  Alt-Tab) and hooks `WM_NCHITTEST` instead: `HTCLIENT` over the ✕, `HTTRANSPARENT` everywhere
  else, which sends the system on down the z-order. Two things follow:
  - **Any new interactive element must paint pixels with non-zero alpha.** This is a layered
    window (`AllowsTransparency="True"`), and Windows lets the mouse through zero-alpha pixels
    before the hook ever runs — a `Background="Transparent"` hit area is not clickable at all.
  - The hit test walks the visual tree rather than testing the button's rectangle, so the
    clickable area is the circle actually drawn. `WM_MOUSEACTIVATE` is answered `MA_NOACTIVATE`
    so a click delivers without taking focus.
- **Dismissal is scoped to one banner, not one lesson.** The ✕ hides all monitors and clears
  `_countingDownTo`/`_hideBannerAt`, but deliberately leaves `_fired` alone: the dismissed phase's
  key is already in there (which is what stops the next tick re-showing it), and `AlertKey`
  includes the phase, so dismissing the early warning still lets the countdown fire later.
- **Log call sites compose with `string.Create(CultureInfo.InvariantCulture, $"…")`** whenever
  anything other than a string is interpolated. A bare `$"…"` with a number, date or enum in it
  is a CA1305 build error, which is the same reason the UI strings are written that way.
- **WinForms is referenced only for `Screen.AllScreens`.** Its implicit usings are removed in the
  csproj so `Application` and `MessageBox` unambiguously mean the WPF ones; refer to
  `System.Windows.Forms.Screen` and `System.Drawing.Rectangle` by full name.

## Release Process

Pushing a `v*.*.*` tag (or a manual `workflow_dispatch`) runs `.github/workflows/release.yml`,
which builds the solution, runs the tests, builds the WiX MSI and creates a GitHub Release with
the MSI attached.
