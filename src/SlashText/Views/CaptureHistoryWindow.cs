using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SlashText.Design;
using SlashText.Models;
using SlashText.Services;

namespace SlashText.Views;

/// <summary>Metadata-only full history. Only the current page is materialized.</summary>
public sealed class CaptureHistoryWindow : Window
{
    private const int PageSize = 25;
    private readonly CaptureRecord[] _records;
    private readonly Func<CaptureRecord, string> _resolvePath;
    private readonly TextBox _search = new();
    private readonly ComboBox _filter = new();
    private readonly StackPanel _rows = new();
    private readonly TextBlock _status = new();
    private readonly Button _previous;
    private readonly Button _next;
    private int _page;
    public CaptureRecord? RequestedRecord { get; private set; }
    public string? RequestedAction { get; private set; }
    internal int VisibleCount => _rows.Children.Count;
    internal void SearchForEvidence(string value) => _search.Text = value;
    internal void PageForEvidence(int page) { _page = page; Refresh(); }
    internal int PageForEvidenceValue => _page;
    internal FrameworkElement Surface { get; private set; } = null!;

    public CaptureHistoryWindow(IEnumerable<CaptureRecord> records, Func<CaptureRecord, string> resolvePath, string search, string filter)
    {
        _records = records.ToArray(); _resolvePath = resolvePath;
        Title = UiText.Get("Histórico de capturas"); Width = 900; Height = 700; MinWidth = 500; MinHeight = 400;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new DockPanel(); var close = Button(UiText.Get("Fechar"), "X", Close); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        header.Children.Add(new TextBlock { Text = UiText.Get("Histórico de capturas"), FontSize = 23, FontWeight = FontWeights.SemiBold }); root.Children.Add(header);
        var filters = new WrapPanel { Margin = new Thickness(0, 18, 0, 16) };
        _search.Width = 300; _search.Margin = new Thickness(0, 0, 12, 6); _search.ToolTip = UiText.Get("Nome, tipo ou data (dd/MM/aaaa)");
        AutomationProperties.SetName(_search, UiText.Get("Buscar capturas por nome ou data")); _search.SetResourceReference(StyleProperty, "Lab.Field");
        _search.Text = search;
        var searchColumn = new StackPanel();
        var searchLabel = new TextBlock { Text = UiText.Get("Buscar por nome, tipo ou data"), FontSize = 11, Margin = new Thickness(0, 0, 0, 5) };
        searchLabel.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted"); searchColumn.Children.Add(searchLabel); searchColumn.Children.Add(_search);
        filters.Children.Add(searchColumn);
        var clearSearch = Button(UiText.Get("Limpar busca"), "X", () => { _search.Clear(); _search.Focus(); }); clearSearch.VerticalAlignment = VerticalAlignment.Bottom; filters.Children.Add(clearSearch);
        _filter.VerticalAlignment = VerticalAlignment.Bottom; _filter.Width = 155; _filter.Margin = new Thickness(12, 0, 0, 6); _filter.SetResourceReference(StyleProperty, "Lab.Combo");
        foreach (var (label, key) in new[] { (UiText.Get("Todos"), "all"), ("Monitor", "monitor"), (UiText.Get("Região"), "regiao"), (UiText.Get("Janela"), "janela"),
            (UiText.Get("Captura longa"), "rolagem"), (UiText.Get("Vídeo MP4"), "video"), ("GIF", "gif") }) _filter.Items.Add(new ComboBoxItem { Content = label, Tag = key });
        _filter.SelectedItem = _filter.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, filter)) ?? _filter.Items[0];
        AutomationProperties.SetName(_filter, UiText.Get("Filtrar histórico por tipo")); filters.Children.Add(_filter); Grid.SetRow(filters, 1); root.Children.Add(filters);
        var scroll = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        var footer = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Right); footer.Children.Add(actions);
        _previous = Button(UiText.Get("Anterior"), "ArrowLeft", () => { _page--; Refresh(); scroll.ScrollToTop(); });
        _next = Button(UiText.Get("Próxima"), "ChevronRight", () => { _page++; Refresh(); scroll.ScrollToTop(); });
        actions.Children.Add(_previous); actions.Children.Add(_next); footer.Children.Add(_status); Grid.SetRow(footer, 3); root.Children.Add(footer);
        var surface = new Border { Child = root, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        surface.SetResourceReference(Border.BackgroundProperty, "Lab.panel"); surface.SetResourceReference(Border.BorderBrushProperty, "Lab.line");
        Surface = surface; Content = surface; SetResourceReference(ForegroundProperty, "Lab.text"); LabMotion.SetEntrance(surface, "Popup");
        _search.TextChanged += (_, _) => { _page = 0; Refresh(); scroll.ScrollToTop(); };
        _filter.SelectionChanged += (_, _) => { _page = 0; Refresh(); scroll.ScrollToTop(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); }
            else if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; _search.Focus(); _search.SelectAll(); } };
        SourceInitialized += (_, _) => ThemeService.ApplyToWindow(this);
        Loaded += (_, _) => { ModalBackdrop.Attach(this, surface, Close); _search.Focus(); };
        Refresh();
    }
    private void Refresh()
    {
        var records = CaptureHistoryQuery.Filter(_records, (_filter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all", _search.Text);
        var pages = Math.Max(1, (records.Count + PageSize - 1) / PageSize); _page = Math.Clamp(_page, 0, pages - 1);
        _rows.Children.Clear();
        foreach (var record in records.Skip(_page * PageSize).Take(PageSize))
        {
            var path = _resolvePath(record); var available = File.Exists(path);
            var pending = record.RequiresFileApproval && !available;
            var row = new WrapPanel { Margin = new Thickness(10) };
            var text = new StackPanel { Width = 290, Margin = new Thickness(0, 0, 12, 8) };
            text.Children.Add(new TextBlock { Text = Path.GetFileName(string.IsNullOrEmpty(path) ? record.FilePath : path), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = path, FontWeight = FontWeights.SemiBold });
            var detail = new TextBlock { Text = $"{CaptureHistoryQuery.Label(record)} · {record.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm} · {record.Width}×{record.Height}" + (pending ? " · " + UiText.Get("Confirme o arquivo do histórico") : available ? "" : UiText.Get(" · Arquivo ausente")), FontSize = 11, TextWrapping = TextWrapping.Wrap };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted"); text.Children.Add(detail); row.Children.Add(text);
            foreach (var (label, icon, action) in new[] { (UiText.Get("Abrir"), "ExternalLink", "open"), (UiText.Get("Copiar"), "Copy", "copy"), (UiText.Get("Editar"), "PenLine", "edit"), (UiText.Get("Excluir"), "Trash2", "delete") })
            {
                if (action == "edit" && record.MediaKind != "image") continue;
                var button = Button(label, icon, () => { RequestedRecord = record; RequestedAction = action; Close(); });
                button.IsEnabled = available || pending || action == "delete"; row.Children.Add(button);
            }
            var border = new Border { Child = row, Margin = new Thickness(0, 0, 0, 8), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
            border.SetResourceReference(Border.BackgroundProperty, "Lab.raised"); border.SetResourceReference(Border.BorderBrushProperty, "Lab.line"); _rows.Children.Add(border);
        }
        _status.Text = records.Count == 0 ? UiText.Get("Nenhuma captura encontrada. Limpe a busca ou troque o filtro.") : UiText.Format($"{records.Count:N0} capturas · página {_page + 1} de {pages}");
        _status.TextWrapping = TextWrapping.Wrap; _status.VerticalAlignment = VerticalAlignment.Center;
        _previous.IsEnabled = _page > 0; _next.IsEnabled = _page + 1 < pages;
    }
    private static Button Button(string text, string icon, Action action)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new LabIcon { Kind = icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 7, 0) });
        var label = new TextBlock { Text = text }; label.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) }); content.Children.Add(label);
        var button = new Button { Content = content, Margin = new Thickness(0, 0, 6, 6), ToolTip = text }; button.SetResourceReference(StyleProperty, "Lab.Button");
        AutomationProperties.SetName(button, text); button.Click += (_, _) => action(); return button;
    }
}
