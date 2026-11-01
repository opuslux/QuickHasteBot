using System.Windows;
using System.Windows.Interop;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.Interop;

internal static class PopupPlacement
{
    /// <summary>
    /// Zeigt das Fenster am Mauszeiger, begrenzt auf den Arbeitsbereich des Monitors unter dem Cursor.
    /// Arbeitet in physischen Pixeln, damit gemischte Skalierungen (100 % / 150 %) stimmen.
    /// </summary>
    public static void ShowAtCursor(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();

        GetCursorPos(out var cursor);
        var monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        var work = info.rcWork;

        // Erst auf den richtigen Monitor schieben: WPF passt die Größe an dessen DPI an
        SetWindowPos(hwnd, 0, work.Left, work.Top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        window.Show();

        GetWindowRect(hwnd, out var rect);
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        var x = Math.Clamp(cursor.X, work.Left, Math.Max(work.Left, work.Right - width));
        // Passt das Fenster unter dem Cursor nicht mehr hin, öffnet es nach oben
        var y = cursor.Y + height <= work.Bottom ? cursor.Y : cursor.Y - height;
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));

        SetWindowPos(hwnd, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
    }

    /// <summary>Abgerundete Ecken wie bei Windows-11-Menüs, auch ohne Fensterrahmen.</summary>
    public static void UseRoundedCorners(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var preference = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }
}
