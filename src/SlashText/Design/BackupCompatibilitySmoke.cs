using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SlashText.Models;
using SlashText.Services;
using SlashText.Views;

namespace SlashText.Design;

// Metadata fixtures only: no MainWindow, input hooks, clipboard or capture devices.
internal static class BackupCompatibilitySmoke
{
    internal static async Task RunAsync(string output, string fixtures)
    {
        Directory.CreateDirectory(output);
        AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable, Path.Combine(output, "fixture"), Path.Combine(output, "unused"), true));
        AppPaths.EnsureDataLayout();
        await new SnippetMarkdownRepository().SaveAsync([new Snippet { Name = "Atual 117", Trigger = "/atual117", Content = "ATUAL FICTÍCIO" }]);
        var original = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(AppPaths.SnippetsFile)));
        var checks = new List<string>();
        foreach (var language in new[] { "pt-BR", "en", "es" })
        foreach (var theme in new[] { "Light", "Dark" })
        foreach (var fixture in new[] { "117-atalhos-incompativeis.zip", "117-preferencias-incompativeis.zip", "117-historico-incompativel.zip" })
        {
            UiText.Apply(language); ThemeService.Apply(theme); string message;
            try { new BackupService().RestoreSnapshot(Path.Combine(fixtures, fixture)); throw new InvalidOperationException("Invalid fixture was applied."); }
            catch (InvalidDataException error) { message = error.Message; }
            Require(original == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(AppPaths.SnippetsFile))), "active fixture bytes preserved");
            var dialog = new SettingsDialogWindow(message, UiText.Get("Não foi possível restaurar"), MessageBoxButton.OK, MessageBoxImage.Warning);
            dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = -10000; dialog.Top = -10000;
            Exception? failure = null; var handled = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) =>
            {
                if (!dialog.IsLoaded) return; timer.Stop(); handled = true;
                try
                {
                    dialog.UpdateLayout();
                    Require(dialog.DefaultButton.IsDefault && dialog.DefaultButton.IsKeyboardFocused, "safe initial keyboard focus");
                    Require(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(dialog.DefaultButton)), "accessible action");
                    Require(dialog.ActualHeight < 850 && dialog.ActualWidth > 0, "wrapped visible content");
                    var image = new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth), (int)Math.Ceiling(dialog.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    image.Render(dialog); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                    using (var file = File.Create(Path.Combine(output, $"{language}-{theme}-{fixture}.png"))) encoder.Save(file);
                    dialog.DefaultButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception error) { failure = error; dialog.Close(); }
            };
            timer.Start(); dialog.ShowDialog(); timer.Stop();
            if (failure is not null) throw failure;
            Require(handled && dialog.Choice == MessageBoxResult.OK, "dismissal only acknowledges refusal");
            checks.Add($"{language}/{theme}/{fixture}: engine refusal, bytes, keyboard, accessibility, layout: PASS");
        }
        new BackupService().RestoreSnapshot(Path.Combine(fixtures, "117-backup-valido.zip"));
        Require((await new SnippetMarkdownRepository().LoadAsync()).Single().Trigger == "/restaurado117", "valid snippet reload");
        Require((await new JsonFileStore<AppSettings>(AppPaths.SettingsFile).LoadAsync()).Theme == "Dark", "valid settings reload");
        var usage = new UsageService(); await usage.LoadAsync(); Require(usage.IsAvailable && usage.Records.Single().Count == 4, "valid usage reload");
        Require((await new CaptureHistoryStore(AppPaths.CaptureHistoryFile).LoadAsync()).Single().RequiresFileApproval, "local history approval retained");
        checks.Add("valid fixture: four actual readers reload; no history reference opened: PASS");
        File.WriteAllLines(Path.Combine(output, "result.txt"), checks);
    }

    private static void Require(bool value, string label) { if (!value) throw new InvalidOperationException("Backup compatibility UI: " + label); }
}
