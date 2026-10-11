using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SlashText.Models;
using SlashText.Services;
using SlashText.Views;

namespace SlashText.Design;

internal static class LanguagesSmoke
{
    internal static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable, Path.Combine(output, "fixture"), Path.Combine(output, "unused-installed"), isCapturePilot: true));
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var checks = new List<string>();
        var regional = CultureInfo.CurrentCulture;
        var keys = UiText.AllCatalogs["pt-BR"].Keys.Order().ToArray();
        foreach (var language in new[] { "pt-BR", "en", "es" })
        {
            var catalog = UiText.AllCatalogs[language];
            Require(keys.SequenceEqual(catalog.Keys.Order()), "Catalog keys match: " + language);
            foreach (var key in keys)
            {
                Require(!string.IsNullOrWhiteSpace(catalog[key]), "Nonempty translation " + key);
                var original = UiText.AllCatalogs["pt-BR"][key];
                Require(Placeholders(original).SequenceEqual(Placeholders(catalog[key])), "Interpolation/variables unchanged " + key);
                if (Regex.IsMatch(original, @"(?<!\{)\{\d"))
                {
                    var parsed = System.Text.CompositeFormat.Parse(catalog[key]);
                    Require(parsed.MinimumArgumentCount == System.Text.CompositeFormat.Parse(original).MinimumArgumentCount, "Valid composite format " + key);
                }
            }
        }
        checks.Add($"{keys.Length} keys: three complete embedded catalogs; interpolation, format specifiers and template variables preserved: PASS");
        await File.WriteAllTextAsync(AppPaths.SettingsFile, "{\"theme\":\"Dark\",\"quickAccentInputDelayMs\":777}");
        UiText.LoadPreference(AppPaths.SettingsFile);
        var legacy = await new JsonFileStore<AppSettings>(AppPaths.SettingsFile).LoadAsync();
        Require(UiText.Language == "pt-BR" && legacy.Language == "pt-BR" && legacy.Theme == "Dark" && legacy.QuickAccentInputDelayMs == 777, "Legacy preference compatibility");
        await File.WriteAllTextAsync(AppPaths.SettingsFile, "{\"language\":\"unsupported\"}");
        UiText.LoadPreference(AppPaths.SettingsFile); Require(UiText.Language == "pt-BR", "Unsupported language fallback");
        await File.WriteAllTextAsync(AppPaths.SettingsFile, "{"); UiText.LoadPreference(AppPaths.SettingsFile); Require(UiText.Language == "pt-BR", "Corrupt preference fallback without writes");
        Require(await File.ReadAllTextAsync(AppPaths.SettingsFile) == "{", "Bootstrap does not mutate corrupt settings");
        var content = "Atalhos /dados {{nome}} {{data:+7d}} Categoria Código — á ñ 😀";
        foreach (var language in new[] { "pt-BR", "en", "es" })
        {
            UiText.Apply(language);
            Require(CultureInfo.CurrentCulture == regional, "Windows regional culture untouched");
            Require(UiText.Get("unknown interface key") == "unknown interface key", "Missing key fallback");
            var value = UiText.Format($"{content} inserido");
            Require(value.StartsWith(content, StringComparison.Ordinal), "Interpolation leaves user text intact");
            Require(ShortcutCategories.Resolve("Código").Name == "Código" && ShortcutCategories.Resolve("Outros").Name == "Outros", "Stable stored category names");
            Require(RecordingPresetCatalog.NormalizeMp4Quality("Máxima") == "Muito alta" && RecordingPresetCatalog.NormalizeMp4Quality("Alta") == "Alta", "Encoder values and legacy aliases stable");
            Require(RecordingPresetCatalog.Mp4Quality.Select(p => p.Value).SequenceEqual(new[] { "Baixa", "Média", "Alta", "Muito alta" }), "Stored recording preset codes stable");
            Require(RecordingPresetCatalog.Mp4Quality[2].Name == UiText.Get("Alta"), "Cached preset labels follow active interface language");
            Require(UiText.UpdateStatus("SlashDesk 3.3.0 está disponível.") == string.Format(regional, UiText.Get("SlashDesk {0} está disponível."), "3.3.0"), "Stored update status localized without changing version");
            Require(UiText.UpdateStatus(content) == content, "Unknown update content untouched");
            Require(CaptureEmojiPicker.Search("", "Todos").Count == NotoEmojiCatalog.Items.Count, "Stable emoji all filter");
            Require(CaptureEmojiPicker.Search("corazon rojo").Any(i => i.Value == "❤️") && CaptureEmojiPicker.Search("red heart").Any(i => i.Value == "❤️"), "Offline multilingual emoji search");
            var personal = new NotoEmojiItem("custom:fixture", "Coração", "fixture.png");
            Require(NotoEmojiCatalog.DisplayName(personal) == "Coração", "Personal image names are never translated");
            foreach (var theme in new[] { "Light", "Dark", "System" })
            foreach (var size in new[] { new Size(1440, 900), new Size(980, 680) })
            {
                ThemeService.Apply(theme);
                var window = new MainWindow(captureEvidence: true);
                var root = (FrameworkElement)window.Content; window.Content = null; LabMotion.SetReduced(root, true);
                var host = new Border { Child = root, Width = size.Width, Height = size.Height };
                host.SetResourceReference(Border.BackgroundProperty, "Lab.bg");
                foreach (var view in new[] { "Shortcuts", "Capture", "Accent", "Statistics", "Settings", "About" })
                {
                    window.ConfigureLanguageEvidence(view, size.Width);
                    if (view == "About") Require(((TextBlock)window.FindName("AboutHeroHeading")).Text == UiText.Get("Ferramentas pequenas.\nPossibilidades maiores."), "About multiline headline translated");
                    host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
                    await Dispatcher.Yield(DispatcherPriority.Loaded);
                    host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
                    if (theme != "System") Save(host, Path.Combine(output, $"{language}-{theme}-{size.Width}-{view}.png"), size, 1);
                }
                window.ConfigureLanguageEvidence("Settings", size.Width);
                host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
                var selector = (ComboBox)window.FindName("LanguageBox");
                Require(selector.Items.Cast<ComboBoxItem>().Select(i => (string)i.Tag).SequenceEqual(new[] { "pt-BR", "en", "es" }), "Selector uses stable language codes");
                Require(selector.ActualWidth > 200 && selector.ActualHeight > 20, "Language selector is reachable");
                Require(((TextBlock)window.FindName("LanguageRestartText")).Text == UiText.Get("O idioma escolhido será aplicado ao reabrir o SlashDesk."), "Restart instruction in active language");
                var restart = (TextBlock)window.FindName("LanguageRestartText");
                var chosen = language == "en" ? "es" : "en";
                selector.SelectedItem = selector.Items.Cast<ComboBoxItem>().Single(item => item.Tag as string == chosen);
                Require(await window.SaveLanguagePreferenceAsync(chosen), "Different language saved");
                Require(restart.Text == UiText.Get("Idioma salvo. Reabra o SlashDesk para aplicar.") &&
                    ReferenceEquals(restart.Foreground, Application.Current.FindResource("Lab.error")) && restart.FontWeight == FontWeights.SemiBold,
                    "Pending restart highlighted in themed red");
                Require(restart.Opacity == 1 && !restart.HasAnimatedProperties && UiText.Language == language,
                    "Reduced motion keeps restart notice static and current interface unchanged");
                if (theme != "System") Save(host, Path.Combine(output, $"{language}-{theme}-{size.Width}-RestartNotice.png"), size, 1);
                if (language == "pt-BR" && theme == "Light" && size.Width == 1440)
                {
                    LabMotion.SetReduced(root, false);
                    Require(await window.SaveLanguagePreferenceAsync(chosen), "Repeated selection replaces notification");
                    Require(restart.HasAnimatedProperties == LabMotion.Allowed(restart), "Pulse respects Windows motion setting");
                    if (LabMotion.Allowed(restart))
                    {
                        await Task.Delay(350);
                        Require(restart.Opacity >= .55 && restart.Opacity < .95, "Restart pulse stays readable");
                        await Task.Delay(3650);
                        Require(restart.Opacity == 1 && ReferenceEquals(restart.Foreground, Application.Current.FindResource("Lab.error")), "Finite pulse ends at full opacity and retains themed red");
                    }
                    LabMotion.SetReduced(root, true);
                }
                Require(await window.SaveLanguagePreferenceAsync(language), "Revert to active language saved");
                selector.SelectedItem = selector.Items.Cast<ComboBoxItem>().Single(item => item.Tag as string == language);
                Require(restart.Opacity == 1 && !restart.HasAnimatedProperties && ReferenceEquals(restart.Foreground, Application.Current.FindResource("Lab.muted")),
                    "Reverting to active language clears restart emphasis");
                foreach (var scale in new[] { 1.25, 1.5, 2 })
                    if (theme == "Light" && size.Width == 980) Save(host, Path.Combine(output, $"{language}-{scale}-Settings.png"), size, scale);
                using (var tray = window.CreateTrayMenu())
                {
                    var captures = tray.Items.OfType<System.Windows.Forms.ToolStripMenuItem>().Single(i => i.Name == "Captures");
                    Require(captures.Text == UiText.Get("Capturas") && captures.Tag as string == "Camera", "Tray label/icons use stable group identity");
                    foreach (var group in tray.Items.OfType<System.Windows.Forms.ToolStripMenuItem>()) Require(group.Tag is string, "Tray group icon resolves");
                }
                var before = UiText.Language;
                Require(await window.SaveLanguagePreferenceAsync(language), "Preference save succeeds");
                var persisted = await new JsonFileStore<AppSettings>(AppPaths.SettingsFile).LoadAsync();
                Require(persisted.Language == language && UiText.Language == before, "Saving does not partially switch running UI");
                UiText.LoadPreference(AppPaths.SettingsFile); Require(UiText.Language == language, "Startup reads persisted language");
                var dialog = new SettingsDialogWindow(UiText.Get("Este atalho será removido da sua lista."), UiText.Get("Excluir atalho?"), MessageBoxButton.YesNo);
                Require((string)dialog.Actions.First().Content == UiText.Get("Cancelar") && (string)dialog.Actions.Last().Content == UiText.Get("Continuar"), "Safe localized dialog actions and defaults");
                dialog.Close(); host.Child = null; window.Close();
            }
            foreach (var theme in new[] { "Light", "Dark" })
            {
                Require(UiText.Language == language, "Auxiliary language remains active: " + UiText.Language + " expected " + language);
                ThemeService.Apply(theme);
                var fields = new VariableInputWindow([new TemplateField("campo_original", content)]);
                Require(Elements<TextBox>(fields).Single().Text == content, "Variable default text unchanged");
                var update = new UpdateAvailableWindow(AuxiliaryFixtures.Update);
                Require(Elements<TextBlock>(update).First().Text == string.Format(regional, UiText.Get("SlashDesk {0} está disponível"), AuxiliaryFixtures.Update.LatestVersion), "Update heading translated: " + Elements<TextBlock>(update).First().Text);
                Require(Elements<TextBox>(update).Single().Text == AuxiliaryFixtures.Update.Notes, "Release notes are external content");
                foreach (var (name, window) in new (string, Window)[]
                {
                    ("Fields", fields), ("Update", update), ("Onboarding", new OnboardingWindow()),
                    ("Progress", new UpdateProgressWindow()), ("CaptureSettings", new CaptureRuleDialog(new CaptureSettings())),
                    ("Prompt", new PromptDialog(UiText.Get("Nome do grupo"), UiText.Get("Nome"), content))
                })
                {
                    var root = (FrameworkElement)window.Content; window.Content = null;
                    var size = new Size(double.IsNaN(window.Width) ? 650 : window.Width, double.IsNaN(window.Height) ? 550 : window.Height);
                    var host = new Border { Child = root, Width = size.Width, Height = size.Height };
                    host.SetResourceReference(Border.BackgroundProperty, "Lab.bg");
                    LabMotion.SetReduced(root, true); host.Measure(size); host.Arrange(new Rect(size)); host.UpdateLayout();
                    Save(host, Path.Combine(output, $"{language}-{theme}-Aux-{name}.png"), size, 1);
                    host.Child = null; window.Close();
                }
            }
            var preferences = await File.ReadAllBytesAsync(AppPaths.SettingsFile);
            var failureWindow = new MainWindow(captureEvidence: true);
            failureWindow.ConfigureLanguageEvidence("Settings", 980); failureWindow.Show();
            var dismiss = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            dismiss.Tick += (_, _) =>
            {
                foreach (var dialog in Application.Current.Windows.OfType<SettingsDialogWindow>().ToArray()) dialog.Close();
            };
            try
            {
                File.Delete(AppPaths.SettingsFile); Directory.CreateDirectory(AppPaths.SettingsFile);
                dismiss.Start();
                Require(!await failureWindow.SaveLanguagePreferenceAsync(language == "en" ? "es" : "en"), "Preference failure reported");
                Require(((ComboBoxItem)((ComboBox)failureWindow.FindName("LanguageBox")).SelectedItem).Tag as string == language, "Failed preference rolls selector back");
                Require(((Grid)failureWindow.FindName("SettingsCardsGrid")).IsEnabled && UiText.Language == language, "Failure releases gate and leaves active language intact");
            }
            finally
            {
                dismiss.Stop(); Directory.Delete(AppPaths.SettingsFile); await File.WriteAllBytesAsync(AppPaths.SettingsFile, preferences); failureWindow.Close();
            }
            checks.Add(language + ": six real screens, three themes, 1440/980 layout, 100/125/150/200% renders; save/reopen and failure rollback; regional formats, user content, categories, encoder, multilingual emoji filter, tray, safe dialog and six auxiliary renders: PASS");
        }
        UiText.Apply("pt-BR");
        await File.WriteAllLinesAsync(Path.Combine(output, "result.txt"), checks.Append("Synthetic isolated fixtures; no hooks, registry, network update or physical DPI/display tests. Native OS controls and release notes keep their own language."));
    }
    private static IEnumerable<string> Placeholders(string value) => Regex.Matches(value, @"\{\{[^{}]*\}\}|(?<!\{)\{[^{}]+\}(?!\})").Select(m => m.Value).Order();
    private static IEnumerable<T> Elements<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var element in Elements<T>(child)) yield return element;
    }
    private static void Require(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); }
    private static void Save(FrameworkElement element, string path, Size size, double scale)
    {
        var image = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        image.Render(element); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
