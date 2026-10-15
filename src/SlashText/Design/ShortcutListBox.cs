using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SlashText.Models;

namespace SlashText.Design;

/// <summary>Data items stay in the collection; WPF realizes and recycles only viewport cards.</summary>
public sealed class ShortcutListBox : ListBox
{
    protected override DependencyObject GetContainerForItemOverride() => new ShortcutListItem();
    protected override bool IsItemItsOwnContainerOverride(object item) => item is ShortcutListItem;

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        ((ShortcutListItem)element).SetSnippet((Snippet)item);
    }

    protected override void ClearContainerForItemOverride(DependencyObject element, object item)
    {
        ((ShortcutListItem)element).SetSnippet(null);
        base.ClearContainerForItemOverride(element, item);
    }

    internal IEnumerable<ShortcutListItem> RealizedItems()
    {
        var host = FindHost(this);
        return host is null ? [] : host.Children.OfType<ShortcutListItem>().ToArray();
    }

    internal void RefreshRealizedItems()
    {
        foreach (var row in RealizedItems()) row.Refresh();
    }

    private static VirtualizingStackPanel? FindHost(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is VirtualizingStackPanel panel && panel.IsItemsHost) return panel;
            if (FindHost(child) is { } found) return found;
        }
        return null;
    }
}

public sealed class ShortcutListItem : ListBoxItem
{
    private readonly StackPanel _card = new();
    private readonly StackPanel _badges = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _command = new() { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _name = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 5, 0, 0) };
    private readonly TextBlock _preview = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 5, 0, 0) };
    private readonly TextBlock _state = new() { FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly LabIcon _star = Badge("Star");
    private readonly LabIcon _pin = Badge("Pin");
    internal Snippet? Snippet { get; private set; }

    public ShortcutListItem()
    {
        var commandRow = new DockPanel();
        DockPanel.SetDock(_badges, Dock.Right);
        commandRow.Children.Add(_badges); commandRow.Children.Add(_command);
        _card.Children.Add(commandRow); _card.Children.Add(_name); _card.Children.Add(_preview); _card.Children.Add(_state);
        _command.SetResourceReference(TextBlock.ForegroundProperty, "Lab.accent-text");
        _name.SetResourceReference(TextBlock.ForegroundProperty, "Lab.text");
        _preview.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted");
        _state.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted");
    }

    internal void SetSnippet(Snippet? snippet)
    {
        Snippet = snippet;
        Content = snippet is null ? null : _card;
        Refresh();
    }

    internal void Refresh()
    {
        var snippet = Snippet;
        _command.Text = snippet?.Trigger ?? "";
        _name.Text = snippet?.Name ?? "";
        _preview.Text = snippet?.Content.Replace('\r', ' ').Replace('\n', ' ') ?? "";
        _badges.Children.Clear();
        if (snippet?.IsFavorite == true) _badges.Children.Add(_star);
        if (snippet?.IsPinned == true) _badges.Children.Add(_pin);
        _state.Text = snippet?.HasLegacyIncompatibleTrigger == true ? UiText.Get("Comando legado · revise para ativar")
            : snippet?.Enabled == false ? UiText.Get("Pausado") : "";
        _state.Visibility = _state.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ToolTip = snippet is null ? null : UiText.Format($"{snippet.Name}\n{snippet.Trigger}\nCategoria: {snippet.Category}");
        RefreshAccessibleName();
    }

    protected override void OnSelected(RoutedEventArgs e) { base.OnSelected(e); RefreshAccessibleName(); }
    protected override void OnUnselected(RoutedEventArgs e) { base.OnUnselected(e); RefreshAccessibleName(); }
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e) => e.Handled = true;

    private void RefreshAccessibleName() => AutomationProperties.SetName(this, Snippet is not { } snippet ? "" :
        UiText.Format($"{snippet.Name}, comando {snippet.Trigger}, categoria {snippet.Category}") + (IsSelected ? UiText.Get(", selecionado") : ""));

    private static LabIcon Badge(string kind)
    {
        var badge = new LabIcon { Kind = kind, Width = 14, Height = 14, Margin = new Thickness(4, 0, 0, 0) };
        badge.SetResourceReference(LabIcon.ForegroundProperty, "Lab.accent-text");
        return badge;
    }
}
