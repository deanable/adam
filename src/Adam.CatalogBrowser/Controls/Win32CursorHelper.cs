using System.Runtime.InteropServices;
using Avalonia;

namespace Adam.CatalogBrowser.Controls;

/// <summary>
/// Provides the current cursor position in screen coordinates via P/Invoke.
/// Used during OLE drag-drop operations where standard Avalonia pointer
/// events are suppressed (Windows).
/// </summary>
internal static class Win32CursorHelper
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// Returns the current cursor position in screen (physical) pixels.
    /// Returns (0,0) if the cursor position cannot be determined.
    /// </summary>
    public static PixelPoint GetScreenCursorPosition()
    {
        if (GetCursorPos(out POINT point))
        {
            return new PixelPoint(point.X, point.Y);
        }

        return new PixelPoint(0, 0);
    }
}
