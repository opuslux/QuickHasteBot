using System.Windows.Interop;
using QuickHasteBot.Core;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.Interop;

/// <summary>
/// Globaler Hotkey über RegisterHotKey auf einem unsichtbaren Message-only-Fenster.
/// </summary>
internal sealed class GlobalHotkey : IDisposable
{
    const int HotkeyId = 0x5148; // "QH"
    static readonly nint HWND_MESSAGE = -3;

    readonly HwndSource _source;
    bool _registered;

    public GlobalHotkey()
    {
        _source = new HwndSource(new HwndSourceParameters("QuickHasteBot.Hotkey") { ParentWindow = HWND_MESSAGE, WindowStyle = 0 });
        _source.AddHook(WndProc);
    }

    public event EventHandler? Pressed;

    public HotkeyGesture? Current { get; private set; }

    /// <summary>False, wenn die Kombination schon von einer anderen App belegt ist.</summary>
    public bool Register(HotkeyGesture gesture)
    {
        Unregister();
        if (!gesture.IsValid) return false;

        _registered = RegisterHotKey(_source.Handle, HotkeyId, (uint)gesture.Modifiers | MOD_NOREPEAT, (uint)gesture.VirtualKey);
        if (_registered)
            Current = gesture;
        else
            Log.Error($"Hotkey {gesture} konnte nicht registriert werden (Win32-Fehler {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()})");
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
            UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
        Current = null;
    }

    nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return 0;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
