using System.Diagnostics;
using SlashText.Models;

namespace SlashText.Services;

internal interface IRecordingController
{
    Guid RecordingId { get; }
    bool IsPaused { get; }
    TimeSpan Elapsed { get; }
    event EventHandler<RecordingProgress>? ProgressChanged;
    void Pause();
    void Resume();
    void Stop();
}

internal sealed class MonotonicRecordingClock
{
    private readonly object _gate = new();
    private readonly Stopwatch _stopwatch = new();
    private TimeSpan _accumulated;
    private bool _stopped;

    public TimeSpan Elapsed
    {
        get
        {
            lock (_gate)
            {
                return _accumulated + _stopwatch.Elapsed;
            }
        }
    }

    public bool IsPaused
    {
        get
        {
            lock (_gate)
            {
                return !_stopped && !_stopwatch.IsRunning;
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            _accumulated = TimeSpan.Zero;
            _stopped = false;
            _stopwatch.Restart();
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _accumulated = TimeSpan.Zero;
            _stopwatch.Reset();
            _stopped = false;
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_stopped || !_stopwatch.IsRunning)
            {
                return;
            }
            _accumulated += _stopwatch.Elapsed;
            _stopwatch.Reset();
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (_stopped || _stopwatch.IsRunning)
            {
                return;
            }
            _stopwatch.Restart();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }
            if (_stopwatch.IsRunning)
            {
                _accumulated += _stopwatch.Elapsed;
                _stopwatch.Reset();
            }
            _stopped = true;
        }
    }
}

internal sealed record RecordingPreset<T>(
    string SourceName,
    T Value,
    string SourceDescription)
{
    public string Name => UiText.Get(SourceName);
    public string Description => UiText.Get(SourceDescription);
}

internal static class RecordingPresetCatalog
{
    public static IReadOnlyList<RecordingPreset<int>> GifFps { get; } =
    [
        new("Recomendado", 10, "10 FPS. Menor consumo de CPU e memória e arquivo menor; adequado para demonstrações simples. FPS controla a quantidade de quadros e a fluidez, não a qualidade."),
        new("Equilibrado", 20, "20 FPS. Equilíbrio entre fluidez, consumo de CPU e memória e tamanho do arquivo; adequado para uso geral. FPS controla a quantidade de quadros e a fluidez, não a qualidade."),
        new("Fluido", 30, "30 FPS. Maior fluidez, com uso de CPU e memória e tamanho de arquivo superiores; recomendado para movimento e máquinas capazes de sustentar a captura. FPS controla a quantidade de quadros e a fluidez, não a qualidade.")
    ];

    public static IReadOnlyList<RecordingPreset<int>> GifQuality { get; } =
    [
        new("Baixa", 32, "Paleta de 32 cores por quadro. Menor fidelidade e arquivo relativamente menor; menor memória do quadro codificado; CPU usada pela quantização. Qualidade controla cores e compressão, não FPS."),
        new("Média", 64, "Paleta de 64 cores por quadro. Fidelidade e tamanho intermediários; CPU usada pela quantização. Qualidade controla cores e compressão, não FPS."),
        new("Alta", 128, "Paleta de 128 cores por quadro. Boa fidelidade para interfaces; arquivo relativamente maior; CPU usada pela quantização. Qualidade controla cores e compressão, não FPS."),
        new("Muito alta", 256, "Paleta de 256 cores por quadro, limite do GIF. Maior fidelidade e arquivo potencialmente maior; CPU usada pela quantização. Qualidade controla cores e compressão, não FPS.")
    ];

    public static IReadOnlyList<RecordingPreset<string>> Mp4Quality { get; } =
    [
        new("Baixa", "Baixa", "H.264 Main, controle por qualidade 55 e bitrate-alvo de 2,5 Mbps. Menor fidelidade e arquivo; menor pressão esperada no encoder. Mantém o FPS selecionado."),
        new("Média", "Média", "H.264 Main, controle por qualidade 70 e bitrate-alvo de 5 Mbps. Equilíbrio entre fidelidade, arquivo e carga do encoder. Mantém o FPS selecionado."),
        new("Alta", "Alta", "H.264 Main, controle por qualidade 85 e bitrate-alvo de 9 Mbps. Alta fidelidade e arquivo maior; carga mais alta no encoder. Mantém o FPS selecionado."),
        new("Muito alta", "Muito alta", "H.264 Main, controle por qualidade 95 e bitrate-alvo de 16 Mbps. Maior fidelidade e arquivo; maior carga esperada de CPU/GPU e memória do encoder. Mantém o FPS selecionado.")
    ];

    public static int NormalizeGifFps(int value) => value switch
    {
        5 => 10,
        15 => 20,
        60 => 30,
        _ => Nearest(GifFps, value).Value
    };

    public static int NormalizeGifQuality(int value) => Nearest(GifQuality, value).Value;

    public static string NormalizeMp4Quality(string? value) =>
        string.Equals(value, "Máxima", StringComparison.OrdinalIgnoreCase)
            ? "Muito alta"
            : Mp4Quality.FirstOrDefault(item =>
                    string.Equals(item.Value, value, StringComparison.OrdinalIgnoreCase))?.Value
                ?? "Alta";

    public static void Normalize(RecordingSettings settings)
    {
        settings.Audio = (settings.Audio ?? new()).Copy();
        settings.GifFps = NormalizeGifFps(settings.GifFps);
        settings.GifQuality = NormalizeGifQuality(settings.GifQuality);
        settings.VideoQuality = NormalizeMp4Quality(settings.VideoQuality);
    }

    private static RecordingPreset<int> Nearest(
        IReadOnlyList<RecordingPreset<int>> presets,
        int value) => presets
        .OrderBy(item => Math.Abs(item.Value - value))
        .ThenBy(item => item.Value)
        .First();
}
