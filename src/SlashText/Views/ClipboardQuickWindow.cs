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
    private readonly Action _manage;
    private readonly TextBox _search;
    private readonly ListBox _list;
    private readonly TextBlock _hint;
    private readonly Button _enable;
    private readonly TextBlock _subtitle;

    public ClipboardQuickWindow(ClipboardHistoryService history,
        Action<ClipboardHistoryItem> choose, Action manage, Action enable)
    {
        _history = history;
        _choose = choose;
        _manage = manage;
        Width = 480;
        Height = 510;
        MinWidth = 400;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = (Brush)Application.Current.Resources["Lab.bg"];

        var root = new Border
        {
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(18),
            BorderThickness = new Thickness(1)
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
        var title = new TextBlock { Text = "SLASHGUEDES  ·  ÁREA DE TRANSFERÊNCIA", FontSize = 15,
            FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Lab.text");
        header.Children.Add(title);
        _subtitle = new TextBlock { Text = "Enter para copiar · Esc para fechar",
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
        Grid.SetRow(_search, 1);
        grid.Children.Add(_search);

        _list = new ListBox
        {
            BorderThickness = new Thickness(0), FontSize = 13,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        _list.MouseDoubleClick += (_, _) => SelectCurrent();
        _list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { SelectCurrent(); e.Handled = true; }
            if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
        };
        Grid.SetRow(_list, 2);
        grid.Children.Add(_list);

        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        _hint = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8) };
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted");
        footer.Children.Add(_hint);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var copy = new Button { Content = "Copiar item", Margin = new Thickness(0, 0, 8, 0) };
        copy.Click += (_, _) => SelectCurrent();
        actions.Children.Add(copy);
        var manageButton = new Button { Content = "Gerenciar" };
        manageButton.Click += (_, _) => { Hide(); _manage(); };
        actions.Children.Add(manageButton);
        _enable = new Button { Content = "Ativar histórico", Margin = new Thickness(8, 0, 0, 0) };
        _enable.Click += (_, _) => { enable(); Refresh(); };
        actions.Children.Add(_enable);
        footer.Children.Add(actions);
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
        if (_list is null || _search is null) return;
        var query = _search.Text.Trim();
        var selectedId = (_list.SelectedItem as ListBoxItem)?.Tag is ClipboardHistoryItem active
            ? active.Id : null;
        var candidates = _history.Items
            .Where(i => string.IsNullOrEmpty(query) ||
                        i.Summary.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                        i.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(i => i.IsFavorite || i.IsPinned)
            .ThenByDescending(i => i.CopiedUtc)
            .Take(70)
            .ToArray();
        _list.Items.Clear();
        foreach (var item in candidates)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
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
            if (item.Kind == "image" && _history.LoadThumbnail(item) is { } bitmap)
            {
                var image = new System.Windows.Controls.Image
                {
                    Source = bitmap, Width = 58, Height = 38,
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
                MaxHeight = 38, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis
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
            var control = new ListBoxItem { Tag = item, Content = row, Padding = new Thickness(7, 4, 7, 4) };
            _list.Items.Add(control);
        }
        _list.SelectedItem = _list.Items.Cast<ListBoxItem>()
            .FirstOrDefault(entry => ((ClipboardHistoryItem)entry.Tag).Id == selectedId)
            ?? _list.Items.Cast<ListBoxItem>().FirstOrDefault();
        var protectedCount = _history.Items.Count(i => i.IsFavorite || i.IsPinned);
        var recentCount = _history.Items.Count - protectedCount;
        _hint.Text = !_history.Enabled ? "Histórico pausado · itens salvos permanecem disponíveis."
            : _history.Items.Count == 0 ? "Nenhum item copiado ainda."
            : $"{recentCount}/{_history.MaxEntries} recentes · {protectedCount} favoritos ou fixados protegidos";
        _subtitle.Text = _history.Shortcut.Replace("+", " + ") +
                         " · Enter para copiar · ☆ para favoritar · Esc para fechar";
        _enable.Visibility = _history.Enabled ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SelectCurrent()
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: ClipboardHistoryItem item }) return;
        Hide();
        _choose(item);
    }
}
