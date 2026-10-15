using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Threading;
using SlashText.Models;
using SlashText.Services;
using SlashText.Views;

namespace SlashText.Design;

internal static partial class ShortcutsWorkspaceSmoke
{
    private static async Task CheckExtrasAsync(string theme, string output)
    {
        foreach (var language in ShortcutCodeEditor.Languages)
            Require((ShortcutCodeEditor.Definition(language.Id, theme != "Light") is not null) == (language.Resource is not null), "Bundled syntax: " + language.Id);
        var window = new MainWindow(captureEvidence: true);
        var root = (FrameworkElement)window.Content; window.Content = null; LabMotion.SetReduced(root, true);
        var owner = new Window { Width = 1440, Height = 900, Content = root, ShowInTaskbar = false };
        var fixture = new Snippet { Name = "Exemplo de código", Trigger = "/codigo", Category = "Desenvolvimento", Format = SnippetFormat.Markdown, Content = "Antes Depois" };
        var other = new Snippet { Name = "Outro atalho", Trigger = "/outro", Content = "Outro" };
        var literal = "// {{nome}} e {{tab}} são literais\nconst meu_valor = 2 * 3;\n\tconsole.log(\"<tag> &amp; ```\");\n  ";
        Border Block() => ShortcutCodeBlockView.GetView(((RichTextBox)window.FindName("ContentEditor")).Document.Blocks.OfType<BlockUIContainer>().Single())!;
        string Raw() { Require(ShortcutCodeBlockView.TryRead(Block(), out var data), "Code metadata survives native undo"); return data.Code; }
        try
        {
            window.PrepareShortcutsEvidence(1440, [fixture, other]); owner.Show(); owner.UpdateLayout();
            window.PrepareShortcutsEvidence(root.ActualWidth, [fixture, other]); window.Height = root.ActualHeight; owner.UpdateLayout();
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Require(window.CategoryNameForEvidence == "Outros" && !window.ShortcutsDraftDirtyForEvidence && fixture.Category == "Desenvolvimento", "Opening legacy category maps visually without changing stored metadata or dirtying the draft");
            var categoryBox = (ComboBox)window.FindName("CategoryBox");
            categoryBox.IsDropDownOpen = true;
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var categoryPopup = (Popup)categoryBox.Template.FindName("PART_Popup", categoryBox);
            var categorySurface = (FrameworkElement)categoryPopup.Child; categorySurface.UpdateLayout();
            Require(categorySurface.ActualWidth > 0 && categorySurface.ActualHeight > 0, "Real category dropdown opens");
            SaveImage(categorySurface, output, $"shortcuts-categories-{theme}", new Size(categorySurface.ActualWidth, categorySurface.ActualHeight), 1);
            categoryBox.IsDropDownOpen = false;
            var editor = (RichTextBox)window.FindName("ContentEditor");
            var paragraph = (Paragraph)editor.Document.Blocks.FirstBlock;
            editor.CaretPosition = paragraph.ContentStart.GetInsertionPosition(LogicalDirection.Forward).GetPositionAtOffset(6)!;
            editor.Selection.Select(editor.CaretPosition, editor.CaretPosition); editor.IsUndoEnabled = false; editor.IsUndoEnabled = true;
            var initial = window.ShortcutContentForEvidence;
            window.InsertCodeForEvidence(new CodeBlockContent("javascript", literal)); owner.UpdateLayout();
            Require(CodeBlockMarkdown.Read(window.ShortcutContentForEvidence).Single().Content.Code == literal, "Inserted code remains literal");
            Require(window.ShortcutContentForEvidence.Contains("Antes", StringComparison.Ordinal) && window.ShortcutContentForEvidence.Contains("Depois", StringComparison.Ordinal), "Code insertion preserves surrounding prose");
            editor.Undo(); Require(window.ShortcutContentForEvidence == initial, "Undo removes inserted block without losing prose");
            editor.Redo(); Require(Raw() == literal, "Redo restores literal code metadata");
            var edited = literal + "\nconsole.log(meu_valor);";
            window.InsertCodeForEvidence(new CodeBlockContent("javascript", edited), Block());
            Require(Raw() == edited, "Edit replaces only code block");
            editor.Undo(); Require(Raw() == literal, "Undo restores previous code");
            editor.Redo(); Require(Raw() == edited, "Redo restores edited code");
            var copyCode = (Button)ShortcutCodeBlockView.Find(Block(), "CodeCopy")!;
            string? copiedCode = null;
            ShortcutCodeBlockView.SetCopySinkForEvidence(Block(), value => copiedCode = value);
            copyCode.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(copiedCode == edited, "Code copy produces exact source after native redo without accessing the system clipboard");
            var editRequested = false;
            Block().AddHandler(ShortcutCodeBlockView.EditRequestedEvent, new RoutedEventHandler((_, args) => { editRequested = true; args.Handled = true; }));
            ((Button)ShortcutCodeBlockView.Find(Block(), "CodeEdit")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(editRequested, "Edit remains interactive after native redo");
            Require(editor.IsDocumentEnabled, "Code actions accept document input");
            Exception? editFailure = null; var editOpened = false;
            var editTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            editTimer.Tick += (_, _) =>
            {
                var popup = Application.Current.Windows.OfType<ShortcutCodeBlockWindow>().FirstOrDefault(w => w.IsVisible);
                if (popup is null) return;
                editTimer.Stop(); editOpened = true;
                try
                {
                    Require(popup.Title == "Editar bloco de código" && popup.CodeEditor.Text == edited, "Live edit opens the selected block");
                    popup.CodeEditor.Text = edited + "\n// edição pelo botão"; popup.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception ex) { editFailure = ex; popup.Close(); }
            };
            // Invoke the production callback on the real button, not a replacement handler.
            window.SelectShortcutForEvidence(other, true); window.SelectShortcutForEvidence(fixture, true);
            window.InsertCodeForEvidence(new CodeBlockContent("javascript", edited));
            var editButton = (Button)ShortcutCodeBlockView.Find(Block(), "CodeEdit")!;
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timeout.Tick += (_, _) => { timeout.Stop(); foreach (var popup in Application.Current.Windows.OfType<ShortcutCodeBlockWindow>().Where(w => w.IsVisible).ToArray()) popup.Close(); };
            editTimer.Start(); timeout.Start(); editButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); editTimer.Stop(); timeout.Stop();
            if (editFailure is not null) throw new InvalidOperationException("Real code Edit", editFailure);
            Require(editOpened && Raw() == edited + "\n// edição pelo botão", "Production Edit saves back into the same block");
            editor.Undo(); Require(Raw() == edited, "Undo real popup edit");
            editor.Redo(); Require(Raw() == edited + "\n// edição pelo botão", "Redo real popup edit");
            edited += "\n// edição pelo botão";
            var content = window.ShortcutContentForEvidence;
            ShortcutCodeBlockView.SetExpanded(Block(), false); ShortcutCodeBlockView.SetExpanded(Block(), true);
            Require(window.ShortcutContentForEvidence == content, "Collapsing a block does not edit its source");
            var preview = (Expander)window.FindName("ShortcutPreviewExpander"); preview.IsExpanded = true;
            window.ExpandShortcutForEvidence(true); owner.UpdateLayout();
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); owner.UpdateLayout();
            Require(((FrameworkElement)window.FindName("ShortcutSidebarPanel")).Visibility == Visibility.Collapsed && Math.Abs(((FrameworkElement)window.FindName("ShortcutEditorPanel")).ActualWidth - ((FrameworkElement)window.FindName("ShortcutWorkspaceGrid")).ActualWidth) < 2, $"Expanded editor occupies actual workspace (editor={editor.ActualWidth}, panel={((FrameworkElement)window.FindName("ShortcutEditorPanel")).ActualWidth}, workspace={((FrameworkElement)window.FindName("ShortcutWorkspaceGrid")).ActualWidth})");
            Require(window.ShortcutContentForEvidence == content && preview.Visibility == Visibility.Visible && preview.IsExpanded, "Expansion preserves draft and shows preview below editor");
            var editorGrip = (Thumb)window.FindName("ShortcutEditorHeightGrip"); var previewGrip = (Thumb)window.FindName("ShortcutPreviewHeightGrip");
            var oldHeight = editor.Height; var previewBorder = (Border)window.FindName("PreviewBorder"); var oldPreviewHeight = previewBorder.Height;
            editorGrip.RaiseEvent(new DragDeltaEventArgs(0, 60)); previewGrip.RaiseEvent(new DragDeltaEventArgs(0, 48));
            Require(editor.Height == oldHeight + 60 && previewBorder.Height == oldPreviewHeight + 48, "Both fields grow independently");
            editorGrip.RaiseEvent(new DragDeltaEventArgs(0, -24)); previewGrip.RaiseEvent(new DragDeltaEventArgs(0, -20));
            owner.UpdateLayout(); Require(editor.Height == oldHeight + 36 && previewBorder.Height == oldPreviewHeight + 28 && window.ShortcutContentForEvidence == content, "Resize down/up preserves rich draft");
            SaveImage(root, output, $"shortcuts-code-expanded-{theme}", new Size(root.ActualWidth, root.ActualHeight), 1);
            window.ExpandShortcutForEvidence(false); owner.UpdateLayout();
            Require(preview.IsExpanded && ((FrameworkElement)window.FindName("ShortcutSidebarPanel")).Visibility == Visibility.Visible, "Collapse restores panels and prior preview state");
            Require(await window.FlagShortcutForEvidence(true) && await window.FlagShortcutForEvidence(false), "Favorite and pin save successfully");
            Require(window.ShortcutsDraftDirtyForEvidence && window.ShortcutContentForEvidence == content, "Preference save never overwrites dirty draft");
            var preference = (await new SnippetMarkdownRepository().LoadAsync()).Single(s => s.Id == fixture.Id);
            Require(preference.IsFavorite && preference.IsPinned && preference.Content == fixture.Content, "Preference saves last committed content, not pending draft");
            var list = (ShortcutListBox)window.FindName("SnippetListPanel");
            window.FilterShortcutForEvidence(true, false); Require(list.Items.Count == 1, "Favorites filter");
            window.FilterShortcutForEvidence(false, true); Require(list.Items.Count == 1, "Pinned filter");
            window.FilterShortcutForEvidence(false, false); Require(list.Items.Count == 2, "All restores list");
            Require(await window.SaveShortcutForEvidence(), "Code persists with real main Save");
            var saved = (await new SnippetMarkdownRepository().LoadAsync()).Single(s => s.Id == fixture.Id);
            Require(saved.IsFavorite && saved.IsPinned && CodeBlockMarkdown.Read(saved.Content).Single().Content.Code == edited, "Save preserves code and preferences");
            window.SelectShortcutForEvidence(saved, true); Require(Raw() == edited && window.ShortcutContentForEvidence == saved.Content && !window.ShortcutsDraftDirtyForEvidence, "Reopen reconstructs code and empty surrounding paragraphs exactly");
            owner.UpdateLayout(); SaveImage(root, output, $"shortcuts-code-{theme}", new Size(root.ActualWidth, root.ActualHeight), 1);
            window.ExpandShortcutForEvidence(true); owner.UpdateLayout();
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); owner.UpdateLayout();
            SaveImage(root, output, $"shortcuts-code-saved-expanded-{theme}", new Size(root.ActualWidth, root.ActualHeight), 1);
            window.ExpandShortcutForEvidence(false); owner.UpdateLayout();
            var beforeDuplicate = File.ReadAllBytes(AppPaths.SnippetsFile);
            window.DuplicateShortcutForEvidence();
            Require(window.SelectedShortcutForEvidence is null && window.ShortcutsDraftDirtyForEvidence && window.ShortcutContentForEvidence == saved.Content, "Duplicate creates editable unsaved draft with all rich content");
            Require(beforeDuplicate.SequenceEqual(File.ReadAllBytes(AppPaths.SnippetsFile)), "Duplicate does not persist before main Save");
            Require(await window.SaveShortcutForEvidence(), "Duplicate saves");
            var reopened = await new SnippetMarkdownRepository().LoadAsync(); var copy = reopened.Single(s => s.Trigger == "/codigo_copia");
            Require(copy.Id != fixture.Id && copy.Content == saved.Content && !copy.IsFavorite && !copy.IsPinned && reopened.Single(s => s.Id == fixture.Id).Content == saved.Content, "Duplicate has independent identity and leaves original unchanged");
            // Reopening the data reloads the actual navigation list, not just a detached
            // repository object: Save uses the selected object from that list.
            window.PrepareShortcutsEvidence(root.ActualWidth, reopened.ToArray());
            window.SelectShortcutForEvidence(copy, true);
            editor.CaretPosition = editor.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward); editor.Selection.Select(editor.CaretPosition, editor.CaretPosition);
            Require(window.CategoryNameForEvidence == "Outros", "Legacy category is displayed as Outros");
            var legacyBytes = File.ReadAllBytes(AppPaths.SnippetsFile);
            window.SelectCategoryForEvidence("Código");
            Require(window.ShortcutsDraftDirtyForEvidence && legacyBytes.SequenceEqual(File.ReadAllBytes(AppPaths.SnippetsFile)), "Changing a category remains a draft until Save");
            window.InsertEmojiForEvidence("😀"); window.InsertEmojiForEvidence("❤️");
            var withEmoji = window.ShortcutContentForEvidence;
            Require(ShortcutEmojiAssets.Pattern().Matches(withEmoji).Count == 2 && RichTextMarkdownConverter.ToHtml(withEmoji).Contains("width:28px", StringComparison.Ordinal), "Google emojis render inline and export at emoji size");
            var emojiPlain = RichTextMarkdownConverter.ToPlainText(withEmoji);
            Require(emojiPlain.Contains("😀", StringComparison.Ordinal) && emojiPlain.Contains("❤️", StringComparison.Ordinal) && !emojiPlain.Contains("[Imagem: Emoji:", StringComparison.Ordinal), "Plain expansion uses real Unicode for Google emoji");
            editor.Undo(); editor.Redo(); Require(window.ShortcutContentForEvidence == withEmoji, "Emoji source survives native undo/redo");
            using (var customBitmap = NotoEmojiCatalog.CreateBitmap("😀")) customBitmap.Save(Path.Combine(output, "custom-shortcut-emoji.png"), System.Drawing.Imaging.ImageFormat.Png);
            var custom = CaptureStampCatalog.Current.Import(Path.Combine(output, "custom-shortcut-emoji.png"), "Meu emoji de teste");
            window.InsertEmojiForEvidence(custom); CaptureStampCatalog.Current.Remove(custom);
            withEmoji = window.ShortcutContentForEvidence;
            Require(ShortcutEmojiAssets.Pattern().Matches(withEmoji).Count == 3, "Custom image copies into shortcut before collection removal");
            Require(await window.SaveShortcutForEvidence(), "Emoji shortcut saves");
            var emojiSaved = (await new SnippetMarkdownRepository().LoadAsync()).Single(s => s.Id == copy.Id);
            Require(emojiSaved.Category == "Código" && emojiSaved.Content == withEmoji, "Selected canonical category survives save/reopen with rich content intact");
            var emojiList = (await new SnippetMarkdownRepository().LoadAsync()).ToArray();
            window.PrepareShortcutsEvidence(root.ActualWidth, emojiList);
            window.SelectShortcutForEvidence(emojiList.Single(s => s.Id == copy.Id), true);
            Require(window.ShortcutContentForEvidence == withEmoji && editor.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<InlineUIContainer>()).Count(i => i.Child is Image) == 3, "Google emoji images reopen inline");
            var proseHtml = RichTextMarkdownConverter.ToHtml("Antes " + ShortcutEmojiAssets.Pattern().Match(withEmoji).Value + " Depois");
            Require(proseHtml.Contains("Antes ", StringComparison.Ordinal) && proseHtml.Contains(" Depois", StringComparison.Ordinal) && proseHtml.Contains("data:image/png;base64,", StringComparison.Ordinal), "Emoji HTML preserves surrounding prose");
            window.ExpandShortcutForEvidence(true); owner.UpdateLayout(); SaveImage(root, output, $"shortcuts-emoji-expanded-{theme}", new Size(root.ActualWidth, root.ActualHeight), 1);
            window.ExpandShortcutForEvidence(false);
            window.ShareShortcutForEvidence();
            Require(window.SelectedShortcutForEvidence is null && window.ShortcutContentForEvidence == ShortcutShareMessage.Content && window.ShortcutsDraftDirtyForEvidence, "Share message creates a new draft without modifying existing shortcut");
            Require(await window.SaveShortcutForEvidence(), "Share message saves");
            var sharing = (await new SnippetMarkdownRepository().LoadAsync()).Single(s => s.Trigger == "/slashdesk");
            Require(sharing.Content.Contains("https://github.com/lucasllira/SlashText/releases/latest", StringComparison.Ordinal), "Share message uses official latest release link");
            window.SetShortcutProtectedForEvidence(true); Require(!await window.FlagShortcutForEvidence(true), "Protected storage cannot modify favorite"); window.SetShortcutProtectedForEvidence(false);
            owner.Width = 980; owner.Height = 680; window.PrepareShortcutsEvidence(980, reopened.ToArray()); owner.UpdateLayout();
            SaveImage(root, output, $"shortcuts-code-narrow-{theme}", new Size(root.ActualWidth, root.ActualHeight), 1);
            var dialog = new ShortcutCodeBlockWindow(new CodeBlockContent("javascript", edited)) { Owner = owner };
            LabMotion.SetReduced(dialog, true); dialog.EnableBackdrop(); Exception? failure = null;
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    dialog.UpdateLayout(); Require(dialog.Surface.ActualHeight <= root.ActualHeight - 32, "Code popup fits native owner");
                    SaveImage((FrameworkElement)dialog.Content, output, $"shortcuts-code-popup-{theme}", new Size(dialog.Width, dialog.Height), 1);
                    dialog.CodeEditor.Text = edited + "\n// salvo"; dialog.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception ex) { failure = ex; dialog.Close(); }
            }), DispatcherPriority.ApplicationIdle);
            Require(dialog.ShowDialog() == true && dialog.Result?.Code == edited + "\n// salvo", "Native code popup returns literal edited source");
            if (failure is not null) throw new InvalidOperationException("Code popup smoke", failure);
            var cancel = new ShortcutCodeBlockWindow(new CodeBlockContent("javascript", edited)) { Owner = owner }; cancel.EnableBackdrop();
            cancel.Loaded += (_, _) => cancel.Dispatcher.BeginInvoke(new Action(() => { cancel.CodeEditor.Text = "não salvar"; cancel.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }), DispatcherPriority.ApplicationIdle);
            Require(cancel.ShowDialog() != true && cancel.Result is null, "Cancel returns no replacement code");
        }
        finally { owner.Close(); window.DisposeShortcutsEvidence(); window.Close(); }
    }
}
