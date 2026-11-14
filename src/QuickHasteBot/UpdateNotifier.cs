using System.Net.Http;
using System.Windows.Threading;
using QuickHasteBot.Core;
using QuickHasteBot.Interop;

namespace QuickHasteBot;

/// <summary>
/// Neue Versionen auf GitHub: fragt einmal am Tag nach dem neuesten Release (stündlich nachgesehen, ob der Tag um ist) und
/// meldet über <see cref="Changed"/>, wenn sich der Hinweis in Tray und Einstellungen ändert.
/// </summary>
internal sealed class UpdateNotifier(AppSettings settings, Action save) : IDisposable
{
    static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };
    DispatcherTimer? _timer;
    bool _checking;

    /// <summary>Der Hinweis auf eine neue Version hat sich geändert. true: eine neue Version, die der Tray einmal meldet.</summary>
    public event Action<bool>? Changed;

    /// <summary>Gleich und danach stündlich nachsehen: läuft das Programm wochenlang, kommt der Hinweis trotzdem.</summary>
    internal void Start()
    {
        _timer = new DispatcherTimer(TimeSpan.FromHours(1), DispatcherPriority.Background, (_, _) => _ = CheckAsync(),
            Dispatcher.CurrentDispatcher);
        _ = CheckAsync();
    }

    /// <summary>Das neuere Release auf GitHub, z.B. "v2.16.0", null wenn diese Version aktuell ist oder nicht gefragt wird.</summary>
    internal string? Available =>
        settings.CheckForUpdates && UpdateCheck.IsNewer(settings.LatestRelease, AppVersion.Informational) ? settings.LatestRelease : null;

    internal void OpenPage()
    {
        if (Available is { } tag)
            Shell.Open(UpdateCheck.ReleasePage(tag));
        else
            AppController.OpenProjectPage();
    }

    internal void SetEnabled(bool on)
    {
        settings.CheckForUpdates = on;
        save();
        Changed?.Invoke(false);
        if (on)
            _ = CheckAsync();
    }

    /// <summary>
    /// Fragt GitHub, wenn es dran ist (einmal am Tag). Eine Version, die neu dazukommt, meldet der Tray einmal; der Eintrag
    /// im Menü und der Link in den Einstellungen bleiben bis zum Update. Fehler (kein Netz) stehen nur im Log.
    /// </summary>
    internal async Task CheckAsync()
    {
        if (_checking || !settings.CheckForUpdates || AppVersion.Numeric(AppVersion.Informational) is null
            || !UpdateCheck.Due(settings.LastUpdateCheck, DateTimeOffset.Now))
            return;
        _checking = true;
        try
        {
            var release = await UpdateCheck.FetchAsync(Client);
            settings.LastUpdateCheck = DateTimeOffset.Now;
            var known = settings.LatestRelease;
            if (release is not null)
                settings.LatestRelease = release.Tag;
            save();
            if (Available is { } tag && !string.Equals(tag, known, StringComparison.OrdinalIgnoreCase))
            {
                Log.Info($"Neue Version auf GitHub: {tag}");
                Changed?.Invoke(true);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Log.Info($"Keine Antwort von GitHub zur neuesten Version: {ex.Message}");
        }
        finally
        {
            _checking = false;
        }
    }

    public void Dispose() => _timer?.Stop();
}
