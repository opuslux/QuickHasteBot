using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.Interop;

internal static class WindowFocus
{
    /// <summary>
    /// Holt ein Fenster in den Vordergrund. Wenn Windows SetForegroundWindow verweigert,
    /// hängen wir uns kurz an den Input-Thread des aktuellen Vordergrundfensters.
    /// </summary>
    public static bool Activate(nint hwnd)
    {
        if (hwnd == 0 || !IsWindow(hwnd)) return false;
        if (GetForegroundWindow() == hwnd) return true;
        if (SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd) return true;

        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var currentThread = GetCurrentThreadId();
        if (foregroundThread == currentThread) return SetForegroundWindow(hwnd);

        AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            AttachThreadInput(currentThread, foregroundThread, false);
        }
        return GetForegroundWindow() == hwnd;
    }

    /// <summary>Wartet, bis das Zielfenster wirklich vorne ist, damit die Eingabe nicht ins Leere geht.</summary>
    public static async Task<bool> ActivateAndWaitAsync(nint hwnd, int timeoutMilliseconds = 500)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (true)
        {
            if (Activate(hwnd)) return true;
            if (Environment.TickCount64 > deadline) return false;
            await Task.Delay(20);
        }
    }
}
