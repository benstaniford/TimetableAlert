using System.Runtime.InteropServices;

namespace TimetableAlert.Overlay;

/// <summary>
/// The handful of user32 calls the overlay needs: making it click-through, and placing it on a
/// specific monitor in physical pixels (which sidesteps WPF's device-independent coordinate
/// space and so stays correct on mixed-DPI desktops).
/// </summary>
internal static partial class NativeMethods
{
    internal const int GwlExstyle = -20;

    internal const int WsExTransparent = 0x00000020;
    internal const int WsExNoActivate = 0x08000000;
    internal const int WsExToolWindow = 0x00000080;

    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoActivate = 0x0010;

    internal static readonly IntPtr HwndTopmost = new(-1);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    internal static partial int GetWindowLong(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    internal static partial int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    /// <summary>Win32 RECT: a rectangle given by its edges, in physical pixels.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal readonly int Width => Right - Left;

        internal readonly int Height => Bottom - Top;
    }
}
