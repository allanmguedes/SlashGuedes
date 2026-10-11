using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SlashText.Design;
using SlashText.Services;
using DrawingBitmap = System.Drawing.Bitmap;

namespace SlashText.Views;

/// <summary>One result experience shared by selection and the existing image editor.</summary>
public sealed class CaptureOcrWindow : Window
{
    private readonly DrawingBitmap _snapshot;
    private readonly CaptureOcrService _service;
    private readonly Action<bool>? _busyChanged;
    private readonly CaptureOcrPulse _pulse = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _text = new() { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Button _copyAll;
    private readonly Button _copySelected;
    private readonly Button _retry;
    private readonly Button _close;
    private CancellationTokenSource? _cancellation;
    private bool _running, _closed;
    private readonly TaskCompletionSource _firstRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool CopiedText { get; private set; }
    internal string ResultText => _text.Text;
    internal bool IsReading => _running;
    internal Task FirstRead => _firstRead.Task;
    internal bool CopyAllEnabled => _copyAll.IsEnabled;
    internal void SelectTextForEvidence(int start, int length) => _text.Select(start, length);
    internal bool CopySelectionForEvidence() => Copy(_text.SelectedText);
    internal bool CopyAllForEvidence() => Copy(_text.Text);

    public CaptureOcrWindow(DrawingBitmap snapshot, Action<bool>? busyChanged = null, CaptureOcrService? service = null)
    {
        _snapshot = new DrawingBitmap(snapshot); _busyChanged = busyChanged; _service = service ?? new();
        Title = UiText.Get("Extrair texto"); Width = 820; Height = 700; MinWidth = 520; MinHeight = 440;
        MaxWidth = Math.Max(520, SystemParameters.WorkArea.Width - 40); MaxHeight = Math.Max(440, SystemParameters.WorkArea.Height - 40);
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        SetResourceReference(ForegroundProperty, "Lab.text");
        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(150) });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new DockPanel(); _close = ActionButton(UiText.Get("Fechar"), Close); DockPanel.SetDock(_close, Dock.Right); header.Children.Add(_close);
        header.Children.Add(new TextBlock { Text = UiText.Get("Extrair texto"), FontSize = 23, FontWeight = FontWeights.SemiBold }); root.Children.Add(header);
        var statusPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 12) };
        var language = _service.Options.Languages switch { "por" => UiText.Get("Português"), "eng" => UiText.Get("Inglês"), _ => UiText.Get("Português + inglês") };
        var subtitle = new TextBlock { Text = UiText.Format($"{language} · {_service.Options.Model} · processamento local"), FontSize = 12 };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted"); statusPanel.Children.Add(subtitle);
        _status.Margin = new Thickness(0, 8, 0, 0); AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        statusPanel.Children.Add(_status); Grid.SetRow(statusPanel, 1); root.Children.Add(statusPanel);
        var previewImage = new Image { Source = CaptureBitmapSource.Create(_snapshot), Stretch = Stretch.Uniform };
        var preview = new Grid { ClipToBounds = true }; preview.Children.Add(previewImage);
        // Fit the tint to the image, including its letterboxing, without changing layout or pixels.
        var previewLayer = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        previewLayer.Children.Add(_pulse); preview.Children.Add(previewLayer);
        preview.SizeChanged += (_, _) => { var scale = Math.Min(preview.ActualWidth / _snapshot.Width, preview.ActualHeight / _snapshot.Height);
            previewLayer.Width = _snapshot.Width * scale; previewLayer.Height = _snapshot.Height * scale; };
        var previewBorder = new Border { Child = preview, Padding = new Thickness(8), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 12) };
        previewBorder.SetResourceReference(Border.BackgroundProperty, "Lab.canvas"); Grid.SetRow(previewBorder, 2); root.Children.Add(previewBorder);
        _text.SetResourceReference(StyleProperty, "Lab.Field"); _text.VerticalContentAlignment = VerticalAlignment.Top; _text.Padding = new Thickness(12);
        AutomationProperties.SetName(_text, UiText.Get("Texto reconhecido, editável")); Grid.SetRow(_text, 3); root.Children.Add(_text);
        var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        _retry = ActionButton(UiText.Get("Tentar novamente"), () => _ = ReadAsync());
        _copySelected = ActionButton(UiText.Get("Copiar seleção"), () => Copy(_text.SelectedText));
        _copyAll = ActionButton(UiText.Get("Copiar tudo"), () => Copy(_text.Text));
        actions.Children.Add(_retry); actions.Children.Add(_copySelected); actions.Children.Add(_copyAll);
        Grid.SetRow(actions, 4); root.Children.Add(actions);
        var surface = new Border { Child = root, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        surface.SetResourceReference(Border.BackgroundProperty, "Lab.panel"); surface.SetResourceReference(Border.BorderBrushProperty, "Lab.line-strong"); Content = surface;
        _text.TextChanged += (_, _) => UpdateActions(); _text.SelectionChanged += (_, _) => UpdateActions();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Loaded += async (_, _) => { ModalBackdrop.Attach(this, surface, Close); await ReadAsync(); };
        Closed += (_, _) => { _closed = true; _cancellation?.Cancel(); _pulse.Stop(); _busyChanged?.Invoke(false);
            if (!_running) _snapshot.Dispose(); };
        UpdateActions();
    }

    private async Task ReadAsync()
    {
        if (_running || _closed) return;
        _running = true; _cancellation = new CancellationTokenSource();
        _text.Clear(); _text.IsEnabled = false; _status.Text = UiText.Get("Reconhecendo texto… Você pode cancelar a leitura.");
        _pulse.Start(); _busyChanged?.Invoke(true); UpdateActions();
        try
        {
            var result = await _service.RecognizeAsync(_snapshot, _cancellation.Token);
            if (_closed) return;
            _text.Text = result.Text;
            _status.Text = string.IsNullOrWhiteSpace(result.Text)
                ? UiText.Get("Nenhum texto encontrado. Tente uma região mais próxima do texto ou uma imagem mais nítida.")
                : result.Confidence < 0.80f
                    ? UiText.Get("Leitura com baixa confiança. Revise o texto; tente aproximar a seleção ou ajustar o OCR nas Configurações de captura.")
                    : UiText.Get("Texto pronto. Você pode corrigir e selecionar um trecho antes de copiar.");
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (!_closed) _status.Text = exception is InvalidOperationException or TimeoutException ? exception.Message
                : UiText.Get("Não foi possível reconhecer o texto. Tente novamente.");
        }
        finally
        {
            _running = false; _pulse.Stop(); _busyChanged?.Invoke(false); _cancellation.Dispose(); _cancellation = null;
            if (_closed) _snapshot.Dispose();
            else { _text.IsEnabled = true; UpdateActions(); _text.Focus(); }
            _firstRead.TrySetResult();
        }
    }
    private void UpdateActions()
    {
        _close.Content = _running ? UiText.Get("Cancelar leitura") : UiText.Get("Fechar");
        AutomationProperties.SetName(_close, _running ? UiText.Get("Cancelar leitura") : UiText.Get("Fechar"));
        _retry.IsEnabled = !_running; _copyAll.IsEnabled = !_running && !string.IsNullOrWhiteSpace(_text.Text);
        _copySelected.IsEnabled = !_running && !string.IsNullOrWhiteSpace(_text.SelectedText);
    }
    private bool Copy(string text)
    {
        if (_running || string.IsNullOrWhiteSpace(text)) return false;
        try { Clipboard.SetText(text); CopiedText = true; _status.Text = UiText.Get("Texto copiado."); return true; }
        catch (System.Runtime.InteropServices.COMException) { _status.Text = UiText.Get("A área de transferência está ocupada. Tente copiar novamente."); return false; }
    }
    private static Button ActionButton(string label, Action callback)
    {
        var button = new Button { Content = label, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 8, 14, 8) };
        button.SetResourceReference(StyleProperty, "Lab.Button"); AutomationProperties.SetName(button, label);
        button.Click += (_, _) => callback(); return button;
    }
}
