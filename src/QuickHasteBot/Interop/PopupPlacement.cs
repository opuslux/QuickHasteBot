using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.Interop;

internal static class PopupPlacement
{
    /// <summary>
    /// Punkt des Popups, beim Öffnen festgelegt, als Anteil von Breite und Höhe (0 links/oben, 1 rechts/unten).
    /// Dort bleibt es, egal wie Liste und Breite sich ändern: Ist der Punkt oben, wächst die Höhe nach unten,
    /// ist er unten, nach oben. Höchstens bis zum Bildschirmrand, danach scrollt die Liste.
    /// Liegt der Punkt in der Mitte der Höhe, zählt er nur beim Öffnen, danach bleibt die Oberkante <paramref name="Top"/>.
    /// </summary>
    public readonly record struct Anchor(POINT Point, RECT Work, double FractionX, double FractionY, int Top);

    /// <summary>
    /// Zeigt das Fenster so, dass der Mauszeiger an der gewünschten Stelle im Popup sitzt, begrenzt auf den
    /// Arbeitsbereich des Monitors unter dem Cursor. Arbeitet in physischen Pixeln, damit gemischte Skalierungen
    /// (100 % / 150 %) stimmen.
    /// </summary>
    public static Anchor ShowAtCursor(Window window, double fractionX, double fractionY)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();

        GetCursorPos(out var cursor);
        var work = WorkArea(MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST));

        // Unsichtbar bleiben, bis das Fenster am Cursor steht und den neuen Inhalt gezeichnet hat,
        // sonst blitzt es kurz in der Ecke bzw. mit dem Inhalt vom letzten Mal auf
        SetCloaked(hwnd, true);

        // Erst auf den richtigen Monitor schieben: WPF passt die Größe an dessen DPI an
        SetWindowPos(hwnd, 0, work.Left, work.Top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        window.MaxHeight = Math.Max(MinHeight, ToDips(hwnd, work.Bottom - work.Top));
        window.Show();
        // SizeToContent gleich auflösen, damit die Höhe für die Richtung schon stimmt
        window.UpdateLayout();

        GetWindowRect(hwnd, out var rect);
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        // Ist auf der gewünschten Seite kein Platz, geht es zur anderen auf; sitzt die Maus in der Mitte,
        // wird nur in den Arbeitsbereich geschoben
        var x = Place(cursor.X, width, fractionX, work.Left, work.Right);
        var y = Place(cursor.Y, height, fractionY, work.Top, work.Bottom);
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));

        // Der Anker ist die gewünschte Stelle im Popup, auch wenn es zur anderen Seite aufging
        var anchor = AnchorAt(x, y, width, height, work, fractionX, fractionY);
        LimitHeight(window, hwnd, anchor);
        window.UpdateLayout();
        PlaceAt(window, anchor, keepTop: false);
        GetWindowRect(hwnd, out rect);

        // Nach dem Zeichnen aufdecken
        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => SetCloaked(hwnd, false));
        return anchor with { Top = rect.Top };
    }

    static void SetCloaked(nint hwnd, bool cloaked)
    {
        var value = cloaked ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref value, sizeof(int));
    }

    // Linke bzw. obere Kante, damit der Punkt an der gewünschten Stelle sitzt, sonst gespiegelt
    static int Place(int point, int size, double fraction, int min, int max)
    {
        var start = point - (int)Math.Round(size * fraction);
        return start >= min && start + size <= max ? start : point - (int)Math.Round(size * (1 - fraction));
    }

    static Anchor AnchorAt(int left, int top, int width, int height, RECT work, double fractionX, double fractionY) =>
        new(new POINT { X = left + (int)Math.Round(width * fractionX), Y = top + (int)Math.Round(height * fractionY) },
            work, fractionX, fractionY, top);

    /// <summary>
    /// Nach einer Größenänderung (Liste, Breite) bleibt der Anker, wo er ist.
    /// Nur wenn das Fenster sonst über den Bildschirmrand hinausginge, wird es hineingeschoben.
    /// Liegt der Anker in der Mitte der Höhe, gilt er nur beim Öffnen: Danach bleibt die Oberkante stehen,
    /// sonst würde das Suchfeld beim Tippen um die halbe Höhenänderung springen, z.B. sobald die Suche beginnt.
    /// Wurde es unten hineingeschoben, geht es beim Schrumpfen wieder an diese Oberkante zurück.
    /// </summary>
    public static void KeepAnchored(Window window, Anchor anchor) =>
        PlaceAt(window, anchor, keepTop: anchor.FractionY is > 0 and < 1);

    static void PlaceAt(Window window, Anchor anchor, bool keepTop)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var (point, work) = (anchor.Point, anchor.Work);

        GetWindowRect(hwnd, out var rect);
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        var x = Math.Clamp(point.X - (int)Math.Round(width * anchor.FractionX), work.Left, Math.Max(work.Left, work.Right - width));
        var top = keepTop ? anchor.Top : point.Y - (int)Math.Round(height * anchor.FractionY);
        var y = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - height));

        if (x != rect.Left || y != rect.Top)
            SetWindowPos(hwnd, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>Nach dem Verschieben oder Verbreitern von Hand: Der Anker wandert mit, an dieselbe Stelle im Popup.</summary>
    public static Anchor Moved(Window window, Anchor previous)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var work = WorkArea(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST));

        GetWindowRect(hwnd, out var rect);
        var anchor = AnchorAt(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, work,
                              previous.FractionX, previous.FractionY);
        LimitHeight(window, hwnd, anchor);
        return anchor;
    }

    static RECT WorkArea(nint monitor)
    {
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        return info.rcWork;
    }

    // Höchstens bis zum Rand des Arbeitsbereichs, zu dem das Popup wächst, erst darüber hinaus scrollt die Liste.
    // Mit dem Anker in der Mitte darf es so hoch wie der Arbeitsbereich werden. Liegt der Anker von Hand nah am Rand,
    // bleibt trotzdem Platz für Suchfeld und ein paar Zeilen, dann rutscht es hinein.
    const double MinHeight = 360;

    static void LimitHeight(Window window, nint hwnd, Anchor anchor)
    {
        var (point, work) = (anchor.Point, anchor.Work);
        var available = anchor.FractionY switch
        {
            <= 0 => work.Bottom - point.Y,
            >= 1 => point.Y - work.Top,
            _ => work.Bottom - work.Top,
        };
        window.MaxHeight = Math.Max(MinHeight, ToDips(hwnd, available));
    }

    static double ToDips(nint hwnd, int pixels) => pixels * 96.0 / GetDpiForWindow(hwnd);

    /// <summary>Abgerundete Ecken wie bei Windows-11-Menüs, auch ohne Fensterrahmen.</summary>
    public static void UseRoundedCorners(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var preference = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }
}
