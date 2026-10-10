using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SlashText.Design;
using SlashText.Services;

namespace SlashText.Views;

/// <summary>Read-only help. Only the owner can resolve a requested highlight target.</summary>
public sealed class ScreenHelpWindow : Window
{
    private readonly ScreenHelpDefinition _definition;
    private readonly TextBox _search = new();
    private readonly StackPanel _navigation = new();
    private readonly StackPanel _detail = new();
    private readonly TextBlock _count = new();
    private readonly ScrollViewer _detailScroll;
    private string _selectedId = "";
    private bool _compact;
    private readonly Grid _body;
    private readonly Border _navBorder;
    private readonly Button _showTarget;
    private readonly Button _clearSearch;
    public string? RequestedTarget { get; private set; }
    internal Border HelpSurface { get; }
    internal void SearchForEvidence(string text) => _search.Text = text;
    internal void OpenTopic(string id) => SelectTopic(_definition.Topics.Single(t => t.Id == id));
    internal int ResultCount { get; private set; }
    internal void EnableBackdrop() => ModalBackdrop.Attach(this, HelpSurface, Close);

    public ScreenHelpWindow(ScreenHelpDefinition definition)
    {
        _definition = definition;
        Title = definition.Title; Width = Math.Min(1040, SystemParameters.WorkArea.Width - 24);
        Height = Math.Min(780, SystemParameters.WorkArea.Height - 24);
        MinWidth = 420; MinHeight = 380; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Background = Brushes.Transparent; AllowsTransparency = true;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(24, 22, 24, 14) };
        var close = ActionButton("Fechar ajuda", "X", Close); close.Width = 38; close.Height = 38;
        close.Padding = new Thickness(8); close.VerticalAlignment = VerticalAlignment.Top;
        var closeGlyph = HelpGlyph("X", 18); closeGlyph.Margin = new Thickness(0); close.Content = closeGlyph;
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var title = new StackPanel(); title.Children.Add(Text(definition.Title, 23, true));
        title.Children.Add(Text(definition.Subtitle, 12, muted: true)); header.Children.Add(title); root.Children.Add(header);
        var searchRow = new DockPanel { Margin = new Thickness(24, 0, 24, 16) };
        _clearSearch = ActionButton("Limpar busca", "X", () => { _search.Clear(); _search.Focus(); });
        _clearSearch.Content = HelpGlyph("X", 16); _clearSearch.Width = 32; _clearSearch.Height = 30; _clearSearch.Padding = new Thickness(6);
        _clearSearch.SetResourceReference(StyleProperty, "Lab.Shortcuts.Ghost");
        _clearSearch.VerticalAlignment = VerticalAlignment.Center; _clearSearch.HorizontalAlignment = HorizontalAlignment.Right;
        _clearSearch.Margin = new Thickness(0, 0, 5, 0); _clearSearch.Visibility = Visibility.Collapsed;
        _search.SetResourceReference(StyleProperty, "Lab.Field"); _search.Padding = new Thickness(36, 9, 42, 9);
        AutomationProperties.SetName(_search, "O que você quer fazer? Buscar na ajuda"); _search.Height = 38;
        var input = new Grid(); input.Children.Add(_search);
        var searchIcon = HelpGlyph("Search", 16); searchIcon.Margin = new Thickness(12, 0, 0, 0); searchIcon.HorizontalAlignment = HorizontalAlignment.Left;
        searchIcon.VerticalAlignment = VerticalAlignment.Center; searchIcon.IsHitTestVisible = false; searchIcon.SetResourceReference(LabIcon.ForegroundProperty, "Lab.muted"); input.Children.Add(searchIcon);
        var hint = Text("O que você quer fazer?", 12, muted: true); hint.Margin = new Thickness(37, 0, 0, 0); hint.IsHitTestVisible = false; input.Children.Add(hint);
        input.Children.Add(_clearSearch);
        _search.TextChanged += (_, _) => { hint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; _clearSearch.Visibility = _search.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible; };
        searchRow.Children.Add(input); Grid.SetRow(searchRow, 1); root.Children.Add(searchRow);
        _body = new Grid(); _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(245) });
        _body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var nav = new StackPanel { Margin = new Thickness(12, 0, 8, 12) }; nav.Children.Add(_count); nav.Children.Add(_navigation);
        _navBorder = new Border { Child = new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, BorderThickness = new Thickness(0, 0, 1, 0) };
        _navBorder.SetResourceReference(Border.BorderBrushProperty, "Lab.line"); Grid.SetRowSpan(_navBorder, 2); _body.Children.Add(_navBorder);
        _detail.Margin = new Thickness(24, 6, 24, 22);
        _detailScroll = new ScrollViewer { Content = _detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(_detailScroll, 1); Grid.SetRowSpan(_detailScroll, 2); _body.Children.Add(_detailScroll);
        Grid.SetRow(_body, 2); root.Children.Add(_body);
        var footer = new Grid { Margin = new Thickness(24, 14, 24, 18) };
        footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; footer.Children.Add(actions);
        _showTarget = ActionButton("Mostrar na tela", "ArrowUpRight", () =>
        {
            RequestedTarget = _definition.Topics.FirstOrDefault(t => t.Id == _selectedId)?.Target;
            Close();
        });
        _showTarget.Margin = new Thickness(0, 0, 8, 0); actions.Children.Add(_showTarget);
        var done = ActionButton("Entendi", "Check", Close, primary: true); actions.Children.Add(done);
        var footerText = Text("Guia offline · Esc ou clique no fundo para fechar", 11, muted: true);
        footerText.Margin = new Thickness(0, 8, 0, 0); Grid.SetRow(footerText, 1); footer.Children.Add(footerText);
        var footerBorder = new Border { Child = footer, BorderThickness = new Thickness(0, 1, 0, 0) }; footerBorder.SetResourceReference(Border.BorderBrushProperty, "Lab.line");
        Grid.SetRow(footerBorder, 3); root.Children.Add(footerBorder);
        HelpSurface = new Border { Child = root, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(8) };
        HelpSurface.SetResourceReference(Border.BackgroundProperty, "Lab.panel"); HelpSurface.SetResourceReference(Border.BorderBrushProperty, "Lab.line-strong");
        Content = HelpSurface; LabMotion.SetEntrance(HelpSurface, "Popup");
        _search.TextChanged += (_, _) => RebuildNavigation();
        _search.KeyDown += (_, e) => { if (e.Key == Key.Enter && ResultCount > 0) _detailScroll.Focus(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
            else if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; _search.Focus(); _search.SelectAll(); }
        };
        SourceInitialized += (_, _) => ThemeService.ApplyToWindow(this);
        Loaded += (_, _) => _search.Focus();
        HelpSurface.SizeChanged += (_, _) => SetCompact(HelpSurface.ActualWidth < 744);
        RebuildNavigation();
    }

    private void SetCompact(bool compact)
    {
        if (_compact == compact) return; _compact = compact;
        _body.ColumnDefinitions[0].Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(245);
        _body.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRowSpan(_navBorder, compact ? 1 : 2); _navBorder.Height = compact ? 128 : double.NaN;
        Grid.SetColumn(_detailScroll, compact ? 0 : 1); Grid.SetRow(_detailScroll, compact ? 1 : 0);
        Grid.SetRowSpan(_detailScroll, compact ? 1 : 2);
    }
    private static string Normalize(string value) => new(value.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).Select(char.ToLowerInvariant).ToArray());
    private ScreenHelpTopic[] Matches() => _definition.Topics.Where(t => Normalize(t.Group + " " + t.Title + " " + t.Description + " " +
        string.Join(" ", t.Steps) + " " + t.Tip + " " + t.Shortcut).Contains(Normalize(_search.Text.Trim()), StringComparison.Ordinal)).ToArray();
    private void RebuildNavigation()
    {
        var topics = Matches(); ResultCount = topics.Length;
        _navigation.Children.Clear(); _count.Text = $"{topics.Length} tópicos · escolha um recurso";
        _count.FontSize = 11; _count.TextWrapping = TextWrapping.Wrap; _count.Margin = new Thickness(8, 0, 0, 10);
        _count.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted");
        foreach (var group in topics.GroupBy(t => t.Group))
        {
            var heading = Text(group.Key, 10, muted: true); heading.Margin = new Thickness(8, 10, 0, 5); _navigation.Children.Add(heading);
            foreach (var topic in group)
            {
                var button = ActionButton(topic.Title, topic.Icon, () => SelectTopic(topic));
                button.SetResourceReference(StyleProperty, "Lab.Shortcuts.Item");
                button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Margin = new Thickness(0, 2, 0, 2);
                button.Padding = new Thickness(8); button.MinHeight = 36; button.Tag = topic.Id;
                if (topic.Id == _selectedId) { button.SetResourceReference(BackgroundProperty, "Lab.tint"); button.SetResourceReference(BorderBrushProperty, "Lab.accent"); }
                _navigation.Children.Add(button);
            }
        }
        if (topics.Length == 0)
        {
            _selectedId = ""; _detail.Children.Clear();
            _showTarget.Visibility = Visibility.Collapsed;
            _detail.Children.Add(Text("Nenhum recurso encontrado", 22, true));
            _detail.Children.Add(Text("Tente o nome de uma ferramenta ou uma ação, como salvar.", 13, muted: true)); return;
        }
        if (!topics.Any(t => t.Id == _selectedId)) SelectTopic(topics[0]);
    }
    private void SelectTopic(ScreenHelpTopic topic)
    {
        _selectedId = topic.Id;
        foreach (var button in _navigation.Children.OfType<Button>())
        {
            button.SetResourceReference(BackgroundProperty, Equals(button.Tag, topic.Id) ? "Lab.tint" : "Lab.panel");
            button.SetResourceReference(BorderBrushProperty, Equals(button.Tag, topic.Id) ? "Lab.accent" : "Lab.panel");
            button.SetResourceReference(ForegroundProperty, Equals(button.Tag, topic.Id) ? "Lab.accent-text" : "Lab.text");
        }
        _detail.Children.Clear(); _detail.Children.Add(Text(topic.Group, 11, muted: true));
        _detail.Children.Add(Text(topic.Title, 25, true)); _detail.Children.Add(Text(topic.Description, 13));
        if (topic.Shortcut is { Length: > 0 })
        {
            var shortcut = Card(Text("Seu atalho: " + topic.Shortcut, 12, true)); shortcut.Margin = new Thickness(0, 10, 0, 0); _detail.Children.Add(shortcut);
        }
        var demo = new HelpIllustration(topic.Demo, topic.Icon);
        var visual = new StackPanel(); visual.Children.Add(demo);
        var replay = ActionButton("Repetir exemplo", "Play", demo.Replay); replay.HorizontalAlignment = HorizontalAlignment.Right;
        visual.Children.Add(replay); var example = Card(visual); example.Margin = new Thickness(0, 16, 0, 16); _detail.Children.Add(example);
        _detail.Children.Add(Text("Como usar", 14, true));
        for (var i = 0; i < topic.Steps.Length; i++)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 9) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var badge = new Border { CornerRadius = new CornerRadius(14), Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Top, Child = Text((i + 1).ToString(), 12, true) };
            if (badge.Child is TextBlock number) { number.HorizontalAlignment = HorizontalAlignment.Center; number.Margin = new Thickness(0); }
            badge.SetResourceReference(BackgroundProperty, "Lab.tint"); row.Children.Add(badge);
            var instruction = Text(topic.Steps[i], 13); Grid.SetColumn(instruction, 1); row.Children.Add(instruction); _detail.Children.Add(row);
        }
        var tipContent = new StackPanel(); tipContent.Children.Add(Text("Dica e detalhes", 12, true));
        tipContent.Children.Add(Text(topic.Tip, 12, muted: true));
        var tip = Card(tipContent); tip.Margin = new Thickness(0, 10, 0, 0);
        tip.SetResourceReference(BackgroundProperty, "Lab.raised"); _detail.Children.Add(tip);
        _showTarget.Visibility = topic.Target is { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        _detailScroll.ScrollToTop(); LabMotion.SetEntrance(_detail, "Page");
        if (_detail.IsLoaded) LabMotion.PlayEntrance(_detail);
    }
    private static TextBlock Text(string text, double size, bool bold = false, bool muted = false)
    {
        var value = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3), VerticalAlignment = VerticalAlignment.Center };
        value.SetResourceReference(TextBlock.ForegroundProperty, muted ? "Lab.muted" : "Lab.text"); return value;
    }
    private static Border Card(UIElement content)
    {
        var card = new Border { Child = content, Padding = new Thickness(14), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
        card.SetResourceReference(BackgroundProperty, "Lab.panel"); card.SetResourceReference(BorderBrushProperty, "Lab.line"); return card;
    }
    private static LabIcon HelpGlyph(string name, double size)
    { var icon = new LabIcon { Kind = name, Width = size, Height = size, Margin = new Thickness(0, 0, 8, 0) }; icon.SetResourceReference(LabIcon.ForegroundProperty, "Lab.text"); return icon; }
    private Button ActionButton(string label, string icon, Action action, bool primary = false)
    {
        var button = new Button { ToolTip = label }; button.SetResourceReference(StyleProperty, primary ? "Lab.Pilot.PrimaryButton" : "Lab.Button");
        AutomationProperties.SetName(button, label);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var glyph = HelpGlyph(icon, 16); glyph.SetBinding(LabIcon.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = button }); row.Children.Add(glyph);
        var text = Text(label, 12); text.MaxWidth = 170; text.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = button }); row.Children.Add(text);
        button.Content = row; button.Click += (_, _) => action(); return button;
    }

    private sealed class HelpIllustration : Grid
    {
        private readonly FrameworkElement _animated;
        public HelpIllustration(string kind, string icon)
        {
            Height = 160; ClipToBounds = true;
            var canvas = new Canvas { Width = 330, Height = 130, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            var desktop = new Border { Width = 330, Height = 118, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
            desktop.SetResourceReference(BackgroundProperty, "Lab.canvas"); desktop.SetResourceReference(BorderBrushProperty, "Lab.line"); canvas.Children.Add(desktop);
            var caption = Text("SLASHDESK · EXEMPLO ILUSTRADO", 9, muted: true); Canvas.SetLeft(caption, 14); Canvas.SetTop(caption, 6); canvas.Children.Add(caption);
            if (kind == "region")
            {
                var selection = new Border { Width = 196, Height = 70, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(3) };
                selection.SetResourceReference(BorderBrushProperty, "Lab.accent");
                var content = new Grid();
                var idea = Text("Uma área para suas ideias", 11, true); idea.HorizontalAlignment = HorizontalAlignment.Center; idea.Margin = new Thickness(10, 0, 10, 0);
                content.Children.Add(idea);
                foreach (var (x, y) in new[] { (0,0), (1,0), (2,0), (0,1), (2,1), (0,2), (1,2), (2,2) })
                {
                    var handle = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), BorderThickness = new Thickness(1),
                        HorizontalAlignment = x == 0 ? HorizontalAlignment.Left : x == 2 ? HorizontalAlignment.Right : HorizontalAlignment.Center,
                        VerticalAlignment = y == 0 ? VerticalAlignment.Top : y == 2 ? VerticalAlignment.Bottom : VerticalAlignment.Center };
                    handle.SetResourceReference(BackgroundProperty, "Lab.panel"); handle.SetResourceReference(BorderBrushProperty, "Lab.accent"); content.Children.Add(handle);
                }
                selection.Child = content; Canvas.SetLeft(selection, 44); Canvas.SetTop(selection, 31); canvas.Children.Add(selection); _animated = selection;
            }
            else
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var labels = kind switch { "outputs" => new[] { "Copiar", "Salvar", "Concluir" }, "media" => new[] { "Imagem", "Vídeo", "GIF" },
                    "toolbar" => new[] { "Área", "Anotar", "Finalizar" }, "recents" => new[] { "Recente 1", "Recente 2", "Recente 3" },
                    "time" => new[] { "Preparar", "Aguardar", "Capturar" }, "emoji" => new[] { "Catálogo", "Meus emojis", "Inserir" },
                    "privacy" => new[] { "Selecionar", "Intensidade", "Conferir" }, "text" => new[] { "Fonte", "Seu texto", "Inserir" },
                    "shape" => new[] { "Forma", "Contorno", "Preencher" },
                    "objects" => new[] { "Selecionar", "Editar", "Aplicar" },
                    "snippet" => new[] { "/comando", "Seu conteúdo", "Salvar" },
                    "workspace" => new[] { "Lista", "Editor", "Variáveis" },
                    "variable" => new[] { "Cursor", "{{nome}}", "Prévia" },
                    "format" => new[] { "Selecionar", "Formatar", "Conferir" },
                    "accent" => new[] { "A + Espaço", "á à â ã", "Soltar → á" },
                    "accent-preview" => new[] { "Letra A", "á à â ã", "Campo de teste" },
                    "accent-preferences" => new[] { "Atraso", "Posição", "Unicode" },
                    "accent-sets" => new[] { "PT-BR", "Conjuntos", "Caracteres" },
                    "accent-exclude" => new[] { "Processos", "Separar por ;", "Salvar" },
                    "statistics-shortcuts" => new[] { "Uso real", "Contagens", "Ranking" },
                    "statistics-captures" => new[] { "Histórico", "Área / mídia", "Contagens" },
                    "statistics-time" => new[] { "Caracteres", "÷ 200", "Minutos" },
                    "statistics-accents" => new[] { "Inserir", "Contagem", "Favoritos" },
                    "statistics-privacy" => new[] { "Local", "Sem conteúdo", "Sem envio" },
                    _ => new[] { "Ferramenta", "Cor", "Desenhar" } };
                for (var i = 0; i < 3; i++)
                {
                    var cell = new StackPanel(); cell.Children.Add(HelpGlyph(i == 0 ? icon : i == 1 ? "SlidersHorizontal" : "Check", 23)); cell.Children.Add(Text(labels[i], 10, true));
                    var box = Card(cell); box.Width = 96; box.Height = 68; box.Padding = new Thickness(9); box.Margin = new Thickness(0, 0, 6, 0); row.Children.Add(box);
                }
                Canvas.SetLeft(row, 14); Canvas.SetTop(row, 35); canvas.Children.Add(row); _animated = row;
            }
            Children.Add(new Viewbox { Child = canvas, Stretch = Stretch.Uniform, Margin = new Thickness(0, 4, 0, 4) });
        }
        public void Replay()
        {
            if (!LabMotion.Allowed(this)) return;
            var move = new TranslateTransform(); _animated.RenderTransform = move;
            var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1200), FillBehavior = FillBehavior.Stop };
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(20, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(550)), new CubicEase { EasingMode = EasingMode.EaseInOut }));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1200)), new CubicEase { EasingMode = EasingMode.EaseInOut }));
            move.BeginAnimation(TranslateTransform.XProperty, animation);
            if (_animated is Border selection)
                selection.BeginAnimation(WidthProperty, new DoubleAnimation(196, 164, TimeSpan.FromMilliseconds(600))
                { AutoReverse = true, FillBehavior = FillBehavior.Stop });
        }
    }
}
