using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace SlashText.Services;

internal static class BackupInputBudget
{
    internal const int Entries = 10_000;
    internal const long EntryBytes = 256L * 1024 * 1024;
    internal const long TotalBytes = 2L * 1024 * 1024 * 1024;
    internal const long ManifestBytes = 8L * 1024 * 1024;
    internal const long MetadataBytes = 64L * 1024 * 1024;
    private const uint CentralDirectoryBytes = 16 * 1024 * 1024;

    // Bound ZIP metadata before ZipArchive materializes its central directory.
    internal static void CheckDirectory(Stream file, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (file.Length < 22 || file.Length > TotalBytes + 64L * 1024 * 1024) throw ExternalInputBudget.Exceeded();
        var tail = new byte[(int)Math.Min(file.Length, 65_557)];
        file.Position = file.Length - tail.Length; file.ReadExactly(tail);
        Span<byte> centralHeader = stackalloc byte[46];
        Span<byte> localHeader = stackalloc byte[30];
        for (var i = tail.Length - 22; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) != 0x06054b50 ||
                i + 22 + BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20)) != tail.Length) continue;
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i + 4)) != 0) throw ExternalInputBudget.Exceeded();
            ulong count = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 10));
            ulong size = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i + 12));
            ulong centralOffset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i + 16));
            ulong footerOffset = (ulong)(file.Length - tail.Length + i);
            if (count == ushort.MaxValue || size == uint.MaxValue || BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i + 16)) == uint.MaxValue)
            {
                var locator = file.Length - tail.Length + i - 20;
                if (locator < 0) throw ExternalInputBudget.Exceeded();
                file.Position = locator; Span<byte> bytes = stackalloc byte[20]; file.ReadExactly(bytes);
                var offset = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
                if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x07064b50 ||
                    BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != 0 || BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]) != 1 ||
                    offset > (ulong)Math.Max(0, file.Length - 56)) throw ExternalInputBudget.Exceeded();
                file.Position = (long)offset; Span<byte> record = stackalloc byte[56]; file.ReadExactly(record);
                if (BinaryPrimitives.ReadUInt32LittleEndian(record) != 0x06064b50 ||
                    BinaryPrimitives.ReadUInt64LittleEndian(record[4..]) is < 44 or > 1024 ||
                    BinaryPrimitives.ReadUInt64LittleEndian(record[16..]) != 0) throw ExternalInputBudget.Exceeded();
                count = BinaryPrimitives.ReadUInt64LittleEndian(record[32..]);
                size = BinaryPrimitives.ReadUInt64LittleEndian(record[40..]);
                centralOffset = BinaryPrimitives.ReadUInt64LittleEndian(record[48..]); footerOffset = offset;
            }
            if (count == 0 || count > Entries + 1 || size > CentralDirectoryBytes) throw ExternalInputBudget.Exceeded();
            if (centralOffset > footerOffset || footerOffset - centralOffset != size) throw ExternalInputBudget.Exceeded();
            file.Position = (long)centralOffset;
            ulong actualCount = 0;
            while ((ulong)file.Position < footerOffset)
            {
                token.ThrowIfCancellationRequested();
                if (++actualCount > Entries + 1 || footerOffset - (ulong)file.Position < 46) throw ExternalInputBudget.Exceeded();
                file.ReadExactly(centralHeader);
                if (BinaryPrimitives.ReadUInt32LittleEndian(centralHeader) != 0x02014b50) throw ExternalInputBudget.Exceeded();
                var skip = BinaryPrimitives.ReadUInt16LittleEndian(centralHeader[28..]) +
                    BinaryPrimitives.ReadUInt16LittleEndian(centralHeader[30..]) + BinaryPrimitives.ReadUInt16LittleEndian(centralHeader[32..]);
                if ((ulong)skip > footerOffset - (ulong)file.Position) throw ExternalInputBudget.Exceeded();
                var next = file.Position + skip;
                var localOffset = BinaryPrimitives.ReadUInt32LittleEndian(centralHeader[42..]);
                if (localOffset != uint.MaxValue)
                {
                    if (localOffset > centralOffset || centralOffset - localOffset < 30) throw ExternalInputBudget.Exceeded();
                    file.Position = localOffset; file.ReadExactly(localHeader);
                    if (BinaryPrimitives.ReadUInt32LittleEndian(localHeader) != 0x04034b50 ||
                        BinaryPrimitives.ReadUInt16LittleEndian(localHeader[6..]) != BinaryPrimitives.ReadUInt16LittleEndian(centralHeader[8..])) throw ExternalInputBudget.Exceeded();
                    if ((BinaryPrimitives.ReadUInt16LittleEndian(localHeader[6..]) & 8) == 0)
                        foreach (var pair in new[] { (18,20), (22,24) })
                        {
                            var localSize = BinaryPrimitives.ReadUInt32LittleEndian(localHeader[pair.Item1..]);
                            var declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(centralHeader[pair.Item2..]);
                            if (localSize != uint.MaxValue && declaredSize != uint.MaxValue && localSize != declaredSize) throw ExternalInputBudget.Exceeded();
                        }
                }
                file.Position = next;
            }
            if (actualCount != count) throw ExternalInputBudget.Exceeded();
            file.Position = 0; token.ThrowIfCancellationRequested(); return;
        }
        throw new InvalidDataException(UiText.Get("O ZIP não contém um diretório válido."));
    }

    internal static void CheckEntries(ZipArchive archive, CancellationToken token)
    {
        if (archive.Entries.Count > Entries + 1) throw ExternalInputBudget.Exceeded();
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var path = entry.FullName.Replace('\\', '/');
            var limit = path.Equals("backup-manifest.json", StringComparison.OrdinalIgnoreCase) ? ManifestBytes :
                path.Contains('/') ? EntryBytes : MetadataBytes;
            if (entry.Length < 0 || entry.Length > limit || total > TotalBytes - entry.Length)
                throw ExternalInputBudget.Exceeded();
            total += entry.Length;
        }
    }
}
