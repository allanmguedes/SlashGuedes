using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;
using System.Globalization;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SlashText.Models;
using SlashText.Services;
using SlashText.Views;

namespace SlashText.Design;

/// <summary>Real WPF editor fixture; no tray, hooks, updater or user data.</summary>
internal static partial class ShortcutsWorkspaceSmoke
{
    internal static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable,
            Path.Combine(output, "data-fixture"), Path.Combine(output, "unused-installed"), isCapturePilot: true));
        Directory.CreateDirectory(AppPaths.AssetsDirectory);
        var imagePath = Path.Combine(AppPaths.AssetsDirectory, "fixture.png");
        using (var image = new System.Drawing.Bitmap(32, 24))
        { image.SetPixel(10, 10, System.Drawing.Color.Cyan); image.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png); }
        var originalAsset = File.ReadAllBytes(imagePath);
        var fixtures = new[]
        {
            new Snippet { Name = "Resposta de boas-vindas", Trigger = "/ola", Category = "Geral", Content = "Olá, {{nome}}!\nComo posso ajudar?" },
            new Snippet { Name = "Assinatura com imagem", Trigger = "/assinatura", Category = "Trabalho", Format = SnippetFormat.Markdown,
                Content = "**Olá**, {{campo|equipe}}!\n<span style=\"font-family:Georgia;font-size:18px;color:#137b53\">Texto formatado</span>\n![Exemplo](assets/fixture.png)\n\n| Item | Valor |\n| --- | --- |\n| Data | {{data}} |" },
            new Snippet { Name = "Retorno da solicitação", Trigger = "/retorno", Category = "Trabalho", Content = "Sua solicitação será revisada em {{data:+7d}}.\nAtenciosamente, {{usuario}}." }
        };
        var checks = new List<string>();
        foreach (var theme in new[] { "Light", "Dark", "System" })
        {
            ThemeService.Apply(theme);
            foreach (var size in new[] { new Size(1440, 900), new Size(980, 680) })
            {
                var window = new MainWindow(captureEvidence: true);
                var root = (FrameworkElement)window.Content; window.Content = null;
                LabMotion.SetReduced(root, true);
                var host = new Border { Child = root, Width = size.Width, Height = size.Height };
                host.SetResourceReference(Border.BackgroundProperty, "Lab.bg");
                void Layout() { host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout(); }
                window.PrepareShortcutsEvidence(size.Width, fixtures); Layout();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Layout();
                Require(!window.ShortcutsDraftDirtyForEvidence, "Selecting a saved snippet must not mark it dirty");
                Require(((TextBox)window.FindName("NameBox")).Text == fixtures[0].Name, "Real editor loads selected metadata");
                Require(window.ShortcutPreviewForEvidence.Contains("Olá, [nome]!", StringComparison.Ordinal), "Real variable preview renders a placeholder");
                Require(await window.SaveShortcutForEvidence(), "Plain save succeeds");
                var plainSaved = (await new SnippetMarkdownRepository().LoadAsync()).Single(s => s.Id == fixtures[0].Id);
                Require(plainSaved.Format == SnippetFormat.Plain && plainSaved.Content == fixtures[0].Content, "Simple content survives real save/reopen");
                // Saving replaces the selected object; keep the fixture aligned for cancellation assertions below.
                fixtures[0] = plainSaved;
                foreach (var name in new[] { "ShortcutHeaderActions", "ShortcutSidebarPanel", "ShortcutEditorPanel", "ShortcutVariablesPanel", "NameBox", "TriggerBox", "CategoryBox" })
                {
                    var element = (FrameworkElement)window.FindName(name);
                    Require(element.ActualWidth > 0 && element.ActualHeight > 0, "Measured control: " + name);
                }
                foreach (var action in new[] { "ShortcutNewButton", "ShortcutSaveButton" })
                {
                    var button = (Button)window.FindName(action);
                    var label = ((StackPanel)button.Content).Children.OfType<TextBlock>().Single();
                    Require(label.Foreground.ToString() == button.Foreground.ToString(), "Primary authored label follows on-accent color: " + action);
                }
                SaveImage(host, output, $"shortcuts-{theme}-plain-{size.Width}", size, 1);
                var preview = (Expander)window.FindName("ShortcutPreviewExpander");
                var eye = (LabIcon)window.FindName("ShortcutPreviewEye");
                preview.IsExpanded = false; Layout(); Require(eye.Kind == "EyeClosed", "Collapsed preview closes its eye");
                preview.IsExpanded = true; Layout(); Require(eye.Kind == "Eye", "Expanded preview opens its eye");
                var nameBox = (TextBox)window.FindName("NameBox"); nameBox.Text += " (rascunho)";
                window.SelectCategoryForEvidence("Mensagens");
                Require(window.ShortcutsDraftDirtyForEvidence, "Metadata edits mark the draft dirty");
                window.SelectShortcutForEvidence(fixtures[1], allowDiscard: false);
                Require(window.SelectedShortcutForEvidence == fixtures[0].Id && nameBox.Text.EndsWith("(rascunho)", StringComparison.Ordinal) && window.CategoryIconForEvidence == "Mail", "Cancelled navigation preserves selection, text and pending category choice");
                window.SelectShortcutForEvidence(fixtures[1], allowDiscard: true); Layout();
                Require(!window.ShortcutsDraftDirtyForEvidence && window.SelectedShortcutForEvidence == fixtures[1].Id, "Confirmed discard loads the requested snippet");
                Require(window.CategoryIconForEvidence == "Briefcase" && window.CategoryNameForEvidence == "Trabalho", "Confirmed discard restores saved category and fixed icon");
                var before = window.ShortcutContentForEvidence;
                Require(before.Contains("assets/fixture.png", StringComparison.Ordinal) && before.Contains("font-family", StringComparison.Ordinal), "Rich content retains images and font styling");
                window.InsertVariableForEvidence("{{hora}}");
                window.SelectCategoryForEvidence("Trabalho");
                Require(window.ShortcutsDraftDirtyForEvidence && window.ShortcutContentForEvidence.Contains("{{hora}}", StringComparison.Ordinal), "Variable chip inserts at the real editor caret");
                Require(!window.ShortcutPreviewForEvidence.Contains("{{hora}}", StringComparison.Ordinal), "Variable insertion updates the real preview");
                Require(await window.SaveShortcutForEvidence(), "Real async save succeeds");
                var saved = (await new SnippetMarkdownRepository().LoadAsync()).Single(s => s.Id == fixtures[1].Id);
                Require(saved.Content == window.ShortcutContentForEvidence && saved.Format == SnippetFormat.Markdown, "Saved/reopened rich content is identical");
                Require(originalAsset.SequenceEqual(File.ReadAllBytes(imagePath)), "Rich editor save does not alter image bytes");
                Require(!window.ShortcutsDraftDirtyForEvidence, "Successful save resets the draft indicator");
                Require(saved.Category == "Trabalho" && window.CategoryIconForEvidence == "Briefcase", "Category choice persists in snippet metadata with its fixed icon");
                var categories = (ComboBox)window.FindName("CategoryBox");
                Require(!categories.IsEditable && categories.Items.OfType<ShortcutCategory>().Select(c => c.Name).SequenceEqual(new[] { "Geral", "Outros", "Trabalho", "Estudos", "Mensagens", "Documentos", "Código", "Comandos" }), "Only eight ordered fixed categories, with no Favorites category");
                Require(((StackPanel)window.FindName("CategoriesPanel")).Children.Count == 9, "All eight sidebar categories plus Todos remain available");
                foreach (var category in ShortcutCategories.All) Require(window.TryFindResource("Lab.Icon." + category.Icon) is Geometry, "Category icon exists: " + category.Name);
                Layout();
                foreach (var scale in new[] { 1d, 1.25d, 1.5d, 2d }) SaveImage(host, output, $"shortcuts-{theme}-rich-{size.Width}", size, scale);
                var variables = (FrameworkElement)window.FindName("ShortcutVariablesPanel"); var editor = (FrameworkElement)window.FindName("ShortcutEditorPanel");
                var widthBefore = editor.ActualWidth;
                window.SetVariablesVisibleForEvidence(false); Layout();
                Require(variables.Visibility == Visibility.Collapsed && editor.ActualWidth > widthBefore, "Hiding variables gives space to the editor");
                window.SetVariablesVisibleForEvidence(true); Layout();
                Require(variables.Visibility == Visibility.Visible && variables.ActualWidth >= 240, "Showing variables restores a usable width");
                var search = (TextBox)window.FindName("SearchBox"); search.Text = "sem resultado neste teste"; Layout();
                Require(((Button)window.FindName("ShortcutSearchClearButton")).Visibility == Visibility.Visible, "Clear search is available when needed");
                var list = (ShortcutListBox)window.FindName("SnippetListPanel");
                Require(list.Items.Count == 0 && ((FrameworkElement)window.FindName("ShortcutListEmptyPanel")).Visibility == Visibility.Visible, "Empty search exposes recovery action");
                search.Clear(); Layout(); Require(list.Items.Count == fixtures.Length, "Clearing search restores the list");
                Require(((Button)window.FindName("ShortcutSearchClearButton")).Visibility == Visibility.Collapsed, "Empty search has no stray clear button");
                window.SetShortcutProtectedForEvidence(true);
                Require(!((Button)window.FindName("ShortcutSaveButton")).IsEnabled && !((Button)window.FindName("ShortcutDeleteButton")).IsEnabled && !((Button)window.FindName("ShortcutImportButton")).IsEnabled, "Protected storage blocks all snippet write actions");
                window.SetShortcutProtectedForEvidence(false);
                foreach (var topic in ShortcutsHelpContent.Create().Topics)
                    Require(topic.Target is null || window.FindName(topic.Target) is FrameworkElement, "Help target exists: " + topic.Id);
                checks.Add($"{theme} {size}: plain/rich save/reopen, fixed category selection/save/discard, preview eye, caret, image preservation, search clear states, variable panel, protected mode, help targets OK");
                window.DisposeShortcutsEvidence(); window.Close();
            }
            var guide = new ScreenHelpWindow(ShortcutsHelpContent.Create());
            guide.OpenTopic("variables"); guide.SearchForEvidence("variaveis");
            Require(guide.ResultCount > 0, "Accent-insensitive help search");
            guide.SearchForEvidence("");
            var surface = guide.HelpSurface; guide.Content = null; LabMotion.SetReduced(surface, true);
            var helpSize = new Size(1040, 780); surface.Measure(helpSize); surface.Arrange(new Rect(helpSize)); surface.UpdateLayout();
            SaveImage(surface, output, $"shortcuts-help-{theme}", helpSize, 1); guide.Close();
            var confirm = new ShortcutConfirmationWindow("Excluir atalho?", "Este atalho será removido da sua lista.", "Resposta de boas-vindas\n/ola · Geral", "Excluir atalho");
            var confirmationSurface = confirm.Surface; confirm.Content = null; LabMotion.SetReduced(confirmationSurface, true);
            var confirmationSize = new Size(500, 350); confirmationSurface.Measure(confirmationSize); confirmationSurface.Arrange(new Rect(confirmationSize)); confirmationSurface.UpdateLayout();
            Require(confirm.CancelButton.IsDefault && !confirm.AcceptButton.IsDefault, "Confirmation defaults to cancelling, never deleting");
            Require(ModalBackdrop.IsOutside(confirmationSurface, new Point(-10, 30)) && !ModalBackdrop.IsOutside(confirmationSurface, new Point(50, 50)), "Background dismissal only targets points outside the surface");
            SaveImage(confirmationSurface, output, $"shortcuts-delete-{theme}", confirmationSize, 1); confirm.Close();
            CheckOwnedModals(theme, output);
            CheckCaptureComplements(theme, output);
            checks.Add($"{theme}: Capture object selection/style/geometry/undo, 75-entry full history/search/pages, real owned help and outside dismissal OK");
            await CheckTypographyAsync(theme, output);
            await CheckExtrasAsync(theme, output);
            checks.Add($"{theme}: code literal save/reopen, insert/edit undo/redo, themed code modal, expansion, duplicate, favorite/pin persistence and filters OK");
            checks.Add($"{theme}: native editor font selection/caret typing, ordered sizes, palette RGB validation and rich save/reopen OK");
            checks.Add($"{theme}: real owned modal layout, default cancel, inside/outside dismissal and no snippet changes OK");
        }
        await ShortcutListSmoke.RunAsync(Path.Combine(output, "virtualized-list"));
        File.WriteAllLines(Path.Combine(output, "result.txt"), checks);
    }

    private static void SaveImage(Visual visual, string output, string name, Size size, double scale)
    {
        var bitmap = new RenderTargetBitmap((int)(size.Width * scale), (int)(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, $"{name}-{scale * 100:0}.png")); encoder.Save(file);
    }

    private static void CheckOwnedModals(string theme, string output)
    {
        // Exercise the actual Window ownership/ShowDialog path, not only detached rasterized cards.
        var before = File.ReadAllBytes(AppPaths.SnippetsFile);
        var ownerRoot = new Grid(); ownerRoot.SetResourceReference(Grid.BackgroundProperty, "Lab.bg");
        var owner = new Window { Width = 980, Height = 680, Content = ownerRoot, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        owner.Show(); owner.UpdateLayout();
        try
        {
            var dialog = new ShortcutConfirmationWindow("Excluir atalho?", "Este atalho será removido da sua lista.", "Resposta de boas-vindas\n/ola · Geral", "Excluir atalho") { Owner = owner };
            LabMotion.SetReduced(dialog, true); dialog.EnableBackdrop();
            Exception? failure = null;
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    dialog.UpdateLayout();
                    Require(dialog.Content is Grid && Math.Abs(dialog.Width - ownerRoot.ActualWidth) < 1, "Modal backdrop covers its real owner client area");
                    SaveImage((FrameworkElement)dialog.Content, output, $"shortcuts-delete-owned-{theme}", new Size(dialog.Width, dialog.Height), 1);
                    Require(!ModalBackdrop.DismissAt(dialog.Surface, new Point(40, 40), dialog.Close), "Clicking inside keeps the confirmation open");
                    dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception exception) { failure = exception; dialog.Close(); }
            }), DispatcherPriority.ApplicationIdle);
            Require(dialog.ShowDialog() != true, "Default cancel never accepts deletion");
            if (failure is not null) throw new InvalidOperationException("Owned confirmation smoke failed", failure);

            var guide = new ScreenHelpWindow(ShortcutsHelpContent.Create()) { Owner = owner };
            LabMotion.SetReduced(guide, true); guide.EnableBackdrop();
            guide.Loaded += (_, _) => guide.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    guide.UpdateLayout();
                    Require(guide.Content is Grid && guide.HelpSurface.ActualHeight <= ownerRoot.ActualHeight - 32, "Help fits the actual owner height");
                    SaveImage((FrameworkElement)guide.Content, output, $"shortcuts-help-owned-{theme}", new Size(guide.Width, guide.Height), 1);
                    Require(ModalBackdrop.DismissAt(guide.HelpSurface, new Point(-10, 30), guide.Close), "Outside dismissal closes the real help modal");
                }
                catch (Exception exception) { failure = exception; guide.Close(); }
            }), DispatcherPriority.ApplicationIdle);
            Require(guide.ShowDialog() != true, "Read-only help dismissal is not a write confirmation");
            if (failure is not null) throw new InvalidOperationException("Owned help smoke failed", failure);
            Require(before.SequenceEqual(File.ReadAllBytes(AppPaths.SnippetsFile)), "Dismissing help/confirmation leaves snippet storage untouched");
        }
        finally { owner.Close(); }
    }

    private static async Task CheckTypographyAsync(string theme, string output)
    {
        var window = new MainWindow(captureEvidence: true);
        var root = (FrameworkElement)window.Content; window.Content = null;
        var owner = new Window { Width = 980, Height = 680, Content = root, ShowInTaskbar = false };
        try
        {
            window.PrepareShortcutsEvidence(980, [new Snippet { Name = "Teste de fontes", Trigger = "/fontes",
                Category = "Geral", Format = SnippetFormat.Markdown, Content = "Texto de teste" }]);
            owner.Show(); owner.UpdateLayout();
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var editor = (RichTextBox)window.FindName("ContentEditor");
            var fonts = (ComboBox)window.FindName("FontFamilyBox");
            var sizes = (ComboBox)window.FindName("FontSizeBox");
            var typographyFixture = new Snippet { Name = "Teste de fontes", Trigger = "/fontes",
                Category = "Geral", Format = SnippetFormat.Markdown, Content = "Texto de teste" };
            void AssertReadableFontControls(string stage)
            {
                owner.UpdateLayout();
                foreach (var box in new[] { fonts, sizes })
                {
                    var label = ((ComboBoxItem)box.SelectedItem).Content.ToString()!;
                    var presenter = (FrameworkElement)box.Template.FindName("Selection", box);
                    var text = new FormattedText(label, CultureInfo.GetCultureInfo("pt-BR"), box.FlowDirection,
                        new Typeface(box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch),
                        box.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(box).PixelsPerDip);
                    Require(presenter.ActualHeight + .5 >= text.Height && presenter.ActualWidth + .5 >= text.WidthIncludingTrailingWhitespace,
                        $"Font control has room for full label and descenders: {box.Name}, {stage}");
                }
                var strip = (FrameworkElement)fonts.Parent;
                var stripSize = new Size(strip.ActualWidth, strip.ActualHeight);
                // Rasterizing an attached child directly includes its layout offset.
                // Map its local bounds to the image so the evidence itself is not cut.
                var drawing = new DrawingVisual();
                using (var context = drawing.RenderOpen())
                    context.DrawRectangle(new VisualBrush(strip) { Stretch = Stretch.Fill }, null, new Rect(stripSize));
                SaveImage(drawing, output, $"shortcuts-font-controls-{stage}-{theme}", stripSize, 1);
            }
            void SelectText()
            {
                editor.Focus();
                var paragraph = (Paragraph)editor.Document.Blocks.FirstBlock;
                editor.Selection.Select(paragraph.ContentStart.GetInsertionPosition(LogicalDirection.Forward),
                    paragraph.ContentEnd.GetInsertionPosition(LogicalDirection.Backward));
            }
            void ChooseFont(string name)
            {
                fonts.Focus(); fonts.SelectedItem = fonts.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, name));
            }
            SelectText();
            Require(editor.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double initialSize && Math.Abs(initialSize - 16) < .01 &&
                sizes.SelectedItem is ComboBoxItem { Content: "12" }, "Unstyled text defaults to readable 12 points");
            Require(!window.ShortcutsDraftDirtyForEvidence && Math.Abs(((FlowDocument)window.FindName("PreviewDocument")).FontSize - 16) < .01,
                "Default display size matches preview without rewriting a saved snippet");
            AssertReadableFontControls("default");
            var legacy = new Snippet { Name = "Tamanho anterior", Trigger = "/legado", Format = SnippetFormat.Markdown,
                Content = "<span style=\"font-size:14px\">Texto anterior</span>" };
            window.PrepareShortcutsEvidence(root.ActualWidth, [legacy]); SelectText();
            Require(editor.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double legacySize && Math.Abs(legacySize - 14) < .01 &&
                sizes.SelectedItem is ComboBoxItem { Content: "10,5", IsEnabled: false }, "Explicit 10.5-point text is preserved and shown as current value, not a preset");
            Require(!window.ShortcutsDraftDirtyForEvidence, "Opening a fractional legacy size does not dirty it");
            AssertReadableFontControls("legacy");
            Require(await window.SaveShortcutForEvidence(), "Legacy size saves without rounding");
            var legacyList = (await new SnippetMarkdownRepository().LoadAsync()).ToArray();
            window.PrepareShortcutsEvidence(root.ActualWidth, legacyList); SelectText();
            Require(editor.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double reopenedLegacy && Math.Abs(reopenedLegacy - 14) < .01,
                "Explicit fractional legacy size survives real save/reopen");
            window.PrepareShortcutsEvidence(root.ActualWidth, [typographyFixture]); SelectText();
            Require(sizes.Items.OfType<ComboBoxItem>().All(item => item.IsEnabled), "Leaving legacy text removes its readout from the normal size choices");
            ChooseFont("Consolas");
            Require(editor.Selection.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily family && family.Source == "Consolas",
                "Font chooser applies the selected text font despite focus transfer");
            Require(fonts.SelectedItem is ComboBoxItem { Tag: "Consolas" }, "Font chooser stays in sync after applying");
            sizes.Focus(); sizes.SelectedItem = sizes.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Content, "24"));
            Require(editor.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double size && Math.Abs(size - 32) < .01, "24 points converts to 32 WPF units");
            var ordered = sizes.Items.OfType<ComboBoxItem>().Select(item => double.Parse((string)item.Tag, CultureInfo.InvariantCulture)).ToArray();
            Require(ordered.SequenceEqual(ordered.OrderBy(n => n)) && sizes.Items.OfType<ComboBoxItem>().All(item => item.IsEnabled) &&
                sizes.Items.OfType<ComboBoxItem>().All(item => int.TryParse(item.Content.ToString(), out _)), "Normal size choices stay ordered whole points with no stale fractional preset");
            var palette = window.ShortcutColorContentForEvidence(false);
            palette.Measure(new Size(560, 200)); palette.Arrange(new Rect(0, 0, 560, 200)); palette.UpdateLayout();
            ((Button)Find(palette, "OpenInkRgb")!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var hex = (TextBox)Find(palette, "InkHex")!; var apply = (Button)Find(palette, "ApplyInkHex")!;
            hex.Text = "#137B53"; apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(editor.Selection.GetPropertyValue(TextElement.ForegroundProperty) is SolidColorBrush ink && ink.Color == Color.FromRgb(19, 123, 83), "Custom hex targets the preserved selected text");
            var content = window.ShortcutContentForEvidence;
            hex.Text = "#oops"; apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(window.ShortcutContentForEvidence == content, "Invalid hex never changes the rich draft");
            var red = (TextBox)Find(palette, "InkRed")!; red.Text = "300"; apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(window.ShortcutContentForEvidence == content, "RGB above 255 never changes the rich draft");
            red.Text = "100"; ((TextBox)Find(palette, "InkGreen")!).Text = "90"; ((TextBox)Find(palette, "InkBlue")!).Text = "80";
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(editor.Selection.GetPropertyValue(TextElement.ForegroundProperty) is SolidColorBrush rgb && rgb.Color == Color.FromRgb(100, 90, 80), "Valid RGB applies to selection");
            var highlight = window.ShortcutColorContentForEvidence(true);
            highlight.Measure(new Size(560, 200)); highlight.Arrange(new Rect(0, 0, 560, 200)); highlight.UpdateLayout();
            var swatches = (StackPanel)Find(highlight, "InkPalette")!;
            swatches.Children.OfType<Button>().Single(button => Equals(button.ToolTip, "Amarelo · #FFD800")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(editor.Selection.GetPropertyValue(TextElement.BackgroundProperty) is SolidColorBrush bg && bg.Color == Color.FromRgb(255, 216, 0), "Highlight palette applies the selected swatch");
            LabMotion.SetReduced(palette, true);
            var paletteSize = new Size(560, 166); palette.Measure(paletteSize); palette.Arrange(new Rect(paletteSize)); palette.UpdateLayout();
            SaveImage(palette, output, $"shortcuts-color-{theme}", paletteSize, 1);
            Require(Find(palette, "InkThickness") is null, "Text palette does not expose annotation thickness");
            Require(await window.SaveShortcutForEvidence(), "Typography and colors save through real repository");
            var saved = (await new SnippetMarkdownRepository().LoadAsync()).Single(s => s.Trigger == "/fontes");
            window.SelectShortcutForEvidence(saved, true); SelectText();
            Require(editor.Selection.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily reopened && reopened.Source == "Consolas", "Chosen font survives save/reopen");
            // Background on a loaded Span is rendered by the span, but is not an inherited
            // Run property. Verify the actual loaded element and the lossless serialized draft.
            var reopenedSpan = ((Paragraph)editor.Document.Blocks.FirstBlock).Inlines.OfType<Span>().Single();
            Require(reopenedSpan.Background is SolidColorBrush reopenedBg && reopenedBg.Color == Color.FromRgb(255, 216, 0)
                && window.ShortcutContentForEvidence == saved.Content, "Highlight and all rich properties survive save/reopen");
            editor.Selection.Text = ""; ChooseFont("Georgia");
            Require(fonts.SelectedItem is ComboBoxItem { Tag: "Georgia" }, "Empty editor keeps the font chosen before typing");
            editor.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,
                new TextComposition(InputManager.Current, editor, "Texto novo"))
                { RoutedEvent = TextCompositionManager.TextInputEvent });
            await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            SelectText();
            Require(editor.Selection.Text.TrimEnd('\r', '\n') == "Texto novo",
                "WPF TextInput inserts text after choosing an empty-editor font: " + editor.Selection.Text.Replace("\r", "<CR>").Replace("\n", "<LF>"));
            Require(editor.Selection.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily typed && typed.Source == "Georgia", "Font chosen before typing formats newly entered text");
            owner.UpdateLayout(); SaveImage(root, output, $"shortcuts-typography-{theme}", new Size(root.ActualWidth, root.ActualHeight), 1);
        }
        finally { owner.Close(); window.DisposeShortcutsEvidence(); window.Close(); }
    }

    private static FrameworkElement? Find(DependencyObject parent, string name)
    {
        if (parent is FrameworkElement element && element.Name == name) return element;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (Find(VisualTreeHelper.GetChild(parent, i), name) is { } found) return found;
        return null;
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Shortcuts workspace: " + message); }
}
