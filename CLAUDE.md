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
    due, drives the banner, and updates the countdown and tray tooltip.
  - `Services/AppSettings` — remembers the timetable path in `%APPDATA%\TimetableAlert\settings.json`.
  - `Overlay/` — `OverlayManager` keeps one `OverlayWindow` per monitor and shows them together.
- **TimetableAlert.Tests** — xUnit over Core.
- **TimetableAlert.Installer** — WiX v3 MSI: Program Files, Start Menu/Desktop shortcuts,
  auto-start via `HKCU\...\Run`, and the sample timetable alongside the exe.

## Things worth knowing before changing this code

- **`Directory.Build.props` is strict**: `TreatWarningsAsErrors`, `AnalysisLevel=latest-All`,
  nullable enabled. Consequently P/Invoke uses source-generated `[LibraryImport]` in
  `Overlay/NativeMethods.cs`, and JSON goes through source-generated `JsonSerializerContext`s
  rather than reflection. The test project deliberately relaxes to `latest-Recommended`.
- **The alert firing rule is a window, not a threshold crossing** (`AlertSchedule.Evaluate`):
  fire the early warning while the lesson is 60–420 seconds away, the countdown while it is
  0–60 seconds away, each once per lesson per day. This is what makes a machine waking from
  sleep mid-run-up show the warning that is currently relevant instead of a burst of stale ones.
  Change it and the sleep/resume tests in `AlertScheduleTests` will tell you.
- **The overlay is positioned in physical pixels** via `SetWindowPos`, not through WPF's `Left`
  and `Top`, so it lands correctly on mixed-DPI multi-monitor desktops. `OverlayWindow.PositionOn`
  runs twice because moving a window between monitors of different DPI re-lays-out its content.
- **Click-through** comes from OR-ing `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`
  into the window's extended style once its HWND exists. Losing any of those makes the banner
  steal focus or block clicks.
- **WinForms is referenced only for `Screen.AllScreens`.** Its implicit usings are removed in the
  csproj so `Application` and `MessageBox` unambiguously mean the WPF ones; refer to
  `System.Windows.Forms.Screen` and `System.Drawing.Rectangle` by full name.

## Release Process

Pushing a `v*.*.*` tag (or a manual `workflow_dispatch`) runs `.github/workflows/release.yml`,
which builds the solution, runs the tests, builds the WiX MSI and creates a GitHub Release with
the MSI attached.
