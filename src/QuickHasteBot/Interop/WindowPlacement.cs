using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using QuickHasteBot.Core;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.Interop;

/// <summary>
/// Grösse und Stelle eines Fensters merken und wiederherstellen, über die Fensterplatzierung von Windows: in Pixeln, die
/// auf jedem Bildschirm gleich gelten, auch bei unterschiedlicher Skalierung. Läge das Fenster auf keinem Bildschirm mehr
/// (Monitor abgesteckt), schiebt Windows es selbst zurück.
/// </summary>
internal static class WindowPlacement
{
    const int SW_HIDE = 0;
    const int SW_SHOWMINIMIZED = 2;
    const int SW_SHOWMAXIMIZED = 3;
    const uint WPF_RESTORETOMAXIMIZED = 0x0002;

    /// <summary>Wie das Fenster gerade steht, null ohne Fensterhandle (schon zu).</summary>
    public static WindowBounds? Read(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var placement = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (hwnd == 0 || !GetWindowPlacement(hwnd, ref placement)) return null;
        var normal = placement.rcNormalPosition;
        // Minimiert geschlossen: so, wie es vorher war
        var maximized = placement.showCmd == SW_SHOWMAXIMIZED
            || placement.showCmd == SW_SHOWMINIMIZED && (placement.flags & WPF_RESTORETOMAXIMIZED) != 0;
        return new WindowBounds(normal.Left, normal.Top, normal.Right - normal.Left, normal.Bottom - normal.Top, maximized);
    }

    /// <summary>
    /// Stellt ein Fenster, das noch nicht sichtbar ist, an die gemerkte Stelle (in SourceInitialized aufrufen, mit
    /// WindowStartupLocation.Manual). <paramref name="offset"/> verschiebt es nach rechts unten, damit ein zweites
    /// Fenster nicht genau auf dem ersten liegt.
    /// </summary>
    public static void Apply(Window window, WindowBounds bounds, int offset = 0)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0 || !bounds.IsUsable) return;
        var placement = new WINDOWPLACEMENT
        {
            length = Marshal.SizeOf<WINDOWPLACEMENT>(),
            // Noch nicht zeigen, das macht Show() gleich selbst
            showCmd = SW_HIDE,
            rcNormalPosition = new RECT
            {
                Left = bounds.Left + offset,
                Top = bounds.Top + offset,
                Right = bounds.Left + offset + bounds.Width,
                Bottom = bounds.Top + offset + bounds.Height,
            },
        };
        SetWindowPlacement(hwnd, placement);
        if (bounds.Maximized)
            window.WindowState = WindowState.Maximized;
    }
}
