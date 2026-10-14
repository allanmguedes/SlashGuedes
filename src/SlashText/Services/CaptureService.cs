using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using SlashText.Models;
using SlashText.Views;

namespace SlashText.Services;

public sealed record CaptureImageEditResult(Bitmap Bitmap, CaptureEditorOutput Output);

public sealed class CaptureService
{
    private const int DwmwaExtendedFrameBounds = 9;
    private const uint MonitorDefaultToNearest = 2;
    private readonly CaptureHistoryStore _historyStore =
        new(AppPaths.CaptureHistoryFile);
    private List<CaptureRecord> _history = [];
    private readonly Dictionary<CaptureRecord, string> _fileApprovals = new();

    public IReadOnlyList<CaptureRecord> History => _history;
    public bool IsHistoryProtected => _historyStore.IsProtected;
    public bool CanRecoverHistory => _historyStore.CanRecover;
    public StoredReadState HistoryReadState => _historyStore.ReadState;
    public async Task RecoverHistoryAsync()
    {
        await _historyStore.RecoverAsync(_history);
        await LoadAsync();
    }
    public Func<Bitmap, CaptureAnnotationKind, Task<CaptureImageEditResult?>>? ImageEditor { get; set; }
    public string ResolveFilePath(CaptureRecord record)
    {
        if (record.RequiresFileApproval && !_fileApprovals.ContainsKey(record)) return string.Empty;
        var path = CapturePathResolver.Resolve(record, AppPaths.Current);
        if (!CaptureHistoryFilePolicy.TryValidate(record, path, out var fingerprint)) return string.Empty;
        if (record.RequiresFileApproval && _fileApprovals[record] != fingerprint) return string.Empty;
        return path;
    }

    // Used only after an explicit click, never for thumbnails or automatic editor loading.
    internal string GetFileApprovalPath(CaptureRecord record)
    {
        if (!_history.Contains(record)) return string.Empty;
        var path = CapturePathResolver.Resolve(record, AppPaths.Current);
        return CaptureHistoryFilePolicy.TryValidate(record, path, out _) ? path : string.Empty;
    }

    internal bool ApproveFileAccess(CaptureRecord record, string expectedPath)
    {
        if (!_history.Contains(record)) return false;
        var path = CapturePathResolver.Resolve(record, AppPaths.Current);
        if (!string.Equals(path, expectedPath, StringComparison.OrdinalIgnoreCase) ||
            !CaptureHistoryFilePolicy.TryValidate(record, path, out var fingerprint)) return false;
        _fileApprovals[record] = fingerprint;
        return true;
    }
    public async Task LoadAsync()
    {
        _fileApprovals.Clear();
        _history = await _historyStore.LoadAsync();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in _history)
        {
            if (string.IsNullOrWhiteSpace(record.Id) || !ids.Add(record.Id))
            {
                record.Id = Guid.NewGuid().ToString("N");
                ids.Add(record.Id);
            }
            if (string.IsNullOrWhiteSpace(record.MediaKind))
            {
                record.MediaKind = Path.GetExtension(ResolveFilePath(record))
                    .Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                    ? "video"
                    : Path.GetExtension(ResolveFilePath(record))
                        .Equals(".gif", StringComparison.OrdinalIgnoreCase)
                        ? "gif"
                        : "image";
            }
        }
    }

    public Rectangle ActiveMonitorBounds()
    {
        var foreground = GetForegroundWindow();
        var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(monitor, ref info)
            ? info.Monitor.ToRectangle()
            : System.Windows.Forms.Screen.PrimaryScreen?.Bounds ??
              new Rectangle(0, 0, 1920, 1080);
    }

    public Rectangle? WindowUnderCursorBounds()
    {
        return WindowBounds(WindowUnderCursorHandle());
    }

    private static Rectangle? WindowBounds(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return null;
        }

        if (DwmGetWindowAttribute(
                window,
                DwmwaExtendedFrameBounds,
                out NativeRect rect,
                Marshal.SizeOf<NativeRect>()) != 0 &&
            !GetWindowRect(window, out rect))
        {
            return null;
        }

        return rect.ToRectangle();
    }

    public IntPtr WindowUnderCursorHandle()
    {
        if (!GetCursorPos(out var point))
        {
            return IntPtr.Zero;
        }
        return GetAncestor(WindowFromPoint(point), 2);
    }

    public RecordingTarget ActiveMonitorTarget()
    {
        var bounds = ActiveMonitorBounds();
        var screen = System.Windows.Forms.Screen.FromRectangle(bounds);
        return new RecordingTarget(
            RecordingTargetKind.Monitor,
            bounds,
            DisplayDeviceName: screen.DeviceName);
    }

    public RecordingTarget? WindowUnderCursorTarget()
    {
        var handle = WindowUnderCursorHandle();
        var bounds = WindowBounds(handle);
        return handle == IntPtr.Zero || bounds is null
            ? null
            : new RecordingTarget(RecordingTargetKind.Window, bounds.Value, handle);
    }

    public RecordingTarget? SelectRecordingRegion(Window? owner, string purpose)
    {
        var selector = new RegionSelectionWindow(purpose);
        if (owner is not null)
        {
            selector.Owner = owner;
        }
        if (selector.ShowDialog() != true)
        {
            return null;
        }
        var bounds = selector.SelectedBounds;
        var screen = System.Windows.Forms.Screen.FromRectangle(bounds);
        return new RecordingTarget(
            RecordingTargetKind.Region,
            bounds,
            DisplayDeviceName: screen.DeviceName);
    }

    public RecordingTarget? SelectRecordingWindow(Window? owner, string purpose)
    {
        var selector = new RegionSelectionWindow(purpose, RecordingWindowAtPoint);
        if (owner is not null) selector.Owner = owner;
        return selector.ShowDialog() == true ? selector.SelectedWindowTarget : null;
    }

    private static RecordingTarget? RecordingWindowAtPoint(System.Drawing.Point point)
    {
        RecordingTarget? target = null;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || IsIconic(window)) return true;
            GetWindowThreadProcessId(window, out var process);
            // The selection sheet and any SlashDesk controls are not recording targets.
            if (process == Environment.ProcessId) return true;
            var bounds = WindowBounds(window);
            if (bounds is null || bounds.Value.Width < 2 || bounds.Value.Height < 2 || !bounds.Value.Contains(point)) return true;
            var kind = new StringBuilder(128); GetClassName(window, kind, kind.Capacity);
            // Clicking desktop/taskbar must not look like selecting a normal window.
            if (kind.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            target = new(RecordingTargetKind.Window, bounds.Value, window);
            return false;
        }, IntPtr.Zero);
        return target;
    }

    public Bitmap? SelectAndEditRegion(
        Window? owner,
        bool includeCursor,
        out CaptureEditorOutput requestedOutput)
    {
        var bitmap = SelectAndEditRegion(owner, includeCursor, out requestedOutput, out var session);
        session?.Dispose(); return bitmap;
    }

    public Bitmap? SelectAndEditRegion(
        Window? owner,
        bool includeCursor,
        out CaptureEditorOutput requestedOutput,
        out CaptureRegionSession? session,
        CaptureOcrSettings? ocrSettings = null)
    {
        session = null;
        requestedOutput = CaptureEditorOutput.Default;
        var selector = new RegionCaptureWindow(includeCursor) { OcrSettings = ocrSettings ?? new() };
        if (owner is not null)
        {
            selector.Owner = owner;
        }
        if (selector.ShowDialog() != true)
        {
            return null;
        }

        requestedOutput = selector.RequestedOutput;
        session = selector.CompletedSession;
        return selector.EditedBitmap;
    }

    public async Task<CaptureRecord?> CaptureAndProcessAsync(
        Rectangle bounds,
        string type,
        CaptureSettings settings,
        bool openEditor = false,
        Window? owner = null,
        CaptureAnnotationKind initialTool = CaptureAnnotationKind.Arrow)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        using var captured = CaptureBitmap(bounds, settings.IncludeCursor);
        return await ProcessBitmapAsync(
            captured,
            type,
            settings,
            openEditor,
            owner,
            initialTool);
    }

    public async Task AddMediaRecordAsync(CaptureRecord record)
    {
        if (IsHistoryProtected) return; // The completed recording file remains usable, without replacing partial history.
        record.RequiresFileApproval = false; // Called only for newly completed local recordings.
        record.PortableRelativePath ??=
            CapturePathResolver.CreatePortableRelativePath(record.FilePath, AppPaths.Current);
        _history.Insert(0, record);
        if (_history.Count > 1000)
        {
            _history.RemoveRange(1000, _history.Count - 1000);
        }
        await _historyStore.SaveAsync(_history);
    }

    /// <summary>Commit the edited pixels and register the exact saved file, not its source.</summary>
    public async Task<CaptureRecord> SaveEditedImageAsync(
        Bitmap bitmap, string path, CaptureSettings settings, CaptureRecord? sourceRecord = null)
    {
        path = Path.GetFullPath(path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg"))
        {
            throw new ArgumentException(UiText.Get("Salve a imagem editada em PNG ou JPEG."), nameof(path));
        }
        var record = _history.FirstOrDefault(item =>
            string.Equals(CapturePathResolver.Resolve(item, AppPaths.Current), path, StringComparison.OrdinalIgnoreCase));
        if (record is not null && string.IsNullOrEmpty(ResolveFilePath(record)))
            throw new InvalidOperationException(UiText.Get("O arquivo do histórico não está autorizado ou não é uma mídia válida."));
        if (record is not null && !record.MediaKind.Equals("image", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(UiText.Get("Uma edição estática não pode substituir uma gravação."));
        }
        var temporary = path + $".editing-{Guid.NewGuid():N}";
        try
        {
            if (extension is ".jpg" or ".jpeg")
            {
                var encoder = ImageCodecInfo.GetImageEncoders().First(item => item.FormatID == ImageFormat.Jpeg.Guid);
                using var parameters = new EncoderParameters(1);
                parameters.Param[0] = new EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality, Math.Clamp(settings.JpegQuality, 1, 100));
                bitmap.Save(temporary, encoder, parameters);
            }
            else bitmap.Save(temporary, ImageFormat.Png);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        if (IsHistoryProtected) record = null; // Do not mutate a legible history row after saving an explicit edit.
        record ??= new CaptureRecord
        {
            RequiresFileApproval = false,
            CreatedAt = DateTimeOffset.Now,
            Type = sourceRecord?.Type ?? "regiao"
        };
        record.FilePath = path;
        record.PortableRelativePath = CapturePathResolver.CreatePortableRelativePath(path, AppPaths.Current);
        record.Width = bitmap.Width;
        record.Height = bitmap.Height;
        if (record.RequiresFileApproval && CaptureHistoryFilePolicy.TryValidate(record, path, out var fingerprint))
            _fileApprovals[record] = fingerprint;
        if (IsHistoryProtected) return record;
        _history.Remove(record);
        _history.Insert(0, record);
        if (_history.Count > 1000) _history.RemoveRange(1000, _history.Count - 1000);
        await _historyStore.SaveAsync(_history);
        return record;
    }

    public async Task<bool> DeleteAsync(string id, bool deleteFile)
    {
        if (IsHistoryProtected) throw new IOException(UiText.Get("O histórico está protegido. Revise os dados em Configurações antes de remover entradas."));
        var record = _history.FirstOrDefault(item => item.Id == id);
        if (record is null)
        {
            return false;
        }
        if (deleteFile)
        {
            var path = ResolveFilePath(record);
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException(UiText.Get("O arquivo do histórico não está autorizado ou não é uma mídia válida."));
            File.Delete(path);
        }
        _fileApprovals.Remove(record);
        _history.Remove(record);
        await _historyStore.SaveAsync(_history);
        return true;
    }

    public async Task<int> CleanOlderThanAsync(int days, bool deleteFiles)
    {
        if (IsHistoryProtected || days <= 0)
        {
            return 0;
        }
        var threshold = DateTimeOffset.Now.AddDays(-days);
        var expired = _history.Where(item => item.CreatedAt < threshold).ToList();
        foreach (var record in expired)
        {
            var path = deleteFiles ? ResolveFilePath(record) : string.Empty;
            // Automatic retention never authorizes restored references on the user's behalf.
            if (!string.IsNullOrWhiteSpace(path) && !record.RequiresFileApproval)
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // A entrada é removida mesmo quando outro aplicativo mantém o arquivo aberto.
                }
            }
            _history.Remove(record);
            _fileApprovals.Remove(record);
        }
        if (expired.Count > 0)
        {
            await _historyStore.SaveAsync(_history);
        }
        return expired.Count;
    }

    public async Task<bool> EditExistingAsync(
        string id,
        CaptureSettings settings,
        Window owner,
        CaptureAnnotationKind initialTool = CaptureAnnotationKind.Arrow)
    {
        var record = _history.FirstOrDefault(item => item.Id == id);
        if (record is null ||
            !record.MediaKind.Equals("image", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(ResolveFilePath(record)) ||
            !File.Exists(ResolveFilePath(record)))
        {
            return false;
        }

        var resolvedPath = ResolveFilePath(record);
        Bitmap source;
        using (var sourceFile = new Bitmap(resolvedPath)) source = new Bitmap(sourceFile);
        using var sourceLifetime = source;
        var editor = new CaptureEditorWindow(source, initialTool)
        {
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        if (editor.ShowDialog() != true || editor.EditedBitmap is null)
        {
            return false;
        }

        using var edited = editor.EditedBitmap;
        if (editor.RequestedOutput == CaptureEditorOutput.Clipboard)
        {
            CopyToClipboard(edited);
            return false; // Copy is not a save and must not overwrite the original.
        }
        var destination = resolvedPath;
        if (editor.RequestedOutput == CaptureEditorOutput.File)
        {
            var jpeg = Path.GetExtension(resolvedPath).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                       Path.GetExtension(resolvedPath).Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = UiText.Get("Salvar uma cópia editada"), FileName = Path.GetFileName(resolvedPath),
                DefaultExt = jpeg ? ".jpg" : ".png", Filter = jpeg ? "JPEG|*.jpg|PNG|*.png" : "PNG|*.png|JPEG|*.jpg"
            };
            if (dialog.ShowDialog(owner) != true) return false;
            destination = dialog.FileName;
        }
        await SaveEditedImageAsync(edited, destination, settings, record);
        return true;
    }

    public static void CopyFileToClipboard(string path)
    {
        Clipboard.SetDataObject(CreateClipboardData(path), copy: true);
    }

    /// <summary>
    /// Images can be pasted into editors/chats; the original file is also kept
    /// for Explorer and for animated GIFs. Videos retain file-copy behavior.
    /// </summary>
    public static DataObject CreateClipboardData(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(UiText.Get("O arquivo não está mais disponível."), path);
        }
        path = Path.GetFullPath(path);
        var data = new DataObject();
        var collection = new System.Collections.Specialized.StringCollection
        {
            path
        };
        data.SetFileDropList(collection);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".tif" or ".tiff")
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(
                stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var image = decoder.Frames[0];
            image.Freeze();
            data.SetImage(image);
        }
        return data;
    }

    public async Task<CaptureRecord?> ProcessEditedRegionAsync(
        Bitmap captured,
        string type,
        CaptureSettings settings,
        CaptureEditorOutput requestedOutput = CaptureEditorOutput.Default)
    {
        return await ProcessBitmapAsync(
            captured,
            type,
            settings,
            openEditor: false,
            owner: null,
            initialTool: CaptureAnnotationKind.Arrow,
            requestedOutput: requestedOutput);
    }

    public async Task<CaptureRecord?> CaptureScrollingAsync(
        IntPtr window,
        Rectangle bounds,
        CaptureSettings settings,
        bool openEditor,
        Window? owner)
    {
        if (window == IntPtr.Zero || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        const int maximumFrames = 40;
        const int maximumOutputHeight = 30000;
        const int unchangedAttemptsBeforeFinish = 3;
        var fallbackDelta = bounds.Height - Math.Max(1, bounds.Height / 6);
        await WaitForModifierKeysReleasedAsync();
        SetForegroundWindow(window);
        await Task.Delay(350);
        var frames = new List<Bitmap>();
        var scrollDeltas = new List<int>();
        try
        {
            frames.Add(CaptureBitmap(bounds, settings.IncludeCursor));
            var stitchedHeight = bounds.Height;
            while (frames.Count < maximumFrames &&
                   stitchedHeight < maximumOutputHeight)
            {
                Bitmap? next = null;
                for (var attempt = 0;
                     attempt < unchangedAttemptsBeforeFinish;
                     attempt++)
                {
                    SetForegroundWindow(window);
                    KeybdEvent(VirtualKeyPageDown, 0, 0, UIntPtr.Zero);
                    KeybdEvent(
                        VirtualKeyPageDown,
                        0,
                        KeyEventKeyUp,
                        UIntPtr.Zero);
                    await Task.Delay(650);

                    var candidate = CaptureBitmap(bounds);
                    if (!AreFramesVisuallyEquivalent(frames[^1], candidate))
                    {
                        next = candidate;
                        break;
                    }
                    candidate.Dispose();
                }

                if (next is null)
                {
                    break;
                }

                var delta = EstimateVerticalScrollDelta(
                    frames[^1],
                    next,
                    fallbackDelta);
                if (stitchedHeight + delta > maximumOutputHeight)
                {
                    next.Dispose();
                    break;
                }

                frames.Add(next);
                scrollDeltas.Add(delta);
                stitchedHeight += delta;
            }

            using var stitched = new Bitmap(
                bounds.Width,
                stitchedHeight,
                PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(stitched))
            {
                graphics.DrawImageUnscaled(frames[0], 0, 0);
                var outputY = bounds.Height;
                for (var index = 1; index < frames.Count; index++)
                {
                    var delta = scrollDeltas[index - 1];
                    graphics.DrawImage(
                        frames[index],
                        new Rectangle(0, outputY, bounds.Width, delta),
                        new Rectangle(
                            0,
                            bounds.Height - delta,
                            bounds.Width,
                            delta),
                        GraphicsUnit.Pixel);
                    outputY += delta;
                }
            }

            return await ProcessBitmapAsync(
                stitched,
                "rolagem",
                settings,
                openEditor,
                owner,
                CaptureAnnotationKind.Arrow);
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    public static bool AreFramesVisuallyEquivalent(Bitmap first, Bitmap second)
    {
        if (first.Width != second.Width || first.Height != second.Height)
        {
            return false;
        }

        const int horizontalSamples = 64;
        const int verticalSamples = 56;
        var left = first.Width / 6;
        var right = Math.Max(left + 1, first.Width * 5 / 6);
        var top = first.Height / 7;
        var bottom = Math.Max(top + 1, first.Height * 13 / 14);
        var changed = 0;
        var informative = 0;
        var totalDifference = 0d;

        for (var row = 0; row < verticalSamples; row++)
        {
            var y = top + Math.Min(
                bottom - top - 1,
                row * (bottom - top) / verticalSamples);
            for (var column = 0; column < horizontalSamples; column++)
            {
                var x = left + Math.Min(
                    right - left - 1,
                    column * (right - left) / horizontalSamples);
                if (LocalContrast(first, x, y) < 8 &&
                    LocalContrast(second, x, y) < 8)
                {
                    continue;
                }

                var difference = PixelDifference(
                    first.GetPixel(x, y),
                    second.GetPixel(x, y));
                totalDifference += difference;
                if (difference > 18)
                {
                    changed++;
                }
                informative++;
            }
        }

        if (informative < 12)
        {
            return AreFramesEquivalentWithoutContrast(
                first,
                second,
                left,
                top,
                right,
                bottom);
        }

        return changed <= Math.Max(2, informative / 20) &&
               totalDifference / informative <= 5;
    }

    public static int EstimateVerticalScrollDelta(
        Bitmap previous,
        Bitmap current,
        int fallbackDelta)
    {
        var height = Math.Min(previous.Height, current.Height);
        var width = Math.Min(previous.Width, current.Width);
        var fallback = Math.Clamp(fallbackDelta, 1, Math.Max(1, height));
        if (width < 8 || height < 32 ||
            previous.Width != current.Width ||
            previous.Height != current.Height)
        {
            return fallback;
        }

        var minimumOverlap = Math.Max(24, height / 30);
        var maximumDelta = height - minimumOverlap;
        var coarseStep = Math.Max(1, height / 180);
        var bestDelta = fallback;
        var bestScore = double.MaxValue;

        for (var delta = 1; delta <= maximumDelta; delta += coarseStep)
        {
            var score = VerticalScrollMatchScore(previous, current, delta);
            if (score < bestScore)
            {
                bestScore = score;
                bestDelta = delta;
            }
        }

        var refineStart = Math.Max(1, bestDelta - coarseStep);
        var refineEnd = Math.Min(maximumDelta, bestDelta + coarseStep);
        for (var delta = refineStart; delta <= refineEnd; delta++)
        {
            var score = VerticalScrollMatchScore(previous, current, delta);
            if (score < bestScore)
            {
                bestScore = score;
                bestDelta = delta;
            }
        }

        return bestScore <= 22 ? bestDelta : fallback;
    }

    private static double VerticalScrollMatchScore(
        Bitmap previous,
        Bitmap current,
        int delta)
    {
        var overlap = previous.Height - delta;
        var topSkip = Math.Min(
            Math.Max(8, previous.Height / 7),
            Math.Max(0, overlap / 2));
        var usableHeight = overlap - topSkip;
        if (usableHeight < 8)
        {
            return double.MaxValue;
        }

        var left = previous.Width / 6;
        var right = Math.Max(left + 1, previous.Width * 5 / 6);
        var horizontalStep = Math.Max(2, (right - left) / 96);
        var verticalStep = Math.Max(1, usableHeight / 96);
        var totalDifference = 0d;
        var mismatches = 0;
        var informative = 0;

        for (var currentY = topSkip;
             currentY < overlap;
             currentY += verticalStep)
        {
            var previousY = currentY + delta;
            for (var x = left; x < right; x += horizontalStep)
            {
                if (LocalContrast(previous, x, previousY) < 8 &&
                    LocalContrast(current, x, currentY) < 8)
                {
                    continue;
                }

                var difference = PixelDifference(
                    previous.GetPixel(x, previousY),
                    current.GetPixel(x, currentY));
                totalDifference += Math.Min(96, difference);
                if (difference > 18)
                {
                    mismatches++;
                }
                informative++;
            }
        }

        if (informative < 24)
        {
            return double.MaxValue;
        }

        return totalDifference / informative +
               mismatches * 36d / informative;
    }

    private static double LocalContrast(Bitmap bitmap, int x, int y)
    {
        var center = bitmap.GetPixel(x, y);
        var right = bitmap.GetPixel(Math.Min(bitmap.Width - 1, x + 2), y);
        var below = bitmap.GetPixel(x, Math.Min(bitmap.Height - 1, y + 2));
        return Math.Max(
            PixelDifference(center, right),
            PixelDifference(center, below));
    }

    private static bool AreFramesEquivalentWithoutContrast(
        Bitmap first,
        Bitmap second,
        int left,
        int top,
        int right,
        int bottom)
    {
        const int samples = 32;
        var changed = 0;
        var totalDifference = 0d;
        var total = 0;
        for (var row = 0; row < samples; row++)
        {
            var y = top + Math.Min(
                bottom - top - 1,
                row * (bottom - top) / samples);
            for (var column = 0; column < samples; column++)
            {
                var x = left + Math.Min(
                    right - left - 1,
                    column * (right - left) / samples);
                var difference = PixelDifference(
                    first.GetPixel(x, y),
                    second.GetPixel(x, y));
                totalDifference += difference;
                if (difference > 12)
                {
                    changed++;
                }
                total++;
            }
        }

        return changed <= Math.Max(1, total / 50) &&
               totalDifference / total <= 3;
    }

    private static async Task WaitForModifierKeysReleasedAsync()
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(2) &&
               (IsKeyDown(VirtualKeyShift) ||
                IsKeyDown(VirtualKeyControl) ||
                IsKeyDown(VirtualKeyMenu) ||
                IsKeyDown(VirtualKeyLeftWindows) ||
                IsKeyDown(VirtualKeyRightWindows)))
        {
            await Task.Delay(25);
        }
    }

    private static bool IsKeyDown(int virtualKey) =>
        (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static double PixelDifference(Color first, Color second) =>
        (Math.Abs(first.R - second.R) +
         Math.Abs(first.G - second.G) +
         Math.Abs(first.B - second.B)) / 3d;

    private async Task<CaptureRecord?> ProcessBitmapAsync(
        Bitmap captured,
        string type,
        CaptureSettings settings,
        bool openEditor,
        Window? owner,
        CaptureAnnotationKind initialTool,
        CaptureEditorOutput requestedOutput = CaptureEditorOutput.Default)
    {
        Bitmap output = captured;
        var save = settings.SaveAutomatically;
        var copy = settings.CopyToClipboard;
        ApplyRequestedOutput(requestedOutput, ref save, ref copy);
        try
        {
            if (openEditor)
            {
                CaptureEditorOutput editorOutput;
                if (ImageEditor is not null)
                {
                    var result = await ImageEditor(captured, initialTool);
                    if (result is null) return null;
                    output = result.Bitmap;
                    editorOutput = result.Output;
                }
                else
                {
                    // Legacy fallback for isolated callers; production shell uses one document.
                    var editor = new CaptureEditorWindow(captured, initialTool);
                    if (owner is { IsVisible: true })
                    { editor.Owner = owner; editor.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
                    if (editor.ShowDialog() != true || editor.EditedBitmap is null) return null;
                    output = editor.EditedBitmap;
                    editorOutput = editor.RequestedOutput;
                }
                switch (editorOutput)
                {
                    case CaptureEditorOutput.Clipboard:
                        save = false;
                        copy = true;
                        break;
                    case CaptureEditorOutput.File:
                        save = true;
                        copy = false;
                        break;
                }
            }

            string filePath = string.Empty;
            if (save)
            {
                filePath = Save(output, type, settings);
            }
            if (copy)
            {
                CopyToClipboard(output);
            }

            var record = new CaptureRecord
            {
                RequiresFileApproval = false,
                CreatedAt = DateTimeOffset.Now,
                Type = type,
                FilePath = filePath,
                PortableRelativePath = CapturePathResolver.CreatePortableRelativePath(
                    filePath,
                    AppPaths.Current),
                Width = output.Width,
                Height = output.Height
            };
            if (IsHistoryProtected) return record;
            _history.Insert(0, record);
            if (_history.Count > 1000)
            {
                _history.RemoveRange(1000, _history.Count - 1000);
            }
            await _historyStore.SaveAsync(_history);
            return record;
        }
        finally
        {
            if (!ReferenceEquals(output, captured))
            {
                output.Dispose();
            }
        }
    }

    private static void ApplyRequestedOutput(
        CaptureEditorOutput requestedOutput,
        ref bool save,
        ref bool copy)
    {
        switch (requestedOutput)
        {
            case CaptureEditorOutput.Clipboard:
                save = false;
                copy = true;
                break;
            case CaptureEditorOutput.File:
                save = true;
                copy = false;
                break;
        }
    }

    public static Bitmap CaptureBitmap(Rectangle bounds, bool includeCursor = false)
    {
        var bitmap = new Bitmap(
            bounds.Width,
            bounds.Height,
            PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(
            bounds.Left,
            bounds.Top,
            0,
            0,
            bounds.Size,
            CopyPixelOperation.SourceCopy);
        if (includeCursor)
        {
            DrawCursor(graphics, bounds);
        }
        return bitmap;
    }

    private static void DrawCursor(Graphics graphics, Rectangle bounds)
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref info) ||
            info.Flags != CursorShowing ||
            info.Handle == IntPtr.Zero ||
            !bounds.Contains(info.Position.X, info.Position.Y))
        {
            return;
        }
        var icon = new IconInfo();
        var hotspotX = 0;
        var hotspotY = 0;
        if (GetIconInfo(info.Handle, out icon))
        {
            hotspotX = (int)icon.HotspotX;
            hotspotY = (int)icon.HotspotY;
            if (icon.ColorBitmap != IntPtr.Zero)
            {
                DeleteObject(icon.ColorBitmap);
            }
            if (icon.MaskBitmap != IntPtr.Zero)
            {
                DeleteObject(icon.MaskBitmap);
            }
        }
        var device = graphics.GetHdc();
        try
        {
            DrawIconEx(
                device,
                info.Position.X - bounds.Left - hotspotX,
                info.Position.Y - bounds.Top - hotspotY,
                info.Handle,
                0,
                0,
                0,
                IntPtr.Zero,
                3);
        }
        finally
        {
            graphics.ReleaseHdc(device);
        }
    }

    public static string ResolveDirectoryTemplate(string template, DateTimeOffset now)
    {
        return template
            .Replace("{year}", now.ToString("yyyy"), StringComparison.OrdinalIgnoreCase)
            .Replace("{month}", now.ToString("MM"), StringComparison.OrdinalIgnoreCase)
            .Replace("{month-name}", now.ToString("MMMM"), StringComparison.OrdinalIgnoreCase)
            .Replace("{day}", now.ToString("dd"), StringComparison.OrdinalIgnoreCase);
    }

    public static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalid.Contains(character) ? '-' : character);
        }
        return builder.ToString().Trim().TrimEnd('.');
    }

    private static string Save(Bitmap bitmap, string type, CaptureSettings settings)
    {
        var now = DateTimeOffset.Now;
        var directory = ResolveDirectoryTemplate(
            Environment.ExpandEnvironmentVariables(settings.OutputDirectoryTemplate),
            now);
        Directory.CreateDirectory(directory);

        var app = ProcessName(GetForegroundWindow());
        var baseName = settings.FileNameTemplate
            .Replace("{date}", now.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", now.ToString("HH-mm-ss"), StringComparison.OrdinalIgnoreCase)
            .Replace("{type}", type, StringComparison.OrdinalIgnoreCase)
            .Replace("{app}", app, StringComparison.OrdinalIgnoreCase);
        baseName = SanitizeFileName(baseName);
        var jpeg = settings.ImageFormat.Equals("JPEG", StringComparison.OrdinalIgnoreCase) ||
                   settings.ImageFormat.Equals("JPG", StringComparison.OrdinalIgnoreCase);
        var extension = jpeg ? ".jpg" : ".png";
        var path = UniquePath(directory, baseName, extension);

        if (!jpeg)
        {
            bitmap.Save(path, ImageFormat.Png);
            return path;
        }

        var encoder = ImageCodecInfo.GetImageEncoders()
            .First(item => item.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(
            System.Drawing.Imaging.Encoder.Quality,
            Math.Clamp(settings.JpegQuality, 1, 100));
        bitmap.Save(path, encoder, parameters);
        return path;
    }

    private static string UniquePath(string directory, string name, string extension)
    {
        var path = Path.Combine(directory, name + extension);
        for (var index = 2; File.Exists(path); index++)
        {
            path = Path.Combine(directory, $"{name}_{index}{extension}");
        }
        return path;
    }

    private static void CopyToClipboard(Bitmap bitmap)
    {
        var handle = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                handle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            Clipboard.SetImage(source);
        }
        finally
        {
            DeleteObject(handle);
        }
    }

    private static string ProcessName(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var processId);
        try
        {
            return processId == 0
                ? "desktop"
                : System.Diagnostics.Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return "desktop";
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", EntryPoint = "keybd_event")]
    private static extern void KeybdEvent(
        byte virtualKey,
        byte scanCode,
        uint flags,
        UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(ref CursorInfo info);

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);

    [DllImport("user32.dll")]
    private static extern bool DrawIconEx(
        IntPtr device,
        int x,
        int y,
        IntPtr icon,
        int width,
        int height,
        int step,
        IntPtr brush,
        int flags);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int maximum);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        IntPtr window,
        int attribute,
        out NativeRect value,
        int size);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr value);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private const int CursorShowing = 1;
    private const byte VirtualKeyPageDown = 0x22;
    private const int VirtualKeyShift = 0x10;
    private const int VirtualKeyControl = 0x11;
    private const int VirtualKeyMenu = 0x12;
    private const int VirtualKeyLeftWindows = 0x5B;
    private const int VirtualKeyRightWindows = 0x5C;
    private const uint KeyEventKeyUp = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo
    {
        public int Size;
        public int Flags;
        public IntPtr Handle;
        public NativePoint Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool IsIcon;
        public uint HotspotX;
        public uint HotspotY;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public readonly Rectangle ToRectangle() =>
            Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
}
