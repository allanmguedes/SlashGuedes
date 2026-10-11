using Microsoft.Win32;
using SlashText.Design;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SlashText.Views;

public partial class DesignGalleryWindow : Window
{
    private string _theme = "Light";
    private bool _ready;
    private readonly string? _smokeOutput;
    public DesignGalleryWindow(string? smokeOutput = null)
    {
        _smokeOutput = smokeOutput;
        InitializeComponent();
        InvalidField.SetBinding(TextBox.TextProperty, new Binding(nameof(Example.Value))
        { Source = new Example(), Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        foreach (var name in new[] { "Keyboard", "Camera", "Languages", "BarChart3", "Settings", "Info", "Monitor", "ScanLine", "AppWindow", "ScrollText", "Film", "Video", "PenLine", "Palette", "Save", "FolderOpen", "Undo2", "Redo2" })
        {
            var icon = new LabIcon { Kind = name, Width = 24, Height = 24, Margin = new Thickness(0,0,18,12), ToolTip = name };
            icon.SetResourceReference(LabIcon.ForegroundProperty, "Lab.text"); IconsPanel.Children.Add(icon);
        }
    }
    private async void GalleryLoaded(object sender, RoutedEventArgs e)
    {
        _ready = true;
        SystemEvents.UserPreferenceChanged += WindowsPreferenceChanged;
        SystemParameters.StaticPropertyChanged += AnimationPreferenceChanged;
        ApplyTheme(false); ValidateField(InvalidField, new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));
        if (_smokeOutput is null) return;
        try { await RunSmoke(); Application.Current.Shutdown(0); }
        catch (Exception exception)
        {
            Directory.CreateDirectory(_smokeOutput); File.WriteAllText(Path.Combine(_smokeOutput, "failure.txt"), exception.ToString());
            Application.Current.Shutdown(1);
        }
    }
    private void GalleryClosed(object? sender, EventArgs e)
    {
        _ready = false;
        SystemEvents.UserPreferenceChanged -= WindowsPreferenceChanged;
        SystemParameters.StaticPropertyChanged -= AnimationPreferenceChanged;
    }
    private void WindowsPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_ready && _theme == "System") Dispatcher.BeginInvoke(() => { if (_ready) ApplyTheme(true); });
    }
    private void AnimationPreferenceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_ready || e.PropertyName != nameof(SystemParameters.ClientAreaAnimation)) return;
        Dispatcher.BeginInvoke(() => { if (_ready) ApplyMotionPreference(); });
    }
    private void ThemeChanged(object sender, RoutedEventArgs e)
    {
        _theme = (string)((RadioButton)sender).Tag;
        if (_ready) ApplyTheme(true);
    }
    private void ApplyTheme(bool animate)
    {
        var dark = _theme == "Dark" || _theme == "System" && Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int light && light == 0;
        LabPalette.Apply(Resources, dark, animate && LabMotion.Allowed(this));
        // Popup HWND inherits resources, but its motion preference is explicitly propagated.
        LabMotion.SetReduced(PopupContent, LabMotion.GetReduced(this));
    }
    private void ApplyMotionPreference()
    {
        var reduced = ReducedSwitch.IsChecked == true || !SystemParameters.ClientAreaAnimation;
        LabMotion.SetReduced(this, reduced); LabMotion.SetReduced(PopupContent, reduced);
        if (reduced) ApplyTheme(false); // cancel any in-flight color transition as well
    }
    private void MotionChanged(object sender, RoutedEventArgs e) { if (_ready) ApplyMotionPreference(); }
    private void TabChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        var tab = ((RadioButton)sender).Content?.ToString() ?? "Atalhos";
        var page = tab switch
        {
            "Captura" => ("Captura", "Superfícies para capturar, editar e gravar com resposta visual imediata."),
            "Acento Rápido" => ("Acento Rápido", "Seleção de caracteres, estados de teclado e preferências de ativação."),
            "Estatísticas" => ("Estatísticas", "Cartões, filtros e hierarquia para apresentar uso sem poluir a leitura."),
            "Configurações" => ("Configurações", "Campos, seleções e switches para preferências persistentes do aplicativo."),
            "Sobre" => ("Sobre", "Informações do produto, versão, atualização e diagnósticos em linguagem clara."),
            _ => ("Atalhos", "Organização, edição e descoberta dos textos prontos do usuário.")
        };
        PageTitle.Text = $"Fundação para {page.Item1}";
        PageDescription.Text = page.Item2;
        Status.Text = $"Aba selecionada: {page.Item1}. O catálogo abaixo é compartilhado; a tela produtiva será migrada na issue correspondente.";
        GalleryScroller.ScrollToTop();
        LabMotion.PlayEntrance(PageSurface);
    }
    private void ActionClicked(object sender, RoutedEventArgs e) => Status.Text = $"{((Button)sender).Content}: clique recebido.";
    private void ReplayClicked(object sender, RoutedEventArgs e) => LabMotion.PlayEntrance(PageSurface);
    private void PopupClicked(object sender, RoutedEventArgs e) => OptionsPopup.IsOpen = true;
    private void PopupOpened(object? sender, EventArgs e) { LabMotion.PlayEntrance(PopupContent); ClosePopupButton.Focus(); }
    private void PopupClosed(object? sender, EventArgs e) => PopupButton.Focus();
    private void ClosePopupClicked(object sender, RoutedEventArgs e) => OptionsPopup.IsOpen = false;
    private void PopupKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { OptionsPopup.IsOpen = false; e.Handled = true; } }
    private void ValidateField(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        var expression = InvalidField.GetBindingExpression(TextBox.TextProperty);
        if (expression is null) return;
        var empty = string.IsNullOrWhiteSpace(InvalidField.Text);
        if (empty) Validation.MarkInvalid(expression, new ValidationError(new RequiredRule(), expression, "Campo obrigatório.", null));
        else Validation.ClearInvalid(expression);
        ValidationMessage.Text = empty ? "Campo obrigatório. Digite para corrigir." : "Campo válido.";
        ValidationMessage.SetResourceReference(TextBlock.ForegroundProperty, empty ? "Lab.error" : "Lab.green");
    }
    private async Task RunSmoke()
    {
        Directory.CreateDirectory(_smokeOutput!);
        // Exercise the animated path used by an interactive theme click.
        _theme = "Dark"; ApplyTheme(true);
        _theme = "Light"; ApplyTheme(true);
        ReducedSwitch.IsChecked = true;
        ApplyMotionPreference();
        foreach (var theme in new[] { "Light", "Dark" })
        {
            (theme == "Dark" ? DarkTheme : LightTheme).IsChecked = true;
            _theme = theme; ApplyTheme(false);
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            UpdateLayout();
            Require(Validation.GetHasError(InvalidField), "Invalid field must expose Validation.HasError");
            InvalidField.Text = "Validado"; Require(!Validation.GetHasError(InvalidField), "Error must clear after input"); InvalidField.Text = "";
            EnabledSwitch.IsChecked = false; EnabledSwitch.IsChecked = true;
            EnabledSwitch.ApplyTemplate();
            var thumb = (FrameworkElement)EnabledSwitch.Template.FindName("Thumb", EnabledSwitch);
            Require(((TranslateTransform)thumb.RenderTransform).X == 18, "Switch must reach checked position in reduced motion");
            var expected = theme == "Dark" ? "#FFF3F3F3" : "#FF202024";
            Require(ValidField.Foreground.ToString() == expected, "TextBox theme did not update");
            FormatCombo.IsDropDownOpen = true;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            FormatCombo.UpdateLayout();
            var dropdown = (Border)FormatCombo.Template.FindName("PopupSurface", FormatCombo);
            Require(dropdown.Background.ToString() == (theme == "Dark" ? "#FF242424" : "#FFFFFFFF"), "ComboBox popup theme");
            FormatCombo.IsDropDownOpen = false;
            OptionsPopup.IsOpen = true;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Require(PopupContent.Background.ToString() == (theme == "Dark" ? "#FF242424" : "#FFFFFFFF"), "Detached popup theme");
            OptionsPopup.IsOpen = false;
            foreach (var tab in new[] { "Captura", "Acento Rápido", "Estatísticas", "Configurações", "Sobre", "Atalhos" })
            {
                var item = FindPageTab(tab);
                item.IsChecked = true;
                Require(PageTitle.Text.Contains(tab, StringComparison.Ordinal), $"Tab content did not change for {tab}");
            }
            Require(Descendants(PrimaryButton).OfType<TextBlock>().Any(t => t.Text == "Ação principal" && t.Foreground.ToString() == (theme == "Dark" ? "#FF082126" : "#FFFFFFFF")), "Primary label must inherit on-accent color: " + string.Join(",", Descendants(PrimaryButton).OfType<TextBlock>().Select(t => t.Text + "=" + t.Foreground)));
            Require(Descendants(NormalButton).OfType<TextBlock>().Any(t => t.Text == "Ação secundária" && t.Foreground.ToString() == expected), "Neutral label must follow theme");
            Require(Descendants(FormatCombo).OfType<TextBlock>().Any(t => t.Text == "PNG — imagem" && t.Foreground.ToString() == expected), "Combo selection label must follow theme");
            await CaptureEditorEvidence(theme);
            CaptureInkPickerEvidence(theme);
            await CaptureOverlayEvidence(theme);
            CaptureCustomEmojiEvidence(theme);
            CaptureHelpEvidence(theme);
            foreach (var size in new[] { new Size(1440,900), new Size(980,680) })
            {
                // A fresh, never-shown visual has no runner work-area layout cached by an HWND.
                var snapshot = new DesignGalleryWindow();
                snapshot._theme = theme;
                (theme == "Dark" ? snapshot.DarkTheme : snapshot.LightTheme).IsChecked = true;
                snapshot._ready = true;
                snapshot.ApplyTheme(false);
                snapshot.ReducedSwitch.IsChecked = true;
                snapshot.ValidateField(snapshot.InvalidField, new TextChangedEventArgs(TextBox.TextChangedEvent, UndoAction.None));
                var panel = snapshot.GalleryRoot;
                snapshot.Content = null;
                panel.Resources = snapshot.Resources;
                LabMotion.SetReduced(panel, true);
                panel.Width = size.Width; panel.Height = size.Height;
                panel.Measure(size); panel.Arrange(new Rect(size)); panel.UpdateLayout();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                panel.Measure(size); panel.Arrange(new Rect(size)); panel.UpdateLayout();
                Require(Math.Abs(panel.ActualWidth - size.Width) < .1 && Math.Abs(panel.ActualHeight - size.Height) < .1,
                    $"Offscreen layout differs from requested size: {panel.ActualWidth}x{panel.ActualHeight}, expected {size}");
                foreach (var scale in new[] { 1d, 1.25d, 1.5d, 2d })
                {
                    var bitmap = new RenderTargetBitmap((int)(size.Width*scale), (int)(size.Height*scale), 96*scale, 96*scale, PixelFormats.Pbgra32);
                    bitmap.Render(panel);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(_smokeOutput!, $"gallery-{theme}-{size.Width}x{size.Height}-{scale*100:0}.png")); png.Save(file);
                }
                snapshot.Close();
            }
        }
        // Exercise real clocks and interruption as well as the reduced-motion path.
        GalleryRoot.ClearValue(WidthProperty); GalleryRoot.ClearValue(HeightProperty);
        ReducedSwitch.IsChecked = false; ApplyMotionPreference();
        LabMotion.PlayEntrance(PageSurface); LabMotion.PlayEntrance(PopupContent);
        EnabledSwitch.IsChecked = false; EnabledSwitch.IsChecked = true;
        await Task.Delay(250);
        Require(PageSurface.Opacity == 1, "Entrance must settle at full opacity");
        ReducedSwitch.IsChecked = true;
        Require(!LabMotion.Allowed(PageSurface), "Reduced motion must propagate");
        File.WriteAllText(Path.Combine(_smokeOutput!, "result.txt"),
            "PASS: interactive theme transition, tabs, validation, switch interruption/rest position, ComboBox and Popup resources, entrance clocks, reduced motion.\n" +
            "16 offscreen PNGs: 1440x900 and 980x680 DIP at 100/125/150/200%. This is NOT a physical mixed-monitor test.\n" +
            "6 capture PNGs: advanced editor, capture rule and shortcuts in Light/Dark; primary fill and text contrast verified.\n" +
            "Additional native evidence: floating bar normal/compact and custom emoji collection in Light/Dark; contrast, real selection geometry and custom import/select/remove exercised.\n" +
            "Manual pending: hover/pressed/focus, Windows theme event, actual DPI/monitors, fonts, visual approval.\n");
    }

    private async Task CaptureEditorEvidence(string theme)
    {
        Services.ThemeService.Apply(theme);
        using var source = new System.Drawing.Bitmap(960, 500);
        using (var graphics = System.Drawing.Graphics.FromImage(source))
        {
            graphics.Clear(System.Drawing.Color.WhiteSmoke);
            using var ink = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(32, 32, 36));
            using var font = new System.Drawing.Font("Segoe UI", 26);
            graphics.DrawString("Uma edição, os mesmos emotes.", font, ink, 48, 56);
        }
        using var annotated = Services.CaptureAnnotationRenderer.Render(source,
            new[] { "👍", "😀", "⭐", "❤️" }.Select((value, index) => new Services.CaptureAnnotation
            {
                Kind = Services.CaptureAnnotationKind.Stamp,
                Start = new Point(160 + index * 190, 250), Text = value, Size = 64
            }).ToList(), 960, 500);
        await CaptureWindowEvidence(new CaptureEditorWindow(annotated), theme, "advanced-editor", new Size(1220, 860), "Concluir");
        await CaptureWindowEvidence(new CaptureRuleDialog(new Models.CaptureSettings()), theme, "capture-rule", new Size(700, 740), "Salvar configurações");
        await CaptureWindowEvidence(new CaptureShortcutDialog(new Models.CaptureSettings()), theme, "capture-shortcuts", new Size(510, 650), "Salvar atalhos");
        await CaptureUnifiedEditorEvidence(annotated, theme);
        await CaptureUnifiedShellEvidence(annotated, theme);
    }

    private async Task CaptureUnifiedEditorEvidence(System.Drawing.Bitmap source, string theme)
    {
        using var editor = new CaptureWorkbenchEditor();
        editor.LoadImage(source);
        var context = new CaptureEditorContextPanel(); context.Attach(editor);
        editor.StateChanged += (_, _) => context.Refresh();
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.Children.Add(context); Grid.SetRow(editor, 1); layout.Children.Add(editor);
        var host = new Border { Child = layout, Width = 1360, Height = 640, Padding = new Thickness(12) };
        host.SetResourceReference(Border.BackgroundProperty, "Lab.canvas");
        LabMotion.SetReduced(host, true);
        foreach (var tool in new[] { Services.CaptureAnnotationKind.Text, Services.CaptureAnnotationKind.Stamp, Services.CaptureAnnotationKind.Blur })
        {
            editor.SelectTool(tool);
            var size = new Size(1360, 640);
            host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
            Require(context.ActualWidth > 1000 && editor.HasImage, "Unified editor real contextual layout");
            if (tool == Services.CaptureAnnotationKind.Text)
                Require(Descendants(context).OfType<ComboBox>().Any(c => c.SelectedItem?.ToString() == "Segoe UI"), "Font selector before text input");
            if (tool == Services.CaptureAnnotationKind.Stamp)
            {
                Require(Descendants(context).OfType<Image>().Count() == Services.NotoEmojiCatalog.QuickItems.Count,
                    "Quick strip stays bounded; complete catalog is in the picker");
                // ComboBox templates also contain a ScrollViewer; select the actual quick strip.
                var strip = Descendants(context).OfType<ScrollViewer>()
                    .Single(s => s.Content is StackPanel && s.HorizontalScrollBarVisibility == ScrollBarVisibility.Hidden);
                Require(strip.ActualHeight >= 48 && strip.HorizontalScrollBarVisibility == ScrollBarVisibility.Hidden,
                    "Emoji buttons have room without a scrollbar clipping their bottoms");
                foreach (var sizeBox in Descendants(context).OfType<ComboBox>())
                    Require(sizeBox.ActualWidth >= 90 && sizeBox.ActualHeight >= 38,
                        "Three-digit emote sizes and dropdown arrow fit without clipping");
                foreach (var width in new[] { 520d, 980d, 1360d })
                {
                    // Resize the parent too: UpdateLayout would otherwise arrange
                    // this child back into the original 1360px host.
                    host.Width = width + 24;
                    var narrowSize = new Size(host.Width, 640);
                    host.Measure(narrowSize); host.Arrange(new Rect(narrowSize)); host.UpdateLayout();
                    Require(Math.Abs(context.ActualWidth - width) < .5, $"Emoji context viewport is {width}px");
                    foreach (var control in Descendants(context).OfType<Control>().Where(c => c is Button or ComboBox))
                    {
                        // ScrollViewer intentionally clips offscreen strip buttons horizontally.
                        var origin = control.TranslatePoint(new Point(), context);
                        Require(origin.Y >= 0 && origin.Y + control.ActualHeight <= context.ActualHeight + .5,
                            $"Emoji control fits vertically at {width}px");
                    }
                    var sizeBox = Descendants(context).OfType<ComboBox>().Single();
                    var point = sizeBox.TranslatePoint(new Point(), context);
                    Require(point.X + sizeBox.ActualWidth <= width + .5,
                        $"Emoji size field fits horizontally at {width}px (right={point.X + sizeBox.ActualWidth})");
                }
                host.Width = 1360;
                context.InvalidateMeasure(); host.InvalidateMeasure();
                host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
            }
            var bitmap = new RenderTargetBitmap(1360, 640, 96, 96, PixelFormats.Pbgra32); bitmap.Render(host);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(_smokeOutput!, $"unified-editor-{tool}-{theme}.png")); png.Save(file);
        }
        var session = editor.Document;
        editor.InsertStamp("⭐", new Point(300, 180));
        editor.SetZoom(2);
        Require(ReferenceEquals(session, editor.Document) && editor.CanUndo, "Unified document survives viewport changes");
        var picker = CaptureEmojiPicker.CreateContent(_ => { }, () => { });
        var pickerSize = new Size(900, 560);
        picker.Measure(pickerSize); picker.Arrange(new Rect(pickerSize)); picker.UpdateLayout();
        Require(Descendants(picker).OfType<Image>().Count() == CaptureEmojiPicker.PageSize,
            "Complete picker decodes at most one page, not thousands of PNGs");
        var next = Descendants(picker).OfType<Button>().Single(b => b.Content is string text && text == "Próxima");
        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); picker.UpdateLayout();
        Require(Descendants(picker).OfType<TextBlock>().Any(t => t.Text.Contains("página 2/")), "Picker next page is functional");
        var category = Descendants(picker).OfType<ComboBox>().Single();
        category.SelectedItem = category.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == "Bandeiras"); picker.UpdateLayout();
        Require(Descendants(picker).OfType<Image>().Count() <= CaptureEmojiPicker.PageSize && next.IsEnabled,
            "Flag category remains paginated and complete");
        var pngPicker = new PngBitmapEncoder();
        var pickerBitmap = new RenderTargetBitmap(900, 560, 96, 96, PixelFormats.Pbgra32); pickerBitmap.Render(picker);
        pngPicker.Frames.Add(BitmapFrame.Create(pickerBitmap));
        using var pickerFile = File.Create(Path.Combine(_smokeOutput!, $"unified-emotes-catalog-{theme}.png")); pngPicker.Save(pickerFile);
    }

    private async Task CaptureUnifiedShellEvidence(System.Drawing.Bitmap source, string theme)
    {
        foreach (var size in new[] { new Size(1440, 900), new Size(980, 680) })
        {
            var window = new MainWindow(captureEvidence: true);
            var root = (FrameworkElement)window.Content; window.Content = null;
            LabMotion.SetReduced(root, true);
            var host = new Border { Child = root, Width = size.Width, Height = size.Height };
            host.SetResourceReference(Border.BackgroundProperty, "Lab.bg");
            void Layout() { host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout(); }
            Services.CaptureEditorDocument? firstDocument = null;
            foreach (var expanded in new[] { false, true })
            {
                window.PrepareCaptureEvidence(source, Services.CaptureAnnotationKind.Text, expanded);
                var document = window.CaptureEvidenceDocument;
                firstDocument ??= document;
                Layout();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                window.ResizeCaptureEvidenceViewport(); Layout();
                Require(ReferenceEquals(firstDocument, window.CaptureEvidenceDocument) && document!.CanUndo, "Shell normal/expanded preserve one document and undo");
                var copy = (Button)window.FindName("CaptureCopyImageButton");
                Require(copy.IsEnabled && copy.ActualWidth > 0, "Unified shell output actions remain usable");
                var commands = (WrapPanel)window.FindName("CaptureOutputCommands");
                var toolbar = (Grid)window.FindName("CaptureWorkbenchToolbar");
                Require(Grid.GetRow(commands) == (toolbar.ActualWidth < (expanded ? 1250 : 1060) ? 1 : 0),
                    $"Toolbar responsive output row: width={toolbar.ActualWidth}, row={Grid.GetRow(commands)}");
                Require(((Button)window.FindName("CaptureMoreToolsButton")).Visibility == (expanded ? Visibility.Collapsed : Visibility.Visible),
                    "Expanded editor replaces More with inline tools");
                foreach (var name in new[] { "CaptureEllipseToolButton", "CaptureLineToolButton", "CaptureNumberToolButton",
                             "CaptureBlurToolButton", "CapturePixelateToolButton", "CaptureCropToolButton", "CaptureResizeToolButton" })
                    Require(((Button)window.FindName(name)).Visibility == (expanded ? Visibility.Visible : Visibility.Collapsed),
                        $"Expanded tool visibility: {name}");
                if (expanded && size.Width == 1440)
                    Require(((Border)window.FindName("CaptureEditorViewport")).ActualHeight > 350,
                        "Expanded viewport must use available space, not count blank page area as chrome");
                var bmp = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32); bmp.Render(host);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp));
                using var file = File.Create(Path.Combine(_smokeOutput!, $"unified-shell-{theme}-{(expanded ? "expanded" : "normal")}-{size.Width}.png")); png.Save(file);
            }
            window.DisposeCaptureEvidence(); window.Close();
        }
    }

    private async Task CaptureWindowEvidence(Window window, string theme, string name, Size size, string label)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        content.Resources = window.Resources;
        LabMotion.SetReduced(content, true);
        var host = new Border { Child = content, Width = size.Width, Height = size.Height };
        host.SetResourceReference(Border.BackgroundProperty, "Lab.shell");
        host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
        var button = Descendants(host).OfType<Button>().Single(b =>
            b.Content is string text && text == label || Descendants(b).OfType<TextBlock>().Any(t => t.Text == label));
        var expectedInk = theme == "Dark" ? "#FF082126" : "#FFFFFFFF";
        Require(button.Background.ToString() == (theme == "Dark" ? "#FF74D1E5" : "#FF337C8F"), name + " primary fill");
        Require(Descendants(button).OfType<TextBlock>().Any(t => t.Text == label && t.Foreground.ToString() == expectedInk),
            name + " primary text contrast");
        if (name == "capture-shortcuts")
            Require(Descendants(host).OfType<TextBlock>().Single(t => t.Text == "Personalizar atalhos").Foreground.ToString() ==
                (theme == "Dark" ? "#FFF3F3F3" : "#FF202024"), "Shortcuts heading must not inherit legacy ink");
        // Include the lower image options in the settings snapshot;
        // Save configurations remains in the fixed footer.
        if (name == "capture-rule")
        {
            Descendants(host).OfType<ScrollViewer>().First().ScrollToBottom();
            host.UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(_smokeOutput!, $"{name}-{theme}.png"));
        png.Save(file);
        window.Close();
    }
    private void CaptureInkPickerEvidence(string theme)
    {
        using var editor = new CaptureWorkbenchEditor();
        using var source = new System.Drawing.Bitmap(320, 180);
        editor.LoadImage(source);
        var document = editor.Document;
        var picker = CaptureInkPicker.CreateContent(editor);
        picker.VerticalAlignment = VerticalAlignment.Top;
        LabMotion.SetReduced(picker, true);
        var size = new Size(724, 340);
        var host = new Border { Child = picker, Padding = new Thickness(12), Width = size.Width, Height = size.Height,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        host.SetResourceReference(Border.BackgroundProperty, "Lab.canvas");
        void Layout() { host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout(); }
        Layout();
        var colors = Descendants(picker).OfType<Button>().Where(b => b.Tag is int).ToArray();
        Require(colors.Length == 30, "Ink picker has 30 individually accessible round colors");
        var selected = colors.Single(b => b.ToolTip?.ToString()?.StartsWith("Azul ·") == true);
        selected.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(editor.InkArgb == (int)selected.Tag && System.Windows.Automation.AutomationProperties.GetItemStatus(selected) == "Selecionada",
            "Click selects a color instead of cycling through a fixed sequence");
        var slider = Descendants(picker).OfType<Slider>().Single(); slider.Value = 7;
        Require(editor.InkThickness == 7, "Ink thickness slider configures the real editor");
        Require(Descendants(picker).OfType<StackPanel>().Single(p => p.Name == "InkRgbPanel").Visibility == Visibility.Collapsed,
            "RGB fields stay hidden until requested");
        Descendants(picker).OfType<Button>().Single(b => b.Name == "OpenInkRgb").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
        var hex = Descendants(picker).OfType<TextBox>().Single(b => b.Name == "InkHex");
        var apply = Descendants(picker).OfType<Button>().Single(b => b.Name == "ApplyInkHex");
        hex.Text = "#ZZZZZZ"; apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(Descendants(picker).OfType<TextBlock>().Any(t => t.Text.StartsWith("Use R") && t.Visibility == Visibility.Visible),
            "Invalid custom color reports an error without changing ink");
        hex.Text = "#E84E60"; apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
        Require(editor.InkArgb == System.Drawing.Color.FromArgb(232, 78, 96).ToArgb() &&
            ReferenceEquals(document, editor.Document) && document!.OperationCount == 0,
            "Custom color applies to future annotations without altering history");
        var red = Descendants(picker).OfType<TextBox>().Single(b => b.Name == "InkRed");
        var green = Descendants(picker).OfType<TextBox>().Single(b => b.Name == "InkGreen");
        var blue = Descendants(picker).OfType<TextBox>().Single(b => b.Name == "InkBlue");
        red.Text = "12"; green.Text = "34"; blue.Text = "56"; apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(editor.InkArgb == System.Drawing.Color.FromArgb(12, 34, 56).ToArgb(), "Typed RGB values apply correctly");
        red.Text = "300"; apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(editor.InkArgb == System.Drawing.Color.FromArgb(12, 34, 56).ToArgb(), "Invalid RGB preserves selected color");
        hex.Text = "#E84E60"; apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
        Require(picker.Background.ToString() == (theme == "Dark" ? "#FF242424" : "#FFFFFFFF"), "Ink picker background follows theme");
        foreach (var control in Descendants(picker).OfType<Control>().Where(c => c is Button or TextBox or Slider))
        {
            if (control is Button { Tag: int }) continue; // Offscreen palette colors deliberately scroll.
            var p = control.TranslatePoint(new Point(), host);
            Require(p.X >= 0 && p.Y >= 0 && p.X + control.ActualWidth <= size.Width && p.Y + control.ActualHeight <= size.Height,
                "Ink picker controls fit without clipping");
        }
        var image = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32); image.Render(host);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(Path.Combine(_smokeOutput!, $"capture-ink-picker-{theme}.png")); png.Save(file);
    }

    private async Task CaptureOverlayEvidence(string theme)
    {
        using var source = new System.Drawing.Bitmap(1000, 700);
        using (var g = System.Drawing.Graphics.FromImage(source))
        { g.Clear(System.Drawing.Color.White); g.FillRectangle(System.Drawing.Brushes.LightGray, 0, 0, 1000, 70); }
        var window = new RegionCaptureWindow(source, pilotVisuals: true);
        window.SetSelectionForEvidence(new Rect(120, 80, 640, 360));
        // The approved toolbar opens owned contextual windows. Let its deferred
        // placement create the owner HWND before detaching it for raster snapshots.
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var toolbar = window.ToolbarForEvidence;
        LabMotion.SetReduced(toolbar, true);
        foreach (var width in new[] { 920, 440, 260 })
        {
            var compact = width != 920;
            window.SetDensityForEvidence(compact);
            window.FitToolbarForEvidence(width - 24);
            toolbar.VerticalAlignment = VerticalAlignment.Top;
            var host = new Border { Child = toolbar, Padding = new Thickness(12), Width = width };
            host.Measure(new Size(width, double.PositiveInfinity));
            var height = (int)Math.Ceiling(host.DesiredSize.Height);
            var size = new Size(width, height); host.Height = height;
            host.SetResourceReference(Border.BackgroundProperty, "Lab.canvas");
            host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
            foreach (var button in Descendants(toolbar).OfType<System.Windows.Controls.Primitives.ButtonBase>().Where(b => b.IsVisible || b.Visibility == Visibility.Visible))
            {
                if (button.ActualWidth == 0) continue;
                var location = button.TranslatePoint(new Point(), host);
                Require(location.X >= 0 && location.X + button.ActualWidth <= width,
                    $"Floating bar button {System.Windows.Automation.AutomationProperties.GetName(button)} fits {width}: x={location.X}, width={button.ActualWidth}");
            }
            Require(toolbar.Background.ToString() == (theme == "Dark" ? "#FF181818" : "#FFFFFFFF"), "Floating bar follows Lab theme");
            foreach (var control in Descendants(toolbar).OfType<Control>().Where(c => c.IsVisible))
            {
                var point = control.TranslatePoint(new Point(), host);
                Require(point.Y >= 0 && point.Y + control.ActualHeight <= height + 1, "Grouped toolbar controls fit vertically");
            }
            var capture = Descendants(toolbar).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Capturar conforme configuração");
            Require(capture.Background.ToString() == (theme == "Dark" ? "#FF74D1E5" : "#FF337C8F"), "Floating bar primary color matches approved capture screen");
            Require(Descendants(capture).OfType<TextBlock>().Single(t => t.Text == "Capturar").Foreground.ToString() ==
                (theme == "Dark" ? "#FF082126" : "#FFFFFFFF"), "Floating capture label keeps on-accent contrast");
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(host);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(_smokeOutput!, $"capture-overlay-bar-{theme}-{(width == 260 ? "narrow" : compact ? "compact" : "normal")}.png")); png.Save(file);
            host.Child = null;
        }
        window.SetDensityForEvidence(false);
        // Interactions need a live presentation source; contextual controls now
        // belong to a separate window instead of a persistent toolbar property row.
        var interactionHost = new Window { Content = toolbar, Width = 920, SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false };
        interactionHost.Show();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        try
        {
            var caneta = Descendants(toolbar).OfType<System.Windows.Controls.Primitives.ToggleButton>()
                .Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Caneta");
            caneta.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Require(caneta.IsChecked == true, "Floating tool click changes the real overlay mode");
            window.AddForEvidence(new Services.CaptureAnnotation { Kind = Services.CaptureAnnotationKind.Rectangle,
                Start = new Point(195, 125), End = new Point(205, 135), FillArgb = System.Drawing.Color.Red.ToArgb(), OutlineArgb = null });
            using (var before = window.RenderForEvidence())
                Require(before.GetPixel(200, 130).R > 240 && before.GetPixel(200, 130).G < 10, "Overlay preview and export share raster composition");
            window.MoveForEvidence(new Vector(40, 20));
            using (var after = window.RenderForEvidence())
                Require(after.Width == 640 && after.Height == 360 && after.GetPixel(160, 110).G < 10 &&
                    after.GetPixel(200, 130).G > 240, "Moving the crop preserves drawings at their desktop positions");
            var privacyTool = Descendants(toolbar).OfType<System.Windows.Controls.Primitives.ToggleButton>()
                .Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Pixelizar");
            privacyTool.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            var intensity = Descendants(window.ContextForEvidence ?? throw new InvalidOperationException("Privacy tool must open its contextual panel"))
                .OfType<Slider>().Single(s => System.Windows.Automation.AutomationProperties.GetName(s) == "Intensidade");
            intensity.Value = 32;
            var mark = window.FinishDragForEvidence(new Point(10, 10), new Point(100, 80));
            Require(mark.Kind == Services.CaptureAnnotationKind.Pixelate && mark.PrivacyStrength == 32,
                "Contextual intensity is captured in the actual output command");
            }
        finally { interactionHost.Content = null; interactionHost.Close(); window.Close(); }
    }

    private void CaptureHelpEvidence(string theme)
    {
        var settings = new Models.CaptureSettings { RegionShortcut = "F10", ActiveMonitorShortcut = "F11" };
        var content = CaptureHelpContent.Create(settings);
        Require(content.Topics.Single(t => t.Id == "region").Shortcut == "F10", "Help uses actual configured shortcuts");
        Require(content.Topics.All(t => TryFindResource("Lab.Icon." + t.Icon) is Geometry), "Help icons resolve to local vectors");
        var window = new ScreenHelpWindow(content);
        var surface = window.HelpSurface; window.Content = null; LabMotion.SetReduced(surface, true);
        foreach (var width in new[] { 1040, 620 })
        {
            var size = new Size(width, 740); var host = new Border { Child = surface, Width = width, Height = size.Height };
            host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
            window.SearchForEvidence("regiao");
            Require(window.ResultCount > 0, "Help search ignores Portuguese accents");
            window.SearchForEvidence("zzzz-no-resource"); Require(window.ResultCount == 0, "Help shows empty search result");
            window.SearchForEvidence(""); window.OpenTopic("region");
            host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
            var close = Descendants(surface).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Fechar ajuda");
            Require(Descendants(close).OfType<LabIcon>().Single().ActualWidth >= 16, "Help close icon remains visible");
            Require(surface.Background.ToString() == (theme == "Dark" ? "#FF181818" : "#FFFFFFFF"), "Help follows current theme");
            foreach (var topic in content.Topics)
            { window.OpenTopic(topic.Id); host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout(); }
            window.OpenTopic("region"); host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
            var bitmap = new RenderTargetBitmap(width, 740, 96, 96, PixelFormats.Pbgra32); bitmap.Render(host);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(_smokeOutput!, $"capture-help-{theme}-{width}.png"))) png.Save(file);
            var highlight = Descendants(surface).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Mostrar na tela");
            if (width == 620) { highlight.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(window.RequestedTarget == "CaptureRegionModeButton", "Help returns a highlight request without executing capture"); }
            host.Child = null;
        }
        window.Close();
    }

    private void CaptureCustomEmojiEvidence(string theme)
    {
        var directory = Path.Combine(_smokeOutput!, "custom-emoji-probe-" + theme);
        Directory.CreateDirectory(directory);
        try
        {
            var catalog = new Services.CaptureStampCatalog(Path.Combine(directory, "collection"));
            var imagePath = Path.Combine(directory, "Selo do projeto.png");
            using (var sample = new System.Drawing.Bitmap(160, 100))
            {
                using var g = System.Drawing.Graphics.FromImage(sample); g.Clear(System.Drawing.Color.Transparent);
                g.FillEllipse(System.Drawing.Brushes.DarkCyan, 4, 4, 152, 92);
                using var font = new System.Drawing.Font("Segoe UI", 32, System.Drawing.FontStyle.Bold);
                g.DrawString("SD", font, System.Drawing.Brushes.White, 42, 22); sample.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
            }
            var value = catalog.Import(imagePath); string? chosen = null;
            var picker = CaptureEmojiPicker.CreateContent(v => chosen = v, () => { }, catalog);
            LabMotion.SetReduced(picker, true);
            var host = new Border { Child = picker, Width = 900, Height = 560 };
            host.SetResourceReference(Border.BackgroundProperty, "Lab.raised");
            var size = new Size(900, 560);
            void Layout() { host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout(); }
            Layout();
            var category = Descendants(picker).OfType<ComboBox>().Single();
            category.SelectedItem = category.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == Services.CaptureStampCatalog.CustomCategory); Layout();
            var item = Descendants(picker).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Selo do projeto");
            item.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(chosen == value, "Custom collection can select a local image stamp");
            var bitmap = new RenderTargetBitmap(900, 560, 96, 96, PixelFormats.Pbgra32); bitmap.Render(host);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(_smokeOutput!, $"capture-custom-emojis-{theme}.png"))) png.Save(file);
            var remove = Descendants(picker).OfType<Button>().Single(b => b.Content is string label && label == "Remover");
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout();
            Require(catalog.Items.Count == 0 && Descendants(picker).OfType<TextBlock>().Any(t => t.Text == "Nenhum emote encontrado."), "Custom removal updates collection and empty state");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private RadioButton FindPageTab(string content) => PageTabs.Children.OfType<RadioButton>()
        .Single(tab => string.Equals(tab.Content?.ToString(), content, StringComparison.Ordinal));
    private sealed class Example { public string Value { get; set; } = ""; }
    private sealed class RequiredRule : ValidationRule
    { public override ValidationResult Validate(object value, CultureInfo cultureInfo) => string.IsNullOrWhiteSpace(value?.ToString()) ? new ValidationResult(false, "Campo obrigatório.") : ValidationResult.ValidResult; }
}
