using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QuickHasteBot.Core;

namespace QuickHasteBot.UI;

/// <summary>Wie ein Schritt-für-Schritt-Lauf ausgegangen ist, bestimmt die Farbe des Titels.</summary>
internal enum StepResult { Done, Stopped, Failed }

/// <summary>
/// Leiste für den Schritt-für-Schritt-Lauf, unten mittig am Zielfenster (<see cref="FloatingBar"/>). Zeigt den Schritt,
/// der als Nächstes läuft, und den danach.
/// </summary>
internal sealed class StepBar : FloatingBar
{
    const int MaxStepLength = 70;

    readonly TextBlock _title = new() { FontWeight = FontWeights.SemiBold, FontSize = 13 };
    readonly TextBlock _state = new() { FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _now = Code();
    readonly TextBlock _then = Code();
    readonly Border _nowBox;
    readonly TextBlock _message = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };
    readonly TextBlock _note = new() { FontSize = 11, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };
    readonly Grid _steps = new() { Margin = new Thickness(0, 6, 0, 0) };
    readonly StackPanel _runButtons = new() { Orientation = Orientation.Horizontal };
    readonly StackPanel _endButtons = new() { Orientation = Orientation.Horizontal };
    readonly Button _next;
    readonly Button _continue;
    TaskCompletionSource<bool>? _choice;

    public StepBar() : base(Strings.StepBarTitle)
    {
        _next = BarButton(Strings.StepBarNext + "  F10", () => NextClicked?.Invoke(this, EventArgs.Empty), accent: true);
        _continue = BarButton(Strings.StepBarContinue + "  F5", () => ContinueClicked?.Invoke(this, EventArgs.Empty));
        _runButtons.Children.Add(_next);
        _runButtons.Children.Add(_continue);
        _runButtons.Children.Add(BarButton(Strings.StepBarStop, () => StopClicked?.Invoke(this, EventArgs.Empty)));
        _endButtons.Children.Add(BarButton(Strings.StepBarAgain, () => _choice?.TrySetResult(true), accent: true));
        _endButtons.Children.Add(BarButton(Strings.StepBarClose, () => _choice?.TrySetResult(false)));
        _endButtons.Visibility = Visibility.Collapsed;

        _steps.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _steps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _steps.RowDefinitions.Add(new RowDefinition());
        _steps.RowDefinitions.Add(new RowDefinition());
        // „Jetzt“ in der Farbe der Markierung im Editor, „Danach“ gedämpft
        _nowBox = new Border { Child = _now, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(-6, 1, 0, 1), HorizontalAlignment = HorizontalAlignment.Left };
        _then.Opacity = 0.6;
        _then.Margin = new Thickness(0, 3, 0, 2);
        AddRow(0, Strings.StepBarNow, _nowBox);
        AddRow(1, Strings.StepBarThen, _then);

        var header = new DockPanel { LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), Children = { _runButtons, _endButtons } };
        DockPanel.SetDock(buttons, Dock.Right);
        header.Children.Add(buttons);
        header.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _title, _state } });

        var body = new StackPanel { Children = { header, _steps, _message, _note } };
        _message.Visibility = Visibility.Collapsed;
        _note.Visibility = Visibility.Collapsed;
        _note.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCautionBrush");
        SetBody(body);
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
        var (fill, border) = StepColors.For(paused ? StepMark.Paused : StepMark.Running);
        _nowBox.Background = fill;
        _nowBox.BorderBrush = border.Brush;

        _title.ClearValue(TextBlock.ForegroundProperty);
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
    public void ShowResult(string title, string? message, StepResult result)
    {
        _runButtons.Visibility = Visibility.Collapsed;
        _endButtons.Visibility = Visibility.Visible;
        _steps.Visibility = Visibility.Collapsed;
        _title.Text = title;
        _state.Text = "";
        _message.Text = message ?? "";
        _message.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        _title.SetResourceReference(TextBlock.ForegroundProperty, result switch
        {
            StepResult.Done => "SystemFillColorSuccessBrush",
            StepResult.Stopped => "SystemFillColorCautionBrush",
            _ => "SystemFillColorCriticalBrush",
        });
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

    /// <summary>Ein Schritt in einer Zeile: Tags wie im Baustein, Text in «», ein Baustein ohne Tags als „Einfügen“.</summary>
    internal static string StepText(FormStep step, bool paste)
    {
        var text = step is TextStep t ? "«" + t.Text.ReplaceLineEndings("⏎") + "»" : KeyTags.Describe(step).ReplaceLineEndings("⏎");
        if (text.Length > MaxStepLength)
            text = text[..(MaxStepLength - 2)] + "…" + text[^1];
        return paste ? Strings.StepBarPaste + " " + text : text;
    }

    void AddRow(int row, string label, FrameworkElement value)
    {
        var caption = new TextBlock { Text = label, FontSize = 12, Opacity = 0.75, Margin = new Thickness(0, 2, 10, 2), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(caption, row);
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        _steps.Children.Add(caption);
        _steps.Children.Add(value);
    }
}
