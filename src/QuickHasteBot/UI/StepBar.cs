using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using QuickHasteBot.Core;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.UI;

/// <summary>
/// Leiste für den Schritt-für-Schritt-Lauf, unten mittig am Zielfenster. Wird nie aktiv (WS_EX_NOACTIVATE, ein Klick
/// beantwortet WM_MOUSEACTIVATE mit MA_NOACTIVATE), damit Tasten und Fokus im Zielfenster bleiben. Zeigt den Schritt,
/// der als Nächstes läuft, und den danach. Am Griff links verschiebbar, danach bleibt sie, wo man sie hingezogen hat.
/// </summary>
internal sealed class StepBar : Window
{
    /// <summary>Abstand zum unteren Rand des Zielfensters, physische Pixel.</summary>
    const int BottomMargin = 24;
    const int ShadowMargin = 8;
    const int MaxStepLength = 70;

    readonly TextBlock _title = new() { FontWeight = FontWeights.SemiBold, FontSize = 13 };
    readonly TextBlock _state = new() { FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _now = Code();
    readonly TextBlock _then = Code();
    readonly TextBlock _message = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };
    readonly TextBlock _note = new() { FontSize = 11, Opacity = 0.8, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };
    readonly Grid _steps = new() { Margin = new Thickness(0, 6, 0, 0) };
    readonly StackPanel _runButtons = new() { Orientation = Orientation.Horizontal };
    readonly StackPanel _endButtons = new() { Orientation = Orientation.Horizontal };
    readonly Button _next;
    readonly Button _continue;
    TaskCompletionSource<bool>? _choice;
    bool _moved;
    POINT _dragStart;
    RECT _dragWindow;

    public StepBar()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Title = Strings.StepBarTitle;

        _next = Button(Strings.StepBarNext + "  F10", () => NextClicked?.Invoke(this, EventArgs.Empty), accent: true);
        _continue = Button(Strings.StepBarContinue + "  F5", () => ContinueClicked?.Invoke(this, EventArgs.Empty));
        _runButtons.Children.Add(_next);
        _runButtons.Children.Add(_continue);
        _runButtons.Children.Add(Button(Strings.StepBarStop, () => StopClicked?.Invoke(this, EventArgs.Empty)));
        _endButtons.Children.Add(Button(Strings.StepBarAgain, () => _choice?.TrySetResult(true), accent: true));
        _endButtons.Children.Add(Button(Strings.StepBarClose, () => _choice?.TrySetResult(false)));
        _endButtons.Visibility = Visibility.Collapsed;

        _steps.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _steps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _steps.RowDefinitions.Add(new RowDefinition());
        _steps.RowDefinitions.Add(new RowDefinition());
        AddRow(0, Strings.StepBarNow, _now);
        AddRow(1, Strings.StepBarThen, _then);

        var header = new DockPanel { LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), Children = { _runButtons, _endButtons } };
        DockPanel.SetDock(buttons, Dock.Right);
        header.Children.Add(buttons);
        header.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _title, _state } });

        var grip = new TextBlock
        {
            Text = "⋮⋮", FontSize = 14, Opacity = 0.6, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top,
            Cursor = Cursors.SizeAll, ToolTip = Strings.StepBarDrag,
        };
        var body = new StackPanel { Children = { header, _steps, _message, _note } };
        _message.Visibility = Visibility.Collapsed;
        _note.Visibility = Visibility.Collapsed;
        var row = new DockPanel { Children = { grip, body } };

        var card = new Border
        {
            Margin = new Thickness(ShadowMargin),
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            MinWidth = 420,
            Child = row,
            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.35 },
            Cursor = Cursors.SizeAll,
        };
        card.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        card.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
        card.MouseLeftButtonDown += StartDrag;
        card.MouseMove += Drag;
        card.MouseLeftButtonUp += (_, _) => card.ReleaseMouseCapture();
        Content = card;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)(style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
            HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
        };
    }

    public event EventHandler? NextClicked;
    public event EventHandler? ContinueClicked;
    public event EventHandler? StopClicked;

    /// <summary>Pausiert vor Schritt <paramref name="index"/> oder führt ihn gerade aus.</summary>
    public void ShowStep(IReadOnlyList<SourcedStep> steps, bool paste, int index, bool paused)
    {
        _choice = null;
        _runButtons.Visibility = Visibility.Visible;
        _endButtons.Visibility = Visibility.Collapsed;
        _steps.Visibility = Visibility.Visible;
        _message.Visibility = Visibility.Collapsed;
        _next.IsEnabled = _continue.IsEnabled = paused;

        _title.Text = string.Format(Strings.StepBarStepFormat, Math.Min(index + 1, steps.Count), steps.Count);
        _state.Text = paused ? Strings.StepBarPaused : Strings.StepBarRunning;
        _now.Text = index < steps.Count ? StepText(steps[index].Step, paste) : Strings.StepBarEnd;
        _then.Text = index + 1 < steps.Count ? StepText(steps[index + 1].Step, paste) : Strings.StepBarEnd;
    }

    /// <summary>Unter den Schritten, z.B. welche Tasten belegt sind oder dass das Feld noch fehlt. Null blendet aus.</summary>
    public void ShowNote(string? note)
    {
        _note.Text = note ?? "";
        _note.Visibility = note is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Ende oder Fehler, mit „Nochmal“ und „Schliessen“.</summary>
    public void ShowResult(string title, string? message, bool failed)
    {
        _runButtons.Visibility = Visibility.Collapsed;
        _endButtons.Visibility = Visibility.Visible;
        _steps.Visibility = Visibility.Collapsed;
        _title.Text = title;
        _state.Text = "";
        _message.Text = message ?? "";
        _message.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        if (failed)
            _title.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        else
            _title.ClearValue(TextBlock.ForegroundProperty);
        ShowNote(null);
    }

    /// <summary>Nach <see cref="ShowResult"/>: true für „Nochmal“, false für „Schliessen“.</summary>
    public Task<bool> WaitForChoiceAsync()
    {
        _choice = new TaskCompletionSource<bool>();
        return _choice.Task;
    }

    /// <summary>Wie ein Klick auf „Nochmal“ oder „Schliessen“, falls gerade danach gefragt wird.</summary>
    public void Choose(bool again) => _choice?.TrySetResult(again);

    /// <summary>Vor dem ersten Mal nicht sichtbar, ohne das Zielfenster zu deaktivieren.</summary>
    public void ShowAt(nint window)
    {
        if (!IsVisible)
        {
            (Left, Top) = (-32000, -32000);
            Show();
        }
        UpdateLayout();
        Place(window);
    }

    /// <summary>Unten mittig ans Fenster, ausser man hat die Leiste selbst verschoben.</summary>
    public void Place(nint window)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (_moved || hwnd == 0) return;
        var root = window == 0 ? 0 : GetAncestor(window, GA_ROOT);
        if (root == 0 || !GetWindowRect(root, out var target) || !GetWindowRect(hwnd, out var bar)) return;

        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromWindow(root, MONITOR_DEFAULTTONEAREST), ref info);
        var (x, y) = Position(target, info.rcWork, bar.Right - bar.Left, bar.Bottom - bar.Top);
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    /// <summary>Unten mittig im Zielfenster, aber immer ganz im Arbeitsbereich des Bildschirms.</summary>
    internal static (int X, int Y) Position(RECT target, RECT work, int width, int height)
    {
        var x = (target.Left + target.Right) / 2 - width / 2;
        var y = Math.Min(target.Bottom, work.Bottom) - height - BottomMargin + ShadowMargin;
        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));
        return (x, y);
    }

    /// <summary>Ein Schritt in einer Zeile: Tags wie im Baustein, Text in «», ein Baustein ohne Tags als „Einfügen“.</summary>
    internal static string StepText(FormStep step, bool paste)
    {
        var text = step is TextStep t ? "«" + t.Text.ReplaceLineEndings("⏎") + "»" : KeyTags.Describe(step).ReplaceLineEndings("⏎");
        if (text.Length > MaxStepLength)
            text = text[..(MaxStepLength - 2)] + "…" + text[^1];
        return paste ? Strings.StepBarPaste + " " + text : text;
    }

    /// <summary>Ein Klick in die Leiste aktiviert sie nicht, das Zielfenster bleibt vorne.</summary>
    internal static nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WM_MOUSEACTIVATE) return 0;
        handled = true;
        return MA_NOACTIVATE;
    }

    void StartDrag(object sender, MouseButtonEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!GetCursorPos(out _dragStart) || !GetWindowRect(hwnd, out _dragWindow)) return;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    void Drag(object sender, MouseEventArgs e)
    {
        if (!((UIElement)sender).IsMouseCaptured || !GetCursorPos(out var cursor)) return;
        _moved = true;
        SetWindowPos(new WindowInteropHelper(this).Handle, HWND_TOPMOST,
            _dragWindow.Left + cursor.X - _dragStart.X, _dragWindow.Top + cursor.Y - _dragStart.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    void AddRow(int row, string label, TextBlock value)
    {
        var caption = new TextBlock { Text = label, FontSize = 12, Opacity = 0.75, Margin = new Thickness(0, 2, 10, 2), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(caption, row);
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        _steps.Children.Add(caption);
        _steps.Children.Add(value);
    }

    static TextBlock Code() => new()
    {
        FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, Margin = new Thickness(0, 2, 0, 2),
        TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 460,
    };

    static Button Button(string text, Action click, bool accent = false)
    {
        var button = new Button { Content = text, Focusable = false, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 3, 10, 4), Cursor = Cursors.Arrow };
        if (accent)
            button.SetResourceReference(StyleProperty, "AccentButtonStyle");
        button.Click += (_, _) => click();
        return button;
    }
}
