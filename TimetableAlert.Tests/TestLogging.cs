using System.Runtime.CompilerServices;
using TimetableAlert.Core.Diagnostics;

namespace TimetableAlert.Tests;

/// <summary>
/// Silences the app's log for the whole test run. Core writes to <c>%APPDATA%</c> by default, and
/// a unit test has no business leaving files in the profile of whoever ran it.
/// </summary>
internal static class TestLogging
{
    [ModuleInitializer]
    internal static void Silence() => Log.Level = LogLevel.Off;
}
