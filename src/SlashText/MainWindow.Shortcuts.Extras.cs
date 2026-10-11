using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using SlashText.Design;
using SlashText.Models;
using SlashText.Services;
using SlashText.Views;

namespace SlashText;

public partial class MainWindow
{
    private Snippet? _shortcutDuplicateSource;
    private bool _showShortcutFavorites;
    private bool _showShortcutPinned;
    private bool _shortcutEditorExpanded;
    private bool _shortcutPreviewBeforeExpansion;
    private double _shortcutNormalEditorHeight = 200;
    private double _shortcutNormalPreviewHeight = 130;
    private double _shortcutExpandedEditorHeight = 280;
    private double _shortcutExpandedPreviewHeight = 180;
    private bool _shortcutExpandedHeightChosen;

    private void DisplayFavorites_OnClick(object sender, RoutedEventArgs e)
    { _showShortcutFavorites = true; _showShortcutPinned = false; _showMostUsed = false; RefreshNavigation(); }
    private void DisplayPinned_OnClick(object sender, RoutedEventArgs e)
    { _showShortcutFavorites = false; _showShortcutPinned = true; _showMostUsed = false; RefreshNavigation(); }
    private void ShortcutActions_OnClick(object sender, RoutedEventArgs e) => ShowShortcutActions((Button)sender, _selected);

    private void ShowShortcutActions(Button anchor, Snippet? snippet)
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        var surface = new Border { Child = panel, Width = 248, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1) };
        surface.SetResourceReference(Border.BackgroundProperty, "Lab.panel"); surface.SetResourceReference(Border.BorderBrushProperty, "Lab.line-strong");
        var popup = new Popup { Child = surface, PlacementTarget = anchor, Placement = PlacementMode.Bottom, VerticalOffset = 6, AllowsTransparency = true, StaysOpen = false };
        void Action(string label, string icon, Func<Task> action, bool requiresSelection = true)
        {
            var button = new Button { HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 9, 10, 9), IsEnabled = (!requiresSelection || snippet is not null) && _snippetStorageAvailable && !_shortcutOperationInProgress };
            button.SetResourceReference(StyleProperty, "Lab.Shortcuts.Item"); AutomationProperties.SetName(button, label);
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new LabIcon { Kind = icon, Width = 17, Height = 17, Margin = new Thickness(0, 0, 9, 0) });
            var text = new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center }; text.SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground") { Source = button }); content.Children.Add(text); button.Content = content;
            button.Click += async (_, _) => { popup.IsOpen = false; await action(); }; panel.Children.Add(button);
        }
        Action(UiText.Get("Duplicar atalho"), "Copy", () => { DuplicateShortcut(snippet!); return Task.CompletedTask; });
        Action(snippet?.IsFavorite == true ? UiText.Get("Remover dos favoritos") : UiText.Get("Adicionar aos favoritos"), "Star", () => SetShortcutFlagAsync(snippet!, favorite: true));
        Action(snippet?.IsPinned == true ? UiText.Get("Desafixar do topo") : UiText.Get("Fixar no topo"), "Pin", () => SetShortcutFlagAsync(snippet!, favorite: false));
        Action(UiText.Get("Divulgar SlashDesk"), "Link", () => { CreateShareShortcut(); return Task.CompletedTask; }, requiresSelection: false);
        LabMotion.SetEntrance(surface, "Popup"); LabMotion.SetReduced(surface, LabMotion.GetReduced(this));
        surface.PreviewKeyDown += (_, args) => { if (args.Key == Key.Escape) { popup.IsOpen = false; args.Handled = true; } };
        popup.Opened += (_, _) => { LabMotion.PlayEntrance(surface); surface.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); };
        popup.Closed += (_, _) => anchor.Focus(); popup.IsOpen = true;
    }

    private static Snippet CopyShortcut(Snippet source) => new()
    {
        Id = source.Id, Name = source.Name, Trigger = source.Trigger, Category = source.Category, Content = source.Content,
        Format = source.Format, Enabled = source.Enabled, ConfirmKeys = source.ConfirmKeys.ToList(),
        HasLegacyIncompatibleTrigger = source.HasLegacyIncompatibleTrigger, IsFavorite = source.IsFavorite, IsPinned = source.IsPinned
    };

    private async Task<bool> SetShortcutFlagAsync(Snippet source, bool favorite)
    {
        if (!_snippetStorageAvailable || _shortcutOperationInProgress || !_snippets.Contains(source)) return false;
        var candidate = CopyShortcut(source);
        if (favorite) candidate.IsFavorite = !candidate.IsFavorite; else candidate.IsPinned = !candidate.IsPinned;
        try
        {
            _shortcutOperationInProgress = true; ShortcutEditorPanel.IsEnabled = false; RefreshShortcutDraftState();
            await _repository.SaveAsync(_snippets.Select(item => ReferenceEquals(item, source) ? candidate : item).ToList());
            _snippets[_snippets.IndexOf(source)] = candidate;
            if (ReferenceEquals(_selected, source)) _selected = candidate;
            _keyboardHook.UpdateSnippets(_snippets); RefreshNavigation(); return true;
        }
        catch (Exception exception)
        {
            AppDiagnosticLog.Write("shortcuts.flag.save_failed", ("exceptionType", exception.GetType().Name));
            StatusText.Text = UiText.Get("Não foi possível salvar a preferência. Tente novamente."); return false;
        }
        finally { _shortcutOperationInProgress = false; ShortcutEditorPanel.IsEnabled = true; RefreshShortcutDraftState(); }
    }

    private void DuplicateShortcut(Snippet source)
    {
        if (!_snippetStorageAvailable || !CanDiscardShortcutDraft()) return;
        BeginNewSnippet(discardAlreadyConfirmed: true);
        _shortcutDuplicateSource = source;
        var suffix = 1; var root = source.HasLegacyIncompatibleTrigger ? "/atalho" : source.Trigger;
        string Candidate(int number)
        {
            var ending = "_copia" + (number == 1 ? "" : number.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return root[..Math.Min(root.Length, TriggerRule.MaximumLength - ending.Length)] + ending;
        }
        var trigger = Candidate(suffix);
        while (TriggerRule.ConflictsWith(trigger, _snippets.Select(item => item.Trigger))) trigger = Candidate(++suffix);
        NameBox.Text = source.Name + UiText.Get(" (cópia)"); TriggerBox.Text = trigger; SelectShortcutCategory(source.Category);
        FormatBox.SelectedIndex = source.Format == SnippetFormat.Markdown ? 1 : 0;
        RichTextMarkdownConverter.Load(ContentEditor, source.Content, source.Format);
        RefreshShortcutDraftState(); UpdatePreview(); NameBox.Focus(); NameBox.SelectAll();
    }

    private void ToggleShortcutEditor_OnClick(object sender, RoutedEventArgs e) => SetShortcutEditorExpanded(!_shortcutEditorExpanded);
    private void SetShortcutEditorExpanded(bool expanded)
    {
        if (_shortcutEditorExpanded == expanded) return;
        if (expanded)
        {
            _shortcutPreviewBeforeExpansion = ShortcutPreviewExpander.IsExpanded;
            _shortcutNormalEditorHeight = ContentEditor.Height; _shortcutNormalPreviewHeight = PreviewBorder.Height;
        }
        else { _shortcutExpandedEditorHeight = ContentEditor.Height; _shortcutExpandedPreviewHeight = PreviewBorder.Height; }
        _shortcutEditorExpanded = expanded;
        ShortcutSidebarPanel.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        ShortcutVariablesPanel.Visibility = !expanded && _shortcutVariablesVisible ? Visibility.Visible : Visibility.Collapsed;
        ShortcutMetadataPanel.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        ShortcutHero.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        ShortcutHeroGap.Height = new GridLength(expanded ? 0 : 24);
        ShortcutVariablesToggleButton.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        ShortcutPreviewExpander.IsExpanded = expanded || _shortcutPreviewBeforeExpansion;
        ShortcutPreviewExpander.Visibility = Visibility.Visible;
        ContentEditor.Height = expanded ? _shortcutExpandedEditorHeight : _shortcutNormalEditorHeight;
        PreviewBorder.Height = expanded ? _shortcutExpandedPreviewHeight : _shortcutNormalPreviewHeight;
        ShortcutExpandEditorText.Text = expanded ? UiText.Get("Recolher editor") : UiText.Get("Expandir editor");
        ShortcutExpandEditorIcon.Kind = expanded ? "Minimize2" : "Maximize2";
        UpdateResponsiveLayout(ActualWidth > 0 ? ActualWidth : Width);
        LabMotion.PlayEntrance(ShortcutEditorPanel); ContentEditor.Focus();
        if (expanded) Dispatcher.BeginInvoke(new Action(() => { UpdateShortcutEditorHeight(); ShortcutEditorScroll.ScrollToTop(); }), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private void UpdateShortcutEditorHeight()
    {
        var height = ShortcutEditorPanel.ActualHeight > 0 ? ShortcutEditorPanel.ActualHeight : (ActualHeight > 0 ? ActualHeight : Height) - 190;
        var toolbar = FormattingToolbar.Visibility == Visibility.Visible ? FormattingToolbar.ActualHeight : 0;
        if (!_shortcutExpandedHeightChosen)
            ContentEditor.Height = Math.Clamp(height - ShortcutEditorActions.ActualHeight - toolbar - PreviewBorder.Height - 170, 140, 740);
    }


    private void ResizeShortcutField_OnDragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeShortcutField((Thumb)sender, e.VerticalChange); e.Handled = true;
    }
    private void ResizeShortcutField_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down or Key.Home)) return;
        var thumb = (Thumb)sender;
        if (e.Key == Key.Home)
        {
            if (Equals(thumb.Tag, "Editor")) ContentEditor.Height = _shortcutEditorExpanded ? 280 : 200;
            else PreviewBorder.Height = _shortcutEditorExpanded ? 180 : 130;
            if (_shortcutEditorExpanded) _shortcutExpandedHeightChosen = true;
        }
        else ResizeShortcutField(thumb, e.Key == Key.Up ? -24 : 24);
        e.Handled = true;
    }
    private void ResizeShortcutField(Thumb thumb, double delta)
    {
        var field = Equals(thumb.Tag, "Editor") ? (FrameworkElement)ContentEditor : PreviewBorder;
        field.Height = Math.Clamp(field.Height + delta, Equals(thumb.Tag, "Editor") ? 120 : 96, 1200);
        if (_shortcutEditorExpanded) _shortcutExpandedHeightChosen = true;
    }

    private void ShortcutEmoji_OnClick(object sender, RoutedEventArgs e)
    {
        var start = ContentEditor.Selection.Start; var end = ContentEditor.Selection.End;
        var value = CaptureEmojiPicker.Show(Window.GetWindow(ContentEditor) ?? this);
        if (value is null) return;
        ContentEditor.Focus(); ContentEditor.Selection.Select(start, end);
        try { InsertShortcutEmoji(value); }
        catch (Exception error) when (error is System.IO.IOException or ArgumentException or InvalidOperationException)
        { StatusText.Text = UiText.Get("Não foi possível inserir o emoji. Tente novamente."); AppDiagnosticLog.Write("shortcuts.emoji.failed", ("exceptionType", error.GetType().Name)); }
    }
    private void InsertShortcutEmoji(string value)
    {
        var token = ShortcutEmojiAssets.Save(value);
        ContentEditor.BeginChange();
        try
        {
            ContentEditor.Selection.Text = "";
            var inline = ShortcutEmojiAssets.CreateInline(token, ContentEditor.CaretPosition)!;
            ContentEditor.CaretPosition = inline.ElementEnd.GetInsertionPosition(LogicalDirection.Forward);
        }
        finally { ContentEditor.EndChange(); }
        RefreshShortcutDraftState(); UpdatePreview();
    }
    private void CreateShareShortcut()
    {
        if (!_snippetStorageAvailable || !CanDiscardShortcutDraft()) return;
        BeginNewSnippet(discardAlreadyConfirmed: true);
        var trigger = "/slashdesk"; var suffix = 2;
        while (TriggerRule.ConflictsWith(trigger, _snippets.Select(item => item.Trigger))) trigger = "/slashdesk" + suffix++;
        NameBox.Text = UiText.Get("Divulgar SlashDesk"); TriggerBox.Text = trigger; SelectShortcutCategory("Geral");
        FormatBox.SelectedIndex = 0; RichTextMarkdownConverter.Load(ContentEditor, ShortcutShareMessage.Content, SnippetFormat.Plain);
        RefreshShortcutDraftState(); UpdatePreview();
    }

    private void InsertCodeBlock_OnClick(object sender, RoutedEventArgs e) => OpenCodeBlock(null);
    private void OpenCodeBlock(Border? view)
    {
        if (view is null && ContentEditor.CaretPosition.Paragraph is { Parent: not FlowDocument })
        { StatusText.Text = UiText.Get("Posicione o cursor fora de listas e tabelas para inserir um bloco de código."); return; }
        var start = ContentEditor.Selection.Start; var end = ContentEditor.Selection.End;
        var dialog = new ShortcutCodeBlockWindow(view is not null && ShortcutCodeBlockView.TryRead(view, out var previous) ? previous : null) { Owner = Window.GetWindow(ContentEditor) ?? this };
        LabMotion.SetReduced(dialog, LabMotion.GetReduced(this)); dialog.EnableBackdrop();
        if (!ShowCaptureDialog(dialog) || dialog.Result is null) return;
        ContentEditor.Focus(); ContentEditor.Selection.Select(start, end);
        ApplyCodeBlock(dialog.Result, view);
    }
    private void ApplyCodeBlock(CodeBlockContent content, Border? existing = null)
    {
        if (existing is null && ContentEditor.CaretPosition.Paragraph is { Parent: not FlowDocument })
        { StatusText.Text = UiText.Get("Posicione o cursor fora de listas e tabelas para inserir um bloco de código."); return; }
        ContentEditor.BeginChange();
        try
        {
            var block = ShortcutCodeBlockView.CreateBlock(content);
            if (existing is not null)
            {
                var old = ContentEditor.Document.Blocks.OfType<BlockUIContainer>().Single(item => ReferenceEquals(ShortcutCodeBlockView.GetView(item), existing));
                ContentEditor.Document.Blocks.InsertBefore(old, block); ContentEditor.Document.Blocks.Remove(old);
            }
            else
            {
                ContentEditor.Selection.Text = "";
                var paragraph = ContentEditor.CaretPosition.Paragraph;
                if (paragraph is not null)
                {
                    // Split at the caret; preserve the text and formatting on both sides.
                    var position = ContentEditor.CaretPosition.InsertParagraphBreak();
                    var next = position.Paragraph!;
                    if (next.Parent is not FlowDocument) throw new InvalidOperationException(UiText.Get("Posicione o cursor em um parágrafo fora da tabela para inserir código."));
                    ContentEditor.Document.Blocks.InsertBefore(next, block);
                    ContentEditor.CaretPosition = next.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
                }
                else { ContentEditor.Document.Blocks.Add(block); ContentEditor.Document.Blocks.Add(new Paragraph()); }
            }
        }
        finally { ContentEditor.EndChange(); }
        RefreshShortcutDraftState(); UpdatePreview();
    }
}
