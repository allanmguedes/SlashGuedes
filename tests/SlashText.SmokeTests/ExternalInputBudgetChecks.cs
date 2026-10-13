using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SlashText.Models;
using SlashText.Services;

internal static class ExternalInputBudgetChecks
{
    private static int _checks;
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException("External inputs: " + message); _checks++; }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception e) when (e is InvalidDataException or JsonException or OperationCanceledException or EndOfStreamException or NotSupportedException) { _checks++; return; }
        throw new InvalidOperationException("Expected rejection: " + message);
    }
    private static async Task RejectAsync(Func<Task> action, string message)
    {
        try { await action(); } catch (Exception e) when (e is InvalidDataException or JsonException or OperationCanceledException or EndOfStreamException or NotSupportedException) { _checks++; return; }
        throw new InvalidOperationException("Expected rejection: " + message);
    }

    internal static async Task RunAsync(string parent)
    {
        var root = Path.Combine(parent, "external-inputs"); Directory.CreateDirectory(root);
        var previous = AppPaths.Current;
        AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable, root, root, isCapturePilot: true));
        try
        {
            // Actual read limits, including sync/span/async, growth and cancellation.
            foreach (var asyncRead in new[] { false, true })
            {
                using var exact = new BoundedInputStream(new MemoryStream(new byte[32]), 32);
                var buffer = new byte[33];
                var read = asyncRead ? await exact.ReadAsync(buffer) : exact.Read(buffer);
                Check(read == 32 && exact.BytesRead == 32, "exact actual-byte limit");
                Check(exact.Read(buffer) == 0, "EOF at exact limit");
                using var oversized = new BoundedInputStream(new MemoryStream(new byte[33]), 32);
                await RejectAsync(async () => { var produced = asyncRead ? await oversized.ReadAsync(buffer) : oversized.Read(buffer); Check(produced >= 0, "read returned"); }, "actual bytes exceed metadata");
            }
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            Reject(() => new BoundedInputStream(new MemoryStream(new byte[1]), 32, canceled.Token).ReadByte(), "cancel before reading");
            var usage=new UsageService();
            var gate=(SemaphoreSlim)typeof(UsageService).GetField("_writeLock",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(usage)!;
            await gate.WaitAsync();
            try { await RejectAsync(()=>usage.RunMaintenanceAsync(()=>throw new Exception("Canceled work must not run"),canceled.Token),"cancel while maintenance waits for a writer"); }
            finally { gate.Release(); }

            var archive = Path.Combine(root, "valid.zip");
            WriteZip(archive, new() { ["snippets.md"] = "old", ["settings.json"] = "{}" });
            BackupService.ValidateSnapshot(archive); Check(true, "ordinary legacy backup accepted");
            var forged = Path.Combine(root, "forged.zip");
            WriteZip(forged, new() { ["assets/large.bin"] = "tiny" }, manifest: "{");
            PatchLength(forged, "assets/large.bin", (uint)(BackupInputBudget.EntryBytes + 1));
            ExpectEarlyBudget(forged, "oversized payload before malformed manifest parsing");
            WriteZip(forged, new() { ["snippets.md"] = "tiny" }, manifest: "{");
            PatchLength(forged, "backup-manifest.json", (uint)(BackupInputBudget.ManifestBytes + 1));
            ExpectEarlyBudget(forged, "oversized manifest before parsing");
            WriteZip(forged, Enumerable.Range(0,9).ToDictionary(n => $"assets/{n}.bin", _ => "tiny"), manifest: "{");
            for (var n=0;n<9;n++) PatchLength(forged, $"assets/{n}.bin", (uint)BackupInputBudget.EntryBytes);
            ExpectEarlyBudget(forged, "aggregate expansion before hashes");
            WriteZip(forged, new() { ["snippets.md"] = "tiny" });
            var bytes = File.ReadAllBytes(forged); var footer = bytes.Length-22;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(footer+8), 10002); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(footer+10), 10002); File.WriteAllBytes(forged,bytes);
            ExpectEarlyBudget(forged, "entry count before central-directory allocation");
            WriteZip(forged, new() { ["snippets.md"] = "tiny" }); bytes=File.ReadAllBytes(forged); footer=bytes.Length-22;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(footer+12), 1); File.WriteAllBytes(forged,bytes);
            ExpectEarlyBudget(forged, "false central-directory byte count");
            WriteZip(forged, new() { ["assets/exact.bin"] = "tiny" });
            PatchLength(forged,"assets/exact.bin",(uint)BackupInputBudget.EntryBytes);
            PatchLength(forged,"backup-manifest.json",(uint)BackupInputBudget.ManifestBytes);
            using(var guarded=File.OpenRead(forged))
            {
                BackupInputBudget.CheckDirectory(guarded,default);
                using var zip=new ZipArchive(guarded,ZipArchiveMode.Read);BackupInputBudget.CheckEntries(zip,default);
                Check(true,"exact entry and manifest declared budgets accepted without decompression");
            }
            WriteZip(forged, new() { ["snippets.md"] = "tiny" }, manifest: "{");
            Reject(() => BackupService.ValidateSnapshot(forged), "small malformed manifest");
            WriteZip(forged,new(){["snippets.md"]="tiny"},JsonSerializer.Serialize(new{schemaVersion=2,files=new[]{new{path=new string('x',32768),size=4,sha256=new string('0',64)}}}));
            ExpectEarlyBudget(forged,"oversize manifest path before feedback or hashes");

            var active = Path.Combine(root,"active"); var backups=Path.Combine(root,"backups"); Directory.CreateDirectory(active); Directory.CreateDirectory(backups);
            var snippets = Path.Combine(active,"snippets.md"); var settings=Path.Combine(active,"settings.json");
            File.WriteAllText(snippets,"active sentinel"); File.WriteAllText(settings,"settings sentinel");
            var previousBackup=Path.Combine(backups,"SlashDesk-backup-sentinel.zip"); File.WriteAllText(previousBackup,"previous backup");
            var service=new BackupService(backups,[snippets,settings],dataDirectory:active);
            var before=new[] { File.ReadAllBytes(snippets),File.ReadAllBytes(settings),File.ReadAllBytes(previousBackup) };
            Reject(() => service.RestoreSnapshot(forged), "rejection preserves active data");
            Reject(() => service.RestoreSnapshot(archive,canceled.Token), "cancel before restore");
            using var mid=new CancellationTokenSource(); service.BeforeRestoreApply=(step,_)=>{if(step==1)mid.Cancel();};
            Reject(() => service.RestoreSnapshot(archive,mid.Token), "cancel after first apply rolls back");
            Check(File.ReadAllBytes(snippets).SequenceEqual(before[0]) && File.ReadAllBytes(settings).SequenceEqual(before[1]) && File.ReadAllBytes(previousBackup).SequenceEqual(before[2]), "all active files and prior backup unchanged");
            Check(!Directory.EnumerateDirectories(backups,".restore-*").Any(), "staging and rollback cleaned");
            service.BeforeRestoreApply=null;
            WriteZip(forged,new(){["snippets.md"]=new string('x',4096)}); PatchLength(forged,"snippets.md",1,patchLocal:false);
            Reject(()=>service.RestoreSnapshot(forged),"actual expanded bytes exceed declared length in legacy extraction");
            Check(File.ReadAllText(snippets)=="active sentinel", "actual-byte rejection never replaces active file");

            var importer=new SnippetImportService(); var file=Path.Combine(root,"import.json");
            var content=new string('a',ExternalInputBudget.SnippetCharacters);
            File.WriteAllText(file,JsonSerializer.Serialize(new{folders=new[]{new{name="test",snippets=new[]{new{name="test",shortcut="/exact",text=content}}}}}));
            Check((await importer.ImportAsync(file,SnippetImportSource.TextBlaze)).Snippets.Single().Content.Length==content.Length,"exact per-snippet content accepted");
            File.WriteAllText(file,JsonSerializer.Serialize(new{folders=new[]{new{name="test",snippets=new[]{new{name="test",shortcut="/over",text=content+"x"}}}}}));
            await RejectAsync(()=>importer.ImportAsync(file,SnippetImportSource.TextBlaze),"over-limit content rejected");
            File.WriteAllText(file,"{"); await RejectAsync(()=>importer.ImportAsync(file,SnippetImportSource.TextBlaze),"malformed small JSON");
            await RejectAsync(()=>importer.ImportAsync(file,SnippetImportSource.TextBlaze,canceled.Token),"import canceled before parse");
            using(var sparse=new FileStream(file,FileMode.Create))sparse.SetLength(ExternalInputBudget.ImportBytes+1);
            foreach(var source in Enum.GetValues<SnippetImportSource>()) await RejectAsync(()=>importer.ImportAsync(file,source),"oversize external file "+source);

            var yaml=Path.Combine(root,"import.yml");
            File.WriteAllText(yaml,"matches:\n  - trigger: /large\n    replace: |\n      "+new string('b',512000)+"\n  - trigger: /second\n    replace: text");
            Check((await importer.ImportAsync(yaml,SnippetImportSource.Espanso)).Snippets.Count==2,"legitimate large YAML retained");
            File.WriteAllText(yaml,"matches:\n  - trigger: /over\n    replace: |\n      "+content+"x");
            await RejectAsync(()=>importer.ImportAsync(yaml,SnippetImportSource.Espanso),"oversize multiline block");
            Reject(()=>ExternalInputBudget.CheckText(new string('\r',ExternalInputBudget.TextLines),default),"CR-only line explosion before Split");
            var md=Path.Combine(root,"import.md"); var repo=new SnippetMarkdownRepository(md,Path.Combine(root,"md-backups"));
            await repo.SaveAsync([new Snippet{Name="legacy large",Trigger="/large",Category="test",Content=content+"x"}]);
            Check((await repo.LoadAsync()).Single().Content.Length==content.Length+1,"existing local repository remains readable");
            await RejectAsync(()=>importer.ImportAsync(md,SnippetImportSource.SlashDesk),"external markdown content budget");
            var text=new StringBuilder();
            for(var n=0;n<ExternalInputBudget.SnippetCount;n++)text.Append($"## /t{n}\n<!-- slashtext:{{\"name\":\"n\"}} -->\n```text\nx\n```\n");
            Check(SnippetMarkdownRepository.ParseImport(text.ToString(),default).Count==ExternalInputBudget.SnippetCount,"exact count accepted");
            text.Append("## /extra\n<!-- slashtext:{\"name\":\"n\"} -->\n```text\nx\n```\n");
            Reject(()=>SnippetMarkdownRepository.ParseImport(text.ToString(),default),"one extra external shortcut");

            // No huge bitmap allocation: test dimensions in small crafted headers first.
            ExternalImageReader.CheckDimensions(8000,8000); Check(true,"64 MP exact boundary");
            Reject(()=>ExternalImageReader.CheckDimensions(8001,8000),"over pixel budget");
            Reject(()=>ExternalImageReader.CheckDimensions(32001,1),"over side budget");
            var png=Path.Combine(root,"fixture.png");
            using(var image=new Bitmap(16,12)){image.Save(png,ImageFormat.Png); image.Save(Path.Combine(root,"fixture.jpg"),ImageFormat.Jpeg); image.Save(Path.Combine(root,"fixture.bmp"),ImageFormat.Bmp); image.Save(Path.Combine(root,"fixture.gif"),ImageFormat.Gif);}
            foreach(var ext in new[]{"png","jpg","bmp"}){using var loaded=ExternalImageReader.Load(Path.Combine(root,"fixture."+ext)); Check(loaded.Width==16&&loaded.Height==12,"ordinary image "+ext);}
            Reject(()=>ExternalImageReader.Load(Path.Combine(root,"fixture.gif")),"GIF stays outside still editor");
            bytes=File.ReadAllBytes(png); BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16),32000); BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20),32000); File.WriteAllBytes(png,bytes);
            Reject(()=>ExternalImageReader.Load(png),"huge declared image rejected before native decode");
            using(var imageFile=new FileStream(png,FileMode.Create))imageFile.SetLength(ExternalInputBudget.ImageBytes+1);
            Reject(()=>ExternalImageReader.Load(png),"encoded image bytes budget");
            File.WriteAllText(png,"tiny");Reject(()=>ExternalImageReader.Load(png),"small malformed image");
            await RejectAsync(()=>ExternalImageReader.DecodeAsync(()=>ExternalImageReader.Load(png),canceled.Token),"canceled image never reaches decoder");
            Console.WriteLine($"External input budgets: {_checks} checks passed.");
        }
        finally { AppPaths.Initialize(previous); }
    }
    private static void ExpectEarlyBudget(string path,string label)
    {
        try { BackupService.ValidateSnapshot(path); } catch(InvalidDataException e) when(e.Message==ExternalInputBudget.Exceeded().Message){_checks++;return;}
        throw new InvalidOperationException("No early budget rejection: "+label);
    }
    private static void WriteZip(string path,Dictionary<string,string> files,string manifest="{\"schemaVersion\":1}")
    {
        using var file=File.Create(path);using var zip=new ZipArchive(file,ZipArchiveMode.Create);
        foreach(var pair in files.Append(new KeyValuePair<string,string>("backup-manifest.json",manifest))){using var writer=new StreamWriter(zip.CreateEntry(pair.Key).Open());writer.Write(pair.Value);}
    }
    private static void PatchLength(string path,string name,uint length,bool patchLocal=true)
    {
        var bytes=File.ReadAllBytes(path);
        for(var n=0;n+46<bytes.Length;n++)
            if(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(n))==0x02014b50)
            {
                var count=BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(n+28));
                if(n+46+count<=bytes.Length&&Encoding.UTF8.GetString(bytes,n+46,count)==name)
                {
                    if(patchLocal) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(n+42))+22),length);
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(n+24),length);File.WriteAllBytes(path,bytes);return;
                }
            }
        throw new InvalidOperationException("Fixture central entry missing");
    }
}
