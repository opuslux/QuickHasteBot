namespace QuickHasteBot.Core;

/// <summary>
/// Minimales Datei-Log. Fehler beim Schreiben werden verschluckt, das Log darf die App nie stören.
/// </summary>
public static class Log
{
    const long MaxSize = 1024 * 1024;

    static readonly Lock Gate = new();
    static string? _path;

    public static void Initialize(string path) => _path = path;

    public static void Info(string message) => Write("INF", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERR", message, ex);

    static void Write(string level, string message, Exception? ex)
    {
        if (_path is null) return;

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        if (ex is not null) line += Environment.NewLine + ex;

        lock (Gate)
        {
            try
            {
                var file = new FileInfo(_path);
                if (file.Exists && file.Length > MaxSize)
                    File.Move(_path, _path + ".old", overwrite: true);

                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
