using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using QuickHasteBot.Core;

namespace QuickHasteBot.Interop;

/// <summary>
/// Führt run() aus Bausteinskripten aus: ein Skript oder Programm starten, auf das Ende warten und Ausgabe und
/// Rückgabewert lesen. Was gestartet wird, entspricht einem Doppelklick im Explorer: .exe direkt, .ps1 mit PowerShell
/// (die Ausführungsrichtlinie gilt), alles andere über cmd, also mit der Dateizuordnung von Windows (.bat, .cmd, .py …).
/// Damit die Ausgabe ankommt, wird sie umgeleitet: Ein sichtbares Konsolenfenster bleibt darum leer.
/// </summary>
internal static class ScriptRunner
{
    const int PollInterval = 50;

    static ScriptRunner() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Blockiert bis zum Ende oder zur Wartezeit.</summary>
    /// <exception cref="RunFailedException">Nicht gefunden, startet nicht, oder nach der Wartezeit nicht fertig.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancelled"/> (Esc) beim Warten.</exception>
    public static RunResult Run(RunRequest request, Func<bool> cancelled)
    {
        var info = StartInfo(request, out var name);
        var wait = request.WaitSeconds > 0;
        info.RedirectStandardOutput = info.RedirectStandardError = wait;
        if (wait)
        {
            // cmd und PowerShell schreiben in die Konsole in der OEM-Codepage (850 für Deutsch)
            var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            info.StandardOutputEncoding = info.StandardErrorEncoding = oem;
        }

        Log.Info($"run(): {request.File}" + (request.Arguments is null ? "" : $" mit \"{request.Arguments}\"")
            + (request.Hidden ? ", versteckt" : "") + (wait ? $", wartet bis {request.WaitSeconds} s" : ", ohne zu warten"));
        Process process;
        try
        {
            process = Process.Start(info) ?? throw new RunFailedException($"{name} didn't start");
        }
        catch (Win32Exception ex)
        {
            Log.Error($"run(): {request.File} startet nicht", ex);
            throw new RunFailedException($"{name} couldn't start: {new Win32Exception(ex.NativeErrorCode).Message}");
        }

        using (process)
        {
            if (!wait) return new RunResult(null, "", "");

            // Beide Ausgaben gleichzeitig lesen, sonst bleibt das Skript bei vollem Puffer stehen
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            var deadline = Environment.TickCount64 + request.WaitSeconds * 1000L;
            while (!process.WaitForExit(PollInterval))
            {
                if (cancelled())
                {
                    Log.Info($"run(): Warten auf {request.File} mit Esc abgebrochen, es läuft weiter");
                    throw new OperationCanceledException();
                }
                if (Environment.TickCount64 > deadline)
                {
                    Log.Info($"run(): {request.File} nach {request.WaitSeconds} s nicht fertig, es läuft weiter");
                    throw new RunFailedException($"{name} didn't finish within {request.WaitSeconds} s");
                }
            }
            process.WaitForExit(); // bis die Ausgabe ganz gelesen ist
            Log.Info($"run(): {request.File} fertig mit {process.ExitCode}");
            return new RunResult(process.ExitCode, output.Result, errors.Result);
        }
    }

    /// <summary>Wie ein Doppelklick, aber ohne Shell, damit sich Ausgabe und Rückgabewert lesen lassen.</summary>
    static ProcessStartInfo StartInfo(RunRequest request, out string name)
    {
        // Schrägstriche gehen auch, dann muss man in JavaScript keine \\ schreiben
        var path = Environment.ExpandEnvironmentVariables(request.File.Trim()).Replace('/', '\\');
        name = Path.GetFileName(path);
        var hasFolder = path.Contains('\\') || path.Contains(':');
        if (hasFolder && !File.Exists(path))
            throw new RunFailedException($"{path} not found");

        var arguments = request.Arguments ?? "";
        var info = Path.GetExtension(path).ToLowerInvariant() switch
        {
            // Ein Name ohne Ordner wie "ping" sucht Windows im PATH
            ".exe" or ".com" or "" => new ProcessStartInfo(path, arguments),
            ".ps1" => new ProcessStartInfo("powershell.exe", $"-NoProfile -File \"{path}\" {arguments}"),
            // /s: cmd nimmt die äusseren Anführungszeichen weg, die inneren um den Pfad bleiben
            _ => new ProcessStartInfo("cmd.exe", $"/d /s /c \"\"{path}\" {arguments}\""),
        };
        info.UseShellExecute = false;
        info.CreateNoWindow = request.Hidden;
        info.WorkingDirectory = hasFolder ? Path.GetDirectoryName(Path.GetFullPath(path)) ?? "" : Environment.CurrentDirectory;
        return info;
    }
}
