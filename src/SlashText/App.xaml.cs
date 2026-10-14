using System.Threading;
using System.Diagnostics;
using System.IO;
using System.Windows;
using SlashText.Services;
using SlashText.Views;

namespace SlashText;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private bool _ownsSingleInstance;
    private bool _helperMode;

    protected override void OnStartup(StartupEventArgs e)
    {
        UiText.Apply(UiText.DefaultLanguage);
        if (e.Args.Length == 2 && e.Args[0] == "--usage-write-ui-smoke")
        {
            _helperMode = true; ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var output = Path.GetFullPath(e.Args[1]);
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await Design.UsageWriteFailureSmoke.RunAsync(output); Shutdown(0); }
                catch (Exception error) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString()); Shutdown(1); }
            }));
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--legacy-backups-smoke")
        {
            _helperMode = true; ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var output = Path.GetFullPath(e.Args[1]);
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await Design.LegacyBackupSmoke.RunAsync(output); Shutdown(0); }
                catch (Exception error) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); Shutdown(1); }
            }));
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--protected-storage-smoke")
        {
            _helperMode = true; ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var output = Path.GetFullPath(e.Args[1]);
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await Design.ProtectedStorageSmoke.RunAsync(output); Shutdown(0); }
                catch (Exception error) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); Shutdown(1); }
            }));
            return;
        }
        if (e.Args.Length == 3 && e.Args[0] == "--backup-compatibility-smoke")
        {
            _helperMode = true; ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var output = Path.GetFullPath(e.Args[1]); var fixtures = Path.GetFullPath(e.Args[2]);
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await Design.BackupCompatibilitySmoke.RunAsync(output, fixtures); Shutdown(0); }
                catch (Exception error) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); Shutdown(1); }
            }));
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == "--restore-recovery-smoke")
        {
            _helperMode = true; ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var output = Path.GetFullPath(e.Args.Length > 1 ? e.Args[1] : "restore-recovery-evidence");
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await Design.RestoreRecoverySmoke.RunAsync(output); Shutdown(0); }
                catch (Exception error) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); Shutdown(1); }
            }));
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--restore-recovery-demo-worker")
        {
            _helperMode = true; Design.RestoreRecoverySmoke.DemoWorker(Path.GetFullPath(e.Args[1])); Shutdown(1); return;
        }
        if (e.Args.Length == 3 && e.Args[0] is "--restore-recovery-demo" or "--restore-recovery-demo-smoke")
        {
            _helperMode = true; ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await Design.RestoreRecoverySmoke.DemoAsync(e.Args[1], Path.GetFullPath(e.Args[2]), e.Args[0] == "--restore-recovery-demo-smoke"); Shutdown(0); }
                catch (Exception error)
                {
                    var output = Path.GetFullPath(e.Args[2]); Directory.CreateDirectory(output);
                    File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
                    if (e.Args[0] == "--restore-recovery-demo") MessageBox.Show(error.GetType().Name, "SlashDesk — teste fictício #118");
                    Shutdown(1);
                }
            }));
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == "--recording-targets-smoke")
        {
            _helperMode = true; ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var output = Path.GetFullPath(e.Args.Length > 1 ? e.Args[1] : "recording-targets-evidence");
            base.OnStartup(e);
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await Design.RecordingTargetSmoke.RunAsync(output); Shutdown(0); }
                catch (Exception exception) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), exception.ToString()); Shutdown(1); }
            }));
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == "--retention-smoke")
        {
            _helperMode = true; ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var output = Path.GetFullPath(e.Args.Length > 1 ? e.Args[1] : "retention-evidence");
            base.OnStartup(e);
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await Design.RetentionSmoke.RunAsync(output); Shutdown(0); }
                catch (Exception exception) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), exception.ToString()); Shutdown(1); }
            }));
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == "--languages-smoke")
        {
            _helperMode = true; ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var output = Path.GetFullPath(e.Args.Length > 1 ? e.Args[1] : "languages-evidence");
            base.OnStartup(e);
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await Design.LanguagesSmoke.RunAsync(output); Shutdown(0); }
                catch (Exception exception) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), exception.ToString()); Shutdown(1); }
            }));
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == "--auxiliary-smoke")
        {
            _helperMode = true; ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var output = Path.GetFullPath(e.Args.Length > 1 ? e.Args[1] : "auxiliary-evidence");
            base.OnStartup(e);
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await Design.AuxiliarySmoke.RunAsync(output); Shutdown(0); }
                catch (Exception exception) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), exception.ToString()); Shutdown(1); }
            }));
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == "--auxiliary-gallery")
        {
            _helperMode = true; ShutdownMode = ShutdownMode.OnMainWindowClose;
            base.OnStartup(e);
            AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable, Path.Combine(Path.GetTempPath(), "SlashDesk-auxiliary-preview"), Path.Combine(Path.GetTempPath(), "SlashDesk-unused"), isCapturePilot: true));
            ThemeService.Apply("System");
            MainWindow = new Design.AuxiliaryReviewWindow(); MainWindow.Show(); return;
        }
        if (e.Args.Length > 0 && e.Args[0] == "--about-smoke")
        {
            _helperMode = true; ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var output = Path.GetFullPath(e.Args.Length > 1 ? e.Args[1] : "about-evidence");
            base.OnStartup(e);
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await Design.AboutSmoke.RunAsync(output); Shutdown(0); }
                catch (Exception exception) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), exception.ToString()); Shutdown(1); }
            }));
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == "--settings-smoke")
        {
            _helperMode = true; ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var output = Path.GetFullPath(e.Args.Length > 1 ? e.Args[1] : "settings-evidence");
            base.OnStartup(e);
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await Design.SettingsSmoke.RunAsync(output); Shutdown(0); }
                catch (Exception exception) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), exception.ToString()); Shutdown(1); }
            }));
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == "--statistics-smoke")
        {
            _helperMode = true; ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var output = Path.GetFullPath(e.Args.Length > 1 ? e.Args[1] : "statistics-evidence");
            base.OnStartup(e);
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await Design.StatisticsSmoke.RunAsync(output); Shutdown(0); }
                catch (Exception exception) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), exception.ToString()); Shutdown(1); }
            }));
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == "--quick-accent-smoke")
        {
            _helperMode = true; ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var output = Path.GetFullPath(e.Args.Length > 1 ? e.Args[1] : "quick-accent-evidence");
            base.OnStartup(e);
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await Design.QuickAccentSmoke.RunAsync(output); Shutdown(0); }
                catch (Exception exception) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), exception.ToString()); Shutdown(1); }
            }));
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == "--recording-audio-smoke")
        {
            _helperMode = true; ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var output = Path.GetFullPath(e.Args.Length > 1 ? e.Args[1] : "recording-audio-evidence");
            base.OnStartup(e);
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await Design.RecordingAudioSmoke.RunAsync(output, e.Args.Contains("--native")); Shutdown(0); }
                catch (Exception exception) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), exception.ToString()); Shutdown(1); }
            }));
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == "--capture-ocr-smoke")
        {
            _helperMode = true; ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var output = Path.GetFullPath(e.Args.Length > 1 ? e.Args[1] : "ocr-evidence");
            var bestModels = e.Args.Length > 2 ? e.Args[2] : null;
            base.OnStartup(e);
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await Design.CaptureOcrSmoke.RunAsync(output, bestModels); Shutdown(0); }
                catch (Exception exception) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), exception.ToString()); Shutdown(1); }
            }));
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == "--capture-ocr-worker")
        {
            _helperMode = true;
            Shutdown(CaptureOcrService.RunWorker(e.Args));
            return;
        }
        if (e.Args.Contains("--shortcuts-workspace-smoke", StringComparer.OrdinalIgnoreCase))
        {
            _helperMode = true; ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var index = Array.FindIndex(e.Args, arg => arg.Equals("--shortcuts-workspace-smoke", StringComparison.OrdinalIgnoreCase));
            var output = Path.GetFullPath(index + 1 < e.Args.Length ? e.Args[index + 1] : "shortcuts-evidence");
            base.OnStartup(e);
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await Design.ShortcutsWorkspaceSmoke.RunAsync(output); Shutdown(0); }
                catch (Exception exception) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), exception.ToString()); Shutdown(1); }
            }));
            return;
        }
        // Developer-only entry point: return before updater, AppPaths, mutex and hooks.
        if (e.Args.Contains("--design-gallery", StringComparer.OrdinalIgnoreCase) ||
            e.Args.Contains("--design-gallery-smoke", StringComparer.OrdinalIgnoreCase))
        {
            _helperMode = true;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            var smokeIndex = Array.FindIndex(e.Args, arg => arg.Equals("--design-gallery-smoke", StringComparison.OrdinalIgnoreCase));
            string? output = smokeIndex >= 0
                ? (smokeIndex + 1 < e.Args.Length ? Path.GetFullPath(e.Args[smokeIndex + 1]) : Path.Combine(AppContext.BaseDirectory, "gallery-evidence"))
                : null;
            if (output is null)
            {
                DispatcherUnhandledException += (_, args) =>
                {
                    MessageBox.Show(
                        UiText.Get("A galeria encontrou um erro e precisa ser fechada.\n\n") + args.Exception,
                        "Galeria de desenvolvimento",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    args.Handled = true;
                    Shutdown(1);
                };
            }
            try
            {
                var gallery = new DesignGalleryWindow(output);
                MainWindow = gallery;
                gallery.Show();
                base.OnStartup(e);
            }
            catch (Exception exception)
            {
                if (output is not null) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), exception.ToString()); }
                else MessageBox.Show(exception.ToString(), "Galeria de desenvolvimento");
                Shutdown(1);
            }
            return;
        }

        if (e.Args.Contains("--capture-toolbar-preview", StringComparer.OrdinalIgnoreCase))
        {
            _helperMode = true;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            var preview = new CaptureToolbarPreviewWindow();
            MainWindow = preview;
            preview.Show();
            base.OnStartup(e);
            return;
        }

        var dataEnvironment = AppDataEnvironment.Detect();
        if (e.Args.Length == 2 && e.Args[0] == "--protected-storage-case" &&
            e.Args[1] is "history" or "settings" or "blocked" && AppPaths.IsProtectedStoragePilot)
            dataEnvironment = new AppDataEnvironment(DistributionMode.Portable,
                Path.Combine(dataEnvironment.ExecutableDirectory, "TESTES", e.Args[1]), dataEnvironment.LocalAppDataDirectory, true);
        if (!dataEnvironment.IsCapturePilot && PortableUpdateService.TryRunHelper(e.Args, out var helperExitCode))
        {
            _helperMode = true;
            Shutdown(helperExitCode);
            return;
        }
        try
        {
            if (!dataEnvironment.IsCapturePilot) PortableUpdateService.ConfirmAndScheduleCleanup(e.Args);
        }
        catch
        {
            // Sem confirmação, o auxiliar restaura o executável anterior.
            _helperMode = true;
            Shutdown(13);
            return;
        }

        AppPaths.Initialize(dataEnvironment);
        if (!EnsurePortableLocationIsWritable(dataEnvironment))
        {
            Shutdown(2);
            return;
        }

        UiText.LoadPreference(AppPaths.SettingsFile);
        // A pilot-only startup probe never starts hooks and stays in its isolated data root.
        if (!(dataEnvironment.IsCapturePilot && e.Args.Contains("--portable-smoke", StringComparer.OrdinalIgnoreCase)))
        {
            // Mantém o identificador legado para impedir que SlashText e SlashDesk
            // monitorem o teclado ao mesmo tempo durante uma atualização.
            _singleInstance = new Mutex(true, "SlashText.SingleInstance", out var created);
            _ownsSingleInstance = created;
            if (!created)
            {
                MessageBox.Show(
                    UiText.Get("O SlashDesk já está em execução na bandeja do Windows."),
                    "SlashDesk",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }
        }

        DataMigrationResult migration;
        try
        {
            migration = AppPaths.EnsureDataLayout();
        }
        catch (RestoreRecoveryRequiredException)
        {
            ShowRestoreRecoveryNotice(blocked: true);
            Shutdown(4);
            return;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                UiText.Get("Não foi possível preparar os dados do SlashDesk. A origem anterior ") +
                UiText.Get("foi preservada e nenhum dado foi ativado parcialmente.\n\n") + exception.Message,
                "SlashDesk",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown(3);
            return;
        }

        if (AppPaths.LastRestoreRecovery.RecoveredCount > 0 && !e.Args.Contains("--portable-smoke", StringComparer.OrdinalIgnoreCase))
            ShowRestoreRecoveryNotice(blocked: false);

        AppDiagnosticLog.Initialize();
        DispatcherUnhandledException += (_, args) =>
            AppDiagnosticLog.WriteException("exception.wpf-dispatcher", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                AppDiagnosticLog.WriteException("exception.app-domain", exception);
            }
            else
            {
                AppDiagnosticLog.Write(
                    "exception.app-domain",
                    ("exceptionType", args.ExceptionObject?.GetType().FullName ?? "unknown"));
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppDiagnosticLog.WriteException("exception.unobserved-task", args.Exception);
            args.SetObserved();
        };

        if (e.Args.Contains("--portable-smoke", StringComparer.OrdinalIgnoreCase))
        {
            AppDiagnosticLog.Write(
                "application.portable-smoke",
                ("is64BitProcess", Environment.Is64BitProcess),
                ("distributionMode", AppPaths.Mode.ToString()),
                ("dataDirectory", AppPaths.DataDirectory),
                ("migrated", migration.Migrated));
            Shutdown(0);
            return;
        }

        _ = Task.Run(() =>
        {
            var removedSessions = TemporarySession.CleanupOrphans();
            AppDiagnosticLog.Write("retention.cleanup", ("removedSessions", removedSessions));
        });

        AppDiagnosticLog.Write(
            "storage.selected",
            ("distributionMode", AppPaths.Mode.ToString()),
            ("dataDirectory", AppPaths.DataDirectory),
            ("migrationSource", migration.SourceDirectory),
            ("migrated", migration.Migrated),
            ("competingSourcePreserved", migration.CompetingSourcePreserved),
            ("migrationWarnings", migration.Warnings.Count));
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase))
        {
            window.Hide();
        }

        base.OnStartup(e);
    }

    private static void ShowRestoreRecoveryNotice(bool blocked)
    {
        UiText.LoadPreference(AppPaths.SettingsFile);
        ThemeService.Apply("System");
        var dialog = RestoreRecoveryDialog.Create(blocked);
        dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
        try
        {
            if (dialog.Choice == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo(AppPaths.BackupsDirectory) { UseShellExecute = true });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            // Folder-launch failure must not prevent protected shutdown.
        }
    }

    private static bool EnsurePortableLocationIsWritable(AppDataEnvironment environment)
    {
        if (environment.TryProbePortableWrite(out var error))
        {
            return true;
        }

        var choice = MessageBox.Show(
            UiText.Get("A versão portátil precisa estar em uma pasta gravável para preservar ") +
            "SlashDeskData. Deseja escolher outra pasta?\n\n" + error,
            UiText.Get("Pasta portátil sem permissão"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes)
        {
            return false;
        }

        using var picker = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = UiText.Get("Escolha uma pasta gravável para o SlashDesk portátil"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (picker.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return false;
        }

        var destination = Path.Combine(picker.SelectedPath, "SlashDesk.exe");
        if (File.Exists(destination))
        {
            MessageBox.Show(
                UiText.Get("A pasta escolhida já contém SlashDesk.exe. Escolha uma pasta vazia ") +
                UiText.Get("para evitar substituir outra instalação."),
                "SlashDesk",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        try
        {
            var current = Environment.ProcessPath
                ?? throw new InvalidOperationException(UiText.Get("Não foi possível localizar SlashDesk.exe."));
            File.Copy(current, destination, overwrite: false);
            Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true });
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                UiText.Get("Não foi possível preparar a nova pasta. Nenhum dado foi movido.\n\n") +
                exception.Message,
                "SlashDesk",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_helperMode)
        {
            AppDiagnosticLog.Write("application.exit", ("exitCode", e.ApplicationExitCode));
        }
        if (_ownsSingleInstance) _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
