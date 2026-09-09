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
- **TimetableAlert** (`net10.0-windows`, WPF `WinExe`) — `App.xaml` sets
  `ShutdownMode="OnExplicitShutdown"`; `MainWindow.xaml` is a zero-size invisible window hosting
  the `TaskbarIcon` and owning an `AlertService`.
  - `Services/AlertService` — a one-second `DispatcherTimer` that asks `AlertSchedule` what is
    due, drives the banner, and updates the countdown and tray tooltip. `PreviewNextAlert`
    shows the next lesson's banner on demand however far off it is; `MainWindow` calls it after
    a successful Load or Reload (once the confirmation dialog is dismissed, so the banner is not
    hidden behind it), but deliberately not on the startup auto-load.
  - `Services/AppSettings` — remembers the timetable path in `%APPDATA%\TimetableAlert\settings.json`.
  - `Overlay/` — `OverlayManager` keeps one `OverlayWindow` per monitor and shows them together.
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
- **WinForms is referenced only for `Screen.AllScreens`.** Its implicit usings are removed in the
  csproj so `Application` and `MessageBox` unambiguously mean the WPF ones; refer to
  `System.Windows.Forms.Screen` and `System.Drawing.Rectangle` by full name.

## Release Process

Pushing a `v*.*.*` tag (or a manual `workflow_dispatch`) runs `.github/workflows/release.yml`,
which builds the solution, runs the tests, builds the WiX MSI and creates a GitHub Release with
the MSI attached.
