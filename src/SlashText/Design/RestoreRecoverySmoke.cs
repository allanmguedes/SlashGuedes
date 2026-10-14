using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
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

// App-owned, inert fixtures only. This helper never creates MainWindow or starts hooks/devices.
internal static class RestoreRecoverySmoke
{
    internal static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output); Initialize(output); Directory.CreateDirectory(AppPaths.BackupsDirectory);
        var checks = new List<string>();
        foreach (var language in new[] { "pt-BR", "en", "es" })
        foreach (var theme in new[] { "Light", "Dark" })
        foreach (var blocked in new[] { false, true })
        {
            UiText.Apply(language); ThemeService.Apply(theme);
            var dialog = RestoreRecoveryDialog.Create(blocked);
            dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = -10000; dialog.Top = -10000;
            Exception? failure = null; var handled = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) =>
            {
                if (!dialog.IsLoaded) return; timer.Stop(); handled = true;
                try
                {
                    dialog.UpdateLayout();
                    Require(dialog.DefaultButton.IsDefault, "safe keyboard default");
                    Require(AutomationProperties.GetName(dialog.DefaultButton) == UiText.Get(blocked ? "Fechar SlashDesk" : "Entendi"), "localized accessible button");
                    Require(dialog.DefaultButton.IsKeyboardFocused, "initial focus");
                    var surface = dialog.Surface;
                    Require(surface.ActualWidth > 0 && surface.ActualHeight > 0 && surface.ActualHeight < 850, "dialog fits and wraps");
                    var image = new RenderTargetBitmap((int)Math.Ceiling(dialog.ActualWidth), (int)Math.Ceiling(dialog.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    image.Render(dialog); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                    using (var file = File.Create(Path.Combine(output, $"{language}-{theme}-{blocked}.png"))) encoder.Save(file);
                    dialog.DefaultButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception error) { failure = error; dialog.Close(); }
            };
            timer.Start(); dialog.ShowDialog(); timer.Stop();
            if (failure is not null) throw failure;
            Require(handled && dialog.Choice == (blocked ? MessageBoxResult.Cancel : MessageBoxResult.OK), "dismissal never authorizes a restore");
            checks.Add($"{language}/{theme}/{blocked}: layout, keyboard default/focus, accessible names, dismissal: PASS");
        }
        await Task.CompletedTask;
        File.WriteAllLines(Path.Combine(output, "result.txt"), checks);
    }

    private static void Initialize(string root) => AppPaths.Initialize(new AppDataEnvironment(
        DistributionMode.Portable, Path.Combine(root, "fixture"), Path.Combine(root, "unused"), true));

    internal static void DemoWorker(string root)
    {
        if (!AppDataEnvironment.Detect().IsCapturePilot || !Path.GetFileName(root).StartsWith("restore-demo-", StringComparison.Ordinal) ||
            File.ReadAllText(Path.Combine(root, "demo-owner.txt")) != "SlashDesk #118 fictitious fixture") throw new InvalidOperationException("Invalid demonstration fixture.");
        Initialize(root);
        var service = new BackupService();
        service.RestoreCheckpoint = (phase, _) => { if (phase == "before-commit") Environment.Exit(73); };
        service.RestoreSnapshot(Path.Combine(root, "incoming.zip"));
        Environment.Exit(74);
    }

    internal static async Task DemoAsync(string mode, string output, bool automated = false)
    {
        if (!AppDataEnvironment.Detect().IsCapturePilot || mode is not ("recover" or "blocked")) throw new InvalidOperationException("This demonstration requires an isolated pilot.");
        var root = Path.Combine(Path.GetFullPath(output), "restore-demo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "demo-owner.txt"), "SlashDesk #118 fictitious fixture"); Initialize(root);
        var original = new Snippet { Id = Guid.Parse("7e9c064c-915f-4644-bafb-cad8d30489e0"), Name = "Fictício anterior", Trigger = "/antes", Content = "DADOS ANTERIORES FICTÍCIOS" };
        var repository = new SnippetMarkdownRepository();
        await repository.SaveAsync([original]);
        await new JsonFileStore<AppSettings>(AppPaths.SettingsFile).SaveAsync(new() { Language = "pt-BR" });
        var before = Hash(AppPaths.SnippetsFile); var archive = new BackupService().CreateManualSnapshot();
        original.Trigger = "/novo"; original.Content = "DADOS NOVOS FICTÍCIOS"; await repository.SaveAsync([original]);
        var incoming = new BackupService().CreateManualSnapshot(); File.Copy(incoming, Path.Combine(root, "incoming.zip"));
        new BackupService().RestoreSnapshot(archive);
        using var worker = new Process { StartInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true } };
        worker.StartInfo.ArgumentList.Add("--restore-recovery-demo-worker"); worker.StartInfo.ArgumentList.Add(root);
        Require(worker.Start(), "demo worker starts");
        try { await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        finally { if (!worker.HasExited) worker.Kill(); }
        Require(worker.ExitCode == 73, "demo interrupted only its owned fixture subprocess");
        FileStream? held = null;
        try
        {
            if (mode == "blocked")
            {
                var transaction = Directory.GetDirectories(AppPaths.BackupsDirectory, ".restore-transaction-*").Single();
                held = new FileStream(Path.Combine(transaction, "originals", "snippets.md"), FileMode.Open, FileAccess.Read, FileShare.None);
            }
            var blocked = false;
            try { AppPaths.EnsureDataLayout(); }
            catch (RestoreRecoveryRequiredException) { blocked = true; }
            Require(blocked == (mode == "blocked"), "demo exercises actual recovery/protection decision");
            ThemeService.Apply("System"); var dialog = RestoreRecoveryDialog.Create(blocked);
            if (dialog.Surface.Child is StackPanel panel)
            {
                var label = new TextBlock { Text = "TESTE COM DADOS FICTÍCIOS", FontSize = 11, Margin = new Thickness(0, 0, 0, 14) };
                label.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted"); panel.Children.Insert(0, label);
            }
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            if (automated)
            {
                dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = -10000; dialog.Top = -10000;
                timer.Tick += (_, _) =>
                {
                    if (!dialog.IsLoaded) return;
                    timer.Stop(); dialog.DefaultButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                timer.Start();
            }
            dialog.ShowDialog(); timer.Stop();
            if (automated) Require(dialog.Choice == (blocked ? MessageBoxResult.Cancel : MessageBoxResult.OK), "demo safe default");
            if (dialog.Choice == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo(AppPaths.BackupsDirectory) { UseShellExecute = true });
        }
        finally { held?.Dispose(); }
        // Leave only coherent fictitious data, even after demonstrating a pending-recovery warning.
        RestoreRecovery.RecoverPending(AppPaths.DataDirectory);
        Require(Hash(AppPaths.SnippetsFile) == before, "demo recovered original content exactly");
        File.WriteAllText(Path.Combine(output, $"resultado-{mode}.txt"), "PASS: only fictitious data; previous snippet bytes restored; recovery message/protection shown.");
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Require(bool value, string label) { if (!value) throw new InvalidOperationException("Restore recovery smoke: " + label); }
}
