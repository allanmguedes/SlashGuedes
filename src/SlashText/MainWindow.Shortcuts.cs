using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using SlashText.Design;
using SlashText.Models;
using SlashText.Services;
using SlashText.Views;

namespace SlashText;

public partial class MainWindow
{
    private string[]? _shortcutDraftBaseline;
    private bool _loadingShortcutDraft;
    private bool _shortcutOperationInProgress;
    private bool _shortcutVariablesVisible = true;
    private double _shortcutVariablesWidth = 280;
    private ScreenHelpHighlighter? _shortcutsHelpHighlight;
    private bool _syncShortcutFormatting;
    private const string CurrentTextSizeItem = "SlashDesk.CurrentTextSize";
    private string[] ReadShortcutDraft() =>
    [
        NameBox.Text, TriggerBox.Text, SelectedShortcutCategory, FormatBox.SelectedIndex.ToString(),
        RichTextMarkdownConverter.Save(ContentEditor,
            FormatBox.SelectedIndex == 1 ? SnippetFormat.Markdown : SnippetFormat.Plain),
        CurrentShortcutCategoryIcon
    ];

    private bool HasUnsavedShortcutDraft => !_loadingShortcutDraft && _shortcutDraftBaseline is not null &&
        !_shortcutDraftBaseline.SequenceEqual(ReadShortcutDraft(), StringComparer.Ordinal);

    private void ResetShortcutDraftBaseline()
    {
        _shortcutDraftBaseline = ReadShortcutDraft();
        RefreshShortcutDraftState();
        RefreshShortcutFormatting();
    }

    private void RefreshShortcutDraftState()
    {
        if (_loadingShortcutDraft || ContentEditor is null || ShortcutSaveStateText is null || FormatBox is null || ShortcutSaveButton is null || ShortcutDeleteButton is null || ShortcutImportButton is null) return;
        var dirty = HasUnsavedShortcutDraft;
        ShortcutSaveStateText.Text = !_snippetStorageAvailable ? "Atalhos em modo protegido" : dirty ? "Alterações não salvas" : "Tudo salvo";
        var color = !_snippetStorageAvailable ? "Lab.error" : dirty ? "Lab.muted" : "Lab.green";
        ShortcutSaveStateText.SetResourceReference(TextBlock.ForegroundProperty, color);
        ShortcutSaveStateIcon.Kind = dirty || !_snippetStorageAvailable ? "CircleHelp" : "Check";
        ShortcutSaveStateIcon.SetResourceReference(LabIcon.ForegroundProperty, color);
        ShortcutSaveButton.IsEnabled = _snippetStorageAvailable && !_shortcutOperationInProgress;
        ShortcutDeleteButton.IsEnabled = _snippetStorageAvailable && _selected is not null && !_shortcutOperationInProgress;
        ShortcutImportButton.IsEnabled = _snippetStorageAvailable && !_shortcutOperationInProgress;
        if (SettingsImportButton is not null) SettingsImportButton.IsEnabled = _snippetStorageAvailable && !_shortcutOperationInProgress;
        if (CategoryBox is not null) CategoryBox.IsEnabled = _snippetStorageAvailable && !_shortcutOperationInProgress;
        if (dirty && StatusText is not null) StatusText.Text = "Alterações não salvas";
    }

    private bool CanDiscardShortcutDraft()
    {
        if (_shortcutOperationInProgress) return false;
        if (!HasUnsavedShortcutDraft) return true;
        if (_shortcutDiscardDecisionForEvidence is bool decision) return decision;
        var dialog = new ShortcutConfirmationWindow("Descartar alterações?", "Este atalho tem alterações que ainda não foram salvas.",
            "Para continuar editando, escolha Cancelar e depois use Salvar.", "Descartar alterações") { Owner = this };
        LabMotion.SetReduced(dialog, LabMotion.GetReduced(this)); dialog.EnableBackdrop();
        return ShowCaptureDialog(dialog);
    }

    private Button CreateCategoryButton(string? category, string label, int count)
    {
        var selected = string.Equals(category, _selectedCategory, StringComparison.CurrentCultureIgnoreCase);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new LabIcon { Kind = category is null ? "FolderOpen" : ShortcutCategories.Resolve(category).Icon, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0) }; row.Children.Add(icon);
        var name = new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        name.SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        Grid.SetColumn(name, 1); row.Children.Add(name);
        var number = new TextBlock { Text = count.ToString(), FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        number.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted"); Grid.SetColumn(number, 2); row.Children.Add(number);
        var button = new Button { Content = row, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 3), ToolTip = $"{label} · {count} atalhos" };
        button.SetResourceReference(StyleProperty, "Lab.Shortcuts.Item");
        button.SetResourceReference(BackgroundProperty, selected ? "Lab.tint" : "Lab.shell");
        button.SetResourceReference(ForegroundProperty, selected ? "Lab.accent-text" : "Lab.text");
        AutomationProperties.SetName(button, $"Categoria {label}, {count} atalhos" + (selected ? ", selecionada" : ""));
        button.Click += (_, _) => { _selectedCategory = category; RefreshNavigation(); };
        return button;
    }

    private Button CreateSnippetButton(Snippet snippet)
    {
        var selected = ReferenceEquals(snippet, _selected);
        var content = new StackPanel();
        var badges = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var kind in new[] { snippet.IsFavorite ? "Star" : null, snippet.IsPinned ? "Pin" : null }.Where(kind => kind is not null))
        {
            var badge = new LabIcon { Kind = kind!, Width = 14, Height = 14, Margin = new Thickness(4, 0, 0, 0) };
            badge.SetResourceReference(LabIcon.ForegroundProperty, "Lab.accent-text"); badges.Children.Add(badge);
        }
        var commandRow = new DockPanel(); DockPanel.SetDock(badges, Dock.Right); commandRow.Children.Add(badges); content.Children.Add(commandRow);
        var command = new TextBlock { Text = snippet.Trigger, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        command.SetResourceReference(TextBlock.ForegroundProperty, "Lab.accent-text"); commandRow.Children.Add(command);
        var name = new TextBlock { Text = snippet.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 5, 0, 0) };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Lab.text"); content.Children.Add(name);
        var excerpt = snippet.Content.Replace('\r', ' ').Replace('\n', ' ');
        var preview = new TextBlock { Text = excerpt, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 5, 0, 0) };
        preview.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted"); content.Children.Add(preview);
        if (!snippet.Enabled || snippet.HasLegacyIncompatibleTrigger)
        {
            var state = new TextBlock { Text = snippet.HasLegacyIncompatibleTrigger ? "Comando legado · revise para ativar" : "Pausado", FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
            state.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted"); content.Children.Add(state);
        }
        var button = new Button { Content = content, Tag = selected ? "Selected" : null, ToolTip = $"{snippet.Name}\n{snippet.Trigger}\nCategoria: {snippet.Category}" };
        button.SetResourceReference(StyleProperty, "Lab.Shortcuts.Item");
        AutomationProperties.SetName(button, $"{snippet.Name}, comando {snippet.Trigger}, categoria {snippet.Category}" + (selected ? ", selecionado" : ""));
        button.Click += (_, _) => SelectSnippet(snippet);
        button.PreviewMouseRightButtonUp += (_, e) => { e.Handled = true; ShowShortcutActions(button, snippet); };
        return button;
    }

    private void ShortcutContent_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        e.Handled = true;
        ContentEditor.MoveFocus(new TraversalRequest(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next));
    }

    private void ClearShortcutSearch_OnClick(object sender, RoutedEventArgs e) { SearchBox.Clear(); SearchBox.Focus(); }

    private string SelectedShortcutCategory => (CategoryBox.SelectedItem as ShortcutCategory)?.Name ?? "Geral";
    private string CurrentShortcutCategoryIcon => ShortcutCategories.Resolve(SelectedShortcutCategory).Icon;

    private void SelectShortcutCategory(string? category)
    {
        CategoryBox.SelectedItem = ShortcutCategories.Resolve(category);
        var legacy = !ShortcutCategories.IsKnown(category);
        ShortcutLegacyCategoryHint.Text = legacy ? $"Categoria anterior: {category}. Ao salvar, será organizada em Outros." : "";
        ShortcutLegacyCategoryHint.Visibility = legacy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShortcutCategory_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshShortcutDraftState();
    }

    private void InitializeShortcutFormatting()
    {
        _syncShortcutFormatting = true;
        try
        {
            var preferred = new[] { "Segoe UI", "Arial", "Calibri", "Verdana", "Georgia", "Consolas" };
            var installed = Fonts.SystemFontFamilies.Select(font => font.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            FontFamilyBox.Items.Clear();
            foreach (var font in preferred.Where(font => installed.Contains(font, StringComparer.OrdinalIgnoreCase))
                .Concat(installed.Except(preferred, StringComparer.OrdinalIgnoreCase).OrderBy(font => font)))
                FontFamilyBox.Items.Add(new ComboBoxItem { Content = font, Tag = font });
        }
        finally { _syncShortcutFormatting = false; }
        RefreshShortcutFormatting();
    }
    private void ShortcutContent_OnSelectionChanged(object sender, RoutedEventArgs e) => RefreshShortcutFormatting();
    private void RefreshShortcutFormatting()
    {
        if (_syncShortcutFormatting || ContentEditor is null || FontFamilyBox is null || ShortcutBoldButton is null) return;
        _syncShortcutFormatting = true;
        try
        {
            var selection = ContentEditor.Selection;
            var font = selection.GetPropertyValue(TextElement.FontFamilyProperty) as FontFamily;
            FontFamilyBox.SelectedItem = FontFamilyBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag as string, font?.Source, StringComparison.OrdinalIgnoreCase));
            var size = selection.GetPropertyValue(TextElement.FontSizeProperty);
            // Keep standard whole-point choices stable. An explicit imported/saved
            // size is a readout of the current selection, not another preset.
            var currentSize = size is double current ? current : double.NaN;
            foreach (var item in FontSizeBox.Items.OfType<ComboBoxItem>()
                         .Where(item => item.Uid == CurrentTextSizeItem &&
                             (!double.TryParse(item.Tag as string, System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out var existing) ||
                              Math.Abs(existing - currentSize) >= .05 || double.IsNaN(currentSize))).ToArray())
                FontSizeBox.Items.Remove(item);
            if (size is double fontSize)
            {
                var sizeItem = FontSizeBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => double.TryParse(item.Tag as string, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) && Math.Abs(n - fontSize) < .05);
                if (sizeItem is null)
                {
                    sizeItem = new ComboBoxItem
                    {
                        Content = (fontSize * .75).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")),
                        Tag = fontSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Uid = CurrentTextSizeItem, IsEnabled = false,
                        ToolTip = "Tamanho existente no texto. Escolha um tamanho da lista para alterar."
                    };
                    var index = FontSizeBox.Items.OfType<ComboBoxItem>().Count(item =>
                        double.TryParse(item.Tag as string, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var n) && n < fontSize);
                    FontSizeBox.Items.Insert(index, sizeItem);
                }
                FontSizeBox.SelectedItem = sizeItem;
            }
            else FontSizeBox.SelectedItem = null;
            ShortcutBoldButton.Tag = selection.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight weight && weight.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight() ? "Selected" : null;
            ShortcutItalicButton.Tag = Equals(selection.GetPropertyValue(TextElement.FontStyleProperty), FontStyles.Italic) ? "Selected" : null;
            ShortcutUnderlineButton.Tag = selection.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection decorations && decorations.Any(d => d.Location == TextDecorationLocation.Underline) ? "Selected" : null;
            var alignment = selection.GetPropertyValue(Paragraph.TextAlignmentProperty);
            foreach (var (button, value) in new[] { (ShortcutAlignLeftButton, TextAlignment.Left), (ShortcutAlignCenterButton, TextAlignment.Center), (ShortcutAlignRightButton, TextAlignment.Right), (ShortcutAlignJustifyButton, TextAlignment.Justify) })
                button.Tag = Equals(alignment, value) ? "Selected" : null;
            ShortcutUndoButton.IsEnabled = ContentEditor.CanUndo; ShortcutRedoButton.IsEnabled = ContentEditor.CanRedo;
        }
        finally { _syncShortcutFormatting = false; }
    }
    private void ApplyShortcutFormatting(DependencyProperty property, object value,
        TextPointer? start = null, TextPointer? end = null)
    {
        // Focus first: activating the RichTextBox after applying caret formatting can reset it.
        // Suppress selection/text feedback until the requested property has finished changing.
        _syncShortcutFormatting = true;
        try
        {
            ContentEditor.Focus();
            if (start is not null && end is not null) ContentEditor.Selection.Select(start, end);
            ContentEditor.Selection.ApplyPropertyValue(property, value);
        }
        finally { _syncShortcutFormatting = false; }
        RefreshShortcutFormatting();
    }

    private Border CreateShortcutColorContent(DependencyProperty property, TextPointer start, TextPointer end)
    {
        var brush = ContentEditor.Selection.GetPropertyValue(property) as SolidColorBrush;
        var initial = brush?.Color ?? (property == TextElement.BackgroundProperty ? Colors.Yellow : Colors.Black);
        var current = unchecked((int)0xFF000000) | initial.R << 16 | initial.G << 8 | initial.B;
        return CaptureInkPicker.CreateColorContent(() => current, argb =>
        {
            current = argb;
            var color = System.Drawing.Color.FromArgb(argb);
            ApplyShortcutFormatting(property, new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)), start, end);
        }, property == TextElement.BackgroundProperty ? "Marca-texto" : "Cor do texto");
    }

    private void ShowShortcutColorPicker(Button anchor, DependencyProperty property)
    {
        var start = ContentEditor.Selection.Start; var end = ContentEditor.Selection.End;
        var content = CreateShortcutColorContent(property, start, end);
        content.MaxWidth = Math.Max(320, Math.Min(Width - 48, SystemParameters.WorkArea.Width - 48));
        var scroll = new ScrollViewer { Content = content,
            MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 48),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom,
            StaysOpen = false, AllowsTransparency = true, VerticalOffset = 6, Child = scroll };
        LabMotion.SetReduced(content, LabMotion.GetReduced(this));
        content.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { popup.IsOpen = false; e.Handled = true; } };
        popup.Opened += (_, _) => { LabMotion.PlayEntrance(content); content.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); };
        popup.Closed += (_, _) => ContentEditor.Focus(); popup.IsOpen = true;
    }

    private void ShortcutUndo_OnClick(object sender, RoutedEventArgs e) { ContentEditor.Undo(); ContentEditor.Focus(); RefreshShortcutFormatting(); }
    private void ShortcutRedo_OnClick(object sender, RoutedEventArgs e) { ContentEditor.Redo(); ContentEditor.Focus(); RefreshShortcutFormatting(); }
    private void ShortcutJustify_OnClick(object sender, RoutedEventArgs e) => EditingCommands.AlignJustify.Execute(null, ContentEditor);

    private void ToggleShortcutVariables_OnClick(object sender, RoutedEventArgs e) => SetShortcutVariablesVisible(!_shortcutVariablesVisible);

    private void SetShortcutVariablesVisible(bool visible)
    {
        if (!visible && _shortcutVariablesVisible) _shortcutVariablesWidth = ShortcutRightColumn.ActualWidth;
        _shortcutVariablesVisible = visible;
        ShortcutVariablesPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ShortcutRightDivider.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible) { ShortcutRightColumn.MinWidth = ShortcutRightMinimum; ShortcutRightColumn.Width = new GridLength(Math.Max(ShortcutRightMinimum, _shortcutVariablesWidth)); }
        NormalizeShortcutColumns(ActualWidth > 0 ? ActualWidth : Width);
        ShortcutVariablesToggleButton.ToolTip = visible ? "Ocultar variáveis" : "Mostrar variáveis";
        if (visible) LabMotion.PlayEntrance(ShortcutVariablesPanel);
    }

    private void OpenShortcutsHelp_OnClick(object sender, RoutedEventArgs e)
    {
        _shortcutsHelpHighlight?.Remove(); _shortcutsHelpHighlight = null;
        var guide = new ScreenHelpWindow(ShortcutsHelpContent.Create()) { Owner = this };
        LabMotion.SetReduced(guide, LabMotion.GetReduced(this));
        guide.EnableBackdrop();
        ShowCaptureDialog(guide);
        if (guide.RequestedTarget is not { } name || FindName(name) is not FrameworkElement target) return;
        // Help never changes format, panel visibility or the current draft to reveal a target.
        if (!target.IsVisible) target = name == "ShortcutVariablesPanel" ? ShortcutVariablesToggleButton : FormatBox;
        target.BringIntoView();
        Dispatcher.BeginInvoke(new Action(() => { if (!target.IsVisible) return; target.Focus(); _shortcutsHelpHighlight = ScreenHelpHighlighter.Show(target); }), DispatcherPriority.Loaded);
    }
}
