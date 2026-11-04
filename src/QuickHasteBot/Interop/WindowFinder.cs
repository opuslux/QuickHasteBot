using System.Diagnostics;
using QuickHasteBot.Core;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.Interop;

/// <summary>Sucht Programmfenster für {window,…} und window=, das zuletzt benutzte zuerst (Z-Reihenfolge).</summary>
internal static class WindowFinder
{
    public static nint Find(WindowSelector selector)
    {
        var names = new Dictionary<uint, string>();
        for (var hwnd = GetTopWindow(0); hwnd != 0; hwnd = GetWindow(hwnd, GW_HWNDNEXT))
        {
            if (!IsMainWindow(hwnd)) continue;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == Environment.ProcessId) continue;
            if (!names.TryGetValue(pid, out var name))
                names[pid] = name = ProcessName(pid);
            if (selector.Matches(name, Title(hwnd)))
                return hwnd;
        }
        return 0;
    }

    /// <summary>Sichtbar, mit Titel, kein Dialog eines anderen Fensters und nicht von Windows versteckt (Store-Apps im Hintergrund).</summary>
    static bool IsMainWindow(nint hwnd)
    {
        if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != 0) return false;
        if ((GetWindowLongPtr(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return false;
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        return Title(hwnd).Length > 0;
    }

    public static unsafe string Title(nint hwnd)
    {
        var buffer = stackalloc char[512];
        var length = GetWindowText(hwnd, buffer, 512);
        return new string(buffer, 0, Math.Max(length, 0));
    }

    static string ProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return "";
        }
    }

    /// <summary>Wiederherstellen, falls minimiert, und nach vorne holen.</summary>
    public static async Task<bool> ActivateAsync(nint hwnd)
    {
        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, SW_RESTORE);
            await Task.Delay(150);
        }
        return await WindowFocus.ActivateAndWaitAsync(hwnd, timeoutMilliseconds: 1500);
    }
}
