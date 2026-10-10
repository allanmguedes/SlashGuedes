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
                        "A galeria encontrou um erro e precisa ser fechada.\n\n" + args.Exception,
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

        DataMigrationResult migration;
        try
        {
            migration = AppPaths.EnsureDataLayout();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                "Não foi possível preparar os dados do SlashDesk. A origem anterior " +
                "foi preservada e nenhum dado foi ativado parcialmente.\n\n" + exception.Message,
                "SlashDesk",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown(3);
            return;
        }

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

        // Mantém o identificador legado para impedir que SlashText e SlashDesk
        // monitorem o teclado ao mesmo tempo durante uma atualização.
        _singleInstance = new Mutex(true, "SlashText.SingleInstance", out var created);
        _ownsSingleInstance = created;
        if (!created)
        {
            MessageBox.Show(
                "O SlashDesk já está em execução na bandeja do Windows.",
                "SlashDesk",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

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

    private static bool EnsurePortableLocationIsWritable(AppDataEnvironment environment)
    {
        if (environment.TryProbePortableWrite(out var error))
        {
            return true;
        }

        var choice = MessageBox.Show(
            "A versão portátil precisa estar em uma pasta gravável para preservar " +
            "SlashDeskData. Deseja escolher outra pasta?\n\n" + error,
            "Pasta portátil sem permissão",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes)
        {
            return false;
        }

        using var picker = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Escolha uma pasta gravável para o SlashDesk portátil",
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
                "A pasta escolhida já contém SlashDesk.exe. Escolha uma pasta vazia " +
                "para evitar substituir outra instalação.",
                "SlashDesk",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        try
        {
            var current = Environment.ProcessPath
                ?? throw new InvalidOperationException("Não foi possível localizar SlashDesk.exe.");
            File.Copy(current, destination, overwrite: false);
            Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true });
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                "Não foi possível preparar a nova pasta. Nenhum dado foi movido.\n\n" +
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
