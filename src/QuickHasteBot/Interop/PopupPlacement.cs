using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.Interop;

internal static class PopupPlacement
{
    /// <summary>
    /// Obere rechte Ecke des Popups, beim Öffnen festgelegt. Dort bleibt es, egal wie Liste und Breite sich ändern:
    /// Die Höhe wächst höchstens bis zum unteren Bildschirmrand, danach scrollt die Liste.
    /// </summary>
    public readonly record struct Anchor(POINT Corner, RECT Work);

    /// <summary>
    /// Zeigt das Fenster links unterhalb des Mauszeigers, begrenzt auf den Arbeitsbereich des Monitors unter dem Cursor.
    /// Arbeitet in physischen Pixeln, damit gemischte Skalierungen (100 % / 150 %) stimmen.
    /// </summary>
    public static Anchor ShowAtCursor(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();

        GetCursorPos(out var cursor);
        var work = WorkArea(MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST));

        // Unsichtbar bleiben, bis das Fenster am Cursor steht und den neuen Inhalt gezeichnet hat,
        // sonst blitzt es kurz in der Ecke bzw. mit dem Inhalt vom letzten Mal auf
        SetCloaked(hwnd, true);

        // Erst auf den richtigen Monitor schieben: WPF passt die Größe an dessen DPI an
        SetWindowPos(hwnd, 0, work.Left, work.Top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        LimitHeight(window, hwnd, work, work.Top);
        window.Show();
        // SizeToContent gleich auflösen, damit die Höhe für die Richtung schon stimmt
        window.UpdateLayout();

        GetWindowRect(hwnd, out var rect);
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        // Bevorzugt links unterhalb des Cursors. Ist links kein Platz, geht es nach rechts auf,
        // passt es unten nicht mehr hin, nach oben.
        var x = cursor.X - width >= work.Left ? cursor.X - width : cursor.X;
        var y = cursor.Y + height <= work.Bottom ? cursor.Y : cursor.Y - height;
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));

        var anchor = new Anchor(new POINT { X = x + width, Y = y }, work);
        LimitHeight(window, hwnd, work, y);
        window.UpdateLayout();
        KeepAnchored(window, anchor);

        // Nach dem Zeichnen aufdecken
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => SetCloaked(hwnd, false));
        return anchor;
    }

    static void SetCloaked(nint hwnd, bool cloaked)
    {
        var value = cloaked ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref value, sizeof(int));
    }

    /// <summary>
    /// Nach einer Größenänderung (Liste, Breite) bleibt die obere rechte Ecke, wo sie ist.
    /// Nur wenn das Fenster sonst über den Bildschirmrand hinausginge, wird es hineingeschoben.
    /// </summary>
    public static void KeepAnchored(Window window, Anchor anchor)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var (corner, work) = (anchor.Corner, anchor.Work);

        GetWindowRect(hwnd, out var rect);
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        var x = Math.Clamp(corner.X - width, work.Left, Math.Max(work.Left, work.Right - width));
        var y = Math.Clamp(corner.Y, work.Top, Math.Max(work.Top, work.Bottom - height));

        if (x != rect.Left || y != rect.Top)
            SetWindowPos(hwnd, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>Nach dem Verschieben oder Verbreitern von Hand: Der Anker wandert an die neue obere rechte Ecke.</summary>
    public static Anchor Moved(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var work = WorkArea(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST));

        GetWindowRect(hwnd, out var rect);
        LimitHeight(window, hwnd, work, rect.Top);
        return new Anchor(new POINT { X = rect.Right, Y = rect.Top }, work);
    }

    static RECT WorkArea(nint monitor)
    {
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        return info.rcWork;
    }

    // Höchstens bis zum unteren Rand des Arbeitsbereichs, erst darüber hinaus scrollt die Liste. Wurde das Popup
    // von Hand weit nach unten geschoben, bleibt trotzdem Platz für Suchfeld und ein paar Zeilen, dann rutscht es hoch.
    const double MinHeight = 360;

    static void LimitHeight(Window window, nint hwnd, RECT work, int top) =>
        window.MaxHeight = Math.Max(MinHeight, (work.Bottom - top) * 96.0 / GetDpiForWindow(hwnd));

    /// <summary>Abgerundete Ecken wie bei Windows-11-Menüs, auch ohne Fensterrahmen.</summary>
    public static void UseRoundedCorners(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var preference = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }
}
