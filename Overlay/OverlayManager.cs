using System.Diagnostics.CodeAnalysis;
using System.Windows;
using Microsoft.Win32;

namespace TimetableAlert.Overlay;

/// <summary>
/// Shows the same banner on every monitor at once, so the warning is seen whichever screen he
/// happens to be looking at. Windows are created once and reused, and rebuilt if the display
/// arrangement changes.
/// </summary>
[SuppressMessage("Design", "CA1063:Implement IDisposable Correctly", Justification = "Sealed, no finalizer, only managed state and one static event subscription to release")]
internal sealed class OverlayManager : IDisposable
{
    private readonly List<OverlayWindow> _windows = [];
    private bool _visible;
    private bool _windowsStale = true;
    private bool _disposed;

    public OverlayManager() => SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

    /// <summary>True while a banner is on screen.</summary>
    public bool IsVisible => _visible;

    /// <summary>Shows the banner on every monitor, replacing whatever it was saying.</summary>
    public void Show(string subject, string timing, string detail)
    {
        EnsureWindows();

        foreach (var window in _windows)
        {
            window.SetText(subject, timing, detail);
        }

        if (!_visible)
        {
            foreach (var window in _windows)
            {
                window.Show();
            }

            _visible = true;
        }

        var screens = System.Windows.Forms.Screen.AllScreens;
        for (var i = 0; i < _windows.Count && i < screens.Length; i++)
        {
            _windows[i].PositionOn(screens[i].WorkingArea);
        }
    }

    /// <summary>
    /// Updates the text of an already-visible banner. The window width is fixed, so the ticking
    /// countdown does not make the banner jump about.
    /// </summary>
    public void UpdateText(string subject, string timing, string detail)
    {
        if (!_visible)
        {
            return;
        }

        foreach (var window in _windows)
        {
            window.SetText(subject, timing, detail);
        }
    }

    /// <summary>Takes the banner off every screen.</summary>
    public void Hide()
    {
        if (!_visible)
        {
            return;
        }

        foreach (var window in _windows)
        {
            window.Hide();
        }

        _visible = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        CloseWindows();
        GC.SuppressFinalize(this);
    }

    private void EnsureWindows()
    {
        var screenCount = System.Windows.Forms.Screen.AllScreens.Length;
        if (!_windowsStale && _windows.Count == screenCount)
        {
            return;
        }

        CloseWindows();
        _visible = false;

        for (var i = 0; i < screenCount; i++)
        {
            _windows.Add(new OverlayWindow());
        }

        _windowsStale = false;
    }

    private void CloseWindows()
    {
        foreach (var window in _windows)
        {
            window.Close();
        }

        _windows.Clear();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // Rebuild on the UI thread the next time a banner is shown; a monitor may have come or
        // gone, and window-to-monitor assignments are no longer trustworthy.
        _ = Application.Current?.Dispatcher.BeginInvoke(() => { _windowsStale = true; });
    }
}
