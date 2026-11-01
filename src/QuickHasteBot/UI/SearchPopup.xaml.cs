using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
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

    public SearchPopup(AppController app)
    {
        InitializeComponent();
        _app = app;
        DataContext = _viewModel = new SearchPopupViewModel(app);
        SourceInitialized += (_, _) => PopupPlacement.UseRoundedCorners(this);
    }

    /// <param name="targetWindow">Fenster, das beim Drücken des Hotkeys vorne war, dorthin wird eingefügt.</param>
    public void ShowFor(nint targetWindow)
    {
        _targetWindow = targetWindow;
        _viewModel.Reset();

        PopupPlacement.ShowAtCursor(this);
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
                Hide();
                _app.Edit(selected ?? _viewModel.CurrentFolder);
                break;
            case Key.E when ctrl:
                Hide();
                _app.RevealInExplorer(selected ?? _viewModel.CurrentFolder);
                break;
            case Key.OemComma when ctrl:
                Hide();
                _app.ShowSettings();
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
