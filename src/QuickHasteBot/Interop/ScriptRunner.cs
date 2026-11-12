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
    /// <summary>So lange wird nach dem Ende noch auf den Rest der Ausgabe gewartet (ms).</summary>
    const int OutputGrace = 1000;

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
            + (request.Directory is null ? "" : $" in {info.WorkingDirectory}")
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
            var output = new Collector(process.StandardOutput);
            var errors = new Collector(process.StandardError);
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

            // Startet das Skript selbst ein Programm (start app.exe), erbt dieses die Ausgabeleitung, und sie geht erst
            // mit ihm zu. Darum nur kurz auf den Rest warten und nehmen, was angekommen ist.
            var rest = Environment.TickCount64 + OutputGrace;
            while (!(output.Done && errors.Done) && Environment.TickCount64 < rest)
            {
                if (cancelled())
                {
                    Log.Info($"run(): Warten auf die Ausgabe von {request.File} mit Esc abgebrochen");
                    throw new OperationCanceledException();
                }
                Thread.Sleep(PollInterval);
            }
            if (!(output.Done && errors.Done))
                Log.Info($"run(): {request.File} ist fertig, ein von ihm gestartetes Programm hält die Ausgabe noch offen");
            Log.Info($"run(): {request.File} fertig mit {process.ExitCode}");
            return new RunResult(process.ExitCode, output.Text, errors.Text);
        }
    }

    /// <summary>
    /// Liest eine Ausgabe auf einem eigenen Thread bis zu ihrem Ende. Anders als ReadToEndAsync gibt es das bisher
    /// Gelesene auch, wenn das Ende nicht kommt. Der Leser bleibt offen, solange jemand in die Leitung schreibt: So bricht
    /// einem weiterlaufenden Skript oder Programm die Ausgabe nicht weg. Ein eigener Thread statt des Threadpools, damit
    /// das Lesen nicht hinter anderer Arbeit ansteht.
    /// </summary>
    sealed class Collector
    {
        readonly StringBuilder _text = new();
        volatile bool _done;

        public Collector(StreamReader reader) =>
            new Thread(() => Read(reader)) { IsBackground = true, Name = "run() output" }.Start();

        public bool Done => _done;

        public string Text
        {
            get { lock (_text) return _text.ToString(); }
        }

        void Read(StreamReader reader)
        {
            var buffer = new char[4096];
            try
            {
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                    lock (_text) _text.Append(buffer, 0, read);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Leitung weg: Was da ist, gilt
            }
            finally
            {
                _done = true;
            }
        }
    }

    /// <summary>Wie ein Doppelklick, aber ohne Shell, damit sich Ausgabe und Rückgabewert lesen lassen.</summary>
    static ProcessStartInfo StartInfo(RunRequest request, out string name)
    {
        // Schrägstriche gehen auch, dann muss man in JavaScript keine \\ schreiben
        var path = Environment.ExpandEnvironmentVariables(request.File.Trim()).Replace('/', '\\');
        name = Path.GetFileName(path);
        var hasFolder = path.Contains('\\') || path.Contains(':');
        var directory = request.Directory is null ? null : Folder(request.Directory);
        // Ein Name ohne Ordner, der im Arbeitsordner liegt: selbst auflösen, cmd sucht dort nicht immer
        // (NoDefaultCurrentDirectoryInExePath)
        if (!hasFolder && directory is not null && File.Exists(Path.Combine(directory, path)))
        {
            path = Path.Combine(directory, path);
            hasFolder = true;
        }
        // Ein relativer Pfad neben dem Baustein, z.B. run('nummer.bat') für eine .bat im selben Ordner: dort starten,
        // egal von wo QuickHasteBot gestartet wurde
        else if (!Path.IsPathRooted(path) && request.SnippetFolder is { Length: > 0 } here && File.Exists(Path.Combine(here, path)))
        {
            path = Path.GetFullPath(Path.Combine(here, path));
            hasFolder = true;
        }
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
        info.WorkingDirectory = directory
            ?? (hasFolder ? Path.GetDirectoryName(Path.GetFullPath(path)) ?? ""
            : Environment.CurrentDirectory);
        return info;

        // Wie beim Pfad: Umgebungsvariablen und Schrägstriche gehen
        static string Folder(string directory)
        {
            var folder = Environment.ExpandEnvironmentVariables(directory.Trim()).Replace('/', '\\');
            if (!Directory.Exists(folder))
                throw new RunFailedException($"folder {folder} not found");
            return Path.GetFullPath(folder);
        }
    }
}
