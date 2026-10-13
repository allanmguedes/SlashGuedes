using System.Runtime.InteropServices;
using System.Text;
using SlashText.Models;
using SlashText.Services;
using WpfDataObject = System.Windows.DataObject;
using WpfFormats = System.Windows.DataFormats;

internal static class ClipboardPrivacyChecks
{
    private static int _checks;
    private static void Check(bool value, string name)
    { if (!value) throw new InvalidOperationException("Clipboard privacy: " + name); _checks++; }

    internal static void Run(string root)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { CheckPayloads(); CheckImagePayload(root); CheckSessionsAsync().GetAwaiter().GetResult(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw new InvalidOperationException("Clipboard checks failed", failure);
        Console.WriteLine($"Clipboard privacy: {_checks} checks passed (no system clipboard writes).");
    }

    private static void CheckImagePayload(string root)
    {
        var previous = AppPaths.Current;
        AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable, Path.Combine(root, "clipboard-fixture"), root, isCapturePilot: true));
        try
        {
            Directory.CreateDirectory(AppPaths.AssetsDirectory);
            var path = Path.Combine(AppPaths.AssetsDirectory, "fixture.png");
            using (var bitmap = new System.Drawing.Bitmap(2, 2)) bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            var data = ClipboardPrivacy.CreateExpansionData("![fixture](assets/fixture.png)", SnippetFormat.Markdown, Guid.NewGuid());
            var expectedImage = Convert.ToBase64String(File.ReadAllBytes(path));
            var html = (string)data.GetData(WpfFormats.Html)!;
            Check(html.Contains("data:image/png;base64," + expectedImage, StringComparison.Ordinal), "rich image bytes retained in HTML");
            using var native = WindowsExpansionClipboard.PrepareNative(data);
            var raw = ReadGlobal(native.Items.Single(i => i.Format == (uint)WpfFormats.GetDataFormat(WpfFormats.Html).Id).Handle);
            Check(Encoding.UTF8.GetString(raw).TrimEnd('\0') == html, "native HTML retains embedded image");
            var explicitCapture = CaptureService.CreateClipboardData(path);
            using var nativeCapture = WindowsExpansionClipboard.PrepareNative(explicitCapture);
            Check(nativeCapture.Items.Single(i => i.Format == 2).Kind == System.Runtime.InteropServices.ComTypes.TYMED.TYMED_GDI, "bitmap requests GDI storage and preserves handle lifetime");
            Check(explicitCapture.GetFormats(false).Contains(WpfFormats.Bitmap) && explicitCapture.GetFormats(false).Contains(WpfFormats.FileDrop), "capture explicit copy retains image and file formats");
            Check(!explicitCapture.GetFormats(false).Any(f => f is ClipboardPrivacy.ExcludeMonitoring or ClipboardPrivacy.IncludeHistory or ClipboardPrivacy.UploadCloud), "explicit capture preserves Windows history/cloud policy");
        }
        finally { AppPaths.Initialize(previous); }
    }

    private static void CheckPayloads()
    {
        foreach (var (value, format) in new[]
        {
            (new string('a', 2400), SnippetFormat.Plain),
            ("ação\nsegunda linha", SnippetFormat.Plain),
            ("**ação**\n\ntexto formatado", SnippetFormat.Markdown)
        })
        {
            var marker = Guid.NewGuid();
            var data = ClipboardPrivacy.CreateExpansionData(value, format, marker);
            var plain = (format == SnippetFormat.Markdown ? RichTextMarkdownConverter.ToPlainText(value) : value).ReplaceLineEndings("\r\n");
            Check((string)data.GetData(WpfFormats.UnicodeText)! == plain, "plain text unchanged");
            Check((string)data.GetData(WpfFormats.Text)! == plain, "ANSI fallback retained");
            using var native = WindowsExpansionClipboard.PrepareNative(data);
            foreach (var name in new[] { ClipboardPrivacy.ExcludeMonitoring, ClipboardPrivacy.IncludeHistory, ClipboardPrivacy.UploadCloud })
            {
                var item = native.Items.Single(i => i.Format == (uint)WpfFormats.GetDataFormat(name).Id);
                var expected = name == ClipboardPrivacy.ExcludeMonitoring ? 1 : 0;
                var bytes = ReadGlobal(item.Handle);
                Check(bytes.Length == 4 && BitConverter.ToUInt32(bytes) == expected, "official raw DWORD: " + name);
            }
            Check(ReadGlobal(native.Items.Single(i => i.Format == (uint)WpfFormats.GetDataFormat(ClipboardPrivacy.ExpansionMarker).Id).Handle).SequenceEqual(marker.ToByteArray()), "opaque expansion identity unchanged");
            var textBytes = ReadGlobal(native.Items.Single(i => i.Format == 13).Handle);
            Check(Encoding.Unicode.GetString(textBytes).TrimEnd('\0') == plain, "native Unicode exact");
            Check(ReadGlobal(native.Items.Single(i => i.Format == 16).Handle).Length == 4, "locale present before CloseClipboard");
            var oem = ReadGlobal(native.Items.Single(i => i.Format == 7).Handle);
            Check(oem.Length > 0 && oem[^1] == 0 && (value.Any(c => c > 127) || Encoding.ASCII.GetString(oem).TrimEnd('\0') == plain), "OEM fallback present before CloseClipboard");
            if (format == SnippetFormat.Markdown)
            {
                var html = (string)data.GetData(WpfFormats.Html)!;
                Check(html.Contains("<strong>" + System.Net.WebUtility.HtmlEncode("ação") + "</strong>", StringComparison.Ordinal), "rich HTML preserved");
                var raw = ReadGlobal(native.Items.Single(i => i.Format == (uint)WpfFormats.GetDataFormat(WpfFormats.Html).Id).Handle);
                Check(Encoding.UTF8.GetString(raw).TrimEnd('\0') == html, "native HTML exact including CF_HTML offsets");
            }
        }
        var explicitCopy = new WpfDataObject(); explicitCopy.SetText("explicit fixture");
        Check(!explicitCopy.GetFormats(false).Contains(ClipboardPrivacy.ExcludeMonitoring), "ordinary explicit copies keep Windows policy");
        foreach (var input in new[] { "one\ntwo", "one\r\ntwo", "one\rtwo", "one\r\ntwo\nthree" })
        {
            var payload = ClipboardPrivacy.CreateExpansionData(input, SnippetFormat.Plain, Guid.NewGuid());
            Check((string)payload.GetData(WpfFormats.UnicodeText)! == input.ReplaceLineEndings("\r\n"), "canonical Windows line endings without doubled CR");
        }
        var emptyHandle = GlobalAlloc(2, UIntPtr.Zero);
        try
        {
            try
            {
                using var emptyCopy = WindowsExpansionClipboard.NativeItem.Duplicate((uint)WpfFormats.GetDataFormat("SlashDesk.Fixture.EmptyMarker").Id, emptyHandle, System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL);
                throw new Exception("Discarded native handle must be refused before publication");
            }
            catch (InvalidOperationException) { Check(true, "discarded zero-byte native handle refused before publication"); }
        }
        finally { GlobalFree(emptyHandle); }
        using var bitmap = new System.Drawing.Bitmap(8, 8);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) graphics.Clear(System.Drawing.Color.Blue);
        var originalHandle = bitmap.GetHbitmap();
        try
        {
            using var copy = WindowsExpansionClipboard.NativeItem.Duplicate(2, originalHandle, System.Runtime.InteropServices.ComTypes.TYMED.TYMED_GDI);
            using var restored = System.Drawing.Image.FromHbitmap(copy.Handle);
            Check(copy.Handle != originalHandle && restored.Width == 8 && ((System.Drawing.Bitmap)restored).GetPixel(4, 4).B == 255, "native bitmap handle copied with pixels preserved");
        }
        finally { DeleteObject(originalHandle); }
    }

    private static async Task CheckSessionsAsync()
    {
        {
            var fake = new FakeClipboard(""); fake.InitialSequence(0);
            using var session = await ExpansionClipboardSession.CaptureAsync(fake, CancellationToken.None);
            await session.PublishAsync(ClipboardPrivacy.CreateExpansionData("fixture", SnippetFormat.Plain, Guid.NewGuid()), CancellationToken.None);
            Check(await session.RestoreAsync() && fake.Formats == 0, "zero initial sequence and empty clipboard");
        }
        foreach (var original in new[] { "", "previous fixture" })
        {
            var fake = new FakeClipboard(original);
            using var session = await ExpansionClipboardSession.CaptureAsync(fake, CancellationToken.None);
            await session.PublishAsync(ClipboardPrivacy.CreateExpansionData("one\ntwo", SnippetFormat.Plain, Guid.NewGuid()), CancellationToken.None);
            Check(fake.Text == "one\r\ntwo", "transient payload published with Windows line endings");
            Check(await session.RestoreAsync() && fake.Text == original && fake.Formats == (original.Length == 0 ? 0 : 1), "restoration including originally empty clipboard");
            Check(await session.RestoreAsync(), "cleanup idempotent");
        }
        foreach (var beforeFirstWrite in new[] { true, false })
        {
            var fake = new FakeClipboard("old");
            using var session = await ExpansionClipboardSession.CaptureAsync(fake, CancellationToken.None);
            if (!beforeFirstWrite) await session.PublishAsync(ClipboardPrivacy.CreateExpansionData("transient", SnippetFormat.Plain, Guid.NewGuid()), CancellationToken.None);
            fake.UserCopy("new user copy");
            try { await session.PublishAsync(ClipboardPrivacy.CreateExpansionData("next segment", SnippetFormat.Plain, Guid.NewGuid()), CancellationToken.None); throw new Exception("Must detect concurrent copy"); }
            catch (OperationCanceledException) { Check(true, "stop expansion after concurrent copy"); }
            Check(await session.RestoreAsync() && fake.Text == "new user copy", "concurrent copy survives cleanup and next segment");
        }
        {
            var fake = new FakeClipboard("old");
            using var session = await ExpansionClipboardSession.CaptureAsync(fake, CancellationToken.None);
            await session.PublishAsync(ClipboardPrivacy.CreateExpansionData("transient", SnippetFormat.Plain, Guid.NewGuid()), CancellationToken.None);
            fake.BusyWrites = 1;
            fake.AfterBusy = () => fake.UserCopy("copy during retry");
            Check(await session.RestoreAsync() && fake.Text == "copy during retry", "recheck ownership after busy restore attempt");
        }
        {
            var fake = new FakeClipboard("old");
            using var session = await ExpansionClipboardSession.CaptureAsync(fake, CancellationToken.None);
            using var cancellation = new CancellationTokenSource();
            await session.PublishAsync(ClipboardPrivacy.CreateExpansionData("transient", SnippetFormat.Plain, Guid.NewGuid()), cancellation.Token);
            cancellation.Cancel();
            try { await session.PublishAsync(ClipboardPrivacy.CreateExpansionData("next", SnippetFormat.Plain, Guid.NewGuid()), cancellation.Token); }
            catch (OperationCanceledException) { Check(true, "cancellation before next write"); }
            Check(await session.RestoreAsync() && fake.Text == "old", "cleanup independent of canceled expansion");
        }
        {
            var fake = new FakeClipboard("old");
            using var session = await ExpansionClipboardSession.CaptureAsync(fake, CancellationToken.None);
            fake.FailAfterChange = true;
            try { await session.PublishAsync(ClipboardPrivacy.CreateExpansionData("partial", SnippetFormat.Plain, Guid.NewGuid()), CancellationToken.None); throw new Exception("Expected write failure"); }
            catch (InvalidOperationException) { Check(true, "partial native write reported"); }
            Check(await session.RestoreAsync() && fake.Text == "old", "partial write still restores original");
        }
        {
            var fake = new FakeClipboard("old");
            using var session = await ExpansionClipboardSession.CaptureAsync(fake, CancellationToken.None);
            await session.PublishAsync(ClipboardPrivacy.CreateExpansionData("transient", SnippetFormat.Plain, Guid.NewGuid()), CancellationToken.None);
            fake.BusyWrites = 20;
            Check(!await session.RestoreAsync() && fake.Text == "transient", "busy cleanup stops after bounded retries and reports failure");
            Check(!await session.RestoreAsync(), "failed cleanup result remains truthful on repeated calls");
        }
    }

    internal static byte[] ReadGlobal(IntPtr handle)
    {
        var size = checked((int)GlobalSize(handle).ToUInt64());
        var pointer = GlobalLock(handle); if (pointer == IntPtr.Zero) throw new Exception("Cannot lock test HGLOBAL");
        try { var bytes = new byte[size]; Marshal.Copy(pointer, bytes, 0, size); return bytes; }
        finally { GlobalUnlock(handle); }
    }
    private sealed class FakeContent(string text, int formats) : IClipboardContent
    { internal string Text { get; } = text; internal int Formats { get; } = formats; public void Dispose() { } }
    private sealed class FakeClipboard(string original) : IExpansionClipboard
    {
        internal string Text { get; private set; } = original;
        internal int Formats { get; private set; } = original.Length == 0 ? 0 : 1;
        private uint _sequence = 1; private bool _owned;
        internal void InitialSequence(uint value) => _sequence = value;
        internal int BusyWrites; internal Action? AfterBusy; internal bool FailAfterChange;
        internal void UserCopy(string text) { Text = text; Formats = 1; _sequence++; _owned = false; }
        public ClipboardSnapshot Capture() => new(new FakeContent(Text, Formats), _sequence);
        public IClipboardContent Prepare(WpfDataObject data) => new FakeContent((string)data.GetData(WpfFormats.UnicodeText)!, data.GetFormats(false).Length);
        public ClipboardWriteResult TryWrite(IClipboardContent content, uint expectedSequence, bool requireOwned)
        {
            if (BusyWrites-- > 0) { AfterBusy?.Invoke(); return new(ClipboardWriteStatus.Busy, expectedSequence); }
            if (expectedSequence != _sequence || (requireOwned && !_owned)) return new(ClipboardWriteStatus.OwnershipLost, _sequence);
            var copy = (FakeContent)content; Text = copy.Text; Formats = copy.Formats; _sequence++; _owned = true;
            if (FailAfterChange) { FailAfterChange = false; return new(ClipboardWriteStatus.Failed, _sequence, true); }
            return new(ClipboardWriteStatus.Success, _sequence, true);
        }
        public void Dispose() { }
    }
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr size);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr handle);
}
