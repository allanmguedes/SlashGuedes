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
        var subtitle = new TextBlock { Text = "Win + Shift + V   •   Enter para copiar   •   Esc para fechar",
            Margin = new Thickness(0, 4, 0, 0), FontSize = 11 };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted");
        header.Children.Add(subtitle);
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

        _list = new ListBox { DisplayMemberPath = "Summary",
            BorderThickness = new Thickness(0), FontSize = 13,
            HorizontalContentAlignment = HorizontalAlignment.Stretch };
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
        _list.ItemsSource = _history.Items
            .Where(i => string.IsNullOrEmpty(query) ||
                        i.Summary.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                        i.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .Take(50).ToList();
        _list.SelectedIndex = _list.Items.Count > 0 ? 0 : -1;
        _hint.Text = !_history.Enabled ? "Histórico pausado. Ative para registrar novas cópias."
            : _history.Items.Count == 0 ? "Ainda não há cópias registradas." 
            : $"{_history.Items.Count} itens salvos · histórico local";
        _enable.Visibility = _history.Enabled ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SelectCurrent()
    {
        if (_list.SelectedItem is not ClipboardHistoryItem item) return;
        Hide();
        _choose(item);
    }
}
