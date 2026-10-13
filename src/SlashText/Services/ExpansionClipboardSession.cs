using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using SlashText.Models;
using System.Windows;

namespace SlashText.Services;

internal static class ClipboardPrivacy
{
    internal const string ExcludeMonitoring = "ExcludeClipboardContentFromMonitorProcessing";
    internal const string IncludeHistory = "CanIncludeInClipboardHistory";
    internal const string UploadCloud = "CanUploadToCloudClipboard";
    internal const string ExpansionMarker = "SlashDesk.Expansion.Marker";

    internal static DataObject CreateExpansionData(string value, SnippetFormat format, Guid marker)
    {
        var data = new DataObject();
        // Registered Windows formats require raw DWORD bytes, not boxed integers,
        // booleans or strings serialized by WPF. No conversion to other formats.
        data.SetData(ExcludeMonitoring, new MemoryStream([1, 0, 0, 0], writable: false), autoConvert: false);
        data.SetData(IncludeHistory, new MemoryStream([0, 0, 0, 0], writable: false), autoConvert: false);
        data.SetData(UploadCloud, new MemoryStream([0, 0, 0, 0], writable: false), autoConvert: false);
        data.SetData(ExpansionMarker, new MemoryStream(marker.ToByteArray(), writable: false), autoConvert: false);
        // CF_UNICODETEXT and CF_TEXT use CRLF line endings. LF-only plain text
        // may be ignored or flattened by Windows edit controls.
        var plain = (format == SnippetFormat.Markdown ? RichTextMarkdownConverter.ToPlainText(value) : value)
            .ReplaceLineEndings("\r\n");
        data.SetData(DataFormats.UnicodeText, plain);
        data.SetData(DataFormats.Text, plain);
        // CloseClipboard synthesizes OEM text and locale when missing, changing
        // the sequence after our guarded write. Publish both explicitly so that
        // only a subsequent external copy invalidates ownership.
        data.SetData(DataFormats.OemText, new MemoryStream(EncodeOem(plain), writable: false), autoConvert: false);
        data.SetData(DataFormats.Locale, new MemoryStream(BitConverter.GetBytes((uint)(GetKeyboardLayout(0).ToInt64() & 0xffff)), writable: false), autoConvert: false);
        if (format == SnippetFormat.Markdown)
            data.SetData(DataFormats.Html, HtmlClipboardFormatter.Create(RichTextMarkdownConverter.ToHtml(value)));
        return data;
    }

    private static byte[] EncodeOem(string text)
    {
        var length = WideCharToMultiByte(1, 0, text, text.Length + 1, null, 0, IntPtr.Zero, IntPtr.Zero);
        if (length == 0) throw new Win32Exception();
        var bytes = new byte[length];
        if (WideCharToMultiByte(1, 0, text, text.Length + 1, bytes, bytes.Length, IntPtr.Zero, IntPtr.Zero) != length) throw new Win32Exception();
        return bytes;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int WideCharToMultiByte(uint page, uint flags, string text, int characters, byte[]? bytes, int byteCount, IntPtr defaultCharacter, IntPtr usedDefault);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint thread);
}

internal interface IClipboardContent : IDisposable { }
internal sealed record ClipboardSnapshot(IClipboardContent Content, uint Sequence) : IDisposable
{
    public void Dispose() => Content.Dispose();
}
internal enum ClipboardWriteStatus { Success, Busy, OwnershipLost, Failed }
internal readonly record struct ClipboardWriteResult(ClipboardWriteStatus Status, uint Sequence, bool Changed = false);
internal interface IExpansionClipboard : IDisposable
{
    ClipboardSnapshot Capture();
    IClipboardContent Prepare(DataObject data);
    // The comparison and write must both happen while the OS clipboard is locked.
    ClipboardWriteResult TryWrite(IClipboardContent content, uint expectedSequence, bool requireOwned);
}

internal sealed class ExpansionClipboardSession : IDisposable
{
    private readonly IExpansionClipboard _clipboard;
    private readonly ClipboardSnapshot _original;
    private uint _sequence;
    private bool _published;
    private bool _ownershipLost;
    private bool _finished;
    private bool _restoreResult = true;

    private ExpansionClipboardSession(IExpansionClipboard clipboard, ClipboardSnapshot original)
    { _clipboard = clipboard; _original = original; _sequence = original.Sequence; }

    internal static async Task<ExpansionClipboardSession> CaptureAsync(IExpansionClipboard clipboard, CancellationToken token)
    {
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try { return new ExpansionClipboardSession(clipboard, clipboard.Capture()); }
                catch (ExternalException) when (attempt < 7) { await Task.Delay(20 + attempt * 15, token); }
            }
        }
        catch { clipboard.Dispose(); throw; }
    }

    internal async Task PublishAsync(DataObject data, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_finished, this);
        using var content = _clipboard.Prepare(data);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var result = _clipboard.TryWrite(content, _sequence, requireOwned: _published);
            if (result.Changed) { _published = true; _sequence = result.Sequence; }
            if (result.Status == ClipboardWriteStatus.Success) return;
            if (result.Status == ClipboardWriteStatus.OwnershipLost)
            {
                _ownershipLost = true;
                throw new OperationCanceledException(UiText.Get("A expansão foi interrompida porque o conteúdo da área de transferência mudou."), token);
            }
            if (result.Status == ClipboardWriteStatus.Failed) throw Unavailable();
            await Task.Delay(20 + attempt * 15, token);
        }
        throw Unavailable();
    }

    internal async Task<bool> RestoreAsync()
    {
        if (_finished) return _restoreResult;
        _finished = true;
        if (!_published || _ownershipLost) return true;
        // Cleanup is independent of expansion cancellation. Recheck ownership on
        // every attempt; a user copy during a busy-clipboard retry must survive.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            ClipboardWriteResult result;
            try { result = _clipboard.TryWrite(_original.Content, _sequence, requireOwned: true); }
            catch (Exception e) when (e is ExternalException or InvalidOperationException) { return _restoreResult = false; }
            if (result.Changed) _sequence = result.Sequence;
            if (result.Status is ClipboardWriteStatus.Success or ClipboardWriteStatus.OwnershipLost) return true;
            if (result.Status == ClipboardWriteStatus.Failed && !result.Changed) return _restoreResult = false;
            await Task.Delay(20 + attempt * 15);
        }
        return _restoreResult = false;
    }

    private static InvalidOperationException Unavailable() => new(UiText.Get("A área de transferência está ocupada. Tente novamente."));
    public void Dispose() { _original.Dispose(); _clipboard.Dispose(); }
}
