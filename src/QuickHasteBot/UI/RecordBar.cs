using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using QuickHasteBot.Core;
using QuickHasteBot.Interop;

namespace QuickHasteBot.UI;

/// <summary>
/// Leiste während einer Aufnahme, unten mittig am Zielfenster (<see cref="FloatingBar"/>, nie aktiv). Zeigt, wie viele
/// Schritte schon aufgenommen sind und den letzten als Tag, dazu die Wahl, welche Tags Felder bekommen. Die Wahl gilt
/// auch rückwirkend, weil der Text erst bei „Fertig“ entsteht.
/// </summary>
internal sealed class RecordBar : FloatingBar
{
    readonly TextBlock _count = new() { FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 };
    readonly TextBlock _last = Code();
    readonly TextBlock _note = new() { FontSize = 11, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };
    readonly Dictionary<RecordMode, ToggleButton> _modes = [];
    readonly TaskCompletionSource<bool> _end = new();

    public RecordBar(RecordMode mode) : base(Strings.RecordBarTitle)
    {
        Mode = mode;

        // Roter Punkt wie bei jeder Aufnahme
        var dot = new TextBlock { Text = "●", FontSize = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");
        var title = new TextBlock { Text = Strings.RecordBarTitle, FontWeight = FontWeights.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0),
            Children =
            {
                BarButton(Strings.RecordBarDone, () => End(keep: true), accent: true),
                BarButton(Strings.RecordBarDiscard, () => End(keep: false)),
            },
        };
        var header = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(buttons, Dock.Right);
        header.Children.Add(buttons);
        header.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { dot, title, _count } });

        var last = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        last.Children.Add(Caption(Strings.RecordBarLast));
        last.Children.Add(_last);

        var modes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        modes.Children.Add(Caption(Strings.RecordBarFields));
        AddMode(modes, RecordMode.Mixed, Strings.RecordBarModeMixed, Strings.RecordBarModeMixedToolTip);
        AddMode(modes, RecordMode.AlwaysSet, Strings.RecordBarModeSet, Strings.RecordBarModeSetToolTip);
        AddMode(modes, RecordMode.AlwaysType, Strings.RecordBarModeType, Strings.RecordBarModeTypeToolTip);
        UpdateModes();

        var hint = new TextBlock { Text = Strings.RecordBarHint, FontSize = 11, Opacity = 0.6, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };
        _note.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCautionBrush");
        _note.Visibility = Visibility.Collapsed;
        SetBody(new StackPanel { Children = { header, last, modes, _note, hint } });
        Show([], RecordNote.None);
    }

    /// <summary>Welche Tags Felder bekommen. Ändert sich mit den drei Knöpfen.</summary>
    public RecordMode Mode { get; private set; }

    public event EventHandler? ModeChanged;

    /// <summary>Der aufgenommene Text bisher, eine Zeile pro Schritt, und was zu melden ist.</summary>
    public void Show(IReadOnlyList<string> lines, RecordNote note)
    {
        _count.Text = lines.Count == 1 ? Strings.RecordBarOneStep : string.Format(Strings.RecordBarStepsFormat, lines.Count);
        _last.Text = lines.Count == 0 ? Strings.RecordBarNothingYet : lines[^1];
        _last.Opacity = lines.Count == 0 ? 0.6 : 1;
        _note.Text = note switch
        {
            RecordNote.Password => Strings.RecordBarPassword,
            RecordNote.Elevated => Strings.RecordBarElevated,
            RecordNote.Unnamed => Strings.RecordBarUnnamed,
            _ => "",
        };
        _note.Visibility = note == RecordNote.None ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Bis „Fertig“ (true) oder „Verwerfen“ (false).</summary>
    public Task<bool> WaitForEndAsync() => _end.Task;

    /// <summary>Wie ein Klick auf „Fertig“ oder „Verwerfen“.</summary>
    public void End(bool keep) => _end.TrySetResult(keep);

    void AddMode(Panel panel, RecordMode mode, string text, string tooltip)
    {
        var button = new ToggleButton
        {
            Content = text, ToolTip = tooltip, Focusable = false, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(8, 2, 8, 3),
            Cursor = Cursors.Arrow,
        };
        button.Click += (_, _) =>
        {
            Mode = mode;
            UpdateModes();
            ModeChanged?.Invoke(this, EventArgs.Empty);
        };
        _modes[mode] = button;
        panel.Children.Add(button);
    }

    void UpdateModes()
    {
        foreach (var (mode, button) in _modes)
            button.IsChecked = mode == Mode;
    }

    static TextBlock Caption(string text) => new()
    {
        Text = text, FontSize = 12, Opacity = 0.75, Margin = new Thickness(0, 2, 10, 2), VerticalAlignment = VerticalAlignment.Center,
    };
}
