using System.Windows;
using SlashText.Models;
using SlashText.Services;

namespace SlashText.Views;

public partial class CaptureShortcutDialog : Window
{
    public string MonitorShortcut => MonitorBox.Text.Trim();
    public string RegionShortcut => RegionBox.Text.Trim();
    public string WindowShortcut => WindowBox.Text.Trim();
    public string ScrollingShortcut => ScrollingBox.Text.Trim();

    public CaptureShortcutDialog(CaptureSettings settings)
    {
        InitializeComponent();
        MonitorBox.Text = settings.ActiveMonitorShortcut;
        RegionBox.Text = settings.RegionShortcut;
        WindowBox.Text = settings.WindowShortcut;
        ScrollingBox.Text = settings.ScrollingShortcut;
        SourceInitialized += (_, _) => ThemeService.ApplyToWindow(this);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                e.Handled = true;
                DialogResult = false;
            }
        };
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        var shortcuts = new[] { MonitorShortcut, RegionShortcut, WindowShortcut, ScrollingShortcut };
        if (shortcuts.Any(item => !GlobalCaptureShortcutService.IsValid(item)))
        {
            AuxiliaryWindowTheme.ShowMessage(this, UiText.Get("Use uma tecla, roda ou botão do mouse válido. A roda exige um modificador."),
                UiText.Get("Atalhos de captura"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (shortcuts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != shortcuts.Length)
        {
            AuxiliaryWindowTheme.ShowMessage(this, UiText.Get("Cada ação precisa ter um atalho diferente."), UiText.Get("Atalhos de captura"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
