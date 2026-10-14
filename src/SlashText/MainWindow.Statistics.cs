using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SlashText.Design;
using SlashText.Models;
using SlashText.Services;
using SlashText.Views;

namespace SlashText;

public partial class MainWindow
{
    private bool _statisticsNarrow;
    private ScreenHelpHighlighter? _statisticsHelpHighlight;

    private void RefreshStatistics()
    {
        var records = _usageService.Records;
        var available = _usageService.IsAvailable;
        long total = 0, characters = 0;
        try
        {
            // Keep the 3.2.0 formulas, including records belonging to deleted shortcuts.
            total = records.Sum(item => item.Count);
            characters = records.Sum(item => item.CharactersSaved);
        }
        catch (OverflowException)
        {
            // Invalid totals in an edited file must not interrupt the other modules.
            available = false;
        }
        SetStatisticsValue(TotalExpansionsText, UiText.Get("Expansões de texto"), available ? total.ToString("N0") : "—");
        SetStatisticsValue(CharactersSavedText, UiText.Get("Caracteres poupados"), available ? characters.ToString("N0") : "—");
        SetStatisticsValue(UsedSnippetsText, UiText.Get("Atalhos com uso registrado"), available ? records.Count(item => item.Count > 0).ToString("N0") : "—", metric: false);
        SetStatisticsValue(TimeSavedText, UiText.Get("Tempo economizado estimado"), available ? $"{Math.Ceiling(characters / 200d):N0} min" : "—");
        SetStatisticsValue(AverageCharactersText, UiText.Get("Média de caracteres por expansão"), available ? total == 0 ? "0" : $"{characters / (double)total:N0}" : "—", metric: false);
        SetStatisticsValue(QuickAccentTotalText, UiText.Get("Acentos inseridos"), available ? _usageService.QuickAccent.Count.ToString("N0") : "—");
        var pending = available && _usageService.HasPendingChanges;
        StatisticsErrorPanel.Visibility = available && !pending ? Visibility.Collapsed : Visibility.Visible;
        if (pending) StatisticsErrorDescription.Text = UiText.Get("As contagens pendentes estão nesta sessão. Tentaremos salvá-las no próximo uso de atalho ou acento. Se o app fechar antes disso, elas poderão ser perdidas.");
        else StatisticsErrorDescription.SetResourceReference(TextBlock.TextProperty, "Ui.0129D6D2BC41D700");
        StatisticsStatusText.Text = UiText.Get(!available ? "Contagens de atalhos e acentos indisponíveis."
            : pending ? "Contagens desta sessão ainda não salvas." : "Contagens locais, atualizadas com o uso.");
        StatisticsStatusText.SetResourceReference(TextBlock.ForegroundProperty, available && !pending ? "Lab.muted" : "Lab.error");

        var captures = _captureService.History;
        SetStatisticsValue(CaptureTotalText, UiText.Get("Capturas no histórico local"), captures.Count.ToString("N0"));
        SetCaptureStatistic(CaptureRegionTotalText, StatisticsRegionBar, UiText.Get("Região"), captures.Count(item => string.Equals(item.Type, "regiao", StringComparison.OrdinalIgnoreCase)), captures.Count);
        SetCaptureStatistic(CaptureMonitorTotalText, StatisticsMonitorBar, "Monitor", captures.Count(item => string.Equals(item.Type, "monitor", StringComparison.OrdinalIgnoreCase)), captures.Count);
        SetCaptureStatistic(CaptureWindowTotalText, StatisticsWindowBar, UiText.Get("Janela"), captures.Count(item => string.Equals(item.Type, "janela", StringComparison.OrdinalIgnoreCase)), captures.Count);
        SetCaptureStatistic(CaptureGifTotalText, StatisticsGifBar, "GIF", captures.Count(item => string.Equals(item.MediaKind, "gif", StringComparison.OrdinalIgnoreCase)), captures.Count);
        SetCaptureStatistic(CaptureMp4TotalText, StatisticsMp4Bar, "MP4", captures.Count(item => string.Equals(item.MediaKind, "video", StringComparison.OrdinalIgnoreCase)), captures.Count);
        StatisticsCaptureHintText.Text = captures.Count == 0 ? UiText.Get("Nenhuma captura no histórico local.") : UiText.Get("Base: capturas que permanecem no histórico local.");

        StatisticsRankingPanel.Children.Clear();
        var ranking = _snippets.Select(item => new { Snippet = item, Usage = _usageService.For(item.Id) })
            .Where(item => item.Usage?.Count > 0).OrderByDescending(item => item.Usage!.Count).Take(8).ToList();
        if (!available || ranking.Count == 0)
            StatisticsRankingPanel.Children.Add(StatisticsEmpty(available ? UiText.Get("Use um atalho para iniciar o ranking.") : UiText.Get("Ranking indisponível: não foi possível ler as contagens.")));
        else
            for (var index = 0; index < ranking.Count; index++)
                StatisticsRankingPanel.Children.Add(CreateStatisticsRankRow(index + 1, ranking[index].Snippet,
                    ranking[index].Usage!.Count, ranking[0].Usage!.Count));

        var accents = _usageService.QuickAccent.Characters.Where(item => item.Value > 0)
            .OrderByDescending(item => item.Value).ThenBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase).Take(8).ToList();
        QuickAccentCharactersPanel.Children.Clear();
        QuickAccentFavoriteText.Text = available && accents.Count > 0 ? accents[0].Key : "—";
        QuickAccentFavoriteText.ToolTip = QuickAccentFavoriteText.Text;
        AutomationProperties.SetName(QuickAccentFavoriteText, UiText.Get("Acento favorito: ") + QuickAccentFavoriteText.Text);
        QuickAccentFavoriteCountText.Text = available && accents.Count > 0
            ? $"{accents[0].Value:N0} {(accents[0].Value == 1 ? UiText.Get("inserção") : UiText.Get("inserções"))}" : "";
        if (!available || accents.Count == 0)
            QuickAccentCharactersPanel.Children.Add(StatisticsEmpty(available ? UiText.Get("Os caracteres usados aparecerão aqui.") : UiText.Get("Contagens de acentos indisponíveis.")));
        else
            foreach (var item in accents.Skip(1))
            {
                var text = new TextBlock { Text = $"{item.Key}  {item.Value:N0}×", TextWrapping = TextWrapping.Wrap };
                text.SetResourceReference(FrameworkElement.StyleProperty, "Lab.Text");
                text.SetResourceReference(TextBlock.ForegroundProperty, "Lab.accent-text");
                var chip = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 7, 7), Child = text, MaxWidth = 220, ToolTip = text.Text };
                chip.SetResourceReference(Border.BackgroundProperty, "Lab.tint");
                QuickAccentCharactersPanel.Children.Add(chip);
            }
        RefreshMostUsed();
    }

    private async Task<bool> TryUpdateAuxiliaryUsageAsync(Func<Task> record)
    {
        try
        {
            await record();
            if (_servicesDisposed || _restoreRecoveryBlocked) return false;
            _quickAccentService.SetUsage(_usageService.QuickAccentCharacterCounts());
            if (QuickAccentView.Visibility == Visibility.Visible) UpdateQuickAccentPreviewSelection();
            RefreshStatistics();
            return _usageService.IsAvailable && !_usageService.HasPendingChanges;
        }
        catch (Exception error)
        {
            // Local auxiliary boundary only: do not turn a successful insertion
            // into a failure or change global Dispatcher exception handling.
            AppDiagnosticLog.WriteException("usage.update.failed", error);
            return false;
        }
    }

    private async Task CompleteSuccessfulExpansionAsync(Snippet snippet, int inserted)
    {
        SafeDiagnosticLog.Write("expansion.completed", new Dictionary<string, object?> { ["insertedCharacterCount"] = inserted });
        var saved = await TryUpdateAuxiliaryUsageAsync(() => _usageService.RecordAsync(snippet, inserted));
        if (_servicesDisposed || _restoreRecoveryBlocked) return;
        StatusText.Text = UiText.Format($"{snippet.Trigger} inserido") + AuxiliaryUsageStatusSuffix(saved);
    }

    private string AuxiliaryUsageStatusSuffix(bool saved) => saved ? "" : " · " + UiText.Get(_usageService.HasPendingChanges
        ? "Estatísticas ainda não salvas." : "Contagens de atalhos e acentos indisponíveis.");

    private void SetStatisticsValue(TextBlock text, string label, string value, bool metric = true)
    {
        text.Text = value;
        if (metric) text.FontSize = value.Length > 14 ? 22 : _statisticsNarrow ? 30 : 36;
        text.ToolTip = $"{label}: {value}";
        AutomationProperties.SetName(text, $"{label}: {value}");
    }

    private void SetCaptureStatistic(TextBlock text, ProgressBar bar, string label, int count, int total)
    {
        SetStatisticsValue(text, label, count.ToString("N0"), metric: false);
        bar.Value = total == 0 ? 0 : count * 100d / total;
        AutomationProperties.SetName(bar, UiText.Format($"{label}: {count:N0} de {total:N0} capturas no histórico"));
    }

    private TextBlock StatisticsEmpty(string text)
    {
        var block = new TextBlock { Text = text, Margin = new Thickness(0, 20, 0, 12) };
        block.SetResourceReference(FrameworkElement.StyleProperty, "Lab.Metadata");
        return block;
    }

    private Grid CreateStatisticsRankRow(int position, Snippet snippet, long count, long maximum)
    {
        var row = new Grid { Margin = new Thickness(0, 16, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var number = new TextBlock { Text = position.ToString("00"), VerticalAlignment = VerticalAlignment.Center };
        number.SetResourceReference(FrameworkElement.StyleProperty, "Lab.Metadata");
        row.Children.Add(number);
        var body = new StackPanel(); Grid.SetColumn(body, 1); row.Children.Add(body);
        var heading = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        heading.ColumnDefinitions.Add(new ColumnDefinition());
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var trigger = new TextBlock { Text = snippet.Trigger, TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = $"{snippet.Trigger} · {snippet.Name}", Margin = new Thickness(0, 0, 12, 0) };
        trigger.SetResourceReference(FrameworkElement.StyleProperty, "Lab.Text");
        trigger.SetResourceReference(TextBlock.FontFamilyProperty, "Lab.MonoFont");
        trigger.TextWrapping = TextWrapping.NoWrap;
        heading.Children.Add(trigger);
        var value = new TextBlock { FontWeight = FontWeights.SemiBold };
        value.SetResourceReference(FrameworkElement.StyleProperty, "Lab.Text");
        SetStatisticsValue(value, UiText.Format($"Usos de {snippet.Trigger}"), count.ToString("N0"), metric: false);
        Grid.SetColumn(value, 1); heading.Children.Add(value); body.Children.Add(heading);
        var bar = new ProgressBar { Value = Math.Clamp(count * 100d / maximum, 0, 100) };
        bar.SetResourceReference(FrameworkElement.StyleProperty, "Lab.Stats.Bar");
        AutomationProperties.SetName(bar, UiText.Format($"{snippet.Trigger}: {count:N0} expansões; posição {position}"));
        body.Children.Add(bar);
        return row;
    }

    private void UpdateStatisticsLayout(double width)
    {
        if (StatisticsMetricsGrid is null) return;
        _statisticsNarrow = width < 1100;
        StatisticsMetricsGrid.Columns = _statisticsNarrow ? 2 : 4;
        StatisticsDetailsGrid.Columns = _statisticsNarrow ? 1 : 3;
        StatisticsMainGrid.ColumnDefinitions[1].Width = new GridLength(_statisticsNarrow ? 0 : 18);
        StatisticsMainGrid.ColumnDefinitions[2].Width = _statisticsNarrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(StatisticsCaptureCard, _statisticsNarrow ? 0 : 2);
        Grid.SetRow(StatisticsCaptureCard, _statisticsNarrow ? 1 : 0);
        StatisticsCaptureCard.Margin = _statisticsNarrow ? new Thickness(0, 18, 0, 0) : new Thickness(0);
        foreach (var text in new[] { TotalExpansionsText, CharactersSavedText, QuickAccentTotalText, CaptureTotalText, TimeSavedText })
            text.FontSize = text.Text.Length > 14 ? 22 : _statisticsNarrow ? 30 : 36;
    }

    private void OpenStatisticsHelp_OnClick(object sender, RoutedEventArgs e)
    {
        _statisticsHelpHighlight?.Remove();
        var guide = new ScreenHelpWindow(StatisticsHelpContent.Create()) { Owner = this };
        LabMotion.SetReduced(guide, LabMotion.GetReduced(this)); guide.EnableBackdrop(); ShowCaptureDialog(guide);
        if (guide.RequestedTarget is not { } name || FindName(name) is not FrameworkElement target) return;
        target.BringIntoView();
        Dispatcher.BeginInvoke(new Action(() =>
        { if (target.IsVisible) _statisticsHelpHighlight = ScreenHelpHighlighter.Show(target); }), DispatcherPriority.Loaded);
    }

    internal async Task ReloadStatisticsForEvidenceAsync(double width)
    {
        await _usageService.LoadAsync(); await _captureService.LoadAsync();
        RefreshStatistics(); ShowView(StatisticsView, StatisticsTabButton); UpdateStatisticsLayout(width);
    }
    internal void SetStatisticsSnippetsForEvidence(IEnumerable<Snippet> snippets)
    { _snippets.Clear(); foreach (var snippet in snippets) _snippets.Add(snippet); }
    internal async Task RecordStatisticsForEvidenceAsync(Snippet snippet)
    {
        await _usageService.RecordAsync(snippet, 65); await _usageService.RecordQuickAccentAsync('é'); RefreshStatistics();
    }
}
