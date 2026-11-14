using System.ComponentModel;
using System.Diagnostics;
using QuickHasteBot.Core;

namespace QuickHasteBot.Interop;

/// <summary>Dateien, Ordner und Adressen mit dem Programm öffnen, das Windows dafür vorsieht. Fehler stehen nur im Log.</summary>
internal static class Shell
{
    /// <summary>Öffnet wie ein Doppelklick im Explorer.</summary>
    public static void Open(string path) => TryStart(new ProcessStartInfo(path) { UseShellExecute = true });

    public static bool TryStart(ProcessStartInfo info)
    {
        try
        {
            Process.Start(info)?.Dispose();
            return true;
        }
        catch (Win32Exception ex)
        {
            Log.Error($"Konnte nicht öffnen: {info.FileName} {info.Arguments}", ex);
            return false;
        }
    }
}
