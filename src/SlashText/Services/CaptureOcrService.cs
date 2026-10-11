using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using Tesseract;
using SlashText.Models;
using ImageFormat = System.Drawing.Imaging.ImageFormat;

namespace SlashText.Services;

public sealed record CaptureOcrResult(string Text, long ElapsedMilliseconds, long PeakWorkingSetBytes = 0,
    float Confidence = 0, bool UsedFallback = false);

/// <summary>Isolated local worker: native failures/timeouts never take down the editor.</summary>
public sealed class CaptureOcrService
{
    public const long MaxPixels = 24_000_000;
    private readonly string _executable;
    private readonly string? _modelsDirectory;
    public CaptureOcrSettings Options { get; }
    public CaptureOcrService(string? executable = null, string? modelsDirectory = null, CaptureOcrSettings? options = null)
    { _executable = executable ?? Environment.ProcessPath!; _modelsDirectory = modelsDirectory; Options = (options ?? new()).Normalize(); }

    public async Task<CaptureOcrResult> RecognizeAsync(Bitmap snapshot, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if ((long)snapshot.Width * snapshot.Height > MaxPixels)
            throw new InvalidOperationException(UiText.Get("A região é muito grande. Selecione um trecho menor para extrair texto."));
        var folder = Path.Combine(Path.GetTempPath(), "SlashDesk-OCR", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, timeout.Token);
        using var process = new Process();
        var clock = Stopwatch.StartNew();
        try
        {
            var input = Path.Combine(folder, "region.png"); var output = Path.Combine(folder, "result.json");
            await Task.Run(() => { linked.Token.ThrowIfCancellationRequested(); snapshot.Save(input, ImageFormat.Png); }, linked.Token);
            process.StartInfo = new ProcessStartInfo(_executable) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var arg in new[] { "--capture-ocr-worker", input, output }) process.StartInfo.ArgumentList.Add(arg);
            process.StartInfo.ArgumentList.Add(_modelsDirectory ?? "");
            process.StartInfo.ArgumentList.Add(JsonSerializer.Serialize(Options));
            linked.Token.ThrowIfCancellationRequested();
            if (!process.Start()) throw new InvalidOperationException(UiText.Get("Não foi possível iniciar a leitura de texto."));
            // Drain without retaining native diagnostics, which may contain recognized text.
            var errors = DrainAsync(process.StandardError); var stdout = DrainAsync(process.StandardOutput);
            try { await process.WaitForExitAsync(linked.Token); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(); await Task.WhenAll(errors, stdout);
                if (cancellation.IsCancellationRequested) throw new OperationCanceledException(cancellation);
                throw new TimeoutException(UiText.Get("A leitura demorou demais. Tente selecionar uma região menor."));
            }
            await Task.WhenAll(errors, stdout); linked.Token.ThrowIfCancellationRequested();
            if (process.ExitCode == 4)
                throw new InvalidOperationException(UiText.Get("Não foi possível carregar os componentes locais do OCR. Confira o requisito Microsoft Visual C++ x64 no LEIA-ANTES do piloto."));
            if (process.ExitCode != 0 || !File.Exists(output))
                throw new InvalidOperationException(UiText.Get("Não foi possível reconhecer o texto. Tente uma região menor ou uma imagem mais nítida."));
            if (new FileInfo(output).Length > 4 * 1024 * 1024) throw new InvalidDataException(UiText.Get("Resultado de texto muito grande."));
            var result = JsonSerializer.Deserialize<CaptureOcrResult>(await File.ReadAllTextAsync(output, linked.Token))
                ?? throw new InvalidDataException(UiText.Get("Resultado OCR inválido."));
            return result with { Text = result.Text.Trim(), ElapsedMilliseconds = clock.ElapsedMilliseconds };
        }
        finally
        {
            // No images or recognized text are retained in the capture history or diagnostic log.
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    private static async Task DrainAsync(StreamReader stream)
    { var buffer = new char[1024]; while (await stream.ReadAsync(buffer) != 0) { } }

    internal static int RunWorker(string[] args)
    {
        if (args.Length is not (3 or 4 or 5) || args[0] != "--capture-ocr-worker") return 2;
        try
        {
            using var original = new Bitmap(args[1]);
            if ((long)original.Width * original.Height > MaxPixels) return 3;
            var options = (args.Length == 5 ? JsonSerializer.Deserialize<CaptureOcrSettings>(args[4]) : null)?.Normalize() ?? new();
            var models = args.Length >= 4 && !string.IsNullOrWhiteSpace(args[3]) ? Path.GetFullPath(args[3]) : ExtractModels(options.Model);
            TesseractEnviornment.CustomSearchPath = AppContext.BaseDirectory;
            using var prepared = PrepareImage(original);
            using var stream = new MemoryStream(); prepared.Save(stream, ImageFormat.Png);
            using var pix = Pix.LoadFromMemory(stream.ToArray());
            using var engine = new TesseractEngine(models, options.Languages, EngineMode.LstmOnly);
            var mode = options.Layout switch { "Block" => PageSegMode.SingleBlock, "Sparse" => PageSegMode.SparseText, _ => PageSegMode.Auto };
            var result = Read(engine, pix, mode);
            if (options.ImproveDifficultImages && (string.IsNullOrWhiteSpace(result.Text) || result.Confidence < 0.80f))
            {
                // Bounded retries, same model and language. Native confidence is only a ranking heuristic.
                foreach (var other in new[] { PageSegMode.SparseText, PageSegMode.SingleBlock }.Where(value => value != mode))
                    result = Prefer(result, Read(engine, pix, other) with { UsedFallback = true });
                engine.SetVariable("thresholding_method", 2); // Sauvola, supported by Tesseract 5.
                result = Prefer(result, Read(engine, pix, mode) with { UsedFallback = true });
                engine.SetVariable("thresholding_method", 0);
                using var contrast = PrepareImage(original, isolateColors: true);
                using var contrastStream = new MemoryStream(); contrast.Save(contrastStream, ImageFormat.Png);
                using var contrastPix = Pix.LoadFromMemory(contrastStream.ToArray());
                result = Prefer(result, Read(engine, contrastPix, options.Layout == "Sparse" ? PageSegMode.SparseText : PageSegMode.SingleBlock) with { UsedFallback = true });
            }
            File.WriteAllText(args[2], JsonSerializer.Serialize(result with { PeakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64 }));
            return 0;
        }
        catch (Exception exception)
        {
            for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
                if (cause is DllNotFoundException) return 4;
            return 1;
        }
    }

    private static CaptureOcrResult Read(TesseractEngine engine, Pix image, PageSegMode mode)
    {
        using var page = engine.Process(image, mode);
        var text = page.GetText().Trim();
        return new(text, 0, Confidence: text.Length == 0 ? 0 : page.GetMeanConfidence());
    }

    private static CaptureOcrResult Prefer(CaptureOcrResult current, CaptureOcrResult candidate) =>
        !string.IsNullOrWhiteSpace(candidate.Text) && (string.IsNullOrWhiteSpace(current.Text) || candidate.Confidence > current.Confidence + 0.02f)
            ? candidate : current;

    internal static string ExtractModels(string model = "Best")
    {
        var fast = model == "Fast";
        var folder = Path.Combine(Path.GetTempPath(), "SlashDesk-OCR-models", fast ? "fast-87416418" : "best-e12c65a9");
        Directory.CreateDirectory(folder);
        foreach (var (language, hash) in fast ? new[] {
            ("eng", "7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2"),
            ("por", "c4932b937207a9514b7514d518b931a99938c02a28a5a5a553f8599ed58b7deb") } : new[] {
            ("eng", "8280aed0782fe27257a68ea10fe7ef324ca0f8d85bd2fd145d1c2b560bcb66ba"),
            ("por", "711de9dbb8052067bd42f16b9119967f30bada80d57e2ef24f65d09f531adb04") })
        {
            var path = Path.Combine(folder, language + ".traineddata");
            if (File.Exists(path)) { using var existing = File.OpenRead(path);
                if (Convert.ToHexStringLower(SHA256.HashData(existing)) == hash) continue; }
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("SlashText.Assets.Ocr." + (fast ? "Fast." : "") + language + ".traineddata")
                    ?? throw new FileNotFoundException("Modelo OCR ausente."))
                using (var destination = File.Create(temporary)) input.CopyTo(destination);
                using (var restored = File.OpenRead(temporary))
                    if (Convert.ToHexStringLower(SHA256.HashData(restored)) != hash) throw new InvalidDataException(UiText.Get("Modelo OCR inválido."));
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return folder;
    }

    internal static Bitmap PrepareImage(Bitmap source, bool isolateColors = false)
    {
        // Preserve original pixels. Only the OCR copy is scaled and polarity-adjusted.
        var scale = Math.Min(source.Height < 180 ? 3d : 2d, Math.Sqrt(MaxPixels / ((double)source.Width * source.Height)));
        scale = Math.Min(scale, 6000d / Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)(source.Width * scale)); var height = Math.Max(1, (int)(source.Height * scale));
        var image = new Bitmap(width + 20, height + 20, PixelFormat.Format24bppRgb);
        double luminance = 0; int samples = 0;
        for (var y = 0; y < source.Height; y += Math.Max(1, source.Height / 24))
        for (var x = 0; x < source.Width; x += Math.Max(1, source.Width / 24))
        { var c = source.GetPixel(x, y); luminance += (c.R + c.G + c.B) / 3d; samples++; }
        var invert = luminance / samples < 110;
        using var graphics = Graphics.FromImage(image); graphics.Clear(Color.White);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        using var attributes = new ImageAttributes();
        if (invert) attributes.SetColorMatrix(new ColorMatrix(new[] {
            new float[] {-1,0,0,0,0}, new float[] {0,-1,0,0,0}, new float[] {0,0,-1,0,0},
            new float[] {0,0,0,1,0}, new float[] {1,1,1,0,1} }));
        graphics.DrawImage(source, new Rectangle(10, 10, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        graphics.Dispose();
        if (isolateColors)
        {
            // A colored title can disappear under grayscale thresholding. Use the brightest RGB
            // channel on the OCR copy; this helps red/yellow/white lettering on dark photographs.
            var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            try
            {
                var bytes = new byte[data.Stride * data.Height]; System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                for (var y = 10; y < height + 10; y++) for (var x = 10; x < width + 10; x++)
                {
                    var i = y * data.Stride + x * 3;
                    var value = invert ? Math.Min(bytes[i], Math.Min(bytes[i + 1], bytes[i + 2])) : Math.Max(bytes[i], Math.Max(bytes[i + 1], bytes[i + 2]));
                    bytes[i] = bytes[i + 1] = bytes[i + 2] = value;
                }
                System.Runtime.InteropServices.Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
            }
            finally { image.UnlockBits(data); }
        }
        return image;
    }
}
