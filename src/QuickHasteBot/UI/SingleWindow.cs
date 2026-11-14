using System.Windows;

namespace QuickHasteBot.UI;

/// <summary>
/// Ein Fenster, das es nur einmal gibt (Einstellungen, Hilfe, KI-Prompt, Element-Picker): <see cref="Show"/> erzeugt es
/// beim ersten Mal, danach kommt dasselbe nach vorne. Ist es geschlossen, entsteht beim nächsten Mal ein neues.
/// </summary>
internal sealed class SingleWindow<T> where T : Window
{
    /// <summary>Das offene Fenster, null wenn keines offen ist.</summary>
    public T? Current { get; private set; }

    /// <param name="create">Baut das Fenster, nur wenn keines offen ist.</param>
    /// <param name="position">Wo ein neues Fenster erscheint (oben links), sonst wo es selbst hin will.</param>
    /// <param name="beforeActivate">Für das neue wie das offene Fenster, bevor es nach vorne kommt.</param>
    public T Show(Func<T> create, Point? position = null, Action<T>? beforeActivate = null)
    {
        if (Current is not { } window)
        {
            window = create();
            if (position is { } p)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                (window.Left, window.Top) = (p.X, p.Y);
            }
            var created = window;
            created.Closed += (_, _) =>
            {
                if (Current == created)
                    Current = null;
            };
            Current = window;
            window.Show();
        }

        beforeActivate?.Invoke(window);
        SingleWindow.Bring(window);
        return window;
    }

    /// <summary>Nimmt das Fenster heraus, z.B. um es zu schliessen und neu aufzubauen: Das nächste <see cref="Show"/> baut ein neues.</summary>
    public T? Take()
    {
        var window = Current;
        Current = null;
        return window;
    }
}

internal static class SingleWindow
{
    /// <summary>Ein minimiertes Fenster wiederherstellen und nach vorne holen.</summary>
    public static void Bring(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
    }
}
