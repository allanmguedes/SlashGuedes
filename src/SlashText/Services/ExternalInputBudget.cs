using System.IO;
using System.Text;
using SlashText.Models;

namespace SlashText.Services;

internal static class ExternalInputBudget
{
    internal const long ImportBytes = 64L * 1024 * 1024;
    internal const int SnippetCount = 10_000;
    internal const int SnippetCharacters = 1_048_576;
    internal const int MetadataCharacters = 16_384;
    internal const int TextLines = 200_000;
    internal const long ImageBytes = 64L * 1024 * 1024;
    internal const long ImagePixels = 64_000_000;
    internal const int ImageSide = 32_000;
    internal static InvalidDataException Exceeded() => new(UiText.Get("O arquivo excede os limites de processamento seguro. Divida o conteúdo em arquivos menores ou reduza o tamanho da imagem."));

    internal static async Task<string> ReadTextAsync(string path, long limit, CancellationToken token)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        if (file.Length > limit) throw Exceeded();
        using var bounded = new BoundedInputStream(file, limit, token, leaveOpen: true);
        using var reader = new StreamReader(bounded, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(token).ConfigureAwait(false);
    }

    internal static void CheckSnippet(Snippet item, int count)
    {
        if (count >= SnippetCount || item.Content.Length > SnippetCharacters ||
            item.Name?.Length > MetadataCharacters || item.Trigger?.Length > MetadataCharacters || item.Category?.Length > MetadataCharacters)
            throw Exceeded();
    }
    internal static void CheckText(string text, CancellationToken token)
    {
        var lines = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            if ((text[i] == '\n' || (text[i] == '\r' && (i + 1 == text.Length || text[i + 1] != '\n'))) && ++lines > TextLines) throw Exceeded();
        }
    }
}

// Also enforce the bytes actually produced by a decompressor/reader, not just metadata.
internal sealed class BoundedInputStream(Stream inner, long limit, CancellationToken token = default, bool leaveOpen = false) : Stream
{
    internal long BytesRead { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
    private int Request(int requested)
    {
        token.ThrowIfCancellationRequested();
        return (int)Math.Min(requested, Math.Max(0, limit - BytesRead) + 1);
    }
    private int Account(int read)
    {
        BytesRead += read;
        if (BytesRead > limit) throw ExternalInputBudget.Exceeded();
        token.ThrowIfCancellationRequested();
        return read;
    }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer) => Account(inner.Read(buffer[..Request(buffer.Length)]));
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
        return Account(await inner.ReadAsync(buffer[..Request(buffer.Length)], linked.Token).ConfigureAwait(false));
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }
    protected override void Dispose(bool disposing) { if (disposing && !leaveOpen) inner.Dispose(); base.Dispose(disposing); }
}
