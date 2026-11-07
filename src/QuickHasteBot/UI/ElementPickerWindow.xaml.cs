using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QuickHasteBot.Core;
using QuickHasteBot.Interop;
using static QuickHasteBot.Interop.NativeMethods;

namespace QuickHasteBot.UI;

/// <summary>
/// Element-Picker: Fadenkreuz auf ein Feld, einen Button oder Link ziehen, loslassen, und der passende Tag wie
/// {focus,#email} landet in der Zwischenablage. Zeigt live, was unter dem Fadenkreuz liegt, und rahmt es rot ein.
/// Nach dem Loslassen prüft er, welche Selektoren genau dieses Element finden, der stabilste zuerst.
/// Bleibt im Vordergrund, damit man ihn neben dem Browser bedienen kann. Esc bricht ab bzw. schliesst.
/// </summary>
internal partial class ElementPickerWindow : Window
{
    readonly AppController _app;
    readonly ElementHighlight _highlight = new();
    bool _picking;
    bool _probing;
    POINT? _pending;
    ElementInfo? _hovered;
    ElementInfo? _picked;
    bool _updating;

    public ElementPickerWindow(AppController app)
    {
        InitializeComponent();
        _app = app;
        IncludeWindowInput.IsChecked = app.Settings.PickerIncludeWindow;
        StatusText.Text = Strings.PickerReady;
        Closed += (_, _) => _highlight.Close();
        Activated += (_, _) => UpdateInsertButton();
    }

    void OnTargetDown(object sender, MouseButtonEventArgs e)
    {
        _picking = true;
        _hovered = null;
        Target.CaptureMouse();
        Mouse.OverrideCursor = Cursors.Cross;
        Crosshair.Visibility = Visibility.Hidden; // wie Spy++: das Fadenkreuz "hängt" jetzt an der Maus
        StatusText.Text = Strings.PickerDragging;
        e.Handled = true;
    }

    void OnTargetMove(object sender, MouseEventArgs e)
    {
        if (_picking && GetCursorPos(out var point))
            Probe(point);
    }

    /// <summary>UI Automation braucht pro Abfrage einige ms: immer nur eine gleichzeitig, danach die neueste Position.</summary>
    async void Probe(POINT point)
    {
        if (_probing)
        {
            _pending = point;
            return;
        }
        _probing = true;
        var info = await Task.Run(() => ElementAutomation.Probe(point.X, point.Y));
        _probing = false;

        if (_picking)
        {
            _hovered = info;
            ShowDetails(info);
            if (info is null) _highlight.Hide();
            else _highlight.ShowAt(info.Bounds);
            StatusText.Text = info is { Blocked: true } ? string.Format(Strings.PickerElevatedFormat, info.Process) : Strings.PickerDragging;
        }

        if (_pending is { } next && (next.X != point.X || next.Y != point.Y) && _picking)
        {
            _pending = null;
            Probe(next);
        }
    }

    void ShowDetails(ElementInfo? info)
    {
        ProgramText.Text = Show(info?.Process);
        TypeText.Text = Show(info?.Type);
        IdText.Text = Show(info?.Id);
        NameText.Text = Show(info?.Name);
        ClassText.Text = Show(info?.ClassName);
        ValueText.Text = Show(info?.Value);

        static string Show(string? value) => string.IsNullOrEmpty(value) ? "–" : value;
    }

    async void OnTargetUp(object sender, MouseButtonEventArgs e)
    {
        if (!_picking) return;
        EndPicking();
        if (_hovered is { } info)
            await Pick(info);
        else
            StatusText.Text = Strings.PickerReady;
    }

    void OnTargetLostCapture(object sender, MouseEventArgs e)
    {
        if (_picking) EndPicking();
    }

    void EndPicking()
    {
        _picking = false;
        _pending = null;
        Mouse.OverrideCursor = null;
        Crosshair.Visibility = Visibility.Visible;
        _highlight.Hide();
        if (Target.IsMouseCaptured) Target.ReleaseMouseCapture();
    }

    async Task Pick(ElementInfo info)
    {
        ShowDetails(info);
        if (info.Blocked)
        {
            _picked = null;
            ResultPanel.Visibility = Visibility.Collapsed;
            StatusText.Text = string.Format(Strings.PickerElevatedFormat, info.Process);
            return;
        }
        _picked = info;
        StatusText.Text = Strings.PickerChecking;
        var selectors = await Task.Run(() => ElementAutomation.VerifiedSelectors(info));
        if (_picked != info) return; // inzwischen ein anderes Element erfasst

        var verified = selectors.Count > 0;
        if (!verified)
            selectors = ElementSelector.Candidates(info.Id, info.Name, info.ClassName, null);

        _updating = true;
        SelectorList.ItemsSource = selectors;
        SelectorList.SelectedIndex = selectors.Count > 0 ? 0 : -1;
        (DefaultAction(info.Type) switch
        {
            ElementAction.Click => ActionClick,
            ElementAction.Set => ActionSet,
            _ => ActionFocus,
        }).IsChecked = true;
        _updating = false;

        ResultPanel.Visibility = Visibility.Visible;
        UpdateInsertButton();
        UpdateTag();
        if (selectors.Count == 0)
        {
            StatusText.Text = Strings.PickerNothing;
            return;
        }
        var copied = Copy();
        StatusText.Text = !verified ? Strings.PickerUnverified : copied ? Strings.PickerCopied : "";
    }

    /// <summary>Dieselbe Regel wie beim Aufnehmen: Buttons und Links klickt man, Checkboxen und Dropdowns setzt man.</summary>
    internal static ElementAction DefaultAction(string type) => Recording.DefaultAction(type);

    void OnActionChanged(object sender, RoutedEventArgs e)
    {
        if (!_updating) UpdateTag();
    }

    void OnSelectorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating) UpdateTag();
    }

    void UpdateTag()
    {
        if (SelectorList.SelectedItem is not ElementSelector selector || _picked is not { } info)
        {
            TagOutput.Text = "";
            return;
        }
        var action = ActionClick.IsChecked == true ? ElementAction.Click : ActionSet.IsChecked == true ? ElementAction.Set : ElementAction.Focus;
        var value = info.Type == "CheckBox" ? "on" : info.Value ?? "";
        var tag = KeyTags.ElementTag(action, selector, value);
        // process= statt nur des Namens: "crm" allein träfe auch einen Browser-Tab mit Crm im Titel
        if (IncludeWindowInput.IsChecked == true && info.Process.Length > 0)
            tag = KeyTags.WindowTag(new WindowSelector(info.Process, null, null)) + tag;
        TagOutput.Text = tag;
    }

    void OnIncludeWindowChanged(object sender, RoutedEventArgs e)
    {
        _app.Settings.PickerIncludeWindow = IncludeWindowInput.IsChecked == true;
        _app.SaveSettings();
        UpdateTag();
        if (Copy()) StatusText.Text = Strings.PickerCopied;
    }

    /// <summary>
    /// Führt den Tag aus dem Textfeld aus wie einen Baustein: holt das Programm nach vorne, sucht das Element und
    /// klickt, setzt oder fokussiert es wirklich. Der Picker bleibt oben, damit man das Ergebnis sieht.
    /// </summary>
    async void OnTest(object sender, RoutedEventArgs e)
    {
        if (_picked is not { } info || TagOutput.Text.Trim().Length == 0) return;
        TestButton.IsEnabled = false;
        StatusText.Text = Strings.PickerTestRunning;
        try
        {
            var error = await _app.TestTagAsync(TagOutput.Text, info.Window);
            StatusText.Text = error ?? Strings.PickerTestDone;
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    void UpdateInsertButton() => InsertButton.Visibility = _app.ActiveEditor is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Tag in den zuletzt benutzten Editor, an die Schreibmarke, und den Editor nach vorne.</summary>
    void OnInsert(object sender, RoutedEventArgs e)
    {
        if (_app.ActiveEditor is not { } editor || TagOutput.Text.Length == 0) return;
        editor.InsertTag(TagOutput.Text);
        editor.Activate();
    }

    void OnCopy(object sender, RoutedEventArgs e)
    {
        if (Copy()) StatusText.Text = Strings.PickerCopied;
    }

    bool Copy()
    {
        if (TagOutput.Text.Length == 0) return false;
        try
        {
            Clipboard.SetText(TagOutput.Text);
            return true;
        }
        catch (ExternalException ex)
        {
            Log.Error("Tag konnte nicht in die Zwischenablage gelegt werden", ex);
            return false;
        }
    }

    void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (_picking)
        {
            _hovered = null;
            EndPicking();
            StatusText.Text = Strings.PickerReady;
        }
        else
            Close();
        e.Handled = true;
    }
}
