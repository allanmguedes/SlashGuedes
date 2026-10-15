using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SlashText.Design;
using SlashText.Models;

namespace SlashText;

public partial class MainWindow
{
    private bool _syncShortcutListSelection;
    private IReadOnlyList<Snippet> _visibleShortcuts = [];
    private IInputElement? _shortcutSelectionFocus;
    private Snippet? _shortcutSelectionFocusSnippet;
    private long _shortcutSelectionFocusVersion;

    private void ShortcutSidebarContent_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ShortcutCategoriesScroll is null || MostUsedPanel?.Parent is not FrameworkElement filters || e.NewSize.Height <= 0) return;
        var gap = e.NewSize.Height < 420 ? 16d : 24d;
        ShortcutSidebarSearchGap.Height = new GridLength(gap);
        ShortcutSidebarCategoryGap.Height = new GridLength(gap);
        // Categories keep their own scrolling; reserve at least one usable card viewport
        // at the supported minimum height rather than letting Auto rows consume the list.
        var available = e.NewSize.Height - SearchBox.DesiredSize.Height - filters.DesiredSize.Height
            - ShortcutCategoryHeading.DesiredSize.Height - (gap * 2) - 12 - 112;
        var maximum = Math.Clamp(available, 36, 152);
        if (Math.Abs(ShortcutCategoriesScroll.MaxHeight - maximum) > .5) ShortcutCategoriesScroll.MaxHeight = maximum;
    }

    private void UpdateShortcutItems(List<Snippet> visible)
    {
        _shortcutSelectionFocusVersion++;
        // Keep the item source and its realized containers when only selection/other UI changed.
        if (!_visibleShortcuts.SequenceEqual(visible))
        {
            _syncShortcutListSelection = true;
            try { _visibleShortcuts = visible; SnippetListPanel.ItemsSource = visible; }
            finally { _syncShortcutListSelection = false; }
        }
        RefreshShortcutListSelection();
        SnippetListPanel.RefreshRealizedItems();
        var empty = visible.Count == 0;
        SnippetListPanel.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        ShortcutListEmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ShortcutListEmptyHint.Text = _snippets.Count == 0 ? UiText.Get("Seu primeiro texto começa aqui.") : UiText.Get("Nenhum atalho corresponde aos filtros.");
        ShortcutListEmptyAction.Content = _snippets.Count == 0 ? UiText.Get("Criar atalho") : UiText.Get("Limpar filtros");
    }

    private void RefreshShortcutListSelection()
    {
        _syncShortcutListSelection = true;
        try { SnippetListPanel.SelectedItem = _visibleShortcuts.Contains(_selected!) ? _selected : null; }
        finally { _syncShortcutListSelection = false; }
    }

    private void ShortcutList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncShortcutListSelection) return;
        var focusVersion = ++_shortcutSelectionFocusVersion;
        var requested = SnippetListPanel.SelectedItem as Snippet;
        if (requested is not null) SelectSnippet(requested);
        // A cancelled discard restores the committed editor selection without reloading its draft.
        RefreshShortcutListSelection();
        if (requested is not null && !ReferenceEquals(requested, _selected))
        {
            var previousFocus = _shortcutSelectionFocus;
            var previousFocusSnippet = _shortcutSelectionFocusSnippet;
            var committed = _selected;
            // WPF may focus the requested row after SelectionChanged returns. Restore
            // only after that input completes, and never override a newer interaction.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (focusVersion != _shortcutSelectionFocusVersion || !ReferenceEquals(committed, _selected) ||
                    Window.GetWindow(SnippetListPanel)?.IsActive != true) return;
                var focused = Keyboard.FocusedElement;
                if (focused is ShortcutListItem currentRow)
                {
                    if (!ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(currentRow), SnippetListPanel) ||
                        (!ReferenceEquals(currentRow.Snippet, requested) && !ReferenceEquals(currentRow.Snippet, committed))) return;
                }
                else if (!ReferenceEquals(focused, SnippetListPanel)) return;
                _syncShortcutListSelection = true;
                try
                {
                    if (previousFocus is UIElement { IsVisible: true, IsEnabled: true } element &&
                        PresentationSource.FromVisual(element) is not null &&
                        (element is not ShortcutListItem row || ReferenceEquals(row.Snippet, previousFocusSnippet))) element.Focus();
                    else if (_selected is not null && _visibleShortcuts.Contains(_selected))
                    {
                        SnippetListPanel.ScrollIntoView(_selected); SnippetListPanel.UpdateLayout();
                        (SnippetListPanel.ItemContainerGenerator.ContainerFromItem(_selected) as ShortcutListItem)?.Focus();
                    }
                    else NameBox.Focus();
                }
                finally
                {
                    _syncShortcutListSelection = false;
                    // Focusing a previously unselected row can select it in WPF.
                    // Keep the editor's committed identity authoritative after cancel.
                    RefreshShortcutListSelection();
                }
            }));
        }
        _shortcutSelectionFocus = null; _shortcutSelectionFocusSnippet = null;
    }

    private void ShortcutList_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => RememberShortcutSelectionFocus();

    private void RememberShortcutSelectionFocus()
    {
        _shortcutSelectionFocusVersion++;
        _shortcutSelectionFocus = Keyboard.FocusedElement;
        _shortcutSelectionFocusSnippet = (_shortcutSelectionFocus as ShortcutListItem)?.Snippet;
    }

    private void ShortcutList_OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject origin && ItemsControl.ContainerFromElement(SnippetListPanel, origin) is ShortcutListItem { Snippet: { } snippet } row)
        {
            e.Handled = true;
            ShowShortcutActions(row, snippet);
        }
    }

    private void ShortcutList_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        RememberShortcutSelectionFocus();
        if (e.Key != Key.Apps && !(e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.Shift)) return;
        if (SnippetListPanel.SelectedItem is Snippet snippet)
        {
            SnippetListPanel.ScrollIntoView(snippet); SnippetListPanel.UpdateLayout();
            if (SnippetListPanel.ItemContainerGenerator.ContainerFromItem(snippet) is ShortcutListItem row)
            { e.Handled = true; ShowShortcutActions(row, snippet); }
        }
    }

    private void ShortcutListEmpty_OnClick(object sender, RoutedEventArgs e)
    {
        if (_snippets.Count == 0) { BeginNewSnippet(); return; }
        _selectedCategory = null; _showMostUsed = false; _showShortcutFavorites = false; _showShortcutPinned = false;
        SearchBox.Clear(); RefreshNavigation(); SearchBox.Focus();
    }
}
