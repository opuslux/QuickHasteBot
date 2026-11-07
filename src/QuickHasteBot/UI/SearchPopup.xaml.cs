using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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

    // Fährt die Maus Richtung rechte Spalte, wechselt die Auswahl erst, wenn sie so lange auf einer Zeile ruht.
    // Sonst würde rechts alles umspringen, sobald man schräg hinüberfährt und dabei andere Zeilen streift.
    static readonly TimeSpan AimDelay = TimeSpan.FromMilliseconds(100);

    // Die Richtung aus mehreren Mausmeldungen bestimmen, einzelne Pixelschritte sind zu ungenau
    const int TrackedMoves = 3;

    readonly AppController _app;
    readonly SearchPopupViewModel _viewModel;
    readonly DispatcherTimer _hoverTimer;
    nint _targetWindow;
    SnippetItem? _pressedItem;
    SnippetItem? _hoverItem;
    Point _lastMouse;
    readonly Queue<Point> _recentMoves = new();
    bool _hoverSelecting;
    bool _dialogOpen;
    PopupPlacement.Anchor? _anchor;
    Point? _dragStart;
    bool _moving;
    PopupPlacement.Anchor? _resizeAnchor;
    int _resizeStartX;
    double _resizeStartWidth;
    double _resizeStartLeft;

    public SearchPopup(AppController app)
    {
        InitializeComponent();
        _app = app;
        DataContext = _viewModel = new SearchPopupViewModel(app);
        ApplyColumnShare(app.Settings.ListColumnShare);
        _hoverTimer = new DispatcherTimer(AimDelay, DispatcherPriority.Input, OnHoverTimer, Dispatcher) { IsEnabled = false };
        // Neuer Inhalt rechts beginnt oben links
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SearchPopupViewModel.ShowDetailItems)) return;
            DetailScroll.ScrollToHome();
            if (_viewModel.DetailItems.Count > 0)
                DetailList.ScrollIntoView(_viewModel.DetailItems[0]);

            // Ein Ordnerinhalt rechts darf das Popup höher machen, statt zu scrollen. Immer neu messen: Füllt schon
            // die alte Liste die Höhe, meldet die neue dieselbe Wunschhöhe und das Layout merkte nichts.
            DetailHost.Grow = _viewModel.ShowDetailItems;
            DetailHost.InvalidateMeasure();
        };
        // Neue Liste links (Öffnen, Suchen, anderer Ordner): die rechte Spalte gibt die Höhe wieder frei
        _viewModel.Items.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
                DetailHost.ResetGrowth();
        };
        // Egal wie das Popup zugeht (Einfügen, Esc, Klick daneben): Ordner fürs nächste Öffnen merken
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false)
                _viewModel.RememberPosition();
        };
        SourceInitialized += (_, _) => PopupPlacement.UseRoundedCorners(this);
        // Die Höhe folgt der Liste, dabei soll das Fenster am Mauszeiger bleiben
        SizeChanged += (_, _) =>
        {
            if (_anchor is { } anchor)
                PopupPlacement.KeepAnchored(this, anchor);
        };
    }

    /// <summary>Wohin das offene Popup einfügen würde. Ein Datei-Hotkey bei offenem Popup fügt auch dort ein.</summary>
    public nint TargetWindow => _targetWindow;

    /// <param name="targetWindow">Fenster, das beim Drücken des Hotkeys vorne war, dorthin wird eingefügt.</param>
    /// <param name="folder">Mit dem Hotkey eines Ordners: in diesem Ordner öffnen statt wie eingestellt.</param>
    public void ShowFor(nint targetWindow, Snippet? folder = null)
    {
        _targetWindow = targetWindow;
        _viewModel.Reset(folder?.RelativePath);
        ApplyWidth();

        _anchor = null;
        var (fractionX, fractionY) = AppSettings.PopupFractions(_app.Settings.PopupPosition);
        _anchor = PopupPlacement.ShowAtCursor(this, fractionX, fractionY);
        // Das Fenster erscheint unter der ruhenden Maus, das ist noch keine Bewegung
        _lastMouse = PointToScreen(Mouse.GetPosition(this));
        _recentMoves.Clear();
        Activate();
        WindowFocus.Activate(new WindowInteropHelper(this).Handle);
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        // Ein gemerkter Suchtext ist markiert: Enter nimmt ihn, Tippen ersetzt ihn
        SearchBox.SelectAll();
    }

    /// <summary>Die Bibliothek wurde neu eingelesen (Datei geändert, anderer Ordner).</summary>
    public void OnLibraryChanged()
    {
        if (IsVisible)
            _viewModel.Refresh();
    }

    void OnDeactivated(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        if (!_dialogOpen)
            Hide();
    }

    // Überall im Fenster lässt es sich verschieben. Erst ab einer kleinen Mausbewegung, damit Klicks Klicks bleiben;
    // Scrollbalken, Suchfeld, Trenner und Knöpfe behalten ihr eigenes Ziehen bzw. ihren Klick.
    void OnWindowMouseDown(object sender, MouseButtonEventArgs e) =>
        _dragStart = IsInside<ScrollBar>(e.OriginalSource) || IsInside<TextBox>(e.OriginalSource) ||
                     IsInside<Thumb>(e.OriginalSource) || IsInside<ButtonBase>(e.OriginalSource)
            ? null
            : e.GetPosition(this);

    /// <summary>Gespeicherte Breite, aber nie schmaler als die Zeile mit den Tastenkürzeln und nie breiter als alle Bildschirme.</summary>
    internal void ApplyWidth()
    {
        // Die Kürzel hängen von Sprache und Einfügemethode ab, deshalb bei jedem Öffnen neu messen. Jedes einzeln,
        // die StackPanel selbst meldet nur, was beim letzten Layout Platz hatte. Vor dem ersten Anzeigen ist die
        // Bindung der Einfügehinweise noch nicht übertragen, das hier sofort nachholen.
        EnterHint.SetCurrentValue(KeyHint.LabelProperty, _viewModel.EnterHint);
        ShiftEnterHint.SetCurrentValue(KeyHint.LabelProperty, _viewModel.ShiftEnterHint);
        var unlimited = new Size(double.PositiveInfinity, double.PositiveInfinity);
        var hints = HintBar.Children.OfType<UIElement>().Sum(hint =>
        {
            hint.Measure(unlimited);
            return hint.DesiredSize.Width;
        });
        MinWidth = Math.Ceiling(hints + HintBar.Margin.Left + HintBar.Margin.Right + Body.Margin.Left + Body.Margin.Right +
                                BorderThickness.Left + BorderThickness.Right);

        var width = double.IsFinite(_app.Settings.PopupWidth) ? _app.Settings.PopupWidth : AppSettings.DefaultPopupWidth;
        Width = Math.Clamp(width, MinWidth, Math.Max(MinWidth, SystemParameters.VirtualScreenWidth));
    }

    // Breite an den Rändern ziehen. Solange gezogen wird, hält das Fenster nicht am Mauszeiger fest,
    // danach wandert der Anker mit, wie nach dem Verschieben.
    void OnResizeStarted(object sender, DragStartedEventArgs e)
    {
        _resizeAnchor = _anchor;
        _anchor = null;
        NativeMethods.GetCursorPos(out var cursor);
        _resizeStartX = cursor.X;
        _resizeStartWidth = ActualWidth;
        _resizeStartLeft = Left;
    }

    void OnResizeDelta(object sender, DragDeltaEventArgs e)
    {
        // Bildschirmkoordinaten statt e.HorizontalChange: Der linke Rand wandert beim Ziehen mit dem Fenster mit
        NativeMethods.GetCursorPos(out var cursor);
        var delta = (cursor.X - _resizeStartX) / VisualTreeHelper.GetDpi(this).DpiScaleX;
        var fromLeft = sender == LeftEdge;
        var width = Math.Max(MinWidth, fromLeft ? _resizeStartWidth - delta : _resizeStartWidth + delta);
        if (fromLeft)
            Left = _resizeStartLeft + _resizeStartWidth - width;
        Width = width;
    }

    void OnResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_resizeAnchor is { } previous)
            _anchor = PopupPlacement.Moved(this, previous);
        _resizeAnchor = null;

        _app.Settings.PopupWidth = Math.Round(Width);
        _app.SaveSettings();
        Keyboard.Focus(SearchBox);
    }

    void ApplyColumnShare(double share)
    {
        share = AppSettings.ClampColumnShare(share);
        ListColumn.Width = new GridLength(share, GridUnitType.Star);
        DetailColumn.Width = new GridLength(1 - share, GridUnitType.Star);
    }

    // Die neue Aufteilung gilt ab jetzt bei jedem Öffnen
    void OnSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        var total = ListColumn.ActualWidth + DetailColumn.ActualWidth;
        if (total > 0)
        {
            _app.Settings.ListColumnShare = Math.Round(AppSettings.ClampColumnShare(ListColumn.ActualWidth / total), 3);
            _app.SaveSettings();
            ApplyColumnShare(_app.Settings.ListColumnShare);
        }
        Keyboard.Focus(SearchBox);
    }

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
        if (anchor is { } previous)
            _anchor = PopupPlacement.Moved(this, previous);
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
        // Was die Maus auswählt, ist schon zu sehen, ein halb sichtbarer Eintrag soll nicht wegscrollen
        if (Results.SelectedItem is not null && !_hoverSelecting)
            Results.ScrollIntoView(Results.SelectedItem);
    }

    // Wie in einem Menü folgt die Auswahl sofort der Maus, ausser sie ist unterwegs zur rechten Spalte
    void OnResultsMouseMove(object sender, MouseEventArgs e)
    {
        // WPF meldet auch eine Bewegung, wenn die Liste unter der ruhenden Maus neu aufgebaut wird (beim Tippen).
        // Das darf die Auswahl der Tastatur nicht überschreiben.
        var position = PointToScreen(e.GetPosition(this));
        if (position == _lastMouse) return;
        _lastMouse = position;

        var previous = _recentMoves.Count > 0 ? _recentMoves.Peek() : position;
        _recentMoves.Enqueue(position);
        if (_recentMoves.Count > TrackedMoves)
            _recentMoves.Dequeue();

        _hoverTimer.Stop();
        if (e.LeftButton == MouseButtonState.Pressed) return;
        _hoverItem = ItemAt(Results, e.OriginalSource);
        if (_hoverItem is null || _hoverItem == _viewModel.Selected) return;

        if (IsHeadingToDetail(previous, position))
            _hoverTimer.Start();
        else
            SelectHovered();
    }

    /// <summary>
    /// Bewegt sich die Maus in das Dreieck zwischen ihr und der linken Kante der rechten Spalte?
    /// Dann werden die beiden Ecken links oben und links unten aus ihrer Sicht steiler – wie bei Untermenüs.
    /// </summary>
    bool IsHeadingToDetail(Point previous, Point current)
    {
        if (current.X <= previous.X || DetailHost.ActualHeight <= 0) return false;

        var top = DetailHost.PointToScreen(new Point(0, 0));
        var bottom = DetailHost.PointToScreen(new Point(0, DetailHost.ActualHeight));
        if (current.X >= top.X) return false;

        static double Slope(Point from, Point to) => (to.Y - from.Y) / (to.X - from.X);
        return Slope(current, top) < Slope(previous, top) && Slope(current, bottom) > Slope(previous, bottom);
    }

    void OnResultsMouseLeave(object sender, MouseEventArgs e)
    {
        _hoverTimer.Stop();
        _recentMoves.Clear();
    }

    // Die Maus ruht auf einer Zeile, obwohl sie Richtung rechte Spalte unterwegs war
    void OnHoverTimer(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        SelectHovered();
    }

    void SelectHovered()
    {
        // Die Liste kann sich inzwischen geändert haben
        if (_hoverItem is null || !_viewModel.Items.Contains(_hoverItem)) return;

        _hoverSelecting = true;
        try
        {
            _viewModel.Selected = _hoverItem;
        }
        finally
        {
            _hoverSelecting = false;
        }
    }

    // Die Einträge sind nicht fokussierbar, deshalb wählt die ListBox per Klick nichts aus – das übernehmen wir
    void OnResultsMouseDown(object sender, MouseButtonEventArgs e)
    {
        _hoverTimer.Stop();
        _pressedItem = e.ClickCount == 1 ? ItemAt(Results, e.OriginalSource) : null;
        if (_pressedItem is not null)
            _viewModel.Selected = _pressedItem;
    }

    void OnResultsMouseUp(object sender, MouseButtonEventArgs e) =>
        OnListMouseUp(Results, e, _ => ActivateSelected(alternate: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)));

    void OnResultsDoubleClick(object sender, MouseButtonEventArgs e) =>
        OnListDoubleClick(Results, e, _ => ActivateSelected(alternate: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)));

    // Rechts bleibt die Auswahl links, wie sie ist: Ein Klick fügt direkt ein bzw. öffnet den Ordner
    void OnDetailMouseDown(object sender, MouseButtonEventArgs e) =>
        _pressedItem = e.ClickCount == 1 ? ItemAt(DetailList, e.OriginalSource) : null;

    void OnDetailMouseUp(object sender, MouseButtonEventArgs e) =>
        OnListMouseUp(DetailList, e, item => ActivateDetail(item.Snippet, alternate: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)));

    void OnDetailDoubleClick(object sender, MouseButtonEventArgs e) =>
        OnListDoubleClick(DetailList, e, item => ActivateDetail(item.Snippet, alternate: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)));

    // Wie in einem Menü erst beim Loslassen auslösen, und nur wenn die Maus noch auf demselben Eintrag ist.
    // Der zweite Klick eines Doppelklicks zählt nicht, sonst würde er gleich im geöffneten Ordner weiterklicken.
    void OnListMouseUp(ListBox list, MouseButtonEventArgs e, Action<SnippetItem> activate)
    {
        var pressed = _pressedItem;
        _pressedItem = null;
        if (!_app.Settings.SingleClick || pressed is null || ItemAt(list, e.OriginalSource) != pressed) return;

        activate(pressed);
        Keyboard.Focus(SearchBox);
    }

    void OnListDoubleClick(ListBox list, MouseButtonEventArgs e, Action<SnippetItem> activate)
    {
        // Doppelklick auf Scrollbalken oder Leerfläche ignorieren
        if (_app.Settings.SingleClick || ItemAt(list, e.OriginalSource) is not { } item) return;

        activate(item);
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

    // Neben der Stecknadel: leerer Editor im offenen Ordner, wie Strg+N
    void OnNewSnippetClick(object sender, RoutedEventArgs e)
    {
        Hide();
        _app.NewSnippet(_viewModel.CurrentFolder);
    }

    void OnEditClick(object sender, MouseButtonEventArgs e) => OnHintClick(e, EditSelected);

    // Stift unten rechts in der Textvorschau: den angezeigten Baustein bearbeiten, wie Strg+O
    void OnEditPreviewClick(object sender, RoutedEventArgs e) => EditSelected();

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
        _app.EditSnippet(_viewModel.Selected?.Snippet ?? _viewModel.CurrentFolder);
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

    // Rechtsklick auf einen Eintrag, links wie rechts. Links wird er dabei markiert, rechts bleibt der Ordner markiert.
    // Auf die freie Fläche: neuer Baustein oder Ordner im offenen Ordner, sonst käme man in einem leeren Ordner nicht dazu.
    void OnListRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var list = (ListBox)sender;
        _hoverTimer.Stop();
        if (ItemAt(list, e.OriginalSource) is not { } item)
        {
            ShowFolderMenu(list);
            e.Handled = true;
            return;
        }

        if (list == Results)
            _viewModel.Selected = item;
        ShowItemMenu(item.Snippet, list);
        e.Handled = true;
    }

    /// <summary>Dateien: Bearbeiten, Umbenennen, Klonen, Löschen. Ordner: Umbenennen. Beide: Hotkey festlegen. Beide: Im Explorer anzeigen, Neuer Baustein.</summary>
    void ShowItemMenu(Snippet snippet, UIElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.MousePoint };
        // Alles andere öffnet schon per Klick die zugeordnete Anwendung
        if (snippet.Kind == SnippetKind.Text)
        {
            menu.Items.Add(MenuEntry(Strings.MenuEdit, MenuIcons.Edit, () =>
            {
                Hide();
                _app.EditSnippet(snippet);
            }));
            if (_app.Settings.UseBuiltInEditor)
                menu.Items.Add(MenuEntry(Strings.MenuEditExternal, MenuIcons.OpenExternal, () =>
                {
                    Hide();
                    _app.Edit(snippet);
                }));
        }
        // Wie Strg+E, aber für den Eintrag unter der Maus: Dateien markiert im Ordner, Ordner selbst geöffnet
        menu.Items.Add(MenuEntry(Strings.MenuShowInExplorer, MenuIcons.Explorer, () =>
        {
            Hide();
            _app.RevealInExplorer(snippet);
        }));
        // Neuer Baustein im Ordner bzw. neben der Datei
        menu.Items.Add(MenuEntry(Strings.MenuNewSnippet, MenuIcons.NewSnippet, () =>
        {
            Hide();
            _app.NewSnippet(snippet);
        }));
        // Immer im offenen Ordner, nicht im Ordner unter der Maus: der Dialog sagt das
        menu.Items.Add(MenuEntry(Strings.MenuNewFolder, MenuIcons.NewFolder, NewFolder));
        menu.Items.Add(MenuEntry(Strings.MenuRename, MenuIcons.Rename, () => Rename(snippet)));
        menu.Items.Add(MenuEntry(Strings.MenuSetHotkey, MenuIcons.Hotkey, () => SetHotkey(snippet)));
        if (!snippet.IsFolder)
        {
            menu.Items.Add(MenuEntry(Strings.MenuClone, MenuIcons.Clone, () => Clone(snippet)));
            menu.Items.Add(MenuEntry(Strings.MenuDelete, MenuIcons.Delete, () => Delete(snippet)));
        }
        menu.IsOpen = true;
    }

    /// <summary>Rechtsklick neben die Einträge: was im offenen Ordner neu entstehen kann.</summary>
    void ShowFolderMenu(UIElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.MousePoint };
        menu.Items.Add(MenuEntry(Strings.MenuNewSnippet, MenuIcons.NewSnippet, () =>
        {
            Hide();
            _app.NewSnippet(_viewModel.CurrentFolder);
        }));
        menu.Items.Add(MenuEntry(Strings.MenuNewFolder, MenuIcons.NewFolder, NewFolder));
        menu.IsOpen = true;
    }

    /// <summary>Neuer Ordner im Ordner, der im Popup offen ist, danach markiert.</summary>
    void NewFolder()
    {
        var parent = _viewModel.CurrentFolder;
        var display = string.Join(" › ", new[] { Strings.BreadcrumbRoot }
            .Concat(parent.RelativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
                .Select(folder => FileHotkey.Split(folder).Name)));
        var dialog = NameDialog.ForNewFolder(parent.FullPath, display,
            name => _app.HotkeyConflictForName(name, isFolder: true, selfPath: null, currentHotkey: null));
        dialog.Owner = this;
        if (WithDialog(dialog.ShowDialog) == true)
            ChangeFile(parent, Strings.NewFolderErrorFormat, () => SnippetFiles.CreateFolder(parent.FullPath, dialog.FileName));
    }

    // Erst ausführen, wenn das Menü zu ist, sonst steht es hinter dem Dialog noch offen
    MenuItem MenuEntry(string header, string glyph, Action action)
    {
        var item = new MenuItem { Header = header, Icon = MenuIcons.Glyph(glyph) };
        item.Click += (_, _) => Dispatcher.BeginInvoke(action, DispatcherPriority.Input);
        return item;
    }

    void Clone(Snippet snippet)
    {
        var dialog = NameDialog.ForClone(snippet, SnippetFiles.SuggestCopyName(snippet.FullPath, Strings.CopySuffix),
            name => _app.HotkeyConflictForName(name, isFolder: false, selfPath: null, currentHotkey: null));
        dialog.Owner = this;
        if (WithDialog(dialog.ShowDialog) == true)
            ChangeFile(snippet, Strings.CloneErrorFormat, () => SnippetFiles.Clone(snippet.FullPath, dialog.FileName));
    }

    void Rename(Snippet snippet)
    {
        var dialog = NameDialog.ForRename(snippet,
            name => _app.HotkeyConflictForName(name, snippet.IsFolder, snippet.FullPath, snippet.Hotkey));
        dialog.Owner = this;
        if (WithDialog(dialog.ShowDialog) == true)
            ChangeFile(snippet, Strings.RenameErrorFormat, () => SnippetFiles.Rename(snippet.FullPath, dialog.FileName));
    }

    /// <summary>Der Hotkey steht im Namen, festlegen heisst also umbenennen.</summary>
    void SetHotkey(Snippet snippet)
    {
        var dialog = new HotkeyDialog(_app, snippet) { Owner = this };
        if (WithDialog(dialog.ShowDialog) == true)
            ChangeFile(snippet, Strings.RenameErrorFormat, () => SnippetFiles.Rename(snippet.FullPath, dialog.NewFileName));
    }

    /// <summary>Kopie anlegen oder umbenennen, danach ist das Ergebnis markiert, sofern es in der Liste steht.</summary>
    void ChangeFile(Snippet snippet, string errorFormat, Func<string> change)
    {
        try
        {
            var path = change();
            _app.Library.Reload();
            _viewModel.RefreshSelecting(Path.GetRelativePath(_app.Library.RootPath, path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Konnte nicht geändert werden: {snippet.FullPath}", ex);
            WithDialog(() => MessageDialog.Show(this, string.Format(errorFormat, ex.Message),
                MessageBoxButton.OK, MessageBoxImage.Warning));
        }
    }

    void Delete(Snippet snippet)
    {
        var name = Path.GetFileName(snippet.FullPath);
        var answer = WithDialog(() => MessageDialog.Show(this, string.Format(Strings.DeleteConfirmFormat, name),
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No));
        if (answer != MessageBoxResult.Yes) return;

        var nextSelection = _viewModel.SelectionAfterRemoving(snippet);
        try
        {
            if (!WithDialog(() => RecycleBin.Delete(snippet.FullPath, new WindowInteropHelper(this).Handle))) return;
            _app.Library.Reload();
            _viewModel.Refresh(nextSelection);
        }
        catch (IOException ex)
        {
            Log.Error($"Löschen von {snippet.FullPath} fehlgeschlagen", ex);
            WithDialog(() => MessageDialog.Show(this, string.Format(Strings.DeleteErrorFormat, name, ex.Message),
                MessageBoxButton.OK, MessageBoxImage.Warning));
        }
    }

    /// <summary>Das Popup verliert den Fokus an den Dialog, soll dabei aber nicht zugehen.</summary>
    T WithDialog<T>(Func<T> show)
    {
        _dialogOpen = true;
        try
        {
            return show();
        }
        finally
        {
            _dialogOpen = false;
            Activate();
            Keyboard.Focus(SearchBox);
        }
    }

    static SnippetItem? ItemAt(ListBox list, object source) =>
        list.ContainerFromElement((DependencyObject)source) is ListBoxItem { DataContext: SnippetItem item } ? item : null;

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        // Pfeile nach links/rechts gehören dem Suchfeld, sobald etwas drinsteht
        var browsing = _viewModel.Query.Length == 0;
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
            // Alt+1 bis Alt+9 wie ein Klick auf die Zeile mit der Nummer, mit Shift die andere Einfügemethode.
            // AltGr meldet sich als Strg+Alt und tippt weiter ins Suchfeld ({, [, ] …).
            case Key.System when (Keyboard.Modifiers & ~ModifierKeys.Shift) == ModifierKeys.Alt &&
                                 e.SystemKey is >= Key.D1 and <= Key.D9:
                if (_viewModel.SelectNumber(e.SystemKey - Key.D0))
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
            case Key.N when ctrl:
                Hide();
                _app.NewSnippet(_viewModel.CurrentFolder);
                break;
            case Key.E when ctrl:
                RevealSelected();
                break;
            case Key.OemComma when ctrl:
                OpenSettings();
                break;
            // Wie im Explorer
            case Key.F2 when selected is not null:
                Rename(selected);
                break;
            default:
                e.Handled = false;
                break;
        }
    }

    void ActivateSelected(bool alternate)
    {
        var snippet = _viewModel.Selected?.Snippet;
        if (snippet is null) return;

        if (snippet.IsFolder)
            _viewModel.EnterFolder(snippet);
        else
            Paste(snippet, alternate);
    }

    void ActivateDetail(Snippet snippet, bool alternate)
    {
        if (snippet.IsFolder)
            _viewModel.EnterFolder(snippet);
        else
            Paste(snippet, alternate);
    }

    async void Paste(Snippet snippet, bool alternate)
    {
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
