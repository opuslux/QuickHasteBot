using System.Runtime.InteropServices;
using System.Windows;
using QuickHasteBot.Core;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.Interop;

/// <summary>
/// Bringt Text ins Zielfenster: per Clipboard + Strg+V oder als Unicode-Tastatureingabe.
/// Ersetzt SendKeys der alten Version (Probleme mit Umlauten, Tastaturlayout und Sonderzeichen wie {}+^%~()).
/// Muss auf dem UI-Thread laufen (Clipboard braucht STA).
/// </summary>
internal sealed class PasteService
{
    /// <summary>So lange bekommt das Zielprogramm Zeit, das Clipboard zu lesen, bevor wir es zurücksetzen.</summary>
    const int ClipboardRestoreDelay = 600;
    const int ModifierReleaseTimeout = 1500;
    const int TypingChunkSize = 200;

    // Clipboard-Verlauf (Win+V) und Sync-Tools sollen unsere temporären Inhalte ignorieren
    const string ExcludeFromHistoryFormat = "ExcludeClipboardContentFromMonitorProcessing";
    const string CanIncludeInHistoryFormat = "CanIncludeInClipboardHistory";

    static readonly string[] PreservedFormats =
    [
        DataFormats.UnicodeText, DataFormats.Text, DataFormats.Rtf, DataFormats.Html, DataFormats.Bitmap, DataFormats.FileDrop,
    ];

    public async Task PasteAsync(string text, PasteMode mode, nint targetWindow)
    {
        if (!await WindowFocus.ActivateAndWaitAsync(targetWindow))
            Log.Info("Zielfenster konnte nicht aktiviert werden, sende trotzdem an das aktuelle Vordergrundfenster");

        // Sonst wird aus Strg+V bei gedrückter Shift-Taste (Shift+Enter) ein Strg+Shift+V
        await WaitForModifiersReleasedAsync();

        if (mode == PasteMode.Clipboard)
            await PasteViaClipboardAsync(text);
        else
            await TypeAsync(text);
    }

    static async Task PasteViaClipboardAsync(string text)
    {
        var backup = SnapshotClipboard();

        var data = new DataObject();
        data.SetText(NormalizeLineEndings(text), TextDataFormat.UnicodeText);
        data.SetData(ExcludeFromHistoryFormat, new MemoryStream([0, 0, 0, 0]));
        data.SetData(CanIncludeInHistoryFormat, new MemoryStream([0, 0, 0, 0]));

        if (!TrySetClipboard(data))
        {
            Log.Error("Clipboard ist blockiert, tippe stattdessen");
            await TypeAsync(text);
            return;
        }

        Send([Key(VK_CONTROL, down: true), Key(VK_V, down: true), Key(VK_V, down: false), Key(VK_CONTROL, down: false)]);

        await Task.Delay(ClipboardRestoreDelay);
        if (backup is not null)
            TrySetClipboard(backup);
    }

    static async Task TypeAsync(string text)
    {
        var inputs = new List<INPUT>();
        foreach (var c in NormalizeLineEndings(text))
        {
            if (c == '\n') continue; // \r\n → ein Enter
            if (c == '\r')
            {
                inputs.Add(Key(VK_RETURN, down: true));
                inputs.Add(Key(VK_RETURN, down: false));
            }
            else
            {
                inputs.Add(Unicode(c, down: true));
                inputs.Add(Unicode(c, down: false));
            }
        }

        // In Häppchen senden, manche Programme verschlucken sonst Zeichen
        for (var i = 0; i < inputs.Count; i += TypingChunkSize)
        {
            Send(inputs.Skip(i).Take(TypingChunkSize).ToArray());
            await Task.Delay(10);
        }
    }

    static async Task WaitForModifiersReleasedAsync()
    {
        var deadline = Environment.TickCount64 + ModifierReleaseTimeout;
        while (AnyModifierDown() && Environment.TickCount64 < deadline)
            await Task.Delay(15);
    }

    static bool AnyModifierDown() =>
        new[] { VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN }.Any(vk => (GetAsyncKeyState(vk) & 0x8000) != 0);

    /// <summary>Kopiert die gängigen Formate, damit wir sie nach dem Einfügen zurückschreiben können.</summary>
    static DataObject? SnapshotClipboard()
    {
        try
        {
            var current = Clipboard.GetDataObject();
            if (current is null) return null;

            var copy = new DataObject();
            var any = false;
            foreach (var format in PreservedFormats)
            {
                if (!current.GetDataPresent(format, autoConvert: false)) continue;
                var value = current.GetData(format, autoConvert: false);
                if (value is null) continue;
                copy.SetData(format, value);
                any = true;
            }
            return any ? copy : null;
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            Log.Error("Clipboard konnte nicht gesichert werden", ex);
            return null;
        }
    }

    /// <summary>Text aus der Zwischenablage für Skripte, leer wenn keiner da ist oder sie blockiert ist.</summary>
    public static string TryGetClipboardText()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : "";
            }
            catch (Exception ex) when (ex is COMException or ExternalException)
            {
                Thread.Sleep(50);
            }
        }
        Log.Error("Clipboard ist blockiert, Skripte sehen eine leere Zwischenablage");
        return "";
    }

    static bool TrySetClipboard(IDataObject data)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                return true;
            }
            catch (Exception ex) when (ex is COMException or ExternalException)
            {
                Thread.Sleep(50);
            }
        }
        return false;
    }

    static string NormalizeLineEndings(string text) => text.ReplaceLineEndings("\r\n");

    static void Send(INPUT[] inputs)
    {
        if (inputs.Length == 0) return;
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            Log.Error($"SendInput hat nur {sent} von {inputs.Length} Eingaben gesendet (Win32-Fehler {Marshal.GetLastPInvokeError()})");
    }

    static INPUT Key(ushort vk, bool down) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = down ? 0 : KEYEVENTF_KEYUP } },
    };

    static INPUT Unicode(char c, bool down) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | (down ? 0 : KEYEVENTF_KEYUP) } },
    };
}
