using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SlashText.Models;
using SlashText.Services;
using Forms = System.Windows.Forms;

namespace SlashText.Views;

// Lightweight keyboard-first quick picker; never takes ownership of existing
// clipboard data until the user explicitly chooses an item.
public sealed class ClipboardQuickWindow : Window
{
    private readonly ClipboardHistoryService _history;
    private readonly Action<ClipboardHistoryItem> _choose;
    private readonly Action<ClipboardHistoryItem> _copyOnly;
    private readonly Action _manage;
    private readonly TextBox _search;
    private readonly ListBox _list;
    private readonly TextBlock _hint;
    private readonly Button _enable;
    private readonly TextBlock _subtitle;
    private readonly ComboBox _categoryFilter;
    private readonly ComboBox _moveToCategory;
    private readonly Button _moveButton;
    private readonly Button _deleteButton;
    private readonly TextBlock _emptyMessage;
    private bool _refreshingCategories;

    public ClipboardQuickWindow(ClipboardHistoryService history,
        Action<ClipboardHistoryItem> choose, Action<ClipboardHistoryItem> copyOnly,
        Action manage, Action enable)
    {
        _history = history;
        _choose = choose;
        _copyOnly = copyOnly;
        _manage = manage;
        Width = 540;
        Height = 640;
        MinWidth = 480;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = (Brush)Application.Current.Resources["Lab.bg"];

        var root = new Border
        {
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18),
            BorderThickness = new Thickness(1.5)
        };
        root.SetResourceReference(Border.BackgroundProperty, "Lab.surface");
        root.SetResourceReference(Border.BorderBrushProperty, "Lab.line");
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Child = grid;
        Content = root;

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var title = new TextBlock { Text = "SLASHGUEDES   /   HISTÓRICO", FontSize = 17,
            FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Lab.text");
        header.Children.Add(title);
        _subtitle = new TextBlock { Text = "Enter para colar · Esc para fechar",
            Margin = new Thickness(0, 4, 0, 0), FontSize = 11 };
        _subtitle.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted");
        header.Children.Add(_subtitle);
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        _search = new TextBox { Height = 38, Margin = new Thickness(0, 0, 0, 12),
            VerticalContentAlignment = VerticalAlignment.Center };
        _search.TextChanged += (_, _) => Refresh();
        _search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && _list is { } list) { list.Focus(); if (list.Items.Count > 0) list.SelectedIndex = 0; e.Handled = true; }
            if (e.Key == Key.Enter) { SelectCurrent(); e.Handled = true; }
        };
        var filterRow = new Grid();
        filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        Grid.SetColumn(_search, 0);
        filterRow.Children.Add(_search);
        _categoryFilter = new ComboBox { Height = 38, Margin = new Thickness(8, 0, 0, 12) };
        _categoryFilter.SelectionChanged += (_, _) =>
        {
            if (!_refreshingCategories) Refresh();
        };
        Grid.SetColumn(_categoryFilter, 1);
        filterRow.Children.Add(_categoryFilter);
        Grid.SetRow(filterRow, 1);
        grid.Children.Add(filterRow);

        _list = new ListBox
        {
            BorderThickness = new Thickness(0), FontSize = 13,
            Padding = new Thickness(0, 4, 4, 4),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        _list.SetResourceReference(Control.BackgroundProperty, "Lab.surface");
        _list.MouseDoubleClick += (_, _) => SelectCurrent();
        _list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { SelectCurrent(); e.Handled = true; }
            if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
        };
        var listArea = new Grid();
        _emptyMessage = new TextBlock
        {
            Text = "Nenhum item neste filtro. Copie algo com Ctrl + C.",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(24),
            FontSize = 13
        };
        _emptyMessage.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted");
        listArea.Children.Add(_list);
        listArea.Children.Add(_emptyMessage);
        Grid.SetRow(listArea, 2);
        grid.Children.Add(listArea);

        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        _hint = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8) };
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted");
        footer.Children.Add(_hint);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var paste = new Button
        {
            Content = "Colar no aplicativo", Margin = new Thickness(0, 0, 7, 0),
            ToolTip = "Se não for possível voltar ao aplicativo, deixa o item copiado."
        };
        paste.Click += (_, _) => SelectCurrent();
        actions.Children.Add(paste);
        var copy = new Button { Content = "Só copiar", Margin = new Thickness(0, 0, 7, 0) };
        copy.Click += (_, _) => SelectCurrent(copyOnly: true);
        actions.Children.Add(copy);
        var manageButton = new Button { Content = "Gerenciar" };
        manageButton.Click += (_, _) => { Hide(); _manage(); };
        actions.Children.Add(manageButton);
        _enable = new Button { Content = "Ativar histórico", Margin = new Thickness(8, 0, 0, 0) };
        _enable.Click += (_, _) => { enable(); Refresh(); };
        actions.Children.Add(_enable);
        footer.Children.Add(actions);

        var categoriesRow = new DockPanel { Margin = new Thickness(0, 9, 0, 0) };
        var categoryLabel = new TextBlock
        {
            Text = "Organizar:", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0)
        };
        categoryLabel.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted");
        DockPanel.SetDock(categoryLabel, Dock.Left);
        categoriesRow.Children.Add(categoryLabel);
        _moveToCategory = new ComboBox
        {
            Width = 190, Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Mova o item selecionado para uma das suas categorias."
        };
        DockPanel.SetDock(_moveToCategory, Dock.Left);
        categoriesRow.Children.Add(_moveToCategory);
        _moveButton = new Button { Content = "Mover", Margin = new Thickness(0, 0, 8, 0) };
        _moveButton.Click += async (_, _) =>
        {
            if (_list.SelectedItem is not ListBoxItem { Tag: ClipboardHistoryItem current }) return;
            var category = _moveToCategory.SelectedItem as string ?? "Sem categoria";
            try
            {
                await _history.ChangeAsync(current,
                    category: category == "Sem categoria" ? string.Empty : category);
                Refresh();
            }
            catch (Exception) { /* Item remains unchanged on failures. */ }
        };
        DockPanel.SetDock(_moveButton, Dock.Left);
        categoriesRow.Children.Add(_moveButton);
        _deleteButton = new Button
        {
            Content = "Excluir item",
            ToolTip = "Exclui somente o item selecionado."
        };
        _deleteButton.Click += async (_, _) =>
        {
            if (_list.SelectedItem is not ListBoxItem { Tag: ClipboardHistoryItem current }) return;
            try { await _history.DeleteAsync(current); Refresh(); }
            catch (Exception) { /* Item remains on disk if the write failed. */ }
        };
        DockPanel.SetDock(_deleteButton, Dock.Left);
        categoriesRow.Children.Add(_deleteButton);
        footer.Children.Add(categoriesRow);

        var clean = new Button
        {
            Content = "Limpar tudo, exceto favoritos",
            ToolTip = "Itens sem estrela serão apagados, mesmo que estejam apenas fixados.",
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        clean.Click += async (_, _) =>
        {
            var count = _history.Items.Count(i => !i.IsFavorite);
            if (count == 0) return;
            if (MessageBox.Show(this,
                    $"Apagar definitivamente {count} item(ns) sem estrela? Somente favoritos serão mantidos.",
                    "Limpar não favoritos", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            try { await _history.ClearNonFavoritesAsync(); Refresh(); }
            catch (Exception)
            {
                MessageBox.Show(this,
                    "Não foi possível limpar o histórico. Os dados foram mantidos.",
                    "SlashGuedes", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        footer.Children.Add(clean);
        Grid.SetRow(footer, 3);
        grid.Children.Add(footer);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
        };
        Deactivated += (_, _) => Hide();
    }

    public void Open()
    {
        Refresh();
        var cursor = Forms.Control.MousePosition;
        var screen = Forms.Screen.FromPoint(cursor).WorkingArea;
        Left = Math.Clamp(cursor.X - Width / 2, screen.Left, Math.Max(screen.Left, screen.Right - Width));
        Top = Math.Clamp(cursor.Y - 28, screen.Top, Math.Max(screen.Top, screen.Bottom - Height));
        Show();
        Activate();
        _search.Focus();
        _search.SelectAll();
    }

    public void Refresh()
    {
        if (_list is null || _search is null || _categoryFilter is null) return;
        var chosenCategory = _categoryFilter.SelectedItem as string ?? "";
        var categories = new[] { "Todas as categorias" }
            .Concat(_history.Categories).ToArray();
        var availableTargets = new[] { "Sem categoria" }
            .Concat(_history.Categories).ToArray();
        var selectedTarget = _moveToCategory.SelectedItem as string ?? "Sem categoria";
        if (!_moveToCategory.Items.OfType<string>().SequenceEqual(availableTargets))
        {
            _moveToCategory.ItemsSource = availableTargets;
            _moveToCategory.SelectedItem = availableTargets.FirstOrDefault(c =>
                c.Equals(selectedTarget, StringComparison.OrdinalIgnoreCase))
                ?? availableTargets[0];
        }
        if (!_categoryFilter.Items.OfType<string>().SequenceEqual(categories))
        {
            _refreshingCategories = true;
            try
            {
                _categoryFilter.ItemsSource = categories;
                _categoryFilter.SelectedItem = categories.FirstOrDefault(c =>
                    c.Equals(chosenCategory, StringComparison.OrdinalIgnoreCase))
                    ?? categories[0];
            }
            finally { _refreshingCategories = false; }
        }
        var filterCategory = _categoryFilter.SelectedItem as string ?? "Todas as categorias";
        var query = _search.Text.Trim();
        var selectedId = (_list.SelectedItem as ListBoxItem)?.Tag is ClipboardHistoryItem active
            ? active.Id : null;
        var candidates = _history.Items
            .Where(i => (filterCategory == "Todas as categorias" ||
                         i.Category.Equals(filterCategory, StringComparison.OrdinalIgnoreCase)) &&
                        (string.IsNullOrEmpty(query) ||
                         i.Summary.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                         i.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            .OrderByDescending(i => i.IsFavorite || i.IsPinned)
            .ThenByDescending(i => i.CopiedUtc)
            .Take(70)
            .ToArray();
        _list.Items.Clear();
        foreach (var item in candidates)
        {
            var row = new DockPanel { Margin = new Thickness(2, 4, 2, 4),
                LastChildFill = true };
            var star = new Button
            {
                Content = item.IsFavorite ? "★" : "☆",
                ToolTip = item.IsFavorite ? "Remover dos favoritos" : "Favoritar e proteger da limpeza",
                FontSize = 19,
                MinWidth = 36,
                Margin = new Thickness(5, 0, 2, 0),
                Padding = new Thickness(4, 1, 4, 1)
            };
            DockPanel.SetDock(star, Dock.Right);
            star.Click += async (_, _) =>
            {
                star.IsEnabled = false;
                try { await _history.ChangeAsync(item, favorite: !item.IsFavorite); }
                catch (Exception) { /* A aba principal continua disponível. */ }
                finally { star.IsEnabled = true; }
                Refresh();
            };
            row.Children.Add(star);
            var pin = new Button
            {
                Content = item.IsPinned ? "◆" : "◇",
                ToolTip = item.IsPinned ? "Desafixar" : "Fixar",
                MinWidth = 30, FontSize = 16,
                Margin = new Thickness(1, 0, 4, 0),
                Padding = new Thickness(3, 1, 3, 1)
            };
            DockPanel.SetDock(pin, Dock.Right);
            pin.Click += async (_, _) =>
            {
                pin.IsEnabled = false;
                try { await _history.ChangeAsync(item, pinned: !item.IsPinned); }
                catch (Exception) { /* No change on failure. */ }
                finally { pin.IsEnabled = true; }
                Refresh();
            };
            row.Children.Add(pin);
            if (item.Kind == "image" && _history.LoadThumbnail(item) is { } bitmap)
            {
                var image = new System.Windows.Controls.Image
                {
                    Source = bitmap, Width = 94, Height = 65,
                    Stretch = Stretch.UniformToFill,
                    Margin = new Thickness(4, 0, 10, 0)
                };
                DockPanel.SetDock(image, Dock.Left);
                row.Children.Add(image);
            }
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var label = new TextBlock
            {
                Text = item.Kind == "image" ? "Imagem copiada" : item.Summary,
                MaxHeight = 48, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.Wrap
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Lab.text");
            text.Children.Add(label);
            var note = new TextBlock
            {
                Text = (item.IsPinned ? "FIXADO · " : "") +
                       (item.IsFavorite ? "FAVORITO · " : "") +
                       (!string.IsNullOrWhiteSpace(item.Category) ? item.Category + " · " : "") +
                       item.CopiedUtc.ToLocalTime().ToString("dd/MM HH:mm"),
                FontSize = 10
            };
            note.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted");
            text.Children.Add(note);
            row.Children.Add(text);
            var card = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 5, 9, 5),
                Child = row,
                Margin = new Thickness(0, 2, 0, 2)
            };
            card.SetResourceReference(Border.BackgroundProperty, "Lab.bg");
            card.SetResourceReference(Border.BorderBrushProperty, "Lab.line");
            var control = new ListBoxItem
            {
                Tag = item, Content = card,
                Padding = new Thickness(3),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            _list.Items.Add(control);
        }
        _emptyMessage.Visibility = _list.Items.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
        _list.SelectedItem = _list.Items.Cast<ListBoxItem>()
            .FirstOrDefault(entry => ((ClipboardHistoryItem)entry.Tag).Id == selectedId)
            ?? _list.Items.Cast<ListBoxItem>().FirstOrDefault();
        var protectedCount = _history.Items.Count(i => i.IsFavorite || i.IsPinned);
        var recentCount = _history.Items.Count - protectedCount;
        _hint.Text = !_history.Enabled ? "Histórico pausado · itens salvos permanecem disponíveis."
            : _history.Items.Count == 0 ? "Nenhum item copiado ainda."
            : $"{recentCount}/{_history.MaxEntries} recentes · {protectedCount} favoritos ou fixados protegidos";
        _subtitle.Text = _history.Shortcut.Replace("+", " + ") +
                         " · Enter: colar · ☆: favorito · ◇: fixar · Esc: fechar";
        _moveButton.IsEnabled = _list.SelectedItem is not null;
        _deleteButton.IsEnabled = _list.SelectedItem is not null;
        _enable.Visibility = _history.Enabled ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SelectCurrent(bool copyOnly = false)
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: ClipboardHistoryItem item }) return;
        Hide();
        if (copyOnly) _copyOnly(item);
        else _choose(item);
    }
}
