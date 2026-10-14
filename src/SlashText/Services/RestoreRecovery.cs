using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SlashText.Services;

internal sealed class RestoreRecoveryRequiredException : IOException
{
    internal RestoreRecoveryRequiredException() : base(UiText.Get(
        "A restauração precisa de recuperação. O material de recuperação disponível será mantido. Feche o SlashDesk e reabra para tentar novamente; não altere nem apague a pasta de backups.")) { }
}

internal sealed record RestoreRecoveryResult(int RecoveredCount, bool CleanupPending);

// A restore journal is private application state, never an entry accepted from an imported ZIP.
// Originals are copied and verified before the first mutation and retained until a terminal state.
internal sealed class RestoreRecovery : IDisposable
{
    private const string Prefix = ".restore-transaction-";
    private const string FinishedPrefix = ".restore-finished-";
    private const string IdentityFile = ".restore-layout-id";
    private const string PendingFile = ".restore-pending-id";
    private const long JournalLimit = 8L * 1024 * 1024;
    private static readonly ConcurrentDictionary<string, byte> ProtectedRoots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private readonly string _data;
    private readonly string _backups;
    private readonly Dictionary<string, string> _targets;
    private readonly LocalFileBoundary _dataBoundary;
    private readonly LocalFileBoundary _backupBoundary;
    private readonly FileStream _lease;
    private string _folder;
    private Journal _journal;
    private bool _disposed;

    internal string Staging => Path.Combine(_folder, "staging");
    private string JournalPath => Path.Combine(_folder, "journal.json");
    private string Originals => Path.Combine(_folder, "originals");
    internal Action<string, int>? Checkpoint { get; set; }

    private RestoreRecovery(string data, string backups, string assets, string emotes, string folder,
        Journal journal, LocalFileBoundary dataBoundary, LocalFileBoundary backupBoundary, FileStream lease)
    {
        _data = Normalize(data); _backups = Normalize(backups); _folder = folder; _journal = journal;
        _targets = Targets(_data, assets, emotes);
        if (_targets.Where(p => p.Key is "assets" or "capture-emotes").Any(p => _backups.Equals(p.Value, StringComparison.OrdinalIgnoreCase) ||
            _backups.StartsWith(p.Value + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException();
        _dataBoundary = dataBoundary; _backupBoundary = backupBoundary; _lease = lease;
    }

    internal static void ThrowIfProtected(string path)
    {
        var full = Path.GetFullPath(path);
        if (ProtectedRoots.Keys.Any(root => full.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            throw new RestoreRecoveryRequiredException();
    }

    internal static RestoreRecovery Begin(string data, string backups, string assets, string emotes)
    {
        data = Normalize(data); backups = Normalize(backups);
        var targets = Targets(data, assets, emotes);
        if (targets.Where(p => p.Key is "assets" or "capture-emotes").Any(p => backups.Equals(p.Value, StringComparison.OrdinalIgnoreCase) ||
            backups.StartsWith(p.Value + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException();
        ThrowIfProtected(data);
        RecoverPending(data, backups, assets, emotes);
        var dataBoundary = LocalFileBoundary.PinDirectory(data, create: true);
        LocalFileBoundary? backupBoundary = null; FileStream? lease = null;
        try
        {
            backupBoundary = LocalFileBoundary.PinDirectory(backups, create: true);
            lease = LocalFileBoundary.OpenFile(Path.Combine(backups, ".restore-recovery.lock"), create: true, exclusive: true);
            // Recheck under the interprocess lease: another service may have started after the first scan.
            if (PendingFolders(backups).Any() || LegacyFolders(backups).Any()) throw new RestoreRecoveryRequiredException();
            var identity = ReadIdentity(data, create: true);
            var id = Guid.NewGuid().ToString("N"); var folder = Path.Combine(backups, Prefix + id);
            Directory.CreateDirectory(folder);
            var journal = new Journal { Id = id, LayoutId = identity, Phase = "Preparing" };
            var transaction = new RestoreRecovery(data, backups, assets, emotes, folder, journal, dataBoundary, backupBoundary, lease);
            transaction.SaveJournal();
            return transaction;
        }
        catch { lease?.Dispose(); backupBoundary?.Dispose(); dataBoundary.Dispose(); throw; }
    }

    internal static RestoreRecoveryResult RecoverPending(string data, string? backups = null, string? assets = null, string? emotes = null)
    {
        data = Normalize(data); backups ??= Path.Combine(data, "Backups");
        try
        {
            if (ReadNode(backups) is null)
            {
                if (ReadNode(Path.Combine(data, PendingFile)) is not null || ProtectedRoots.ContainsKey(data)) { ProtectedRoots[data] = 0; throw new RestoreRecoveryRequiredException(); }
                return new(0, false);
            }
            var folders = PendingFolders(backups).Concat(FinishedFolders(backups)).ToList(); var legacy = LegacyFolders(backups).Any();
            if (folders.Count == 0 && !legacy)
            {
                if (ReadNode(Path.Combine(data, PendingFile)) is not null || ProtectedRoots.ContainsKey(data)) { ProtectedRoots[data] = 0; throw new RestoreRecoveryRequiredException(); }
                return new(0, false);
            }
            if (legacy || folders.Count > 16) throw new InvalidDataException();
            using var dataBoundary = LocalFileBoundary.PinDirectory(data);
            using var backupBoundary = LocalFileBoundary.PinDirectory(backups);
            using var lease = LocalFileBoundary.OpenFile(Path.Combine(backups, ".restore-recovery.lock"), create: true, exclusive: true);
            var identity = ReadIdentity(data, create: false);
            var recovered = 0; var cleanupPending = false;
            // Only one unfinished transaction is supported; conflicting records are never replayed blindly.
            // A finished name is published only after a durable terminal/no-mutation phase.
            // Partial cleanup may remove its journal; preserve such leftovers without replaying them.
            var loaded = folders.Where(folder => !Path.GetFileName(folder).StartsWith(FinishedPrefix, StringComparison.Ordinal) ||
                File.Exists(Path.Combine(folder, "journal.json"))).Select(folder => (Folder: folder, State: ReadJournal(folder, identity))).ToList();
            var pendingId = ReadPending(data);
            if (pendingId is not null && !loaded.Any(pair => pair.State.Id == pendingId && pair.State.Phase is "Applying" or "Recovering" or "Committed" or "Recovered")) throw new InvalidDataException();
            if (loaded.Count(pair => pair.State.Phase is "Applying" or "Recovering") > 1) throw new InvalidDataException();
            foreach (var pair in loaded)
            {
                var transaction = new RestoreRecovery(data, backups, assets ?? Path.Combine(data, "assets"),
                    emotes ?? Path.Combine(data, "capture-emotes"), pair.Folder, pair.State, dataBoundary, backupBoundary, lease);
                if (pair.State.Phase is "Applying" or "Recovering") { transaction.Rollback(); recovered++; }
                else if (pair.State.Phase is "Committed" or "Recovered") transaction.FinishTerminal();
                if (!transaction.TryCleanup()) cleanupPending = true;
            }
            ProtectedRoots.TryRemove(data, out _);
            return new(recovered, cleanupPending);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            ProtectedRoots[data] = 0;
            throw new RestoreRecoveryRequiredException();
        }
    }

    internal void Prepare(IEnumerable<string> files, bool assets, bool emotes, CancellationToken token)
    {
        var labels = files.Select(file => file.ToLowerInvariant()).ToList(); if (assets) labels.Add("assets"); if (emotes) labels.Add("capture-emotes");
        if (labels.Count == 0 || labels.Count > 6 || labels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != labels.Count)
            throw new InvalidDataException();
        foreach (var label in labels)
        {
            token.ThrowIfCancellationRequested();
            var target = Target(label); var directory = label is "assets" or "capture-emotes";
            if (directory) Directory.CreateDirectory(Path.Combine(Staging, label));
            var original = Scan(target, directory); var replacement = Scan(Path.Combine(Staging, label), directory);
            if (!replacement.Exists) throw new InvalidDataException();
            CopySnapshot(target, Path.Combine(Originals, label), original, token);
            if (!Matches(Path.Combine(Originals, label), original)) throw new IOException();
            _journal.Items.Add(new Item { Label = label, Original = original, Replacement = replacement });
        }
        // Original snapshots are durable; prepared phase still authorizes no application mutations.
        _journal.Phase = "Prepared"; SaveJournal(); Checkpoint?.Invoke("prepared", -1);
    }

    internal void Apply(Action<int, string>? before, CancellationToken token)
    {
        _journal.Phase = "Applying"; SaveJournal();
        WriteDurable(Path.Combine(_data, PendingFile), Encoding.ASCII.GetBytes(_journal.Id));
        ProtectedRoots[_data] = 0;
        for (var index = 0; index < _journal.Items.Count; index++)
        {
            token.ThrowIfCancellationRequested(); var item = _journal.Items[index];
            before?.Invoke(index, item.Original.Directory ? item.Label + "/" : item.Label);
            token.ThrowIfCancellationRequested();
            if (!Matches(Target(item.Label), item.Original) || !Matches(Path.Combine(Staging, item.Label), item.Replacement)) throw new IOException();
            _journal.StartedIndex = index; SaveJournal(); Checkpoint?.Invoke("before-apply", index);
            var target = Target(item.Label); var staged = Path.Combine(Staging, item.Label);
            if (item.Original.Directory)
            {
                if (Directory.Exists(target))
                {
                    var detached = Path.Combine(_folder, "detached", item.Label); Directory.CreateDirectory(Path.GetDirectoryName(detached)!);
                    Directory.Move(target, detached); Checkpoint?.Invoke("directory-detached", index);
                }
                Directory.Move(staged, target);
            }
            else if (File.Exists(target)) File.Replace(staged, target, null, ignoreMetadataErrors: true);
            else File.Move(staged, target);
            Checkpoint?.Invoke("after-apply", index);
        }
        token.ThrowIfCancellationRequested();
        foreach (var item in _journal.Items)
            if (!Matches(Target(item.Label), item.Replacement)) throw new IOException();
        Checkpoint?.Invoke("before-commit", _journal.StartedIndex);
        _journal.Phase = "Committed";
        try { SaveJournal(); }
        catch
        {
            // An in-memory phase is not a durable commit. Retain recovery on a failed write.
            _journal.Phase = "Applying";
            throw;
        }
        Checkpoint?.Invoke("committed", _journal.StartedIndex);
        FinishTerminal();
        // Cleanup errors do not turn a confirmed commit into a failed restore or destroy its journal.
        TryCleanup();
    }

    internal void Abort()
    {
        try
        {
            if (_journal.Phase is "Applying" or "Recovering") Rollback();
            else if (_journal.Phase is "Committed" or "Recovered") FinishTerminal();
            TryCleanup();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        { ProtectedRoots[_data] = 0; throw new RestoreRecoveryRequiredException(); }
    }

    private void Rollback()
    {
        // Validate every required original before changing any active data, including empty directories.
        foreach (var item in _journal.Items.Take(_journal.StartedIndex + 1))
            if (!Matches(Path.Combine(Originals, item.Label), item.Original)) throw new InvalidDataException();
        _journal.Phase = "Recovering"; SaveJournal();
        for (var index = _journal.StartedIndex; index >= 0; index--)
        {
            var item = _journal.Items[index]; var target = Target(item.Label);
            if (Matches(target, item.Original)) continue;
            if ((File.Exists(target) || Directory.Exists(target)) && !Matches(target, item.Replacement)) throw new IOException();
            Checkpoint?.Invoke("before-rollback", index);
            if (!item.Original.Exists)
            {
                if (item.Original.Directory) DeleteTree(target);
                else if (File.Exists(target)) File.Delete(target);
            }
            else
            {
                var temporary = Path.Combine(_folder, "recover", item.Label);
                // Retry replaces only its owned scratch copy; originals are never the cleanup target.
                if (ReadNode(temporary) is { } attributes)
                {
                    if (attributes.HasFlag(FileAttributes.Directory)) DeleteTree(temporary);
                    else
                    {
                        EnsureOrdinary(temporary, false);
                        using var parent = LocalFileBoundary.PinDirectory(Path.GetDirectoryName(temporary)!);
                        File.Delete(temporary);
                    }
                }
                CopySnapshot(Path.Combine(Originals, item.Label), temporary, item.Original, CancellationToken.None);
                if (!Matches(temporary, item.Original)) throw new IOException();
                if (item.Original.Directory)
                {
                    if (Directory.Exists(target))
                    {
                        var displaced = Path.Combine(_folder, "displaced", Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(Path.GetDirectoryName(displaced)!); Directory.Move(target, displaced);
                        Checkpoint?.Invoke("rollback-directory-detached", index);
                    }
                    Directory.Move(temporary, target);
                }
                else if (File.Exists(target)) File.Replace(temporary, target, null, ignoreMetadataErrors: true);
                else File.Move(temporary, target);
            }
            Checkpoint?.Invoke("after-rollback", index);
            if (!Matches(target, item.Original)) throw new IOException();
        }
        _journal.Phase = "Recovered"; SaveJournal();
        FinishTerminal();
    }

    private void FinishTerminal()
    {
        var pending = ReadPending(_data);
        if (pending is not null)
        {
            if (pending != _journal.Id) throw new InvalidDataException();
            File.Delete(Path.Combine(_data, PendingFile));
        }
        ProtectedRoots.TryRemove(_data, out _);
    }

    private static string? ReadPending(string data)
    {
        var path = Path.Combine(data, PendingFile);
        if (ReadNode(path) is null) return null;
        EnsureOrdinary(path, false);
        using var input = LocalFileBoundary.OpenFile(path, writable: false, denyWrites: true);
        if (input.Length != 32) throw new InvalidDataException();
        var bytes = new byte[32]; input.ReadExactly(bytes); var id = Encoding.ASCII.GetString(bytes);
        return Guid.TryParseExact(id, "N", out _) ? id : throw new InvalidDataException();
    }

    private bool TryCleanup()
    {
        try
        {
            if (_journal.Phase is "Applying" or "Recovering") throw new IOException();
            if (Path.GetFileName(_folder).StartsWith(Prefix, StringComparison.Ordinal))
            {
                var finished = Path.Combine(_backups, FinishedPrefix + _journal.Id);
                Directory.Move(_folder, finished); _folder = finished;
            }
            DeleteTree(_folder); return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    private void SaveJournal()
    {
        var text = JsonSerializer.Serialize(_journal, Options);
        if (Encoding.UTF8.GetByteCount(text) > JournalLimit) throw new InvalidDataException();
        WriteDurable(JournalPath, Encoding.UTF8.GetBytes(text));
    }

    private static Journal ReadJournal(string folder, string identity)
    {
        EnsureOrdinary(folder, true);
        using var input = LocalFileBoundary.OpenFile(Path.Combine(folder, "journal.json"), writable: false, denyWrites: true);
        if (input.Length is < 1 or > JournalLimit) throw new InvalidDataException();
        var journal = JsonSerializer.Deserialize<Journal>(input, Options) ?? throw new InvalidDataException();
        if (journal.Version != 1 || journal.LayoutId != identity ||
            (Path.GetFileName(folder) != Prefix + journal.Id && Path.GetFileName(folder) != FinishedPrefix + journal.Id) ||
            !Guid.TryParseExact(journal.Id, "N", out _) || journal.Phase is not ("Preparing" or "Prepared" or "Applying" or "Recovering" or "Committed" or "Recovered") ||
            journal.Items is null || journal.Items.Count > 6 || journal.Items.Any(item => item is null) || journal.StartedIndex < -1 || journal.StartedIndex >= journal.Items.Count ||
            journal.Items.Select(i => i.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() != journal.Items.Count)
            throw new InvalidDataException();
        foreach (var item in journal.Items)
        {
            if (item.Label is not ("snippets.md" or "settings.json" or "usage.json" or "capture-history.json" or "assets" or "capture-emotes")) throw new InvalidDataException();
            ValidateSnapshot(item.Original, item.Label); ValidateSnapshot(item.Replacement, item.Label);
        }
        if ((journal.Phase is "Preparing" or "Prepared") && journal.StartedIndex != -1) throw new InvalidDataException();
        if (Path.GetFileName(folder).StartsWith(FinishedPrefix, StringComparison.Ordinal) && journal.Phase is "Applying" or "Recovering") throw new InvalidDataException();
        if (journal.Phase == "Committed" && journal.StartedIndex != journal.Items.Count - 1) throw new InvalidDataException();
        return journal;
    }

    private static void ValidateSnapshot(Snapshot snapshot, string label)
    {
        if (snapshot is null || snapshot.Directory != (label is "assets" or "capture-emotes") || snapshot.Files is null || snapshot.Directories is null ||
            snapshot.Files.Count > BackupInputBudget.Entries || snapshot.Directories.Count > BackupInputBudget.Entries ||
            (!snapshot.Exists && (snapshot.Files.Count != 0 || snapshot.Directories.Count != 0))) throw new InvalidDataException();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var file in snapshot.Files)
        {
            if (file is null || file.Relative is null || (snapshot.Directory ? !SafeRelative(file.Relative) : file.Relative != "") || !paths.Add(file.Relative) ||
                file.Size < 0 || file.Size > BackupInputBudget.EntryBytes || total > BackupInputBudget.TotalBytes - file.Size ||
                file.Hash is null || file.Hash.Length != 64 || !file.Hash.All(Uri.IsHexDigit)) throw new InvalidDataException();
            total += file.Size;
        }
        if (!snapshot.Directory && snapshot.Exists && snapshot.Files.Count != 1) throw new InvalidDataException();
        if (snapshot.Directories.Any(dir => !SafeRelative(dir)) || snapshot.Directories.Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.Directories.Count) throw new InvalidDataException();
    }

    private static Snapshot Scan(string path, bool directory)
    {
        // File.Exists hides access errors; an unreadable original is never treated as absent.
        try { _ = File.GetAttributes(path); }
        catch (FileNotFoundException) { return new() { Directory = directory }; }
        catch (DirectoryNotFoundException) { return new() { Directory = directory }; }
        EnsureOrdinary(path, directory);
        var result = new Snapshot { Exists = true, Directory = directory }; long total = 0;
        if (directory)
        {
            var pending = new Stack<string>(); pending.Push(path);
            while (pending.TryPop(out var current))
            {
                using var currentBoundary = LocalFileBoundary.PinDirectory(current);
                foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                {
                    var isDirectory = File.GetAttributes(entry).HasFlag(FileAttributes.Directory); EnsureOrdinary(entry, isDirectory);
                    var relative = Path.GetRelativePath(path, entry); if (!SafeRelative(relative)) throw new InvalidDataException();
                    if (isDirectory) { result.Directories.Add(relative); pending.Push(entry); }
                    else Add(entry, relative);
                    if (result.Files.Count > BackupInputBudget.Entries || result.Directories.Count > BackupInputBudget.Entries) throw new InvalidDataException();
                }
            }
        }
        else Add(path, "");
        return result;
        void Add(string file, string relative)
        {
            using var parentBoundary = LocalFileBoundary.PinDirectory(Path.GetDirectoryName(file)!);
            using var input = LocalFileBoundary.OpenFile(file, writable: false, denyWrites: true);
            if (input.Length > BackupInputBudget.EntryBytes || total > BackupInputBudget.TotalBytes - input.Length) throw new InvalidDataException();
            total += input.Length;
            result.Files.Add(new StoredFile { Relative = relative, Size = input.Length, Hash = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant() });
        }
    }

    private static bool Matches(string path, Snapshot expected)
    {
        var actual = Scan(path, expected.Directory);
        if (actual.Exists != expected.Exists || actual.Files.Count != expected.Files.Count || actual.Directories.Count != expected.Directories.Count) return false;
        return expected.Directories.All(d => actual.Directories.Contains(d, StringComparer.OrdinalIgnoreCase)) &&
            expected.Files.All(f => actual.Files.Any(a => a.Relative.Equals(f.Relative, StringComparison.OrdinalIgnoreCase) && a.Size == f.Size && a.Hash.Equals(f.Hash, StringComparison.OrdinalIgnoreCase)));
    }

    private static void CopySnapshot(string source, string destination, Snapshot snapshot, CancellationToken token)
    {
        if (!snapshot.Exists) return;
        if (snapshot.Directory)
        {
            Directory.CreateDirectory(destination);
            foreach (var directory in snapshot.Directories) Directory.CreateDirectory(Path.Combine(destination, directory));
        }
        foreach (var file in snapshot.Files)
        {
            token.ThrowIfCancellationRequested();
            var from = snapshot.Directory ? Path.Combine(source, file.Relative) : source;
            var to = snapshot.Directory ? Path.Combine(destination, file.Relative) : destination;
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            using var inputBoundary = LocalFileBoundary.PinDirectory(Path.GetDirectoryName(from)!);
            using var outputBoundary = LocalFileBoundary.PinDirectory(Path.GetDirectoryName(to)!);
            using var input = LocalFileBoundary.OpenFile(from, writable: false, denyWrites: true);
            using var output = LocalFileBoundary.OpenFile(to, create: true, exclusive: true);
            if (input.Length != file.Size) throw new IOException();
            var buffer = new byte[81920]; long bytes = 0; int read;
            while ((read = input.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); bytes += read; if (bytes > file.Size) throw new IOException(); output.Write(buffer, 0, read); }
            output.Flush(flushToDisk: true);
            if (bytes != file.Size) throw new IOException();
        }
    }

    private static string ReadIdentity(string data, bool create)
    {
        var path = Path.Combine(data, IdentityFile);
        if (ReadNode(path) is null && create) WriteDurable(path, Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("N")));
        using var input = LocalFileBoundary.OpenFile(path, writable: false, denyWrites: true);
        if (input.Length != 32) throw new InvalidDataException();
        var bytes = new byte[32]; input.ReadExactly(bytes); var id = Encoding.ASCII.GetString(bytes);
        return Guid.TryParseExact(id, "N", out _) ? id : throw new InvalidDataException();
    }

    private static void WriteDurable(string path, byte[] bytes)
    {
        var parent = Path.GetDirectoryName(path)!;
        using var boundary = LocalFileBoundary.PinDirectory(parent);
        var temporary = Path.Combine(parent, ".restore-write-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { output.Write(bytes); output.Flush(flushToDisk: true); }
            if (File.Exists(path)) { EnsureOrdinary(path, false); File.Replace(temporary, path, null, ignoreMetadataErrors: true); }
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private string Target(string label) => _targets.TryGetValue(label, out var target) ? target : throw new InvalidDataException();
    private static Dictionary<string, string> Targets(string data, string assets, string emotes)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in new[] { "snippets.md", "settings.json", "usage.json", "capture-history.json" }) result.Add(file, Path.Combine(data, file));
        result.Add("assets", Normalize(assets)); result.Add("capture-emotes", Normalize(emotes));
        if (result.Values.Any(target => !target.StartsWith(data + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException();
        if (result.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count || result["assets"].StartsWith(result["capture-emotes"] + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            result["capture-emotes"].StartsWith(result["assets"] + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
        return result;
    }
    private static FileAttributes? ReadNode(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    private static bool SafeRelative(string path) => !string.IsNullOrEmpty(path) && !Path.IsPathRooted(path) && !path.Contains(':') &&
        path.Split(['\\', '/']).All(part => part is not ("" or "." or "..") && !part.EndsWith('.') && !part.EndsWith(' '));
    private static void EnsureOrdinary(string path, bool directory)
    {
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReparsePoint) || attributes.HasFlag(FileAttributes.Directory) != directory) throw new IOException();
    }
    private static IEnumerable<string> PendingFolders(string backups) => Directory.EnumerateDirectories(backups, Prefix + "*").Where(folder => Guid.TryParseExact(Path.GetFileName(folder)[Prefix.Length..], "N", out _));
    private static IEnumerable<string> FinishedFolders(string backups) => Directory.EnumerateDirectories(backups, FinishedPrefix + "*").Where(folder => Guid.TryParseExact(Path.GetFileName(folder)[FinishedPrefix.Length..], "N", out _));
    private static IEnumerable<string> LegacyFolders(string backups) => Directory.EnumerateDirectories(backups, ".restore-*").Where(folder =>
        Path.GetFileName(folder).StartsWith(".restore-staging-", StringComparison.Ordinal) || Path.GetFileName(folder).StartsWith(".restore-rollback-", StringComparison.Ordinal));

    // The checked roots are either a single owned transaction or a whitelisted active directory.
    // Reject links throughout before recursive deletion and keep the checked parent pinned.
    private void DeleteTree(string path)
    {
        if (!Directory.Exists(path)) return;
        var full = Normalize(path);
        if (!(full == _folder || full.StartsWith(_folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            _targets.Where(p => p.Key is "assets" or "capture-emotes").Any(p => p.Value.Equals(full, StringComparison.OrdinalIgnoreCase)))) throw new IOException();
        using var parent = LocalFileBoundary.PinDirectory(Path.GetDirectoryName(full)!);
        DeleteChecked(full);
        static void DeleteChecked(string directory)
        {
            EnsureOrdinary(directory, true);
            using (var boundary = LocalFileBoundary.PinDirectory(directory))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    var childDirectory = File.GetAttributes(entry).HasFlag(FileAttributes.Directory);
                    EnsureOrdinary(entry, childDirectory);
                    if (childDirectory) DeleteChecked(entry); else File.Delete(entry);
                }
            }
            Directory.Delete(directory, recursive: false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _lease.Dispose(); _backupBoundary.Dispose(); _dataBoundary.Dispose();
    }

    internal sealed class Journal
    {
        public int Version { get; set; } = 1;
        public string Id { get; set; } = "";
        public string LayoutId { get; set; } = "";
        public string Phase { get; set; } = "Preparing";
        public int StartedIndex { get; set; } = -1;
        public List<Item> Items { get; set; } = [];
    }
    internal sealed class Item
    {
        public string Label { get; set; } = "";
        public Snapshot Original { get; set; } = new();
        public Snapshot Replacement { get; set; } = new();
    }
    internal sealed class Snapshot
    {
        public bool Exists { get; set; }
        public bool Directory { get; set; }
        public List<StoredFile> Files { get; set; } = [];
        public List<string> Directories { get; set; } = [];
    }
    internal sealed class StoredFile
    {
        public string Relative { get; set; } = "";
        public long Size { get; set; }
        public string Hash { get; set; } = "";
    }
}
