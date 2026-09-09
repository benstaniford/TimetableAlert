using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Interop;

namespace TimetableAlert.Overlay;

/// <summary>
/// One borderless, always-on-top, click-through banner. One of these exists per monitor; they
/// are created once and reused for the life of the app.
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by WPF XAML framework")]
internal sealed partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>Sets the three lines of text shown in the banner.</summary>
    public void SetText(string subject, string timing, string detail)
    {
        SubjectText.Text = subject;
        TimingText.Text = timing;
        DetailText.Text = detail;
    }

    /// <summary>
    /// Centres the banner near the top of <paramref name="workArea"/>, which is in physical
    /// pixels. Runs twice because moving to a monitor with a different DPI makes WPF re-lay-out
    /// the content, changing the window's physical width; the second pass re-centres it.
    /// </summary>
    public void PositionOn(System.Drawing.Rectangle workArea)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        for (var pass = 0; pass < 2; pass++)
        {
            UpdateLayout();

            if (!NativeMethods.GetWindowRect(handle, out var bounds))
            {
                return;
            }

            var x = workArea.Left + ((workArea.Width - bounds.Width) / 2);
            var y = workArea.Top + (workArea.Height / 12);

            _ = NativeMethods.SetWindowPos(
                handle,
                NativeMethods.HwndTopmost,
                x,
                y,
                0,
                0,
                NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // Click-through, never focusable, and absent from Alt-Tab: the banner interrupts him
        // visually without ever interrupting what he is typing into.
        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GwlExstyle);
        style |= NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow;
        _ = NativeMethods.SetWindowLong(handle, NativeMethods.GwlExstyle, style);
    }
}
