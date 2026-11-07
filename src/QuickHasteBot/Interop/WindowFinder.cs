using System.ComponentModel;
using System.Diagnostics;
using QuickHasteBot.Core;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.Interop;

/// <summary>Sucht Programmfenster für {window,…}, {start,…} und window=, das zuletzt benutzte zuerst (Z-Reihenfolge).</summary>
internal static class WindowFinder
{
    public static nint Find(WindowSelector selector)
    {
        var names = new Dictionary<uint, string>();
        for (var hwnd = GetTopWindow(0); hwnd != 0; hwnd = GetWindow(hwnd, GW_HWNDNEXT))
        {
            if (!IsMainWindow(hwnd)) continue;
            var pid = AppProcessId(hwnd);
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

    /// <summary>
    /// Der Prozess hinter einem Hauptfenster. Store-Apps wie der Rechner laufen in einem Rahmen, der ApplicationFrameHost
    /// gehört: dann der Prozess der App im Rahmen (CalculatorApp), so wie ihn UI Automation, Picker und Recorder melden.
    /// </summary>
    public static uint AppProcessId(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        if (ClassName(hwnd) != "ApplicationFrameWindow") return pid;
        for (var child = GetWindow(hwnd, GW_CHILD); child != 0; child = GetWindow(child, GW_HWNDNEXT))
        {
            if (ClassName(child) != "Windows.UI.Core.CoreWindow") continue;
            GetWindowThreadProcessId(child, out var app);
            if (app != 0) return app;
        }
        return pid;
    }

    static unsafe string ClassName(nint hwnd)
    {
        var buffer = stackalloc char[256];
        var length = GetClassName(hwnd, buffer, 256);
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

    /// <summary>So lange wartet {start,…} auf das Fenster eines gerade gestarteten Programms.</summary>
    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Startet ein Programm für {start,…} auf dem Weg, den QuickHasteBot auch für Dateien nimmt (ShellExecute wie ein
    /// Doppelklick im Explorer): ohne cmd oder PowerShell dazwischen, sichtbar und mit den Rechten des Benutzers.
    /// </summary>
    /// <param name="process">Kann null sein, wenn Windows das Programm über eine laufende Instanz öffnet.</param>
    public static bool TryStart(StartStep start, out Process? process, out string error)
    {
        Log.Info($"Starte Programm für {{start}}: {start.Program}" + (start.Arguments is null ? "" : $" mit \"{start.Arguments}\""));
        try
        {
            process = Process.Start(new ProcessStartInfo(start.Program, start.Arguments ?? "") { UseShellExecute = true });
            error = "";
            return true;
        }
        catch (Win32Exception ex)
        {
            Log.Error($"Programm konnte nicht gestartet werden: {start.Program}", ex);
            process = null;
            // Nur der Windows-Text ("Das System kann die angegebene Datei nicht finden."), ohne .NETs Arbeitsverzeichnis
            error = new Win32Exception(ex.NativeErrorCode).Message;
            return false;
        }
    }

    /// <summary>
    /// Wartet, bis das gestartete Programm Eingaben annimmt (höchstens <paramref name="timeoutMilliseconds"/>).
    /// False, wenn <paramref name="cancelled"/> vorher zuschlägt. Programme ohne Nachrichtenschleife oder solche, die
    /// sich beenden und ein anderes weiterlaufen lassen, zählen als bereit.
    /// </summary>
    public static async Task<bool> WaitForInputIdleAsync(Process process, Func<bool> cancelled, int timeoutMilliseconds = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            try
            {
                if (process.WaitForInputIdle(0)) return true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
            if (cancelled()) return false;
            await Task.Delay(50);
        }
        return true;
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
