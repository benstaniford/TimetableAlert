using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TimetableAlert.Overlay;

/// <summary>
/// One borderless, always-on-top banner, click-through everywhere except its dismiss button. One
/// of these exists per monitor; they are created once and reused for the life of the app.
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by WPF XAML framework")]
internal sealed partial class OverlayWindow : Window
{
    private HwndSource? _source;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    /// <summary>Raised when the dismiss button is clicked.</summary>
    public event EventHandler? Dismissed;

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

        // Never focusable and absent from Alt-Tab: the banner interrupts the eye without ever
        // interrupting what is being typed. Click-through is deliberately *not* WS_EX_TRANSPARENT
        // here — that would swallow the dismiss button too. It comes from OnWindowMessage below.
        var style = NativeMethods.GetWindowLong(handle, NativeMethods.GwlExstyle);
        style |= NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow;
        _ = NativeMethods.SetWindowLong(handle, NativeMethods.GwlExstyle, style);

        _source = PresentationSource.FromVisual(this) as HwndSource;
        _source?.AddHook(OnWindowMessage);
    }

    /// <summary>
    /// Makes the banner click-through everywhere except the dismiss button, by answering every
    /// hit test itself: HTCLIENT over the button, HTTRANSPARENT elsewhere so the system carries
    /// on down the z-order and the click lands on whatever is underneath.
    /// </summary>
    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WmMouseActivate:
                // Take the click, but stay in the background: whatever was being typed into keeps focus.
                handled = true;
                return NativeMethods.MaNoActivate;

            case NativeMethods.WmNcHitTest:
                handled = true;
                return IsOverDismissButton(ScreenPointOf(lParam)) ? NativeMethods.HtClient : NativeMethods.HtTransparent;

            default:
                return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Unpacks a hit test's screen coordinates. They are two *signed* 16-bit values: a monitor
    /// to the left of the primary one has negative x.
    /// </summary>
    private static Point ScreenPointOf(IntPtr lParam)
    {
        var packed = (long)lParam;
        return new Point((short)(packed & 0xFFFF), (short)((packed >> 16) & 0xFFFF));
    }

    private bool IsOverDismissButton(Point screenPoint)
    {
        if (_source is null)
        {
            // PointFromScreen throws if the visual is not connected to a presentation source.
            return false;
        }

        // Screen pixels to DIPs via the window's own composition target, so this stays right at
        // any per-monitor DPI. Hit-testing the visual tree rather than the button's rectangle
        // means the clickable area is the drawn circle, not the square it sits in.
        var hit = VisualTreeHelper.HitTest(this, PointFromScreen(screenPoint));
        for (var visual = hit?.VisualHit; visual is not null; visual = VisualTreeHelper.GetParent(visual))
        {
            if (ReferenceEquals(visual, DismissButton))
            {
                return true;
            }
        }

        return false;
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e) => Dismissed?.Invoke(this, EventArgs.Empty);

    private void OnClosed(object? sender, EventArgs e)
    {
        // These windows are closed and rebuilt whenever the display arrangement changes.
        SourceInitialized -= OnSourceInitialized;
        Closed -= OnClosed;

        _source?.RemoveHook(OnWindowMessage);
        _source = null;
    }
}
