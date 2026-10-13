using System.Buffers.Binary;
using System.Drawing;
using System.IO;

namespace SlashText.Services;

internal static class ExternalImageReader
{
    private static readonly SemaphoreSlim Decoders = new(2, 2);
    internal static async Task<T> DecodeAsync<T>(Func<T> decode, CancellationToken token = default)
    {
        await Decoders.WaitAsync(token).ConfigureAwait(false);
        try { return await Task.Run(decode, token).ConfigureAwait(false); }
        finally { Decoders.Release(); }
    }
    internal static void CheckDimensions(long width, long height)
    {
        if (width <= 0 || height <= 0 || width > ExternalInputBudget.ImageSide || height > ExternalInputBudget.ImageSide ||
            width > ExternalInputBudget.ImagePixels / height) throw ExternalInputBudget.Exceeded();
    }

    // Read only format headers before invoking a pixel decoder. JPEG marker scanning
    // is bounded too; a codec does not get to allocate pixels before dimensions pass.
    internal static Size CheckHeader(Stream stream, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (stream.Length > ExternalInputBudget.ImageBytes) throw ExternalInputBudget.Exceeded();
        stream.Position = 0; Span<byte> header = stackalloc byte[26]; stream.ReadExactly(header);
        long w, h;
        if (header[..8].SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 }) && header[12..16].SequenceEqual("IHDR"u8))
        {
            w = BinaryPrimitives.ReadUInt32BigEndian(header[16..]); h = BinaryPrimitives.ReadUInt32BigEndian(header[20..]);
        }
        else if (header[..2].SequenceEqual("BM"u8))
        {
            var dib = BinaryPrimitives.ReadUInt32LittleEndian(header[14..]);
            if (dib == 12) { w = BinaryPrimitives.ReadUInt16LittleEndian(header[18..]); h = BinaryPrimitives.ReadUInt16LittleEndian(header[20..]); }
            else if (dib >= 40) { w = BinaryPrimitives.ReadInt32LittleEndian(header[18..]); h = Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(header[22..])); }
            else throw Unsupported();
        }
        else if (header[..3].SequenceEqual("GIF"u8))
            throw new NotSupportedException(UiText.Get("GIF é uma gravação; sua edição ficará para outra etapa."));
        else if (header[0] == 0xff && header[1] == 0xd8)
        {
            stream.Position = 2;
            Span<byte> size = stackalloc byte[2];
            Span<byte> frame = stackalloc byte[5];
            while (stream.Position < Math.Min(stream.Length, 1_048_576))
            {
                token.ThrowIfCancellationRequested();
                if (stream.ReadByte() != 0xff) throw Unsupported();
                int marker; do { marker = stream.ReadByte(); } while (marker == 0xff && stream.Position < 1_048_576);
                if (marker < 0 || marker is 0xda or 0xd9) break;
                if (marker is 0xd8 or 0x01 or >= 0xd0 and <= 0xd7) continue;
                stream.ReadExactly(size);
                var length = BinaryPrimitives.ReadUInt16BigEndian(size);
                if (length < 2 || stream.Position + length - 2 > stream.Length) throw Unsupported();
                if (marker is >= 0xc0 and <= 0xcf && marker is not (0xc4 or 0xc8 or 0xcc))
                {
                    if (length < 8) throw Unsupported();
                    stream.ReadExactly(frame);
                    h = BinaryPrimitives.ReadUInt16BigEndian(frame[1..]); w = BinaryPrimitives.ReadUInt16BigEndian(frame[3..]);
                    CheckDimensions(w,h); stream.Position = 0; return new Size((int)w,(int)h);
                }
                stream.Position += length - 2;
            }
            throw Unsupported();
        }
        else throw Unsupported();
        CheckDimensions(w,h); stream.Position = 0; return new Size((int)w,(int)h);
    }
    private static InvalidDataException Unsupported() => new(UiText.Get("Abra uma imagem PNG, JPEG ou BMP válida para editar."));

    internal static Bitmap Load(string path, CancellationToken token = default)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var size = CheckHeader(file, token);
        using var source = new Bitmap(file);
        CheckDimensions(source.Width, source.Height);
        if (source.Size != size) throw Unsupported();
        token.ThrowIfCancellationRequested();
        var owned = new Bitmap(source);
        if (token.IsCancellationRequested) { owned.Dispose(); token.ThrowIfCancellationRequested(); }
        return owned;
    }
}
