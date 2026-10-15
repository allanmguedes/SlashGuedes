using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using SlashText.Models;
using SlashText.Services;

namespace SlashText;

public partial class MainWindow
{
    // Used only by isolated smoke fixtures. Normal startup never assigns this decision.
    private bool? _shortcutDiscardDecisionForEvidence;

    internal void PrepareShortcutsEvidence(double width, Snippet[] snippets)
    {
        Width = width; _shortcutDraftBaseline = null; _selected = null;
        ReplaceList(snippets);
        SelectSnippet(snippets[0]);
        ShowView(ShortcutsView, ShortcutsTabButton);
        UpdateResponsiveLayout(width);
        StatusText.Text = "Piloto Atalhos · dados ilustrativos de teste";
    }

    internal bool ShortcutsDraftDirtyForEvidence => HasUnsavedShortcutDraft;
    internal string ShortcutContentForEvidence => ReadShortcutDraft()[4];
    internal Guid? SelectedShortcutForEvidence => _selected?.Id;
    internal Task<bool> SaveShortcutForEvidence() => TrySaveShortcutAsync();

    internal void SelectShortcutForEvidence(Snippet snippet, bool allowDiscard)
    {
        _shortcutDiscardDecisionForEvidence = allowDiscard;
        try { SelectSnippet(snippet); }
        finally { _shortcutDiscardDecisionForEvidence = null; }
    }

    internal void SelectShortcutThroughListForEvidence(Snippet snippet, bool allowDiscard)
    {
        _shortcutDiscardDecisionForEvidence = allowDiscard;
        try { SnippetListPanel.SelectedItem = snippet; }
        finally { _shortcutDiscardDecisionForEvidence = null; }
    }

    internal void BeginNewShortcutForEvidence() => BeginNewSnippet();

    internal bool FocusShortcutWithoutSelectingForEvidence(Snippet snippet)
    {
        _syncShortcutListSelection = true;
        try { return (SnippetListPanel.ItemContainerGenerator.ContainerFromItem(snippet) as UIElement)?.Focus() == true; }
        finally { _syncShortcutListSelection = false; RefreshShortcutListSelection(); }
    }

    internal void WithShortcutDiscardDecisionForEvidence(bool allowDiscard, Action action)
    {
        _shortcutDiscardDecisionForEvidence = allowDiscard;
        try { action(); }
        finally { _shortcutDiscardDecisionForEvidence = null; }
    }

    internal void InsertVariableForEvidence(string token)
    {
        var caret = ContentEditor.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        ContentEditor.Selection.Select(caret, caret);
        VariableChip_OnClick(new System.Windows.Controls.Button { Tag = token }, new RoutedEventArgs());
        UpdatePreview();
    }

    internal void SetVariablesVisibleForEvidence(bool visible) => SetShortcutVariablesVisible(visible);
    internal void SetShortcutProtectedForEvidence(bool value) { _snippetStorageAvailable = !value; RefreshShortcutDraftState(); }
    internal string ShortcutPreviewForEvidence => new TextRange(PreviewDocument.ContentStart, PreviewDocument.ContentEnd).Text;
    internal void SelectCategoryForEvidence(string name) => SelectShortcutCategory(name);
    internal string CategoryIconForEvidence => CurrentShortcutCategoryIcon;
    internal string CategoryNameForEvidence => SelectedShortcutCategory;

    internal Border ShortcutColorContentForEvidence(bool highlight) => CreateShortcutColorContent(
        highlight ? TextElement.BackgroundProperty : TextElement.ForegroundProperty,
        ContentEditor.Selection.Start, ContentEditor.Selection.End);

    internal void ExpandShortcutForEvidence(bool expanded) => SetShortcutEditorExpanded(expanded);
    internal void InsertCodeForEvidence(CodeBlockContent content, Border? existing = null) => ApplyCodeBlock(content, existing);
    internal void InsertEmojiForEvidence(string value) => InsertShortcutEmoji(value);
    internal void ShareShortcutForEvidence() => CreateShareShortcut();
    internal Task<bool> FlagShortcutForEvidence(bool favorite) => SetShortcutFlagAsync(_selected!, favorite);
    internal void DuplicateShortcutForEvidence()
    {
        _shortcutDiscardDecisionForEvidence = true;
        try { DuplicateShortcut(_selected!); }
        finally { _shortcutDiscardDecisionForEvidence = null; }
    }
    internal void FilterShortcutForEvidence(bool favorite, bool pinned)
    { _showShortcutFavorites = favorite; _showShortcutPinned = pinned; _showMostUsed = false; RefreshNavigation(); }

    internal void DisposeShortcutsEvidence()
    {
        _shortcutsHelpHighlight?.Remove();
        DisposeCaptureEvidence();
    }
}
