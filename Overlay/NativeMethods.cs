using System.Runtime.InteropServices;

namespace TimetableAlert.Overlay;

/// <summary>
/// The handful of user32 calls and message constants the overlay needs: answering hit tests so
/// the banner stays click-through everywhere except its dismiss button, and placing it on a
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

    internal const int WmNcHitTest = 0x0084;
    internal const int WmMouseActivate = 0x0021;

    /// <summary>Hit-test reply meaning "not mine": the system keeps looking down the z-order.</summary>
    internal const int HtTransparent = -1;

    /// <summary>Hit-test reply meaning the point is over this window's client area.</summary>
    internal const int HtClient = 1;

    /// <summary>Reply to WM_MOUSEACTIVATE: deliver the click, but do not activate the window.</summary>
    internal const int MaNoActivate = 3;

    internal static readonly IntPtr HwndTopmost = new(-1);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    internal static partial int GetWindowLong(IntPtr hWnd, int nIndex);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    internal static partial int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
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
