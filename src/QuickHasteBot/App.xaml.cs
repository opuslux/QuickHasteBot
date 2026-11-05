using System.Windows;
using System.Windows.Threading;
using QuickHasteBot.Core;
using QuickHasteBot.Interop;
using QuickHasteBot.UI;

namespace QuickHasteBot;

/// <summary>
/// Einstieg: Single-Instance, Logging, dann übernimmt der <see cref="AppController"/>.
/// Ein zweiter Start öffnet die Einstellungen der laufenden Instanz,
/// "QuickHasteBot.exe --search" öffnet dort das Such-Popup (z.B. für andere Launcher).
/// </summary>
public partial class App : Application
{
    const string InstanceId = "QuickHasteBot-5D0B7C1E-3F4A-4E6B-9A0C-2B8E61F4D7A3";
    const string SettingsSignalName = @"Local\" + InstanceId + "-Settings";
    const string SearchSignalName = @"Local\" + InstanceId + "-Search";

    Mutex? _instanceMutex;
    EventWaitHandle? _settingsSignal;
    EventWaitHandle? _searchSignal;
    readonly List<RegisteredWaitHandle> _signalWaits = [];
    AppController? _controller;
    bool _reportingError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = AppPaths.ForCurrentProcess();
        Log.Initialize(paths.LogFile);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unbehandelter Fehler", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unbeobachteter Task-Fehler", args.Exception);
            args.SetObserved();
        };

        var wantsSearch = e.Args.Any(a => a.Equals("--search", StringComparison.OrdinalIgnoreCase) || a.Equals("/search", StringComparison.OrdinalIgnoreCase));

        _instanceMutex = new Mutex(initiallyOwned: true, @"Local\" + InstanceId, out var isFirstInstance);
        _settingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, SettingsSignalName);
        _searchSignal = new EventWaitHandle(false, EventResetMode.AutoReset, SearchSignalName);

        if (!isFirstInstance)
        {
            // Wir haben gerade Vordergrund-Rechte (vom Benutzer gestartet) und geben sie an die laufende Instanz weiter
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            (wantsSearch ? _searchSignal : _settingsSignal).Set();
            Shutdown();
            return;
        }

        _controller = new AppController(paths);
        _controller.Start();

        ListenFor(_settingsSignal, () => _controller.ShowSettings());
        ListenFor(_searchSignal, () => _controller.ShowSearch());

        if (wantsSearch)
            _controller.ShowSearch();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        foreach (var wait in _signalWaits)
            wait.Unregister(null);
        _controller?.Dispose();
        _settingsSignal?.Dispose();
        _searchSignal?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    void ListenFor(WaitHandle signal, Action action) =>
        _signalWaits.Add(ThreadPool.RegisterWaitForSingleObject(
            signal, (_, _) => Dispatcher.BeginInvoke(action), null, Timeout.Infinite, executeOnlyOnce: false));

    void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unbehandelter Fehler im UI-Thread", e.Exception);
        e.Handled = true;
        var message = string.Format(Strings.UnexpectedErrorFormat, e.Exception.Message);

        // Scheitert das eigene Fenster selbst (oder kommt der nächste Fehler, während es offen ist),
        // bleibt die Meldung von Windows, statt Fehler um Fehler aufzustapeln
        if (_reportingError)
        {
            MessageBox.Show(message, "QuickHasteBot", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        _reportingError = true;
        try
        {
            MessageDialog.Show(message, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _reportingError = false;
        }
    }
}
