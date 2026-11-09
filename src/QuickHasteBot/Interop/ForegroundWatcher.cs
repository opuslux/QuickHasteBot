using System.Runtime.InteropServices;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.Interop;

/// <summary>
/// Meldet, wenn ein Fenster eines anderen Programms nach vorne kommt. Fenster von QuickHasteBot selbst (Popup, Editor,
/// Dialoge) meldet Windows gar nicht erst. Muss auf einem Thread mit Nachrichtenschleife laufen, die Meldung kommt dort.
/// </summary>
internal sealed class ForegroundWatcher : IDisposable
{
    // Der Delegat muss leben, solange Windows ihn aufrufen kann
    readonly WinEventProc _callback;
    readonly Action<nint> _changed;
    nint _hook;

    public ForegroundWatcher(Action<nint> changed)
    {
        _changed = changed;
        _callback = OnWinEvent;
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0,
            Marshal.GetFunctionPointerForDelegate(_callback), 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (_hook == 0)
            Core.Log.Info("Fensterwechsel lassen sich nicht verfolgen (SetWinEventHook)");
    }

    void OnWinEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd != 0)
            _changed(hwnd);
    }

    public void Dispose()
    {
        if (_hook == 0) return;
        UnhookWinEvent(_hook);
        _hook = 0;
    }
}
