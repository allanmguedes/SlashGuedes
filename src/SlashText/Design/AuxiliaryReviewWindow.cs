using System.Windows;
using System.Windows.Controls;
using SlashText.Models;
using SlashText.Services;
using SlashText.Views;

namespace SlashText.Design;

/// <summary>Explicit review entrypoint. Only window decisions are exercised; no updater or recorder is started.</summary>
internal sealed class AuxiliaryReviewWindow : Window
{
    private readonly QuickAccentWindow _accent = new();
    private readonly SuggestionWindow _suggestions = new();
    internal AuxiliaryReviewWindow()
    {
        Title = "SlashDesk · Prévia das janelas · #69"; Width = 760; Height = 640;
        MinWidth = 600; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new TextBlock { Text = "Conferir interfaces auxiliares", FontSize = 26, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Prévia com dados de exemplo. Usa as janelas do app, sem instalar atualizações, iniciar gravações ou modificar atalhos. Para testar os fluxos reais, abra o piloto normalmente.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 20) });
        var themes = new WrapPanel();
        foreach (var (label, value) in new[] { ("Claro", "Light"), ("Preto", "Dark"), ("Seguir o Windows", "System") })
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 16) };
            button.Click += (_, _) => ThemeService.Apply(value); themes.Children.Add(button);
        }
        panel.Children.Add(themes);
        Add("Preencher campos", () => new VariableInputWindow(AuxiliaryFixtures.Fields).ShowDialog());
        Add("Editar nome", () => PromptDialog.Show(this, "Editar nome", "Nome do grupo", "Exemplo de revisão"));
        Add("Boas-vindas", () => new OnboardingWindow { Owner = this }.ShowDialog());
        Add("Atualização disponível · exemplo", () => new UpdateAvailableWindow(AuxiliaryFixtures.Update) { Owner = this }.ShowDialog());
        Add("Progresso e cancelamento · exemplo", () =>
        {
            var progress = new UpdateProgressWindow { Owner = this };
            progress.Report(new UpdateProgress("Prévia: recebendo o pacote e verificando sua integridade antes da instalação.", 45, 100));
            using var registration = progress.Cancellation.Token.Register(() => progress.Dispatcher.BeginInvoke(new Action(progress.AllowClose)));
            progress.ShowDialog();
        });
        Add("Prévia de GIF · exemplo", () => { using var gif = AuxiliaryFixtures.Gif(); new GifPreviewWindow(gif) { Owner = this }.ShowDialog(); });
        Add("Confirmação e cancelamento", () => AuxiliaryWindowTheme.ShowMessage(this, "Esta é uma prévia da confirmação. Nenhum arquivo será excluído.", "Revisar confirmação", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning));
        Add("Mostrar barras de acentos e sugestões", () =>
        {
            if (_accent.IsVisible) { _accent.Hide(); _suggestions.Hide(); return; }
            _accent.UpdateChoices("áàâãä", 2, "BottomCenter", true);
            _suggestions.UpdateSuggestions(AuxiliaryFixtures.Snippets, PointToScreen(new Point(30, 100)), 0);
        });
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AuxiliaryWindowTheme.Apply(this);
        Closed += (_, _) => { _accent.Close(); _suggestions.Close(); };
        void Add(string text, Action show)
        {
            var button = new Button { Content = text, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
            button.Click += (_, _) => show(); panel.Children.Add(button);
        }
    }
}

internal static class AuxiliaryFixtures
{
    internal static IReadOnlyList<TemplateField> Fields => Enumerable.Range(1, 12).Select(i => new TemplateField("campo_" + i, i == 1 ? "Exemplo" : "")).ToArray();
    internal static IReadOnlyList<Snippet> Snippets => Enumerable.Range(1, 9).Select(i => new Snippet { Trigger = "/atalho_de_exemplo_" + i, Name = "Texto de exemplo para revisar a lista " + i }).ToArray();
    internal static UpdateCheckResult Update => new(UpdateCheckStatus.UpdateAvailable, true, "3.2.0", "3.3.0 · exemplo", "Prévia", null,
        "DADOS DE EXEMPLO PARA REVISÃO VISUAL\n\nEsta janela não inicia a atualização nesta prévia.\n\n• Novas telas de Atalhos e Captura\n• Acento Rápido e Configurações\n• Interfaces auxiliares com o tema do app", 125_000_000, null);
    internal static GifRecordingResult Gif()
    {
        var frames = new List<System.Drawing.Bitmap>();
        foreach (var color in new[] { System.Drawing.Color.FromArgb(8, 126, 139), System.Drawing.Color.FromArgb(19, 93, 109) })
        {
            var frame = new System.Drawing.Bitmap(480, 260);
            using var graphics = System.Drawing.Graphics.FromImage(frame); graphics.Clear(color);
            using var font = new System.Drawing.Font("Segoe UI", 20);
            graphics.DrawString("SlashDesk · GIF de exemplo", font, System.Drawing.Brushes.White, 45, 100); frames.Add(frame);
        }
        return new GifRecordingResult(frames, 2, new System.Drawing.Rectangle(0, 0, 480, 260));
    }
}
