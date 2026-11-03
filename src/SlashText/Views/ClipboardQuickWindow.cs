using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
    private readonly Action<IReadOnlyList<ClipboardHistoryItem>> _pasteMany;
    private readonly List<string> _orderedImageIds = [];
    private readonly HashSet<string> _selectedOrganizeIds = new(StringComparer.Ordinal);
    private bool _multiSelectMode;
    private bool _organizeMode;
    private bool _restoringMultiSelection;
    private readonly Button _multiButton;
    private readonly Button _organizeButton;
    private readonly Button _pasteManyButton;
    private readonly TextBlock _multiHint;
    private readonly Action _manage;
    private readonly Action<double, double> _onMoved;
    private readonly StackPanel _dragHeader;
    private double? _preferredLeft;
    private double? _preferredTop;
    private bool _isDraggingPopup;
    private readonly TextBox _search;
    // WPF text editing stays on the real TextBox; this independent TextBlock
    // makes the query legible even on machines where PART_ContentHost paints
    // editable text invisibly despite a correct effective Foreground brush.
    private readonly TextBlock _searchVisibleText;
    private readonly ListBox _list;
    private readonly TextBlock _hint;
    private readonly Button _enable;
    private readonly TextBlock _subtitle;
    private readonly ComboBox _categoryFilter;
    private readonly ComboBox _moveToCategory;
    private readonly Button _renameCategoryButton;
    private readonly Button _deleteCategoryButton;
    private readonly StackPanel _categoryEditor;
    private readonly TextBox _categoryNameBox;
    private string? _editingCategory;
    private sealed record CategoryFilter(string Name, bool All, string Label)
    {
        public override string ToString() => Label;
    }
    private sealed record CategoryTarget(string Name, string Label)
    {
        public override string ToString() => Label;
    }
    private readonly Button _moveButton;
    private readonly Button _deleteButton;
    private readonly TextBlock _emptyMessage;
    private bool _refreshingCategories;
    private readonly DispatcherTimer _searchRefreshTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(140)
    };

    public ClipboardQuickWindow(ClipboardHistoryService history,
        Action<ClipboardHistoryItem> choose, Action<ClipboardHistoryItem> copyOnly,
        Action<IReadOnlyList<ClipboardHistoryItem>> pasteMany,
        Action manage, Action enable,
        double? savedLeft, double? savedTop, Action<double, double> onMoved)
    {
        _history = history;
        _choose = choose;
        _copyOnly = copyOnly;
        _pasteMany = pasteMany;
        _manage = manage;
        _onMoved = onMoved;
        if (savedLeft is { } x && savedTop is { } y &&
            double.IsFinite(x) && double.IsFinite(y))
        {
            _preferredLeft = x;
            _preferredTop = y;
        }
        Width = 560;
        Height = 630;
        MinWidth = 480;
        MinHeight = 420;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // Theme-aware resources must remain dynamic: never snapshot a color
        // when constructing a popup that survives theme switches.
        SetResourceReference(BackgroundProperty, "Lab.bg");

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
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Child = grid;
        Content = root;

        _dragHeader = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 14),
            Background = Brushes.Transparent,
            Cursor = Cursors.SizeAll,
            ToolTip = "Segure e arraste este cabeçalho para mover o Alt+V."
        };
        _dragHeader.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 1 || e.ButtonState != MouseButtonState.Pressed) return;
            var previousLeft = Left;
            var previousTop = Top;
            _isDraggingPopup = true;
            try { DragMove(); }
            catch (InvalidOperationException) { /* Mouse was released before drag started. */ }
            finally
            {
                _isDraggingPopup = false;
                if (IsVisible && (Math.Abs(Left - previousLeft) > 0.5 ||
                                  Math.Abs(Top - previousTop) > 0.5))
                {
                    _preferredLeft = Left;
                    _preferredTop = Top;
                    _onMoved(Left, Top);
                }
            }
            e.Handled = true;
        };
        var title = new TextBlock { Text = "SLASHGUEDES   /   HISTÓRICO", FontSize = 17,
            FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Lab.text");
        _dragHeader.Children.Add(title);
        _subtitle = new TextBlock { Text = "Enter para colar · Esc para fechar",
            Margin = new Thickness(0, 4, 0, 0), FontSize = 11 };
        _subtitle.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted");
        _dragHeader.Children.Add(_subtitle);
        Grid.SetRow(_dragHeader, 0);
        grid.Children.Add(_dragHeader);

        _search = new TextBox { Height = 38, Margin = new Thickness(0, 0, 0, 12),
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Pesquise textos, imagens e categorias do histórico." };
        // The popup already has a proven TextBlock renderer; its template
        // hides only the shared glyph overlay to prevent duplicate labels.
        _search.Tag = "SkipReadableOverlay";
        _search.SetResourceReference(FrameworkElement.StyleProperty, "Lab.Field");
        _search.SetResourceReference(Control.BackgroundProperty, "Lab.input");
        // Keep editing, IME, keyboard navigation and selection on the TextBox,
        // but render the query via a TextBlock like the visible header labels.
        // Its text is intentionally transparent so a broken native text
        // renderer cannot duplicate or clash with the overlay.
        _search.Foreground = Brushes.Transparent;
        _search.SetResourceReference(TextBox.CaretBrushProperty, "Lab.accent-text");
        _search.SetResourceReference(TextBox.SelectionBrushProperty, "Lab.accent");
        _searchVisibleText = new TextBlock
        {
            Text = "Pesquisar no histórico...",
            IsHitTestVisible = false,
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 14,
            Margin = new Thickness(12, 0, 15, 12)
        };
        _searchVisibleText.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted");
        // Throttle filtering rather than allocating new cards on every keystroke.
        _searchRefreshTimer.Tick += (_, _) =>
        {
            _searchRefreshTimer.Stop();
            if (IsVisible) Refresh();
        };
        _search.TextChanged += (_, _) =>
        {
            UpdateSearchTextDisplay();
            _searchRefreshTimer.Stop();
            _searchRefreshTimer.Start();
        };
        _search.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && _list is { } list) { list.Focus(); if (list.Items.Count > 0) list.SelectedIndex = 0; e.Handled = true; }
            if (e.Key == Key.Enter)
            {
                if (_multiSelectMode) PasteSelectedImages();
                else if (!_organizeMode) SelectCurrent();
                e.Handled = true;
            }
        };
        var filterRow = new Grid();
        filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(165) });
        Grid.SetColumn(_search, 0);
        filterRow.Children.Add(_search);
        // Layer after the TextBox: the user still types into the real input,
        // while visible glyphs come from the reliable TextBlock renderer.
        Grid.SetColumn(_searchVisibleText, 0);
        filterRow.Children.Add(_searchVisibleText);
        _categoryFilter = new ComboBox
        {
            Height = 38, Margin = new Thickness(8, 0, 0, 12),
            ToolTip = "Exiba todas as cópias, as sem categoria ou uma categoria específica."
        };
        _categoryFilter.SetResourceReference(FrameworkElement.StyleProperty, "Lab.Combo");
        _categoryFilter.SetResourceReference(Control.ForegroundProperty, "Lab.text");
        _categoryFilter.SelectionChanged += (_, _) =>
        {
            if (!_refreshingCategories) Refresh();
        };
        Grid.SetColumn(_categoryFilter, 1);
        filterRow.Children.Add(_categoryFilter);
        Grid.SetRow(filterRow, 1);
        grid.Children.Add(filterRow);

        // Category management stays INSIDE Alt+V, avoiding focus loss to a dialog.
        var categoryTools = new StackPanel { Margin = new Thickness(0, 0, 0, 7) };
        var categoryButtons = new WrapPanel();
        var createCategoryButton = new Button
        {
            Content = "+ Nova categoria", Margin = new Thickness(0, 0, 7, 0),
            ToolTip = "Crie uma pasta de cópias sem abrir o gerenciador."
        };
        createCategoryButton.Click += (_, _) => BeginCategoryEdit(rename: false);
        categoryButtons.Children.Add(createCategoryButton);
        _renameCategoryButton = new Button
        {
            Content = "Renomear", Margin = new Thickness(0, 0, 7, 0),
            ToolTip = "Renomear a categoria exibida (mantém todas as cópias)."
        };
        _renameCategoryButton.Click += (_, _) => BeginCategoryEdit(rename: true);
        categoryButtons.Children.Add(_renameCategoryButton);
        _deleteCategoryButton = new Button
        {
            Content = "Excluir categoria",
            ToolTip = "Apaga somente a categoria; os itens permanecem salvos."
        };
        _deleteCategoryButton.Click += async (_, _) => await DeleteCurrentCategoryAsync();
        categoryButtons.Children.Add(_deleteCategoryButton);
        categoryTools.Children.Add(categoryButtons);

        _categoryEditor = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Collapsed
        };
        _categoryNameBox = new TextBox
        {
            Width = 290, Height = 32, MaxLength = ClipboardCategoryRules.MaximumNameLength,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
            ToolTip = "Nome da categoria (1 a 50 caracteres)."
        };
        _categoryNameBox.SetResourceReference(FrameworkElement.StyleProperty, "Lab.Field");
        _categoryNameBox.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await SaveCategoryEditAsync();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelCategoryEdit();
            }
        };
        _categoryEditor.Children.Add(_categoryNameBox);
        var saveCategoryButton = new Button
        {
            Content = "Salvar", Margin = new Thickness(0, 0, 7, 0)
        };
        saveCategoryButton.Click += async (_, _) => await SaveCategoryEditAsync();
        _categoryEditor.Children.Add(saveCategoryButton);
        var cancelCategoryButton = new Button { Content = "Cancelar" };
        cancelCategoryButton.Click += (_, _) => CancelCategoryEdit();
        _categoryEditor.Children.Add(cancelCategoryButton);
        categoryTools.Children.Add(_categoryEditor);
        Grid.SetRow(categoryTools, 2);
        grid.Children.Add(categoryTools);

        _list = new ListBox
        {
            BorderThickness = new Thickness(0), FontSize = 13,
            Padding = new Thickness(0, 4, 4, 4),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        _list.SetResourceReference(Control.BackgroundProperty, "Lab.surface");
        _list.SetResourceReference(Control.ForegroundProperty, "Lab.text");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
        ScrollViewer.SetCanContentScroll(_list, true);
        VirtualizingPanel.SetIsVirtualizing(_list, true);
        VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(_list, ScrollUnit.Pixel);
        // Cards and thumbnails are created only when scrolled into view.
        var cardTemplate = new DataTemplate(typeof(ClipboardHistoryItem));
        var cardFactory = new FrameworkElementFactory(typeof(ClipboardCardView));
        // Binding keeps a Window reference out of shared template values.
        cardFactory.SetBinding(ClipboardCardView.OwnerProperty,
            new Binding { RelativeSource = new RelativeSource(
                RelativeSourceMode.FindAncestor, typeof(ClipboardQuickWindow), 1) });
        cardTemplate.VisualTree = cardFactory;
        _list.ItemTemplate = cardTemplate;
        _list.MouseDoubleClick += (_, e) =>
        {
            // A double-click on a star or pin should not paste into another app.
            if (!_multiSelectMode && !_organizeMode &&
                !IsInsideButton(e.OriginalSource as DependencyObject))
                SelectCurrent();
        };
        _list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                if (!_organizeMode) SelectCurrent();
                e.Handled = true;
            }
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
        Grid.SetRow(listArea, 3);
        grid.Children.Add(listArea);

        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        _hint = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8) };
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted");
        footer.Children.Add(_hint);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
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
        _enable = new Button { Content = "Pausar histórico", Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Pausar impede novas capturas, mas mantém itens existentes." };
        _enable.Click += (_, _) => { enable(); Refresh(); };
        actions.Children.Add(_enable);
        footer.Children.Add(actions);

        // An explicit mode prevents accidental multi-paste when the user
        // simply wants to select/copy a single text item.
        var multiActions = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
        _organizeButton = new Button
        {
            Content = "Selecionar cópias", Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Selecione textos e imagens para mover juntos, sem colar nada."
        };
        _organizeButton.Click += (_, _) => SetOrganizeMode(!_organizeMode);
        multiActions.Children.Add(_organizeButton);
        _multiButton = new Button
        {
            Content = "Selecionar vários prints", Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Escolha os prints pela ordem em que devem ser colados."
        };
        _multiButton.Click += (_, _) => SetMultiSelectMode(!_multiSelectMode);
        multiActions.Children.Add(_multiButton);
        _pasteManyButton = new Button
        {
            Content = "Colar prints em sequência", IsEnabled = false,
            ToolTip = "Cola cada imagem separada no programa anterior, sem pressionar Enter."
        };
        _pasteManyButton.Click += (_, _) => PasteSelectedImages();
        multiActions.Children.Add(_pasteManyButton);
        footer.Children.Add(multiActions);
        _multiHint = new TextBlock
        {
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap, FontSize = 11,
            Visibility = Visibility.Collapsed
        };
        _multiHint.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted");
        footer.Children.Add(_multiHint);

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
            ToolTip = "Escolha a categoria para o item ou as cópias selecionadas."
        };
        _moveToCategory.SetResourceReference(FrameworkElement.StyleProperty, "Lab.Combo");
        _moveToCategory.SetResourceReference(Control.ForegroundProperty, "Lab.text");
        DockPanel.SetDock(_moveToCategory, Dock.Left);
        categoriesRow.Children.Add(_moveToCategory);
        _moveButton = new Button { Content = "Mover", Margin = new Thickness(0, 0, 8, 0) };
        _moveButton.Click += async (_, _) =>
        {
            string[] ids;
            if (_organizeMode) ids = _selectedOrganizeIds.ToArray();
            else if (_list.SelectedItem is ClipboardHistoryItem current) ids = [current.Id];
            else return;
            if (ids.Length == 0) return;
            var target = (_moveToCategory.SelectedItem as CategoryTarget)?.Name ?? string.Empty;
            _moveButton.IsEnabled = false;
            try
            {
                await _history.MoveItemsAsync(ids, target);
                if (_organizeMode) SetOrganizeMode(false);
                Refresh();
                SelectCategoryFilter(target);
            }
            catch (Exception error)
            {
                MessageBox.Show(this, "Não foi possível mover as cópias: " + error.Message,
                    "SlashGuedes", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally { UpdateMultiControls(); }
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
            if (_list.SelectedItem is not ClipboardHistoryItem current) return;
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
        Grid.SetRow(footer, 4);
        grid.Children.Add(footer);
        _list.SelectionChanged += OnListSelectionChanged;

        // All footer buttons and cards share the same palette; don't apply
        // AuxiliaryWindowTheme.Apply(), which would Close rather than Hide on Esc.
        AuxiliaryWindowTheme.ApplyTree(root);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
        };
        Deactivated += (_, _) =>
        {
            if (_isDraggingPopup) return;
            _searchRefreshTimer.Stop();
            Hide();
        };
    }

    public void Open()
    {
        _searchRefreshTimer.Stop();
        // A previously dismissed organizer must not intercept Enter on the next Alt+V.
        if (_organizeMode) SetOrganizeMode(false);
        Refresh();
        var cursor = Forms.Control.MousePosition;
        var dpi = VisualTreeHelper.GetDpi(this);
        var sx = dpi.DpiScaleX;
        var sy = dpi.DpiScaleY;
        var useSavedPosition = _preferredLeft is { } savedX &&
                               _preferredTop is { } savedY &&
                               double.IsFinite(savedX) && double.IsFinite(savedY);
        // Use the remembered monitor when possible. Screen.FromPoint falls
        // back to the nearest connected monitor if one was unplugged.
        var anchor = useSavedPosition
            ? new System.Drawing.Point(
                (int)Math.Clamp(savedX * sx, int.MinValue + 1d, int.MaxValue - 1d),
                (int)Math.Clamp(savedY * sy, int.MinValue + 1d, int.MaxValue - 1d))
            : cursor;
        var screen = Forms.Screen.FromPoint(anchor).WorkingArea;
        Width = Math.Min(560, Math.Max(MinWidth, screen.Width / sx - 24));
        Height = Math.Min(630, Math.Max(MinHeight, screen.Height / sy - 24));
        var minimumLeft = screen.Left / sx;
        var minimumTop = screen.Top / sy;
        var maximumLeft = Math.Max(minimumLeft, screen.Right / sx - Width);
        var maximumTop = Math.Max(minimumTop, screen.Bottom / sy - Height);
        Left = Math.Clamp(useSavedPosition ? savedX : cursor.X / sx - Width / 2,
            minimumLeft, maximumLeft);
        Top = Math.Clamp(useSavedPosition ? savedY : cursor.Y / sy - 28,
            minimumTop, maximumTop);
        Show();
        Activate();
        _search.Focus();
        _search.SelectAll();
    }

    private void UpdateSearchTextDisplay()
    {
        var hasText = _search.Text.Length != 0;
        _searchVisibleText.Text = hasText ? _search.Text : "Pesquisar no histórico...";
        _searchVisibleText.SetResourceReference(TextBlock.ForegroundProperty,
            hasText ? "Lab.text" : "Lab.muted");
    }

    // UI regression: verify the ACTUAL visible presentation is synchronized
    // with the input. Testing TextBox.Foreground alone missed this bug.
    internal void ValidateSearchTextPresentation()
    {
        var original = _search.Text;
        try
        {
            _search.Text = "Texto visível 123";
            if (_searchVisibleText.Text != _search.Text ||
                _searchVisibleText.Visibility != Visibility.Visible ||
                _searchVisibleText.IsHitTestVisible)
                throw new InvalidOperationException(
                    "A pesquisa não está sincronizada com o texto visível.");
            AuxiliaryWindowTheme.RequireReadableText(
                _searchVisibleText, "Clipboard.SearchText", "Lab.input");
            _search.Text = "";
            if (_searchVisibleText.Text != "Pesquisar no histórico...")
                throw new InvalidOperationException(
                    "O placeholder da pesquisa não foi restaurado.");
        }
        finally { _search.Text = original; }
    }

    internal void ValidateMovableHeader()
    {
        if (_dragHeader.Cursor != Cursors.SizeAll ||
            _dragHeader.Background != Brushes.Transparent)
            throw new InvalidOperationException("O Alt+V não possui área de arraste ativa.");
    }

    internal void ValidateContrast()
    {
        // The editable TextBox foreground is deliberately transparent.
        // The visible text is drawn by an accessible high-contrast TextBlock.
        AuxiliaryWindowTheme.RequireReadableText(
            _searchVisibleText, "Clipboard.SearchText", "Lab.input");
        AuxiliaryWindowTheme.RequireReadable(_categoryFilter, "Clipboard.Filter");
        AuxiliaryWindowTheme.RequireReadable(_moveToCategory, "Clipboard.MoveCategory");
    }

    public void Refresh()
    {
        if (_list is null || _search is null || _categoryFilter is null) return;
        var chosen = _categoryFilter.SelectedItem as CategoryFilter;
        var categories = new[]
        {
            new CategoryFilter(string.Empty, true, $"Todas as categorias ({_history.Items.Count})"),
            new CategoryFilter(string.Empty, false,
                $"Sem categoria ({_history.Items.Count(item => string.IsNullOrEmpty(item.Category))})")
        }.Concat(_history.Categories.Select(name => new CategoryFilter(name, false,
            $"{name} ({_history.Items.Count(item => item.Category.Equals(name, StringComparison.OrdinalIgnoreCase))})")))
            .ToArray();
        var availableTargets = new[] { new CategoryTarget(string.Empty, "Sem categoria") }
            .Concat(_history.Categories.Select(name => new CategoryTarget(name, name))).ToArray();
        var selectedTarget = (_moveToCategory.SelectedItem as CategoryTarget)?.Name ?? string.Empty;
        if (!_moveToCategory.Items.OfType<CategoryTarget>().SequenceEqual(availableTargets))
        {
            _moveToCategory.ItemsSource = availableTargets;
            _moveToCategory.SelectedItem = availableTargets.FirstOrDefault(c =>
                c.Name.Equals(selectedTarget, StringComparison.OrdinalIgnoreCase))
                ?? availableTargets[0];
        }
        if (!_categoryFilter.Items.OfType<CategoryFilter>().SequenceEqual(categories))
        {
            _refreshingCategories = true;
            try
            {
                _categoryFilter.ItemsSource = categories;
                _categoryFilter.SelectedItem = categories.FirstOrDefault(c =>
                    chosen is not null && c.All == chosen.All &&
                    c.Name.Equals(chosen.Name, StringComparison.OrdinalIgnoreCase)) ?? categories[0];
            }
            finally { _refreshingCategories = false; }
        }
        var filter = _categoryFilter.SelectedItem as CategoryFilter ?? categories[0];
        var editableCategory = !filter.All && filter.Name.Length > 0;
        _renameCategoryButton.IsEnabled = editableCategory;
        _deleteCategoryButton.IsEnabled = editableCategory;
        var query = _search.Text.Trim();
        var selectedId = (_list.SelectedItem as ClipboardHistoryItem)?.Id;
        _orderedImageIds.RemoveAll(id => !_history.Items.Any(i => i.Id == id && i.Kind == "image"));
        _selectedOrganizeIds.RemoveWhere(id => !_history.Items.Any(i => i.Id == id));
        var candidates = _history.Items
            .Where(i => (filter.All ||
                         i.Category.Equals(filter.Name, StringComparison.OrdinalIgnoreCase)) &&
                        (string.IsNullOrEmpty(query) ||
                         i.Summary.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                         i.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            .OrderByDescending(i => i.IsFavorite || i.IsPinned)
            .ThenByDescending(i => i.CopiedUtc)
            .Take(70)
            .ToArray();
        // Virtualized data binding avoids eagerly decoding every offscreen card.
        _restoringMultiSelection = true;
        try
        {
            _list.ItemsSource = candidates;
            _emptyMessage.Visibility = _list.Items.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
            if (_multiSelectMode || _organizeMode)
            {
                _list.SelectedItems.Clear();
                foreach (var item in candidates.Where(i => _multiSelectMode
                             ? _orderedImageIds.Contains(i.Id) : _selectedOrganizeIds.Contains(i.Id)))
                    _list.SelectedItems.Add(item);
            }
            else
                _list.SelectedItem = candidates.FirstOrDefault(item => item.Id == selectedId)
                    ?? candidates.FirstOrDefault();
        }
        finally { _restoringMultiSelection = false; }
        UpdateMultiControls();
        var protectedCount = _history.Items.Count(i => i.IsFavorite || i.IsPinned);
        var recentCount = _history.Items.Count - protectedCount;
        _hint.Text = !_history.Enabled ? "Histórico pausado · itens salvos permanecem disponíveis."
            : _history.Items.Count == 0 ? "Nenhum item copiado ainda."
            : $"{recentCount}/{_history.MaxEntries} recentes · {protectedCount} favoritos ou fixados protegidos";
        _subtitle.Text = _history.Shortcut.Replace("+", " + ") +
                         " · Enter: colar · ☆: favorito · ◇: fixar · Esc: fechar";
        _enable.Content = _history.Enabled ? "Pausar histórico" : "Ativar histórico";
        _enable.Visibility = Visibility.Visible;
    }

    private void BeginCategoryEdit(bool rename)
    {
        var filter = _categoryFilter.SelectedItem as CategoryFilter;
        if (rename && (filter is null || filter.All || filter.Name.Length == 0)) return;
        _editingCategory = rename ? filter!.Name : null;
        _categoryNameBox.Text = _editingCategory ?? string.Empty;
        _categoryEditor.Visibility = Visibility.Visible;
        _categoryNameBox.Focus();
        _categoryNameBox.SelectAll();
    }

    private void CancelCategoryEdit()
    {
        _categoryEditor.Visibility = Visibility.Collapsed;
        _categoryNameBox.Clear();
        _editingCategory = null;
    }

    private async Task SaveCategoryEditAsync()
    {
        if (_categoryEditor.Visibility != Visibility.Visible) return;
        var name = _categoryNameBox.Text.Trim();
        try
        {
            if (_editingCategory is { } original)
                await _history.RenameCategoryAsync(original, name);
            else
                await _history.AddCategoryAsync(name);
            var hasSelectedCopies = _organizeMode
                ? _selectedOrganizeIds.Count > 0
                : !_multiSelectMode && _list.SelectedItem is ClipboardHistoryItem;
            CancelCategoryEdit();
            Refresh();
            // Creating a category while copies are selected should prepare the
            // destination, not hide the selected copies behind an empty filter.
            if (hasSelectedCopies)
                _moveToCategory.SelectedItem = _moveToCategory.Items
                    .OfType<CategoryTarget>().FirstOrDefault(target =>
                        target.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            else
                SelectCategoryFilter(name);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "Não foi possível salvar a categoria: " + error.Message,
                "SlashGuedes", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task DeleteCurrentCategoryAsync()
    {
        if (_categoryFilter.SelectedItem is not CategoryFilter filter ||
            filter.All || filter.Name.Length == 0) return;
        if (MessageBox.Show(this,
            $"Excluir a categoria \"{filter.Name}\"? As cópias serão mantidas sem categoria.",
            "SlashGuedes", MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            await _history.DeleteCategoryAsync(filter.Name);
            CancelCategoryEdit();
            Refresh();
            SelectCategoryFilter(string.Empty);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "Não foi possível excluir a categoria: " + error.Message,
                "SlashGuedes", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SelectCategoryFilter(string name)
    {
        if (_categoryFilter.Items.OfType<CategoryFilter>().FirstOrDefault(option =>
            !option.All && option.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } filter)
        {
            _categoryFilter.SelectedItem = filter;
            Refresh();
        }
    }

    // The grid reserves fixed columns for ☆ / ◇ so long text cannot hide them.
    private FrameworkElement BuildCardRow(ClipboardHistoryItem item)
    {
        var row = new Grid { Margin = new Thickness(2, 4, 2, 4) };
        var extraColumn = _multiSelectMode || _organizeMode ? 1 : 0;
        if (_multiSelectMode || _organizeMode)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });
            var ordinal = _orderedImageIds.IndexOf(item.Id) + 1;
            var marker = new TextBlock
            {
                Text = _organizeMode ? (_selectedOrganizeIds.Contains(item.Id) ? "☑" : "□")
                    : item.Kind != "image" ? "—" : ordinal > 0 ? ordinal.ToString() : "□",
                FontWeight = FontWeights.Bold, FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = _organizeMode ? "Clique para incluir ou remover esta cópia da organização."
                    : item.Kind == "image" ? "Clique para adicionar este print à sequência."
                    : "Seleção múltipla disponível apenas para imagens."
            };
            marker.SetResourceReference(TextBlock.ForegroundProperty, "Lab.accent-text");
            row.Children.Add(marker);
        }
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var star = new Button
        {
            Content = item.IsFavorite ? "★" : "☆",
            ToolTip = item.IsFavorite ? "Remover dos favoritos" : "Favoritar e proteger da limpeza",
            FontSize = 19, MinWidth = 36,
            Margin = new Thickness(4, 0, 2, 0),
            Padding = new Thickness(4, 1, 4, 1)
        };
        Grid.SetColumn(star, 2 + extraColumn);
        star.Click += async (_, e) =>
        {
            e.Handled = true;
            if (!_multiSelectMode && !_organizeMode) _list.SelectedItem = item;
            star.IsEnabled = false;
            try { await _history.ChangeAsync(item, favorite: !item.IsFavorite); }
            catch (Exception) { /* Item remains unchanged on failures. */ }
            finally { star.IsEnabled = true; }
            Refresh();
        };
        row.Children.Add(star);
        var pin = new Button
        {
            Content = item.IsPinned ? "◆" : "◇",
            ToolTip = item.IsPinned ? "Desafixar" : "Fixar",
            FontSize = 16, MinWidth = 32,
            Margin = new Thickness(1, 0, 2, 0),
            Padding = new Thickness(3, 1, 3, 1)
        };
        Grid.SetColumn(pin, 3 + extraColumn);
        pin.Click += async (_, e) =>
        {
            e.Handled = true;
            if (!_multiSelectMode && !_organizeMode) _list.SelectedItem = item;
            pin.IsEnabled = false;
            try { await _history.ChangeAsync(item, pinned: !item.IsPinned); }
            catch (Exception) { /* Item remains unchanged on failures. */ }
            finally { pin.IsEnabled = true; }
            Refresh();
        };
        row.Children.Add(pin);
        if (item.Kind == "image" && _history.LoadThumbnail(item) is { } bitmap)
        {
            var image = new System.Windows.Controls.Image
            {
                Source = bitmap, Width = 90, Height = 62,
                Stretch = Stretch.UniformToFill, Margin = new Thickness(3, 0, 8, 0)
            };
            Grid.SetColumn(image, extraColumn);
            row.Children.Add(image);
        }
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 5, 0)
        };
        var label = new TextBlock
        {
            Text = item.Kind == "image" ? "Imagem copiada" : item.Summary,
            MaxHeight = 48, FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
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
            FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis
        };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted");
        text.Children.Add(note);
        Grid.SetColumn(text, 1 + extraColumn);
        row.Children.Add(text);
        return row;
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button) return true;
            source = source is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source) : null;
        }
        return false;
    }

    // Recycled WPF template presenter: builds an item only when visible.
    public sealed class ClipboardCardView : Border
    {
        public static readonly DependencyProperty OwnerProperty =
            DependencyProperty.Register(nameof(Owner), typeof(ClipboardQuickWindow),
                typeof(ClipboardCardView), new PropertyMetadata(null,
                    (d, _) => ((ClipboardCardView)d).UpdateContent()));

        public ClipboardQuickWindow? Owner
        {
            get => (ClipboardQuickWindow?)GetValue(OwnerProperty);
            set => SetValue(OwnerProperty, value);
        }

        internal void RefreshMultiMarker() => UpdateContent();

        public ClipboardCardView()
        {
            CornerRadius = new CornerRadius(10);
            BorderThickness = new Thickness(1);
            Padding = new Thickness(9, 5, 9, 5);
            Margin = new Thickness(0, 2, 0, 2);
            SetResourceReference(BackgroundProperty, "Lab.bg");
            SetResourceReference(BorderBrushProperty, "Lab.line");
            DataContextChanged += (_, _) => UpdateContent();
        }

        private void UpdateContent()
        {
            Child = Owner is { } popup && DataContext is ClipboardHistoryItem item
                ? popup.BuildCardRow(item) : null;
        }
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoringMultiSelection) return;
        if (_organizeMode)
        {
            foreach (var item in e.RemovedItems.OfType<ClipboardHistoryItem>())
                _selectedOrganizeIds.Remove(item.Id);
            foreach (var item in e.AddedItems.OfType<ClipboardHistoryItem>())
                _selectedOrganizeIds.Add(item.Id);
            UpdateMultiControls();
            RefreshVisibleCardMarkers(_list);
            return;
        }
        if (!_multiSelectMode)
        {
            _moveButton.IsEnabled = _list.SelectedItem is ClipboardHistoryItem;
            _deleteButton.IsEnabled = _list.SelectedItem is ClipboardHistoryItem;
            return;
        }
        // WPF SelectionMode.Multiple makes plain clicks toggle selection.
        // Preserve selection order independently of list sort/filter order.
        foreach (var item in e.RemovedItems.OfType<ClipboardHistoryItem>())
            _orderedImageIds.Remove(item.Id);
        foreach (var item in e.AddedItems.OfType<ClipboardHistoryItem>())
        {
            if (item.Kind == "image")
            {
                if (!_orderedImageIds.Contains(item.Id) &&
                    _orderedImageIds.Count < ClipboardSequentialPasteService.MaximumImages)
                    _orderedImageIds.Add(item.Id);
                else if (!_orderedImageIds.Contains(item.Id))
                {
                    _restoringMultiSelection = true;
                    _list.SelectedItems.Remove(item);
                    _restoringMultiSelection = false;
                }
            }
            else
            {
                _restoringMultiSelection = true;
                _list.SelectedItems.Remove(item);
                _restoringMultiSelection = false;
            }
        }
        UpdateMultiControls();
        // Only visible card controls are updated; keep virtualized/offscreen
        // thumbnails unloaded while selection order changes.
        RefreshVisibleCardMarkers(_list);
    }

    private static void RefreshVisibleCardMarkers(DependencyObject node)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is ClipboardCardView card) card.RefreshMultiMarker();
            else RefreshVisibleCardMarkers(child);
        }
    }

    private void SetMultiSelectMode(bool active)
    {
        if (active && _organizeMode) SetOrganizeMode(false);
        _restoringMultiSelection = true;
        try
        {
            _orderedImageIds.Clear();
            if (active)
            {
                // SelectedItems cannot be manipulated while SelectionMode.Single
                // is active. First switch to Multiple, then clear the old item.
                _list.SelectionMode = SelectionMode.Multiple;
                _list.SelectedItems.Clear();
            }
            else
            {
                // Clear while STILL in Multiple mode; only then go back to
                // Single. Windows UI smoke caught this ordering regression.
                _list.SelectedItems.Clear();
                _list.SelectionMode = SelectionMode.Single;
                _list.SelectedItem = null;
            }
            _multiSelectMode = active;
        }
        finally { _restoringMultiSelection = false; }
        Refresh();
    }

    private void SetOrganizeMode(bool active)
    {
        if (active && _multiSelectMode) SetMultiSelectMode(false);
        _restoringMultiSelection = true;
        try
        {
            _selectedOrganizeIds.Clear();
            if (active)
            {
                _list.SelectionMode = SelectionMode.Multiple;
                _list.SelectedItems.Clear();
            }
            else
            {
                _list.SelectedItems.Clear();
                _list.SelectionMode = SelectionMode.Single;
                _list.SelectedItem = null;
            }
            _organizeMode = active;
        }
        finally { _restoringMultiSelection = false; }
        Refresh();
    }

    private void UpdateMultiControls()
    {
        _multiButton.Content = _multiSelectMode ? "Cancelar seleção" : "Selecionar vários prints";
        _organizeButton.Content = _organizeMode
            ? $"Cancelar organização ({_selectedOrganizeIds.Count})" : "Selecionar cópias";
        _pasteManyButton.Visibility = _multiSelectMode ? Visibility.Visible : Visibility.Collapsed;
        _pasteManyButton.IsEnabled = _multiSelectMode && _orderedImageIds.Count >= 2;
        _pasteManyButton.Content = _orderedImageIds.Count >= 2
            ? $"Colar {_orderedImageIds.Count} prints" : "Colar prints em sequência";
        _multiHint.Visibility = _multiSelectMode || _organizeMode
            ? Visibility.Visible : Visibility.Collapsed;
        if (_organizeMode)
            _multiHint.Text = _selectedOrganizeIds.Count == 0
                ? "Clique nas cópias (textos ou prints) para selecionar. Depois escolha a categoria e toque em Mover."
                : $"{_selectedOrganizeIds.Count} cópia(s) selecionada(s). Mover não cola nem envia mensagens.";
        else if (_multiSelectMode)
            _multiHint.Text = _orderedImageIds.Count == 0
                ? "Clique nos prints na ordem desejada (mínimo 2, máximo 15)."
                : $"{_orderedImageIds.Count} selecionado(s) na ordem dos cliques · Esc durante a colagem cancela o restante. Nenhuma mensagem será enviada.";
        _moveButton.IsEnabled = !_multiSelectMode &&
            (_organizeMode ? _selectedOrganizeIds.Count > 0 : _list.SelectedItem is ClipboardHistoryItem);
        _deleteButton.IsEnabled = !_multiSelectMode && !_organizeMode &&
            _list.SelectedItem is ClipboardHistoryItem;
        _moveToCategory.IsEnabled = !_multiSelectMode;
    }

    private void PasteSelectedImages()
    {
        if (!_multiSelectMode) return;
        var selected = ClipboardSequentialPasteService.ResolveSelection(
            _history.Items, _orderedImageIds);
        if (selected.Count < 2) return;
        Hide();
        SetMultiSelectMode(false);
        _pasteMany(selected);
    }

    internal bool MultiSelectionEnabled => _multiSelectMode;
    internal bool OrganizeSelectionEnabled => _organizeMode;
    internal void EnableMultiSelectionForSmoke() => SetMultiSelectMode(true);
    internal void CancelMultiSelectionForSmoke() => SetMultiSelectMode(false);
    internal void EnableOrganizeSelectionForSmoke() => SetOrganizeMode(true);
    internal void CancelOrganizeSelectionForSmoke() => SetOrganizeMode(false);

    private void SelectCurrent(bool copyOnly = false)
    {
        if (_organizeMode) return; // Organizing must NEVER paste.
        if (_multiSelectMode) { PasteSelectedImages(); return; }
        if (_list.SelectedItem is not ClipboardHistoryItem item) return;
        Hide();
        if (copyOnly) _copyOnly(item);
        else _choose(item);
    }
}
