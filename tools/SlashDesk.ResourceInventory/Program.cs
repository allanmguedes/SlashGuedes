using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

// Metadata-only inspection. Never Assembly.Load, deserialize application
// objects, invoke native exports or execute the inspected program.
if (args.Length != 2) throw new ArgumentException("Expected assembly and output JSON paths.");
using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var reader = pe.GetMetadataReader();
var directory = pe.PEHeaders.CorHeader!.ResourcesDirectory;
var resources = pe.GetSectionData(directory.RelativeVirtualAddress).GetContent();
var entries = new List<object>();
foreach (var handle in reader.ManifestResources)
{
    var resource = reader.GetManifestResource(handle);
    if (!resource.Implementation.IsNil) continue;
    var name = reader.GetString(resource.Name);
    var offset = checked((int)resource.Offset);
    var length = BitConverter.ToInt32(resources.AsSpan(offset, 4));
    var data = resources.AsSpan(offset + 4, length).ToArray();
    entries.Add(new { name, length, sha256 = Convert.ToHexStringLower(SHA256.HashData(data)) });
    if (name == "SlashText.Assets.NotoEmoji.catalog-assets.zip")
    {
        using var archive = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        if (archive.Entries.Count > 10000) throw new InvalidDataException("Unexpected emoji archive size.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in archive.Entries)
        {
            if (!names.Add(item.FullName) || item.FullName.Contains('/') || item.FullName.Contains('\\') ||
                !item.FullName.EndsWith(".png", StringComparison.Ordinal) || item.Length > 10_000_000)
                throw new InvalidDataException("Unexpected emoji archive entry.");
            using var content = item.Open();
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            var png = buffer.ToArray();
            if (png.LongLength != item.Length) throw new InvalidDataException("Emoji length differs.");
            using var blobHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            blobHash.AppendData(System.Text.Encoding.ASCII.GetBytes($"blob {png.Length}\0"));
            blobHash.AppendData(png);
            entries.Add(new { name = name + "!/" + item.FullName, length = item.Length,
                sha256 = Convert.ToHexStringLower(SHA256.HashData(png)),
                gitBlobSha1 = Convert.ToHexStringLower(blobHash.GetHashAndReset()) });
        }
    }
}
File.WriteAllText(args[1], JsonSerializer.Serialize(new {
    assemblyVersion = reader.GetAssemblyDefinition().Version.ToString(), executed = false, entries
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Inventory: inspected {entries.Count} embedded resources/assets without executing the app.");
