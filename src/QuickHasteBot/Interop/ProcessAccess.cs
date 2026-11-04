using System.Diagnostics;
using System.Runtime.InteropServices;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.Interop;

/// <summary>
/// Läuft ein Programm mit Administratorrechten (höhere Integritätsstufe), verwirft Windows Tastendrücke und
/// UI-Automation-Zugriffe von QuickHasteBot stillschweigend (UIPI). QuickHasteBot soll dafür nicht selbst als
/// Administrator laufen müssen, sondern den Fall erkennen und erklären.
/// </summary>
internal static class ProcessAccess
{
    const int MediumIntegrity = 0x2000;

    static readonly Lazy<int> OwnLevel = new(() => IntegrityLevel(GetCurrentProcess()) ?? MediumIntegrity);

    /// <summary>Windows lässt keine Eingaben in dieses Fenster zu.</summary>
    public static bool IsBlocked(nint hwnd)
    {
        if (hwnd == 0) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || pid == Environment.ProcessId) return false;

        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0) return true; // nicht einmal das: geschützter Systemprozess
        try
        {
            // Kein Zugriff aufs Token heisst praktisch immer: erhöht gestartet
            return (IntegrityLevel(process) ?? int.MaxValue) > OwnLevel.Value;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>Programmname zum Fenster, z.B. "crm", für Meldungen und {window,…}.</summary>
    public static string ProcessName(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
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

    static int? IntegrityLevel(nint process)
    {
        if (!OpenProcessToken(process, TOKEN_QUERY, out var token)) return null;
        var buffer = Marshal.AllocHGlobal(256);
        try
        {
            if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, 256, out _)) return null;
            // TOKEN_MANDATORY_LABEL beginnt mit dem Zeiger auf die SID, deren letzte Unterautorität ist die Stufe
            var sid = Marshal.ReadIntPtr(buffer);
            var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            return count == 0 ? null : Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            CloseHandle(token);
        }
    }
}
