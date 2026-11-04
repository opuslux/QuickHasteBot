using System.ComponentModel;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.Interop;

/// <summary>
/// Löschen wie im Explorer: in den Papierkorb, von dort wiederherstellbar. Wo es keinen gibt (z.B. Netzlaufwerk),
/// fragt Windows selbst, ob die Datei endgültig gelöscht werden soll.
/// </summary>
internal static class RecycleBin
{
    const int ErrorCancelled = 1223;

    /// <returns>false, wenn bei der Nachfrage von Windows abgebrochen wurde.</returns>
    /// <exception cref="IOException">Datei gesperrt, fehlende Rechte usw.</exception>
    public static bool Delete(string path, nint owner)
    {
        var operation = new SHFILEOPSTRUCT
        {
            hwnd = owner,
            wFunc = FO_DELETE,
            // Die Liste endet mit zwei Nullzeichen, eines hängt das Marshalling an
            pFrom = path + '\0',
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING,
        };

        var result = SHFileOperation(ref operation);
        if (result == ErrorCancelled || operation.fAnyOperationsAborted) return false;
        if (result != 0)
            throw new IOException(new Win32Exception(result).Message);
        return true;
    }
}
