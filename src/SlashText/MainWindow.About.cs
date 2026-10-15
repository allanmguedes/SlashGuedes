using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using SlashText.Models;
using SlashText.Services;

namespace SlashText;

public partial class MainWindow
{
    private static string ProjectUrl => $"https://github.com/{UpdateService.ActiveRepository}";
    private Action<Uri>? _aboutEvidenceLinkLauncher;
    private bool _manualUpdateCheckInProgress;

    private void RefreshAboutPresentation()
    {
        AboutVersionText.Text = UiText.Format($"Versão {ProductVersion()}");
        AboutDistributionText.Text = AppPaths.IsPortable
            ? UiText.Get("Distribuição portátil. Leve o aplicativo e seus dados com você, sem criar uma conta.")
            : UiText.Get("Distribuição instalada. Ferramentas integradas ao seu dia a dia no Windows, sem criar uma conta.");
        AboutPilotNoteText.Text = AppPaths.IsCapturePilot
            ? UiText.Get("Neste piloto, a consulta de atualizações e a inicialização com o Windows ficam desativadas. Suas preferências são preservadas.")
            : UiText.Get("A verificação automática segue a preferência definida em Configurações.");
        CheckUpdatesButton.IsEnabled = !AppPaths.IsCapturePilot && !_manualUpdateCheckInProgress;
    }

    private void UpdateAboutLayout(double width)
    {
        if (AboutCardsGrid is null) return;
        var narrow = width < 1100;
        var cards = new[] { AboutWindowsCard, AboutPrivacyCard, AboutProjectCard };
        for (var index = 0; index < cards.Length; index++)
        {
            Grid.SetColumn(cards[index], narrow ? 0 : index);
            Grid.SetColumnSpan(cards[index], narrow ? 3 : 1);
            Grid.SetRow(cards[index], narrow ? index : 0);
        }
        AboutHeroHeading.FontSize = narrow ? 32 : 42;
        AboutHeroHeading.LineHeight = narrow ? 38 : 49;
        AboutLinksPanel.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Center;
    }

    private void OpenAboutLink(string address)
    {
        try
        {
            var uri = new Uri(address, UriKind.Absolute);
            if (uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com" ||
                !uri.AbsolutePath.StartsWith($"/{UpdateService.ActiveRepository}/", StringComparison.OrdinalIgnoreCase) &&
                !uri.AbsolutePath.Equals($"/{UpdateService.ActiveRepository}", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(UiText.Get("Destino do projeto inválido."));
            if (_aboutEvidenceLinkLauncher is not null) _aboutEvidenceLinkLauncher(uri);
            else Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            AboutLinkStatusText.Visibility = Visibility.Collapsed;
        }
        catch (Exception)
        {
            AboutLinkStatusText.Text = UiText.Get("Não foi possível abrir o navegador. Tente novamente ou acesse github.com/") + UpdateService.ActiveRepository + ".";
            AboutLinkStatusText.Visibility = Visibility.Visible;
        }
    }

    private void AboutCurrentReleaseNotes_OnClick(object sender, RoutedEventArgs e) => OpenAboutLink($"{ProjectUrl}/releases/tag/v{ProductVersion()}");
    private void AboutReportIssue_OnClick(object sender, RoutedEventArgs e) => OpenAboutLink($"{ProjectUrl}/issues/new/choose");
    private void AboutLicense_OnClick(object sender, RoutedEventArgs e) => OpenAboutLink($"{ProjectUrl}/blob/main/LICENSE");

    private async Task<UpdateCheckResult> CheckUpdatesAndRefreshAsync()
    {
        LastUpdateResultText.Text = SettingsLastUpdateResultText.Text = UiText.Get("Consultando o GitHub…");
        var result = await _updateService.CheckAsync(force: true);
        UpdateUpdateDisplay(await _updateService.LoadStateAsync());
        return result;
    }

    internal MainWindow(UpdateService updateService) : this(captureEvidence: true, updateService: updateService) { }

    internal void ConfigureAboutEvidence(double width, Action<Uri> launcher)
    {
        _aboutEvidenceLinkLauncher = launcher;
        ShowView(AboutView, AboutTabButton);
        UpdateAboutLayout(width);
    }

    internal Task<UpdateCheckResult> CheckAboutUpdatesEvidenceAsync() => CheckUpdatesAndRefreshAsync();
}
