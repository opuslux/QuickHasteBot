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
    int _resizeStartY;
    double _resizeStartHeight;
    double _resizeStartTop;
    double _resizeMinHeight;
    bool _heightChanged;
    ForegroundWatcher? _foregroundWatcher;
    bool _running;
    bool _closed;

    public SearchPopup(AppController app)
    {
        InitializeComponent();
        _app = app;
        DataContext = _viewModel = new SearchPopupViewModel(app);
        ApplyListWidth();
        KeepOpen = app.Settings.KeepPopupOpen;
        _hoverTimer = new DispatcherTimer(AimDelay, DispatcherPriority.Input, OnHoverTimer, Dispatcher) { IsEnabled = false };
        // Neuer Inhalt rechts beginnt oben links
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SearchPopupViewModel.InDetail))
                ShowKeyboardColumn();
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
        Closed += (_, _) =>
        {
            _closed = true;
            _foregroundWatcher?.Dispose();
        };
        // Die Höhe folgt der Liste (ohne gespeicherte Höhe), dabei soll das Fenster am Mauszeiger bleiben
        SizeChanged += (_, _) =>
        {
            if (_anchor is { } anchor)
                PopupPlacement.KeepAnchored(this, anchor);
        };
    }

    /// <summary>Wohin das offene Popup einfügen würde. Ein Datei-Hotkey bei offenem Popup fügt auch dort ein.</summary>
    public nint TargetWindow => _targetWindow;

    /// <summary>
    /// Das Schloss: Bekommt ein anderes Fenster den Fokus, schliesst das Popup nicht. Sonst nichts: Es ist dann ein
    /// normales Fenster (nicht Topmost), das Windows wie jedes andere nach hinten und vorne holt. Esc schliesst es
    /// trotzdem. Gespeichert (<see cref="AppSettings.KeepPopupOpen"/>).
    /// </summary>
    public bool KeepOpen
    {
        get => KeepOpenToggle.IsChecked == true;
        private set => KeepOpenToggle.IsChecked = value;
    }

    /// <param name="targetWindow">Fenster, das beim Drücken des Hotkeys vorne war, dorthin wird eingefügt.</param>
    /// <param name="folder">Mit dem Hotkey eines Ordners: in diesem Ordner öffnen statt wie eingestellt.</param>
    public void ShowFor(nint targetWindow, Snippet? folder = null)
    {
        // Offen gelassen (Hotkey eines Ordners): dort weitermachen, wo das Popup steht
        var keptOpen = KeepOpen && IsVisible;
        Topmost = !KeepOpen;

        _targetWindow = targetWindow;
        _viewModel.Reset(folder?.RelativePath);
        ApplyWidth();
        ApplyHeight();

        if (!keptOpen)
        {
            _anchor = null;
            var (fractionX, fractionY) = AppSettings.PopupFractions(_app.Settings.PopupPosition);
            _anchor = PopupPlacement.ShowAtCursor(this, fractionX, fractionY);
            // Das Fenster erscheint unter der ruhenden Maus, das ist noch keine Bewegung
            _lastMouse = PointToScreen(Mouse.GetPosition(this));
            _recentMoves.Clear();
        }
        FocusSearch();
    }

    /// <summary>Hotkey oder Klick aufs Tray-Symbol bei offen gelassenem Popup: nach vorne und zurück ins Suchfeld, Ordner und Stelle bleiben.</summary>
    /// <param name="foreground">
    /// Das Fenster, das beim Drücken vorne war. Ist es ein anderes Programm, fügt das Popup jetzt dort ein. 0 lässt das Ziel.
    /// </param>
    public void Refocus(nint foreground)
    {
        // Läuft gerade ein Baustein, kommt das Popup danach von selbst wieder
        if (!IsVisible) return;
        if (foreground != 0 && !IsOwnWindow(foreground))
            _targetWindow = foreground;
        FocusSearch();
    }

    void FocusSearch()
    {
        Activate();
        WindowFocus.Activate(new WindowInteropHelper(this).Handle);
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        // Ein gemerkter Suchtext ist markiert: Enter nimmt ihn, Tippen ersetzt ihn
        SearchBox.SelectAll();
    }

    static bool IsOwnWindow(nint window)
    {
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        return processId == Environment.ProcessId;
    }

    void OnKeepOpenChanged(object sender, RoutedEventArgs e)
    {
        _foregroundWatcher?.Dispose();
        _foregroundWatcher = KeepOpen ? new ForegroundWatcher(OnForegroundChanged, ownProcess: true) : null;
        // Offen gelassen ein normales Fenster. Wird es gerade umgeschaltet, ist es aktiv und bleibt so vorne.
        Topmost = !KeepOpen;
        if (_app.Settings.KeepPopupOpen != KeepOpen)
        {
            _app.Settings.KeepPopupOpen = KeepOpen;
            _app.SaveSettings();
        }
        if (IsActive)
            Keyboard.Focus(SearchBox);
    }

    // Klickt man bei offen gelassenem Popup in ein anderes Programm, fügt der nächste Baustein dort ein. Was ein
    // laufender Baustein nach vorne holt ({window}, {start}), zählt nicht. Die Reihenfolge der Fenster bleibt Windows überlassen.
    internal void OnForegroundChanged(nint window)
    {
        if (IsVisible && !_running && !IsOwnWindow(window))
            _targetWindow = window;
    }

    /// <summary>
    /// Versteckt das Popup, solange ein Baustein läuft: Das Zielfenster bekommt die Eingaben und {click} trifft nicht
    /// das Popup. Offen gelassen ist es danach wieder vorne an derselben Stelle, ohne dem Zielfenster den Fokus zu nehmen.
    /// </summary>
    public async Task RunHiddenAsync(Func<Task> run)
    {
        var keepOpen = KeepOpen;
        _running = true;
        Hide();
        try
        {
            await run();
        }
        finally
        {
            _running = false;
            if (keepOpen && KeepOpen && !_closed && !IsVisible)
            {
                ShowActivated = false;
                try
                {
                    Show();
                }
                finally
                {
                    ShowActivated = true;
                }
                // Nicht Topmost: Ohne Aktivieren käme es nicht von selbst vor das Zielfenster
                NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, NativeMethods.HWND_TOP, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
        }
    }

    /// <summary>Bearbeiten, Explorer, Einstellungen: offen gelassen bleibt das Popup dabei stehen.</summary>
    void HideUnlessKeptOpen()
    {
        if (!KeepOpen)
            Hide();
    }

    /// <summary>Die Bibliothek wurde neu eingelesen (Datei geändert, anderer Ordner).</summary>
    /// <summary>Am Icon einer Zeile: wie oft benutzt, zuletzt wann, gelernte Suchtexte und ob der Suchtext ihn hochholt.</summary>
    void OnIconToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SnippetItem item } icon) return;
        if (UsageTooltip(item) is { } text)
            icon.ToolTip = text;
        else
            e.Handled = true;
    }

    internal string? UsageTooltip(SnippetItem item) =>
        UsageText.Tooltip(_app.SnippetData.StatsFor(item.Snippet.RelativePath), _viewModel.IsSearching ? _viewModel.Query : null, DateTime.UtcNow);

    public void OnLibraryChanged()
    {
        if (IsVisible)
            _viewModel.Refresh();
    }

    void OnDeactivated(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        if (!_dialogOpen && !KeepOpen)
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

        // Bis v2.15 war die Liste ein Anteil der Breite: einmal mit dieser Breite umrechnen, dann sieht es aus wie vorher
        if (_app.Settings.ListColumnShare is { } share)
        {
            var columns = Width - BorderThickness.Left - BorderThickness.Right - Body.Margin.Left - Body.Margin.Right - ColumnSplitter.Width;
            _app.Settings.ListColumnWidth = Math.Round(AppSettings.ClampColumnShare(share) * columns);
            _app.Settings.ListColumnShare = null;
            _app.SaveSettings();
            ApplyListWidth();
        }
    }

    /// <summary>
    /// Gespeicherte Höhe, sonst so hoch wie die Liste. Nach oben begrenzt <see cref="PopupPlacement"/> auf den
    /// Bildschirm, die gespeicherte Höhe bleibt dabei.
    /// </summary>
    internal void ApplyHeight()
    {
        if (_app.Settings.PopupHeight is { } height && double.IsFinite(height))
        {
            SizeToContent = SizeToContent.Manual;
            // Von Hand zu klein eingetragen: noch Platz für Suchfeld, Fusszeile und die Mindesthöhe der Spalten
            Height = Math.Max(height, MinFixedHeight);
        }
        else
        {
            ClearValue(HeightProperty);
            SizeToContent = SizeToContent.Height;
        }
    }

    // Was über und unter den Spalten liegt, plus deren Mindesthöhe. Vor dem ersten Anzeigen ist das noch nicht gemessen.
    double MinFixedHeight => Columns.ActualHeight > 0 && ActualHeight > Columns.ActualHeight
        ? ActualHeight - Columns.ActualHeight + Columns.MinHeight
        : PopupPlacement.MinHeight;

    // Höhe am oberen und unteren Rand ziehen. Erst eine echte Bewegung legt die Höhe fest, ein Klick (Doppelklick
    // zum Zurücksetzen) nicht. Höchstens bis zum Rand des Arbeitsbereichs.
    void OnHeightResizeStarted(object sender, DragStartedEventArgs e)
    {
        _resizeAnchor = _anchor;
        _anchor = null;
        NativeMethods.GetCursorPos(out var cursor);
        _resizeStartY = cursor.Y;
        _resizeStartHeight = ActualHeight;
        _resizeStartTop = Top;
        _resizeMinHeight = Math.Min(ActualHeight, MinFixedHeight);
        _heightChanged = false;
    }

    void OnHeightResizeDelta(object sender, DragDeltaEventArgs e)
    {
        // Bildschirmkoordinaten wie bei der Breite: Der obere Rand wandert beim Ziehen mit dem Fenster mit
        NativeMethods.GetCursorPos(out var cursor);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        var delta = (cursor.Y - _resizeStartY) / scale;
        if (!_heightChanged && Math.Abs(delta) < 1) return;

        var (workTop, workBottom) = _resizeAnchor is { } anchor
            ? (anchor.Work.Top / scale, anchor.Work.Bottom / scale)
            : (SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom);
        var fromTop = sender == TopEdge;
        var max = fromTop ? _resizeStartTop + _resizeStartHeight - workTop : workBottom - _resizeStartTop;
        var height = Math.Clamp(fromTop ? _resizeStartHeight - delta : _resizeStartHeight + delta,
                                _resizeMinHeight, Math.Max(_resizeMinHeight, max));

        if (!_heightChanged)
        {
            _heightChanged = true;
            SizeToContent = SizeToContent.Manual;
            // Die Grenze vom Öffnen gilt nur in eine Richtung, nach dem Ziehen setzt PopupPlacement.Moved sie neu
            MaxHeight = double.PositiveInfinity;
        }
        if (fromTop)
            Top = _resizeStartTop + _resizeStartHeight - height;
        Height = height;
    }

    void OnHeightResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_resizeAnchor is { } previous)
            _anchor = PopupPlacement.Moved(this, previous);
        _resizeAnchor = null;

        if (_heightChanged)
        {
            _heightChanged = false;
            UpdateLayout();
            _app.Settings.PopupHeight = Math.Round(ActualHeight);
            _app.SaveSettings();
        }
        Keyboard.Focus(SearchBox);
    }

    // Doppelklick auf den oberen oder unteren Rand: wieder so hoch wie die Liste, das Fenster bleibt am Anker
    void OnHeightReset(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var anchor = _anchor ?? _resizeAnchor;
        _app.Settings.PopupHeight = null;
        _app.SaveSettings();
        ApplyHeight();
        UpdateLayout();
        if (anchor is { } kept)
            PopupPlacement.KeepAnchored(this, kept);
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

    /// <summary>
    /// Die Liste links so breit wie gespeichert, rechts bekommt den Rest. Ist das Popup zu schmal dafür, wird sie nur
    /// in der Anzeige schmaler, damit rechts die Mindestbreite bleibt; breiter gezogen kommt die gespeicherte Breite zurück.
    /// </summary>
    internal void ApplyListWidth()
    {
        var width = AppSettings.ClampListColumnWidth(_app.Settings.ListColumnWidth, ListColumn.MinWidth);
        if (Columns.ActualWidth > 0)
            width = Math.Min(width, Math.Max(ListColumn.MinWidth, Columns.ActualWidth - ColumnSplitter.Width - DetailColumn.MinWidth));
        ListColumn.Width = new GridLength(width);
        DetailColumn.Width = new GridLength(1, GridUnitType.Star);
    }

    void OnColumnsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
            ApplyListWidth();
    }

    // Die neue Breite links gilt ab jetzt bei jedem Öffnen
    void OnSplitterDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (ListColumn.ActualWidth > 0)
        {
            _app.Settings.ListColumnWidth = Math.Round(ListColumn.ActualWidth);
            _app.SaveSettings();
            ApplyListWidth();
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

    /// <summary>
    /// Links und rechts kann je eine Zeile markiert sein. In Akzentfarbe nur die, wo die Tastatur ist (Pfeile, Enter,
    /// F2 …), die andere grau. Rechts ist ohne Tastatur nichts markiert, deshalb betrifft es nur die linke Liste.
    /// </summary>
    void ShowKeyboardColumn()
    {
        const string selected = "ListBoxItemSelectedBackgroundThemeBrush";
        const string selectedPointerOver = "ListBoxItemSelectedBackgroundPointerOverThemeBrush";
        if (_viewModel.InDetail)
        {
            Results.Resources[selected] = FindResource("InactiveSelectionBrush");
            Results.Resources[selectedPointerOver] = FindResource("InactiveSelectionPointerOverBrush");
        }
        else
        {
            Results.Resources.Remove(selected);
            Results.Resources.Remove(selectedPointerOver);
        }
    }

    void OnDetailSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DetailList.SelectedItem is not null)
            DetailList.ScrollIntoView(DetailList.SelectedItem);
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
        else
            LeaveDetailOnEmptyClick(e);
    }

    // Ein Klick neben die Einträge hebt die Markierung rechts auf (etwa die vom Rechtsklick), wie die Pfeiltaste nach links
    void LeaveDetailOnEmptyClick(MouseButtonEventArgs e)
    {
        if (!IsInside<ScrollBar>(e.OriginalSource))
            _viewModel.LeaveDetail();
    }

    void OnResultsMouseUp(object sender, MouseButtonEventArgs e) =>
        OnListMouseUp(Results, e, _ => ActivateSelected(alternate: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)));

    void OnResultsDoubleClick(object sender, MouseButtonEventArgs e) =>
        OnListDoubleClick(Results, e, _ => ActivateSelected(alternate: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)));

    // Rechts wird der Eintrag markiert wie links, die Auswahl links bleibt, wie sie ist. Mit Einzelklick fügt
    // das Loslassen ihn dann direkt ein bzw. öffnet den Ordner.
    void OnDetailMouseDown(object sender, MouseButtonEventArgs e)
    {
        var item = ItemAt(DetailList, e.OriginalSource);
        _pressedItem = e.ClickCount == 1 ? item : null;
        if (item is null)
            LeaveDetailOnEmptyClick(e);
        else
            _viewModel.DetailSelected = item;
    }

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
        HideUnlessKeptOpen();
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

    // Ohne Auswahl gilt der angezeigte Ordner. Ist die Tastatur rechts, der Eintrag dort.
    void EditSelected()
    {
        HideUnlessKeptOpen();
        _app.EditSnippet(_viewModel.CurrentSnippet ?? _viewModel.CurrentFolder);
    }

    void RevealSelected()
    {
        HideUnlessKeptOpen();
        _app.RevealInExplorer(_viewModel.CurrentSnippet ?? _viewModel.CurrentFolder);
    }

    void OpenSettings()
    {
        HideUnlessKeptOpen();
        _app.ShowSettings();
    }

    // Rechtsklick auf einen Eintrag, links wie rechts: Er wird markiert, damit man sieht, wofür das Menü gilt. Rechts
    // wie mit der Pfeiltaste nach rechts, der Ordner links bleibt markiert.
    // Auf die freie Fläche: neuer Baustein oder Ordner in dem Ordner, den die Spalte zeigt, sonst käme man in einem leeren Ordner nicht dazu.
    void OnListRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var list = (ListBox)sender;
        _hoverTimer.Stop();
        if (ItemAt(list, e.OriginalSource) is not { } item)
        {
            // Das Menü gilt dem Ordner, keinem Eintrag
            _viewModel.LeaveDetail();
            ShowFolderMenu(list);
            e.Handled = true;
            return;
        }

        if (list == Results)
            _viewModel.Selected = item;
        else
            _viewModel.DetailSelected = item;
        ShowItemMenu(item.Snippet, list);
        e.Handled = true;
    }

    /// <summary>
    /// Oben als Symbole Kopieren, Ausschneiden, Einfügen, Umbenennen, Löschen. Darunter für Textbausteine Bearbeiten,
    /// für alle Im Explorer anzeigen, Neuer Baustein, Neuer Ordner, Hotkey festlegen. Geklont wird mit Kopieren und
    /// Einfügen im selben Ordner.
    /// </summary>
    void ShowItemMenu(Snippet snippet, UIElement target)
    {
        var menu = ItemMenu(snippet);
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    internal ContextMenu ItemMenu(Snippet snippet)
    {
        var menu = new ContextMenu();
        // Neuer Baustein, Ordner und Einfügen im Ordner unter der Maus, bei einer Datei daneben
        var parent = snippet.IsFolder ? snippet : snippet.Parent ?? _viewModel.CurrentFolder;
        menu.Items.Add(MenuIconRow.Create(menu,
            [
                new(MenuIcons.Copy, Strings.MenuCopy, true, () => _viewModel.Copy(snippet)),
                new(MenuIcons.Cut, Strings.MenuCut, true, () => _viewModel.Cut(snippet)),
                PasteCommand(parent),
                new(MenuIcons.Rename, Strings.MenuRename, true, AfterMenu(() => Rename(snippet))),
                // Ordner mit allem Inhalt, die Rückfrage sagt es
                new(MenuIcons.Delete, Strings.MenuDelete, true, AfterMenu(() => Delete(snippet))),
            ]));
        menu.Items.Add(new Separator());
        // Alles andere öffnet schon per Klick die zugeordnete Anwendung
        if (snippet.Kind == SnippetKind.Text)
        {
            menu.Items.Add(MenuEntry(Strings.MenuEdit, MenuIcons.Edit, () =>
            {
                HideUnlessKeptOpen();
                _app.EditSnippet(snippet);
            }));
            if (_app.Settings.UseBuiltInEditor)
                menu.Items.Add(MenuEntry(Strings.MenuEditExternal, MenuIcons.OpenExternal, () =>
                {
                    HideUnlessKeptOpen();
                    _app.Edit(snippet);
                }));
        }
        // Wie Strg+E, aber für den Eintrag unter der Maus: Dateien wie Ordner markiert im übergeordneten Ordner
        menu.Items.Add(MenuEntry(Strings.MenuShowInExplorer, MenuIcons.Explorer, () =>
        {
            HideUnlessKeptOpen();
            _app.RevealInExplorer(snippet);
        }));
        menu.Items.Add(MenuEntry(Strings.MenuNewSnippet, MenuIcons.NewSnippet, () =>
        {
            HideUnlessKeptOpen();
            _app.NewSnippet(parent);
        }));
        menu.Items.Add(MenuEntry(Strings.MenuNewFolder, MenuIcons.NewFolder, () => NewFolder(parent)));
        menu.Items.Add(MenuEntry(Strings.MenuSetHotkey, MenuIcons.Hotkey, () => SetHotkey(snippet)));
        return menu;
    }

    /// <summary>
    /// Rechtsklick neben die Einträge: was in dem Ordner neu entstehen kann, dessen Inhalt die Spalte zeigt. Links der
    /// offene Ordner, rechts der links markierte.
    /// </summary>
    void ShowFolderMenu(ListBox list) =>
        ShowFolderMenu(list, list == DetailList && _viewModel.Selected?.Snippet is { IsFolder: true } shown ? shown : _viewModel.CurrentFolder);

    // Rechts bei einem leeren Ordner steht statt der Liste nur der Hinweis: auch dort neu anlegen können
    void OnDetailTextRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.Selected?.Snippet is not { IsFolder: true } folder) return;
        ShowFolderMenu((UIElement)sender, folder);
        e.Handled = true;
    }

    void ShowFolderMenu(UIElement target, Snippet parent)
    {
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.MousePoint };
        menu.Items.Add(MenuIconRow.Create(menu, [PasteCommand(parent)]));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuEntry(Strings.MenuNewSnippet, MenuIcons.NewSnippet, () =>
        {
            HideUnlessKeptOpen();
            _app.NewSnippet(parent);
        }));
        menu.Items.Add(MenuEntry(Strings.MenuNewFolder, MenuIcons.NewFolder, () => NewFolder(parent)));
        menu.IsOpen = true;
    }

    /// <summary>Neuer Ordner in diesem Ordner, danach markiert, links oder rechts.</summary>
    void NewFolder(Snippet parent)
    {
        var display = string.Join(" › ", new[] { Strings.BreadcrumbRoot }
            .Concat(parent.RelativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)));
        var dialog = NameDialog.ForNewFolder(parent.FullPath, display);
        dialog.Owner = this;
        if (WithDialog(dialog.ShowDialog) == true)
            ChangeFile(parent, Strings.NewFolderErrorFormat, () => SnippetFiles.CreateFolder(parent.FullPath, dialog.FileName));
    }

    // Erst ausführen, wenn das Menü zu ist, sonst steht es hinter dem Dialog noch offen
    MenuItem MenuEntry(string header, string glyph, Action action)
    {
        var item = new MenuItem { Header = header, Icon = MenuIcons.Glyph(glyph) };
        item.Click += (_, _) => AfterMenu(action)();
        return item;
    }

    Action AfterMenu(Action action) => () => Dispatcher.BeginInvoke(action, DispatcherPriority.Input);

    /// <summary>Einfügen in diesen Ordner, ausgegraut, wenn nichts ausgeschnitten oder kopiert ist oder es dort nicht hingeht. Der Tooltip sagt, was.</summary>
    MenuIconCommand PasteCommand(Snippet folder)
    {
        var clipped = _viewModel.Clipped;
        var name = clipped is null ? Strings.MenuPaste : string.Format(Strings.MenuPasteFormat, clipped.Name);
        return new(MenuIcons.Paste, name, _viewModel.CanPasteInto(folder), AfterMenu(() => Paste(folder)));
    }

    /// <summary>
    /// Ausgeschnittenes wird verschoben, mit Hotkey und Nutzung; gibt es den Namen dort schon, bleibt alles, wie es ist.
    /// Kopiertes bekommt dann wie im Explorer "Name - Kopie", im selben Ordner ist das ein Klon. Die Kopie hat keinen
    /// Hotkey, der steht beim Pfad des Originals. Danach ist das Eingefügte markiert, wenn es zu sehen ist.
    /// </summary>
    void Paste(Snippet folder)
    {
        if (_viewModel.Clipped is not { } clipped) return;
        var name = Path.GetFileName(clipped.FullPath);
        var exists = SnippetFiles.Check(folder.FullPath, name) == NameProblem.Exists;

        if (!_viewModel.ClippedIsCut)
        {
            if (exists)
                name = SnippetFiles.SuggestCopyName(clipped.FullPath, Strings.CopySuffix, folder.FullPath);
            ChangeFile(clipped, Strings.CopyErrorFormat, () => SnippetFiles.Copy(clipped.FullPath, folder.FullPath, name));
            return;
        }

        if (exists)
        {
            WithDialog(() => MessageDialog.Show(this, string.Format(Strings.PasteExistsFormat, name), MessageBoxButton.OK, MessageBoxImage.Warning));
            return;
        }
        ChangeFile(clipped, Strings.MoveErrorFormat, () =>
        {
            var path = SnippetFiles.Move(clipped.FullPath, folder.FullPath, name);
            _app.SnippetMoved(clipped.FullPath, path);
            _viewModel.ClearClipped();
            return path;
        });
    }

    void Rename(Snippet snippet)
    {
        var dialog = NameDialog.ForRename(snippet);
        dialog.Owner = this;
        if (WithDialog(dialog.ShowDialog) == true)
            ChangeFile(snippet, Strings.RenameErrorFormat, () =>
            {
                var path = SnippetFiles.Rename(snippet.FullPath, dialog.FileName);
                _app.SnippetMoved(snippet.FullPath, path);
                if (_app.Library.RelativeOf(path) is { } renamed)
                    _viewModel.PathMoved(snippet.RelativePath, renamed);
                return path;
            });
    }

    /// <summary>Der Hotkey steht in snippets.json, der Name bleibt.</summary>
    void SetHotkey(Snippet snippet)
    {
        var dialog = new HotkeyDialog(_app, snippet) { Owner = this };
        if (WithDialog(dialog.ShowDialog) != true) return;
        _app.SetFileHotkey(snippet.RelativePath, dialog.NewGesture);
        _viewModel.RefreshSelecting(snippet.RelativePath);
    }

    /// <summary>Kopieren, verschieben, umbenennen oder neu anlegen, danach ist das Ergebnis markiert, sofern es in der Liste steht.</summary>
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
        var question = snippet.IsFolder ? Strings.DeleteFolderConfirmFormat : Strings.DeleteConfirmFormat;
        var answer = WithDialog(() => MessageDialog.Show(this, string.Format(question, name),
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No));
        if (answer != MessageBoxResult.Yes) return;

        var nextSelection = _viewModel.SelectionAfterRemoving(snippet);
        try
        {
            if (!WithDialog(() => RecycleBin.Delete(snippet.FullPath, new WindowInteropHelper(this).Handle))) return;
            _app.SnippetDeleted(snippet);
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
        // Pfeile nach links/rechts gehören dem Suchfeld, sobald etwas drinsteht. Nur am Ende des Suchtexts (leer: immer)
        // führt der Pfeil nach rechts in den Ordnerinhalt rechts, dort zurück der Pfeil nach links.
        var browsing = _viewModel.Query.Length == 0;
        var inDetail = _viewModel.InDetail;
        var selected = _viewModel.CurrentSnippet;

        e.Handled = true;
        switch (e.Key)
        {
            // Erst den Suchtext löschen (etwa einen gemerkten), das zweite Escape schliesst
            case Key.Escape when _viewModel.Query.Length > 0:
                SearchBox.Text = "";
                break;
            case Key.Escape:
                Hide();
                break;
            case Key.T when ctrl:
                KeepOpen = !KeepOpen;
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
            case Key.Enter when inDetail:
                ActivateDetail(_viewModel.DetailSelected!.Snippet, alternate: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
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
            // Wie in einer Spaltenansicht: Ein Ordner rechts lässt die Spalten weiterrücken
            case Key.Right when inDetail:
                if (selected is { IsFolder: true })
                    _viewModel.ShiftColumns(selected);
                break;
            case Key.Left when inDetail:
                _viewModel.LeaveDetail();
                break;
            // Mit und ohne Suchtext gleich: in den Ordnerinhalt rechts, geöffnet wird nichts
            case Key.Right when _viewModel.ShowDetailItems && IsCaretAtEnd():
                // Ein markierter Suchtext bleibt stehen, Weitertippen hängt an statt zu ersetzen
                SearchBox.Select(SearchBox.Text.Length, 0);
                _viewModel.EnterDetail();
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
                HideUnlessKeptOpen();
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

    // Auch wenn der Text bis zum Ende markiert ist: Der Pfeil würde die Schreibmarke dort nur noch ans Ende setzen
    bool IsCaretAtEnd() => SearchBox.SelectionStart + SearchBox.SelectionLength == SearchBox.Text.Length;

    void ActivateSelected(bool alternate)
    {
        var snippet = _viewModel.Selected?.Snippet;
        if (snippet is null) return;

        // Aus den Suchergebnissen gewählt: zählt auch für diesen Suchtext. Ordner nur dann, Durchklicken zählt nicht.
        var query = _viewModel.IsSearching ? _viewModel.Query : null;
        if (snippet.IsFolder)
        {
            if (query is not null)
                _app.RecordUse(snippet, UseSource.Search, query);
            _viewModel.EnterFolder(snippet);
        }
        else
            Paste(snippet, alternate, query is null ? UseSource.Browse : UseSource.Search, query);
    }

    // Rechte Spalte: der Inhalt eines Ordners, auch bei einer Suche nur durchgeblättert
    void ActivateDetail(Snippet snippet, bool alternate)
    {
        if (snippet.IsFolder)
            _viewModel.EnterFolder(snippet);
        else
            Paste(snippet, alternate, UseSource.Browse, query: null);
    }

    async void Paste(Snippet snippet, bool alternate, UseSource source, string? query)
    {
        try
        {
            await RunHiddenAsync(() => _app.ActivateAsync(snippet, alternate, _targetWindow, source, query));
        }
        catch (Exception ex)
        {
            Log.Error($"Einfügen von {snippet.RelativePath} fehlgeschlagen", ex);
        }
    }
}
