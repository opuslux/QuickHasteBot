using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using QuickHasteBot.Core;
using QuickHasteBot.Interop;

namespace QuickHasteBot.UI;

/// <summary>
/// Das Popup am Mauszeiger. Wird einmal erzeugt und danach nur versteckt/gezeigt, damit es sofort da ist.
/// Die Tastatur bleibt immer im Suchfeld, die Liste wird von hier aus gesteuert.
/// </summary>
internal partial class SearchPopup : Window
{
    const int PageSize = 8;

    readonly AppController _app;
    readonly SearchPopupViewModel _viewModel;
    nint _targetWindow;
    SnippetItem? _pressedItem;
    PopupPlacement.Anchor? _anchor;
    Point? _dragStart;
    bool _moving;

    public SearchPopup(AppController app)
    {
        InitializeComponent();
        _app = app;
        DataContext = _viewModel = new SearchPopupViewModel(app);
        SourceInitialized += (_, _) => PopupPlacement.UseRoundedCorners(this);
        // Die Höhe folgt der Liste, dabei soll das Fenster am Mauszeiger bleiben
        SizeChanged += (_, _) =>
        {
            if (_anchor is { } anchor)
                PopupPlacement.KeepAnchored(this, anchor);
        };
    }

    /// <param name="targetWindow">Fenster, das beim Drücken des Hotkeys vorne war, dorthin wird eingefügt.</param>
    public void ShowFor(nint targetWindow)
    {
        _targetWindow = targetWindow;
        _viewModel.Reset();

        _anchor = null;
        _anchor = PopupPlacement.ShowAtCursor(this);
        Activate();
        WindowFocus.Activate(new WindowInteropHelper(this).Handle);
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    /// <summary>Die Bibliothek wurde neu eingelesen (Datei geändert, anderer Ordner).</summary>
    public void OnLibraryChanged()
    {
        if (IsVisible)
            _viewModel.Refresh();
    }

    void OnDeactivated(object? sender, EventArgs e) => Hide();

    // Überall im Fenster lässt es sich verschieben. Erst ab einer kleinen Mausbewegung, damit Klicks Klicks bleiben;
    // Scrollbalken und Suchfeld behalten ihr eigenes Ziehen.
    void OnWindowMouseDown(object sender, MouseButtonEventArgs e) =>
        _dragStart = IsInside<ScrollBar>(e.OriginalSource) || IsInside<TextBox>(e.OriginalSource) ? null : e.GetPosition(this);

    void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragStart = null;
            return;
        }

        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragStart = null;
        _pressedItem = null;
        Mouse.Capture(null);

        // Während des Ziehens nicht zurückspringen, auch wenn ein Monitorwechsel die Größe ändert
        var anchor = _anchor;
        _anchor = null;
        _moving = true;
        try
        {
            DragMove();
        }
        finally
        {
            _moving = false;
        }
        if (anchor is not null)
            _anchor = PopupPlacement.Moved(this, anchor.Value);
        Keyboard.Focus(SearchBox);
    }

    static bool IsInside<T>(object source) where T : DependencyObject
    {
        // Runs im Text sind keine Visuals, dort führt der logische Baum weiter
        for (var element = source as DependencyObject; element is not null;
             element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element))
        {
            if (element is T) return true;
        }
        return false;
    }

    void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Results.SelectedItem is not null)
            Results.ScrollIntoView(Results.SelectedItem);
    }

    // Die Einträge sind nicht fokussierbar, deshalb wählt die ListBox per Klick nichts aus – das übernehmen wir
    void OnResultsMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressedItem = e.ClickCount == 1 ? ItemAt(e) : null;
        if (_pressedItem is not null)
            _viewModel.Selected = _pressedItem;
    }

    // Wie in einem Menü erst beim Loslassen auslösen, und nur wenn die Maus noch auf demselben Eintrag ist.
    // Der zweite Klick eines Doppelklicks zählt nicht, sonst würde er gleich im geöffneten Ordner weiterklicken.
    void OnResultsMouseUp(object sender, MouseButtonEventArgs e)
    {
        var pressed = _pressedItem;
        _pressedItem = null;
        if (!_app.Settings.SingleClick || pressed is null || ItemAt(e) != pressed) return;

        ActivateSelected(alternate: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        Keyboard.Focus(SearchBox);
    }

    void OnResultsDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Doppelklick auf Scrollbalken oder Leerfläche ignorieren
        if (_app.Settings.SingleClick || ItemAt(e) is null) return;

        ActivateSelected(alternate: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        Keyboard.Focus(SearchBox);
    }

    void OnBreadcrumbClick(object sender, MouseButtonEventArgs e)
    {
        // Das Loslassen am Ende des Verschiebens ist kein Klick
        if (_moving || sender is not FrameworkElement { DataContext: Breadcrumb { Target: { } target } }) return;

        _viewModel.GoTo(target);
        Keyboard.Focus(SearchBox);
        e.Handled = true;
    }

    void OnEditClick(object sender, MouseButtonEventArgs e) => OnHintClick(e, EditSelected);

    void OnExplorerClick(object sender, MouseButtonEventArgs e) => OnHintClick(e, RevealSelected);

    void OnSettingsClick(object sender, MouseButtonEventArgs e) => OnHintClick(e, OpenSettings);

    void OnHintClick(MouseButtonEventArgs e, Action command)
    {
        if (_moving) return;
        command();
        e.Handled = true;
    }

    // Ohne Auswahl gilt der angezeigte Ordner
    void EditSelected()
    {
        Hide();
        _app.Edit(_viewModel.Selected?.Snippet ?? _viewModel.CurrentFolder);
    }

    void RevealSelected()
    {
        Hide();
        _app.RevealInExplorer(_viewModel.Selected?.Snippet ?? _viewModel.CurrentFolder);
    }

    void OpenSettings()
    {
        Hide();
        _app.ShowSettings();
    }

    SnippetItem? ItemAt(MouseButtonEventArgs e) =>
        Results.ContainerFromElement((DependencyObject)e.OriginalSource) is ListBoxItem { DataContext: SnippetItem item } ? item : null;

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        var browsing = !_viewModel.IsSearching;
        var selected = _viewModel.Selected?.Snippet;

        e.Handled = true;
        switch (e.Key)
        {
            case Key.Escape:
                Hide();
                break;
            case Key.Down:
                _viewModel.MoveSelection(1);
                break;
            case Key.Up:
                _viewModel.MoveSelection(-1);
                break;
            case Key.PageDown:
                _viewModel.MoveSelection(PageSize);
                break;
            case Key.PageUp:
                _viewModel.MoveSelection(-PageSize);
                break;
            case Key.Home when ctrl:
            case Key.End when ctrl:
                _viewModel.SelectEdge(last: e.Key == Key.End);
                break;
            case Key.Enter:
                ActivateSelected(alternate: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                break;
            case Key.Right when browsing && selected is { IsFolder: true }:
                _viewModel.EnterFolder(selected);
                break;
            case Key.Left when browsing:
                _viewModel.GoUp();
                break;
            // Backspace geht nur hoch, wenn es nichts mehr zu löschen gibt
            case Key.Back when _viewModel.Query.Length == 0:
                _viewModel.GoUp();
                break;
            case Key.O when ctrl:
                EditSelected();
                break;
            case Key.E when ctrl:
                RevealSelected();
                break;
            case Key.OemComma when ctrl:
                OpenSettings();
                break;
            default:
                e.Handled = false;
                break;
        }
    }

    async void ActivateSelected(bool alternate)
    {
        var snippet = _viewModel.Selected?.Snippet;
        if (snippet is null) return;

        if (snippet.IsFolder)
        {
            _viewModel.EnterFolder(snippet);
            return;
        }

        Hide();
        try
        {
            await _app.ActivateAsync(snippet, alternate, _targetWindow);
        }
        catch (Exception ex)
        {
            Log.Error($"Einfügen von {snippet.RelativePath} fehlgeschlagen", ex);
        }
    }
}
