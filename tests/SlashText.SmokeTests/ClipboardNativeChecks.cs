using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SlashText.Models;
using SlashText.Services;
using WpfClipboard = System.Windows.Clipboard;

internal static class ClipboardNativeChecks
{
    private static readonly StringBuilder NativeTrace = new();
    private static string? TracePath;
    internal static async Task RunChildAsync(string root)
    {
        var output = Path.Combine(root, "isolated-clipboard");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--clipboard-native-isolated"); start.ArgumentList.Add(output);
        using var child = Process.Start(start)!;
        if (!await Task.Run(() => child.WaitForExit(30000))) { child.Kill(); throw new Exception("Own clipboard fixture child timed out"); }
        if (child.ExitCode is not (0 or 2)) throw new InvalidOperationException(File.ReadAllText(Path.Combine(output, "result.txt")));
        Console.WriteLine(File.ReadAllText(Path.Combine(output, "result.txt")));
    }

    internal static int RunIsolated(string output)
    {
        Directory.CreateDirectory(output);
        Exception? failure = null;
        var isolationReady = false;
        try
        {
            // Select a new, noninteractive window station BEFORE any WPF window.
            // Its clipboard is separate from the user's interactive clipboard.
            var stationName = "SlashDeskClipboardFixture-" + Guid.NewGuid().ToString("N");
            var station = CreateWindowStation(stationName, 0, 0x002E, IntPtr.Zero);
            if (station == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateWindowStation for fixture failed");
            if (!SetProcessWindowStation(station)) throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetProcessWindowStation for fixture failed");
            // Explicit creator-only DACL avoids inheriting station-specific access
            // bits as desktop rights. This applies only to our new fixture desktop.
            var descriptor = IntPtr.Zero;
            var attributesPointer = IntPtr.Zero;
            IntPtr desktop;
            try
            {
                var sid = WindowsIdentity.GetCurrent().User!.Value;
                if (!ConvertStringSecurityDescriptorToSecurityDescriptor("D:P(A;;GA;;;" + sid + ")", 1, out descriptor, out _)) throw new Win32Exception();
                var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
                attributesPointer = Marshal.AllocHGlobal(attributes.Length);
                Marshal.StructureToPtr(attributes, attributesPointer, false);
                desktop = CreateDesktop("Default", null, IntPtr.Zero, 0, 0x0083, attributesPointer);
            }
            finally { if (attributesPointer != IntPtr.Zero) Marshal.FreeHGlobal(attributesPointer); if (descriptor != IntPtr.Zero) LocalFree(descriptor); }
            if (desktop == IntPtr.Zero) throw new Win32Exception();
            isolationReady = true;
            var exit = StartWorker(stationName + "\\Default", output);
            if (exit != 0) throw new InvalidOperationException(File.Exists(Path.Combine(output, "result.txt")) ? File.ReadAllText(Path.Combine(output, "result.txt")) : $"Native worker failed (0x{exit:X8}) before writing its result. " + (File.Exists(Path.Combine(output, "trace.txt")) ? File.ReadAllText(Path.Combine(output, "trace.txt")) : "No trace"));
            GC.KeepAlive(station); GC.KeepAlive(desktop);
        }
        catch (Exception e) { failure = e; }
        // The child exits immediately; OS closes its station/desktop and native
        // fixture handles. Never switch back and write to the user's station.
        var unavailable = !isolationReady && failure is Win32Exception { NativeErrorCode: 5 };
        File.WriteAllText(Path.Combine(output, "result.txt"), unavailable ?
            "Clipboard native: UNAVAILABLE (Windows denied creation/selection of an isolated station; interactive clipboard was not accessed. Native/Win+V pilot validation remains required)." : failure?.ToString() ??
            "Clipboard native: PASS (separate noninteractive Windows station; raw flags, empty/text/rich/image restoration, concurrent copy, locked retry and 18 native editor paste/restoration combinations including empty/present EnterpriseDataProtectionId).");
        return unavailable ? 2 : failure is null ? 0 : 1;
    }

    private static int StartWorker(string desktop, string output)
    {
        var executable = Environment.ProcessPath!;
        var arguments = "\"" + executable + "\" ";
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            arguments += "\"" + System.Reflection.Assembly.GetEntryAssembly()!.Location + "\" ";
        arguments += "--clipboard-native-worker \"" + output + "\"";
        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Desktop = desktop, Flags = 1, ShowWindow = 0 };
        if (!CreateProcess(executable, new StringBuilder(arguments), IntPtr.Zero, IntPtr.Zero, false, 0x08000000, IntPtr.Zero, null, ref startup, out var process))
            throw new Win32Exception();
        try
        {
            if (WaitForSingleObject(process.Process, 25000) != 0) { TerminateProcess(process.Process, 1); throw new TimeoutException("Own native fixture worker timed out"); }
            if (!GetExitCodeProcess(process.Process, out var code)) throw new Win32Exception();
            return unchecked((int)code);
        }
        finally { CloseHandle(process.Thread); CloseHandle(process.Process); }
    }

    internal static int RunWorker(string output)
    {
        Directory.CreateDirectory(output);
        Exception? failure = null;
        try
        {
            // The worker's initial desktop is chosen by CreateProcess before CLR
            // STA/OLE initialization. Refuse manual invocation on WinSta0.
            var station = GetProcessWindowStation();
            var name = new StringBuilder(256);
            if (!GetUserObjectName(station, 2, name, 512, out _) || !name.ToString().StartsWith("SlashDeskClipboardFixture-", StringComparison.Ordinal))
                throw new InvalidOperationException("Native worker requires its private fixture station");
            if (!GetUserObjectFlags(station, 1, out var flags, Marshal.SizeOf<UserObjectFlags>(), out _) || (flags.Flags & 1) != 0)
                throw new InvalidOperationException("Native worker refuses an interactive window station");
            TracePath = Path.Combine(output, "trace.txt");
            var thread = new Thread(() =>
            {
                try
                {
                    var dispatcher = Dispatcher.CurrentDispatcher;
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                    dispatcher.BeginInvoke(new Action(async () =>
                    {
                        try { await CheckRealBackendAsync(); }
                        catch (Exception e) { failure = e; }
                        finally { dispatcher.InvokeShutdown(); }
                    }));
                    Dispatcher.Run();
                }
                catch (Exception e) { failure = e; }
            });
            thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        }
        catch (Exception e) { failure = e; }
        File.WriteAllText(Path.Combine(output, "result.txt"), failure is null ? "Clipboard native worker: PASS" : failure + Environment.NewLine + NativeTrace);
        return failure is null ? 0 : 1;
    }

    private static void Check(bool value, string name) { if (!value) throw new InvalidOperationException("Native clipboard: " + name); }
    private static void Trace(string value)
    { NativeTrace.AppendLine(value); if (TracePath is not null) File.AppendAllText(TracePath, value + Environment.NewLine); }
    private static System.Windows.DataObject Payload() => ClipboardPrivacy.CreateExpansionData("fixture\ntransient", SnippetFormat.Plain, Guid.NewGuid());
    private static async Task CheckRealBackendAsync()
    {
        WpfClipboard.Clear();
        using (var session = await ExpansionClipboardSession.CaptureAsync(new TracedClipboard(), CancellationToken.None))
        {
            await session.PublishAsync(Payload(), CancellationToken.None);
            Check(WpfClipboard.GetText() == "fixture\r\ntransient", "native publication readable by WPF");
            Check(OpenClipboard(IntPtr.Zero), "read native flags");
            try
            {
                foreach (var name in new[] { ClipboardPrivacy.IncludeHistory, ClipboardPrivacy.UploadCloud })
                {
                    var bytes = ClipboardPrivacyChecks.ReadGlobal(GetClipboardData((uint)System.Windows.DataFormats.GetDataFormat(name).Id));
                    Check(bytes.Length == 4 && BitConverter.ToUInt32(bytes) == 0, "zero DWORD present in OS clipboard: " + name);
                }
            }
            finally { CloseClipboard(); }
            Check(await session.RestoreAsync() && !WpfClipboard.GetDataObject()!.GetFormats(false).Any(), "originally empty restored to empty");
        }
        foreach (var kind in new[] { "text", "rich", "image" })
        {
            var data = new System.Windows.DataObject(); data.SetText("prior fixture");
            var html = HtmlClipboardFormatter.Create("<b>prior fixture</b>");
            if (kind == "rich") { data.SetData(System.Windows.DataFormats.Html, html); data.SetData(System.Windows.DataFormats.Rtf, "{\\rtf1 prior fixture}"); }
            if (kind == "image") data.SetImage(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat((byte)0xA3, 16).ToArray(), 8));
            WpfClipboard.SetDataObject(data, true);
            var logical = WpfClipboard.GetDataObject()!;
            foreach (var format in logical.GetFormats(false))
                Trace($"prior {kind}: format={format}, type={logical.GetData(format, false)?.GetType().Name ?? "null"}");
            byte[]? previousPixels = null;
            if (kind == "image") { previousPixels = new byte[16]; WpfClipboard.GetImage()!.CopyPixels(previousPixels, 8, 0); }
            using var session = await ExpansionClipboardSession.CaptureAsync(new WindowsExpansionClipboard(), CancellationToken.None);
            Trace($"prior {kind}: capture complete");
            await session.PublishAsync(Payload(), CancellationToken.None);
            Trace($"prior {kind}: publish complete");
            Check(await session.RestoreAsync() && WpfClipboard.GetText() == "prior fixture", "restore prior " + kind);
            Trace($"prior {kind}: restore complete");
            if (kind == "rich") Check((string)WpfClipboard.GetData(System.Windows.DataFormats.Html)! == html, "restore HTML byte offsets");
            if (kind == "image") { var image = WpfClipboard.GetImage()!; var pixels = new byte[16]; image.CopyPixels(pixels, 8, 0); Check(image.PixelWidth == 2 && pixels.SequenceEqual(previousPixels!), "restore bitmap pixels"); }
        }
        await CheckNativeEditorCopiesAsync();
        using (var owner = new HwndSource(new HwndSourceParameters("FixtureUnreadableMetadata") { ParentWindow = new IntPtr(-3), WindowStyle = 0 }))
        {
            Check(OpenClipboard(owner.Handle), "open unknown unavailable fixture");
            try
            {
                Check(EmptyClipboard(), "empty unknown unavailable fixture");
                PutBytes(13, Encoding.Unicode.GetBytes("prior fixture\0"));
                var unknown = (uint)System.Windows.DataFormats.GetDataFormat("SlashDesk.Fixture.UnavailableFormat").Id;
                Check(SetClipboardData(unknown, IntPtr.Zero) == IntPtr.Zero && Marshal.GetLastPInvokeError() == 0, "declare unknown unrenderable format");
            }
            finally { CloseClipboard(); }
            try
            {
                using var session = await ExpansionClipboardSession.CaptureAsync(new WindowsExpansionClipboard(), CancellationToken.None);
                throw new Exception("Unknown unavailable data must not be discarded");
            }
            catch (InvalidOperationException) { Check(WpfClipboard.GetText() == "prior fixture", "unknown format refusal keeps original clipboard unchanged"); }
        }
        WpfClipboard.SetText("old");
        using (var session = await ExpansionClipboardSession.CaptureAsync(new WindowsExpansionClipboard(), CancellationToken.None))
        {
            await session.PublishAsync(Payload(), CancellationToken.None); WpfClipboard.SetText("concurrent copy");
            Check(await session.RestoreAsync() && WpfClipboard.GetText() == "concurrent copy", "preserve actual concurrent OLE copy");
        }
        using (var session = await ExpansionClipboardSession.CaptureAsync(new WindowsExpansionClipboard(), CancellationToken.None))
        {
            await session.PublishAsync(Payload(), CancellationToken.None);
            using var ready = new ManualResetEventSlim(); using var proceed = new ManualResetEventSlim();
            Exception? competitorFailure = null;
            var competitor = new Thread(() =>
            {
                try
                {
                    using var window = new HwndSource(new HwndSourceParameters("FixtureClipboardCompetitor") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
                    Check(OpenClipboard(window.Handle), "competitor holds lock");
                    try
                    {
                        ready.Set(); Check(proceed.Wait(TimeSpan.FromSeconds(5)), "release competitor");
                        var bytes = Encoding.Unicode.GetBytes("copy during busy retry\0");
                        var handle = GlobalAlloc(2, (UIntPtr)bytes.Length); var pointer = GlobalLock(handle);
                        Marshal.Copy(bytes, 0, pointer, bytes.Length); GlobalUnlock(handle);
                        Check(EmptyClipboard() && SetClipboardData(13, handle) != IntPtr.Zero, "competitor publishes own copy");
                    }
                    finally { CloseClipboard(); }
                }
                catch (Exception e) { competitorFailure = e; ready.Set(); }
            });
            competitor.SetApartmentState(ApartmentState.STA); competitor.Start();
            Check(ready.Wait(TimeSpan.FromSeconds(5)), "competitor ready");
            var restore = session.RestoreAsync(); proceed.Set();
            Check(await restore, "busy restore completed"); competitor.Join();
            if (competitorFailure is not null) throw competitorFailure;
            Check(WpfClipboard.GetText() == "copy during busy retry", "new copy survives actual locked retry");
        }
        WpfClipboard.Clear();
    }

    private static async Task CheckNativeEditorCopiesAsync()
    {
        using var owner = new HwndSource(new HwndSourceParameters("FixtureNativeEditor") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        // Match an editor: without ES_AUTOVSCROLL a multiline EDIT refuses
        // pasted text beyond its visible area; that is not a clipboard failure.
        var edit = CreateWindowEx(0, "EDIT", "", 0x80000000 | 0x0004 | 0x0040 | 0x0080, 0, 0, 400, 200, owner.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Check(edit != IntPtr.Zero, "create private multiline paste destination");
        SendMessage(edit, 0x00C5, new IntPtr(1024 * 1024), IntPtr.Zero); // EM_SETLIMITTEXT
        try
        {
            foreach (var prior in new[] { "native-unicode", "native-ansi", "native-edp-empty", "native-edp-present", "empty", "image" })
            foreach (var payload in new[] { "long", "multiline", "rich" })
            {
                const string sentinel = "SENTINELA CLIP107 — conteúdo fictício anterior.";
                var opaque = (uint)System.Windows.DataFormats.GetDataFormat("SlashDesk.Fixture.OpaqueEditorFormat").Id;
                var opaqueBytes = new byte[] { 0xff, 0, 0x81, 0x7f, 0x22 };
                if (prior.StartsWith("native-", StringComparison.Ordinal))
                {
                    Check(OpenClipboard(owner.Handle), "open editor clipboard");
                    try
                    {
                        Check(EmptyClipboard(), "empty editor clipboard");
                        if (prior != "native-ansi") PutBytes(13, Encoding.Unicode.GetBytes(sentinel + "\0"));
                        else
                        {
                            // A classic editor supplies ANSI/OEM plus locale;
                            // Windows synthesizes Unicode for other consumers.
                            PutBytes(1, Encoding.ASCII.GetBytes("SENTINELA ASCII\0"));
                            PutBytes(7, Encoding.ASCII.GetBytes("SENTINELA ASCII\0"));
                            PutBytes(16, BitConverter.GetBytes(0x0409u));
                        }
                        PutBytes(opaque, opaqueBytes);
                        PutBytes((uint)System.Windows.DataFormats.GetDataFormat("SlashDesk.Fixture.NulEditorMarker").Id, [0]);
                        var edp = (uint)System.Windows.DataFormats.GetDataFormat("EnterpriseDataProtectionId").Id;
                        if (prior == "native-edp-empty")
                        {
                            Check(SetClipboardData(edp, IntPtr.Zero) == IntPtr.Zero && Marshal.GetLastPInvokeError() == 0, "advertise empty EDP metadata");
                        }
                        else if (prior == "native-edp-present") PutBytes(edp, Encoding.Unicode.GetBytes("fixture.invalid\0"));
                    }
                    finally { CloseClipboard(); }
                }
                else if (prior == "image")
                    WpfClipboard.SetImage(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat((byte)0xA3, 16).ToArray(), 8));
                else WpfClipboard.Clear();

                var value = payload switch
                {
                    "long" => new string('a', 2400),
                    "multiline" => "CLIP107 LINHA UM\nCLIP107 LINHA DOIS\nCLIP107 LINHA TRÊS",
                    _ => "**CLIP107 FORMATADO**\n\nTexto em *itálico*."
                };
                var format = payload == "rich" ? SnippetFormat.Markdown : SnippetFormat.Plain;
                var expected = (format == SnippetFormat.Markdown ? RichTextMarkdownConverter.ToPlainText(value) : value).ReplaceLineEndings("\r\n");
                using var session = await ExpansionClipboardSession.CaptureAsync(new WindowsExpansionClipboard(), CancellationToken.None);
                await session.PublishAsync(ClipboardPrivacy.CreateExpansionData(value, format, Guid.NewGuid()), CancellationToken.None);
                Check(SetWindowText(edit, ""), "clear private edit fixture");
                SendMessage(edit, 0x0302, IntPtr.Zero, IntPtr.Zero); // WM_PASTE; no interactive input.
                var inserted = new StringBuilder(expected.Length + 1);
                GetWindowText(edit, inserted, inserted.Capacity);
                Trace($"editor paste counts: prior={prior}, payload={payload}, expected={expected.Length}, actual={GetWindowTextLength(edit)}");
                Check(inserted.ToString() == expected, "actual edit paste: " + prior + "/" + payload);
                Check(await session.RestoreAsync(), "restore editor clipboard: " + prior + "/" + payload);
                if (prior.StartsWith("native-", StringComparison.Ordinal))
                {
                    Check(WpfClipboard.GetText() == (prior == "native-ansi" ? "SENTINELA ASCII" : sentinel), "restore external text");
                    Check(OpenClipboard(owner.Handle), "inspect restored opaque bytes");
                    try
                    {
                        Check(ClipboardPrivacyChecks.ReadGlobal(GetClipboardData(opaque)).SequenceEqual(opaqueBytes), "opaque editor bytes restored without conversion");
                        Check(ClipboardPrivacyChecks.ReadGlobal(GetClipboardData((uint)System.Windows.DataFormats.GetDataFormat("SlashDesk.Fixture.NulEditorMarker").Id)).SequenceEqual(new byte[] { 0 }), "NUL editor marker preserved");
                        var edp = (uint)System.Windows.DataFormats.GetDataFormat("EnterpriseDataProtectionId").Id;
                        if (prior == "native-edp-empty")
                        {
                            Check(!IsClipboardFormatAvailable(edp), "unrenderable empty hint not reintroduced as fake enterprise identity");
                        }
                        else if (prior == "native-edp-present")
                            Check(ClipboardPrivacyChecks.ReadGlobal(GetClipboardData(edp)).SequenceEqual(Encoding.Unicode.GetBytes("fixture.invalid\0")), "nonempty EDP identity bytes retained exactly");
                    }
                    finally { CloseClipboard(); }
                }
                else if (prior == "image") Check(WpfClipboard.GetImage()?.PixelWidth == 2, "restore image after actual paste");
                else Check(CountClipboardFormats() == 0, "restore empty after actual paste");
                Trace("editor paste PASS: " + prior + "/" + payload);
            }
        }
        finally { DestroyWindow(edit); }
    }

    private static void PutBytes(uint format, byte[] bytes)
    {
        var handle = GlobalAlloc(2, (UIntPtr)bytes.Length);
        Check(handle != IntPtr.Zero, "allocate fixture bytes");
        if (bytes.Length > 0)
        {
            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero) { GlobalFree(handle); throw new InvalidOperationException("Cannot lock fixture bytes"); }
            try { Marshal.Copy(bytes, 0, pointer, bytes.Length); }
            finally { GlobalUnlock(handle); }
        }
        if (SetClipboardData(format, handle) == IntPtr.Zero) { GlobalFree(handle); throw new InvalidOperationException("Cannot publish fixture bytes"); }
    }
    private sealed class TracedClipboard : IExpansionClipboard
    {
        private readonly WindowsExpansionClipboard _inner = new();
        public ClipboardSnapshot Capture()
        {
            var snapshot = _inner.Capture();
            Trace($"capture: sequence={snapshot.Sequence}, formats={((WindowsExpansionClipboard.NativeContent)snapshot.Content).Items.Count}");
            return snapshot;
        }
        public IClipboardContent Prepare(System.Windows.DataObject data) => _inner.Prepare(data);
        public ClipboardWriteResult TryWrite(IClipboardContent content, uint expectedSequence, bool requireOwned)
        {
            Trace($"before: expected={expectedSequence}, current={GetClipboardSequenceNumber()}, formats={CountClipboardFormats()}, requireOwned={requireOwned}");
            var result = _inner.TryWrite(content, expectedSequence, requireOwned);
            Trace($"after: result={result.Status}, returned={result.Sequence}, current={GetClipboardSequenceNumber()}, formats={CountClipboardFormats()}");
            return result;
        }
        public void Dispose() => _inner.Dispose();
    }
    [DllImport("user32.dll", EntryPoint = "CreateWindowStationW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowStation(string name, uint flags, uint access, IntPtr security);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetProcessWindowStation(IntPtr station);
    [DllImport("user32.dll", EntryPoint = "CreateDesktopW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateDesktop(string name, string? device, IntPtr mode, uint flags, uint access, IntPtr security);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo
    { internal int Size; internal string? Reserved, Desktop, Title; internal int X, Y, Width, Height, CharsX, CharsY, FillAttribute, Flags; internal short ShowWindow, ReservedSize; internal IntPtr ReservedBytes, Input, Output, Error; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { internal IntPtr Process, Thread; internal int ProcessId, ThreadId; }
    [StructLayout(LayoutKind.Sequential)] private struct UserObjectFlags { internal int Inherit, Reserved; internal uint Flags; }
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment, string? directory, ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll")] private static extern IntPtr GetProcessWindowStation();
    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetUserObjectName(IntPtr handle, int index, StringBuilder name, int length, out int needed);
    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetUserObjectFlags(IntPtr handle, int index, out UserObjectFlags flags, int length, out int needed);
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { internal int Length; internal IntPtr Descriptor; internal int Inherit; }
    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string sddl, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern int CountClipboardFormats();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint format, IntPtr handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr size);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr handle);
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowText(IntPtr window, string text);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")] private static extern int GetWindowTextLength(IntPtr window);
}
