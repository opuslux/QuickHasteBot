using System.Windows;
using System.Windows.Interop;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.Interop;

internal static class PopupPlacement
{
    /// <summary>Wo das Popup aufgegangen ist, damit es beim Wachsen und Schrumpfen dort bleibt.</summary>
    public readonly record struct Anchor(POINT Cursor, RECT Work, bool OpensUp);

    /// <summary>
    /// Zeigt das Fenster links unterhalb des Mauszeigers, begrenzt auf den Arbeitsbereich des Monitors unter dem Cursor.
    /// Arbeitet in physischen Pixeln, damit gemischte Skalierungen (100 % / 150 %) stimmen.
    /// </summary>
    public static Anchor ShowAtCursor(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();

        GetCursorPos(out var cursor);
        var work = WorkArea(MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST));

        // Erst auf den richtigen Monitor schieben: WPF passt die Größe an dessen DPI an
        SetWindowPos(hwnd, 0, work.Left, work.Top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        LimitHeight(window, hwnd, work);
        window.Show();

        GetWindowRect(hwnd, out var rect);
        // Passt das Fenster unter dem Cursor nicht mehr hin, öffnet es nach oben
        var anchor = new Anchor(cursor, work, OpensUp: cursor.Y + (rect.Bottom - rect.Top) > work.Bottom);
        KeepAnchored(window, anchor);
        return anchor;
    }

    /// <summary>
    /// Richtet das Fenster nach einer Größenänderung neu am Anker aus: bevorzugt links unterhalb des Cursors
    /// (Cursor in der oberen rechten Ecke), sonst nach rechts bzw. nach oben.
    /// </summary>
    public static void KeepAnchored(Window window, Anchor anchor)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var (cursor, work) = (anchor.Cursor, anchor.Work);

        GetWindowRect(hwnd, out var rect);
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        var x = cursor.X - width >= work.Left ? cursor.X - width : cursor.X;
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        var y = anchor.OpensUp ? cursor.Y - height : cursor.Y;
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));

        if (x != rect.Left || y != rect.Top)
            SetWindowPos(hwnd, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>
    /// Nach dem Verschieben von Hand: Der Anker wandert an die neue obere (bzw. beim Aufklappen nach oben untere)
    /// rechte Ecke, die Wachsrichtung bleibt.
    /// </summary>
    public static Anchor Moved(Window window, Anchor previous)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var work = WorkArea(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST));
        LimitHeight(window, hwnd, work);

        GetWindowRect(hwnd, out var rect);
        var corner = new POINT { X = rect.Right, Y = previous.OpensUp ? rect.Bottom : rect.Top };
        return previous with { Cursor = corner, Work = work };
    }

    static RECT WorkArea(nint monitor)
    {
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        return info.rcWork;
    }

    // Höchstens so hoch wie der Bildschirm, erst darüber hinaus scrollt die Liste
    static void LimitHeight(Window window, nint hwnd, RECT work) =>
        window.MaxHeight = (work.Bottom - work.Top) * 96.0 / GetDpiForWindow(hwnd);

    /// <summary>Abgerundete Ecken wie bei Windows-11-Menüs, auch ohne Fensterrahmen.</summary>
    public static void UseRoundedCorners(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var preference = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }
}
