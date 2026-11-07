using System.Windows.Interop;
using QuickHasteBot.Core;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.Interop;

/// <summary>
/// Globale Hotkeys über RegisterHotKey auf einem unsichtbaren Message-only-Fenster: der Hotkey fürs Popup und
/// beliebig viele weitere für Dateien und Ordner, jeder mit eigener Id.
/// </summary>
internal sealed class GlobalHotkey : IDisposable
{
    const int HotkeyId = 0x5148; // "QH"

    /// <summary>Nur kurz registriert, um zu prüfen, ob ein anderes Programm die Kombination hält.</summary>
    const int ProbeId = 0x5149;

    /// <summary>Ab hier die Ids der Datei-Hotkeys. Windows erlaubt Programmen 0 bis 0xBFFF.</summary>
    public const int FirstFileId = 1;
    public const int LastFileId = 0x5000;

    /// <summary>Schritt für Schritt: F10 nächster Schritt, F5 weiter. Nur während eines Laufs registriert.</summary>
    public const int StepNextId = 0x514A;
    public const int StepContinueId = 0x514B;
    const uint VK_F5 = 0x74, VK_F10 = 0x79;

    static readonly nint HWND_MESSAGE = -3;

    readonly HwndSource _source;
    readonly HashSet<int> _registered = [];

    public GlobalHotkey()
    {
        _source = new HwndSource(new HwndSourceParameters("QuickHasteBot.Hotkey") { ParentWindow = HWND_MESSAGE, WindowStyle = 0 });
        _source.AddHook(WndProc);
    }

    /// <summary>Der Hotkey fürs Popup.</summary>
    public event EventHandler? Pressed;

    /// <summary>Ein Datei-Hotkey, mit der Id aus <see cref="Register(int, HotkeyGesture)"/>.</summary>
    public event EventHandler<int>? FilePressed;

    /// <summary>F10 oder F5 im Schritt-für-Schritt-Lauf: <see cref="StepNextId"/> oder <see cref="StepContinueId"/>.</summary>
    public event EventHandler<int>? StepPressed;

    /// <summary>
    /// F10 und F5 für den Schritt-für-Schritt-Lauf, ohne Zusatztaste. Bewusst nicht über <see cref="HotkeyGesture"/>:
    /// das sind keine einstellbaren Hotkeys, sondern feste Tasten, nur solange der Lauf dauert.
    /// </summary>
    /// <returns>Was sich registrieren liess, je Taste. Belegt ein anderes Programm sie, gehen nur die Knöpfe der Leiste.</returns>
    public (bool Next, bool Continue) RegisterStepKeys() => (RegisterKey(StepNextId, VK_F10), RegisterKey(StepContinueId, VK_F5));

    public void UnregisterStepKeys()
    {
        Unregister(StepNextId);
        Unregister(StepContinueId);
    }

    bool RegisterKey(int id, uint vk)
    {
        Unregister(id);
        if (!RegisterHotKey(_source.Handle, id, MOD_NOREPEAT, vk))
        {
            Log.Info($"Taste 0x{vk:X2} für Schritt für Schritt ist belegt (Win32-Fehler {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()})");
            return false;
        }
        _registered.Add(id);
        return true;
    }

    public HotkeyGesture? Current { get; private set; }

    /// <summary>False, wenn die Kombination schon von einer anderen App belegt ist.</summary>
    public bool Register(HotkeyGesture gesture)
    {
        Unregister();
        if (!Register(HotkeyId, gesture)) return false;
        Current = gesture;
        return true;
    }

    public void Unregister()
    {
        Unregister(HotkeyId);
        Current = null;
    }

    /// <summary>False, wenn die Kombination ungültig oder schon belegt ist (auch von uns selbst unter einer anderen Id).</summary>
    public bool Register(int id, HotkeyGesture gesture)
    {
        Unregister(id);
        if (!gesture.IsValid) return false;

        if (!RegisterHotKey(_source.Handle, id, (uint)gesture.Modifiers | MOD_NOREPEAT, (uint)gesture.VirtualKey))
        {
            Log.Error($"Hotkey {gesture} konnte nicht registriert werden (Win32-Fehler {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()})");
            return false;
        }
        _registered.Add(id);
        return true;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id))
            UnregisterHotKey(_source.Handle, id);
    }

    /// <summary>
    /// Ob sich die Kombination gerade registrieren liesse. Was wir selbst registriert haben, zählt dabei als belegt,
    /// darum vorher mit den eigenen Einträgen vergleichen.
    /// </summary>
    public bool IsFree(HotkeyGesture gesture)
    {
        if (!gesture.IsValid) return false;
        if (!RegisterHotKey(_source.Handle, ProbeId, (uint)gesture.Modifiers | MOD_NOREPEAT, (uint)gesture.VirtualKey))
            return false;
        UnregisterHotKey(_source.Handle, ProbeId);
        return true;
    }

    nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY) return 0;

        var id = (int)wParam;
        if (id == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        else if (id is >= FirstFileId and <= LastFileId)
        {
            handled = true;
            FilePressed?.Invoke(this, id);
        }
        else if (id is StepNextId or StepContinueId)
        {
            handled = true;
            StepPressed?.Invoke(this, id);
        }
        return 0;
    }

    public void Dispose()
    {
        foreach (var id in _registered.ToList())
            Unregister(id);
        Current = null;
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
