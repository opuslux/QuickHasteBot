using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace QuickHasteBot.Tests;

/// <summary>
/// Nachbau eines Auswahlfelds, wie es Web-Bibliotheken bauen: ein schreibgeschütztes Textfeld, das aufklappen kann,
/// und eine Liste, die woanders im Fenster steht statt darunter. Wählt man eine Option, zeigt das Feld sie und die
/// Liste geht wieder zu.
/// </summary>
internal sealed class PopupSelect : TextBox
{
    public PopupSelect(ListBox list)
    {
        IsReadOnly = true;
        List = list;
        list.Visibility = Visibility.Collapsed;
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Content: string text })
                Text = text;
            list.Visibility = Visibility.Collapsed;
        };
    }

    public ListBox List { get; }

    public bool IsOpen
    {
        get => List.Visibility == Visibility.Visible;
        set => List.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    sealed class Peer(PopupSelect owner) : TextBoxAutomationPeer(owner), IExpandCollapseProvider
    {
        public override object GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.ExpandCollapse ? this : base.GetPattern(patternInterface);

        public ExpandCollapseState ExpandCollapseState => owner.IsOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

        public void Expand() => owner.Dispatcher.Invoke(() => owner.IsOpen = true);

        public void Collapse() => owner.Dispatcher.Invoke(() => owner.IsOpen = false);
    }
}
