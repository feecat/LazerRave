using System.IO.Compression;

namespace LazerRave.Content;

public static class SongPackFiles
{
    public static async Task<string> Install(string zip, string applicationRoot, CancellationToken cancellation, IProgress<TransferProgress>? progress = null, string? title = null)
    {
        if (new FileInfo(zip).Length is <= 0 or > SongContent.MaxArchiveBytes)
            throw new InvalidDataException("The ZIP must be within the 128 MiB download limit.");
        LibraryFolders.Ensure(applicationRoot);
        var shared = LibraryFolders.Shared(applicationRoot);
        var hash = await SongContent.HashFile(zip, cancellation);
        var incoming = Path.Combine(shared, ".incoming");
        Directory.CreateDirectory(incoming);
        LibraryFolders.NoLink(incoming);
        var staging = Path.Combine(incoming, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using var archive = ZipFile.OpenRead(zip);
            if (archive.Entries.Count is 0 or > 10000) throw new InvalidDataException("Unsupported ZIP entry count.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                cancellation.ThrowIfCancellationRequested();
                var name = SongContent.SafePath(entry.FullName.Replace('\\', '/').TrimEnd('/'));
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & 0x400) != 0 || !names.Add(name))
                    throw new InvalidDataException("ZIP contains links or duplicate paths.");
                for (var parent = name.Contains('/') ? name[..name.LastIndexOf('/')] : ""; parent.Length > 0;
                     parent = parent.Contains('/') ? parent[..parent.LastIndexOf('/')] : "") directories.Add(parent);
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { directories.Add(name); continue; }
                files.Add(name);
                if (!SongContent.IsResource(name) || entry.Length > SongContent.MaxExpandedBytes || (SongContent.IsChart(name) && entry.Length > 5 * 1024 * 1024))
                    throw new InvalidDataException("ZIP contains an unsupported resource or exceeds the size limit.");
                total = checked(total + entry.Length);
                if (total > SongContent.MaxExpandedBytes) throw new InvalidDataException("Expanded ZIP exceeds 512 MiB.");
            }
            if (files.Overlaps(directories) || !files.Any(SongContent.IsChart))
                throw new InvalidDataException("ZIP must contain BMS charts without conflicting file paths.");
            long completed = 0;
            foreach (var entry in archive.Entries)
            {
                cancellation.ThrowIfCancellationRequested();
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
                var name = SongContent.SafePath(entry.FullName);
                var path = SongContent.Resolve(staging, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using var source = entry.Open();
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
                var buffer = new byte[65536]; long size = 0; int read; uint crc = uint.MaxValue;
                while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
                {
                    size += read;
                    if (size > entry.Length) throw new InvalidDataException("ZIP resource exceeds its declared size.");
                    for (int i = 0; i < read; i++) crc = crcTable[(crc ^ buffer[i]) & 255] ^ (crc >> 8);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    completed += read; progress?.Report(new("Installing", completed, total));
                }
                if (size != entry.Length || ~crc != entry.Crc32) throw new InvalidDataException("ZIP resource checksum is invalid.");
            }
            await installGate.WaitAsync(cancellation);
            try
            {
                var installed = ReadReceipt(applicationRoot, hash);
                var folder = FolderName(title ?? Path.GetFileNameWithoutExtension(zip));
                if (Directory.EnumerateFiles(staging).Any())
                {
                    if (installed is { Count: 1 }) folder = installed.Keys.Single();
                    var wrapped = Path.Combine(incoming, Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(wrapped);
                    Directory.Move(staging, Path.Combine(wrapped, folder));
                    staging = wrapped;
                }
                var children = Directory.GetDirectories(staging);
                var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var child in children)
                {
                    var name = Path.GetFileName(child);
                    if (name.StartsWith('.')) throw new InvalidDataException("ZIP cannot install into a reserved shared directory.");
                    if (installed is not null)
                    {
                        if (!installed.TryGetValue(name, out var target) || !await SameFiles(child, SongContent.Resolve(shared, target), cancellation))
                            throw new InvalidDataException("An installed pack was modified or is incomplete. Existing files were preserved.");
                        mapping.Add(name, target);
                    }
                    else mapping.Add(name, AvailableName(shared, name, mapping.Values));
                }
                if (installed is null) PublishFolders(applicationRoot, hash, staging, mapping, cancellation);
                return mapping.Count == 1 ? SongContent.Resolve(shared, mapping.Values.Single()) : shared;
            }
            finally { installGate.Release(); }
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                if (!Path.GetFullPath(staging).StartsWith(Path.GetFullPath(incoming) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Unsafe pack staging cleanup path.");
                LibraryFolders.NoLink(staging);
                Directory.Delete(staging, true);
            }
        }
    }

    private static readonly SemaphoreSlim installGate = new(1);
    private static string ReceiptPath(string root, string hash, bool create = false)
    {
        if (!SongContent.IsHash(hash)) throw new InvalidDataException("Invalid pack identity.");
        var userdata = Path.Combine(Path.GetFullPath(root), "userdata");
        var directory = Path.Combine(userdata, "song-packs");
        foreach (var path in new[] { userdata, directory })
        {
            if (Directory.Exists(path)) LibraryFolders.NoLink(path);
            if (create) Directory.CreateDirectory(path);
        }
        return Path.Combine(directory, hash + ".txt");
    }
    private static Dictionary<string, string>? ReadReceipt(string root, string hash)
    {
        var path = ReceiptPath(root, hash);
        if (!File.Exists(path)) return null;
        LibraryFolders.NoLink(path);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(path))
        {
            var parts = line.Split('\t');
            if (parts.Length != 2 || parts.Any(p => p.Contains('/') || p.Contains('\\') || SongContent.SafePath(p) != p || p.StartsWith('.')) || !result.TryAdd(parts[0], parts[1]))
                throw new InvalidDataException("Invalid installed pack record.");
        }
        return result.Count > 0 ? result : null;
    }
    public static string? InstalledDirectory(string applicationRoot, string hash)
    {
        var mapping = ReadReceipt(applicationRoot, hash);
        if (mapping is null) return null;
        var shared = LibraryFolders.Shared(applicationRoot);
        foreach (var name in mapping.Values)
        {
            var path = SongContent.Resolve(shared, name);
            if (!Directory.Exists(path)) return null;
            LibraryFolders.NoLink(path);
        }
        return mapping.Count == 1 ? SongContent.Resolve(shared, mapping.Values.Single()) : shared;
    }
    private static string FolderName(string title)
    {
        var name = System.Text.RegularExpressions.Regex.Replace(title, "[\\x00-\\x1f<>:\"/\\\\|?*]", "_").Trim(' ', '.');
        if (name.Length > 120) name = name[..120];
        try { if (name.Length > 0 && !name.StartsWith('.')) return SongContent.SafePath(name); }
        catch (InvalidDataException) { }
        return "Song pack";
    }
    private static string AvailableName(string shared, string name, IEnumerable<string>? reserved = null)
    {
        SongContent.SafePath(name);
        var target = name;
        for (var index = 2; Path.Exists(Path.Combine(shared, target)) || reserved?.Contains(target, StringComparer.OrdinalIgnoreCase) == true; index++) target = name + " (" + index + ")";
        return target;
    }
    private static async Task<bool> SameFiles(string source, string destination, CancellationToken cancellation)
    {
        if (!Directory.Exists(destination)) return false;
        LibraryFolders.NoLink(destination);
        foreach (var path in Directory.EnumerateFileSystemEntries(source, "*", SearchOption.AllDirectories))
        {
            LibraryFolders.NoLink(path);
            var relative = Path.GetRelativePath(source, path).Replace('\\', '/');
            var target = SongContent.Resolve(destination, relative);
            if (!Path.Exists(target)) return false;
            for (var parent = Path.GetDirectoryName(target); parent is not null && Path.GetFullPath(parent).StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); parent = Path.GetDirectoryName(parent))
                LibraryFolders.NoLink(parent);
            LibraryFolders.NoLink(target);
            if (File.Exists(path) && (!File.Exists(target) || await SongContent.HashFile(path, cancellation) != await SongContent.HashFile(target, cancellation))) return false;
        }
        return true;
    }
    private static void PublishFolders(string root, string hash, string staging, Dictionary<string, string> mapping, CancellationToken cancellation)
    {
        var shared = LibraryFolders.Shared(root);
        var moved = new List<(string Source, string Target)>();
        var receipt = ReceiptPath(root, hash, true);
        var temporary = receipt + "." + Guid.NewGuid().ToString("N") + ".pending";
        try
        {
            foreach (var (name, targetName) in mapping)
            {
                cancellation.ThrowIfCancellationRequested();
                var source = SongContent.Resolve(staging, name);
                var target = SongContent.Resolve(shared, targetName);
                Directory.Move(source, target);
                moved.Add((source, target));
            }
            File.WriteAllLines(temporary, mapping.Select(pair => pair.Key + "\t" + pair.Value));
            File.Move(temporary, receipt);
        }
        catch
        {
            foreach (var (source, target) in moved.AsEnumerable().Reverse()) Directory.Move(target, source);
            throw;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void MigrateLegacy(string root)
    {
        var shared = LibraryFolders.Shared(root);
        foreach (var source in Directory.GetDirectories(shared, "pack-*"))
        {
            var hash = Path.GetFileName(source)[5..];
            if (!SongContent.IsHash(hash) || File.Exists(ReceiptPath(root, hash))) continue;
            LibraryFolders.NoLink(source);
            foreach (var path in Directory.EnumerateFileSystemEntries(source, "*", SearchOption.AllDirectories)) LibraryFolders.NoLink(path);
            if (Directory.EnumerateFiles(source).Any())
            {
                var incoming = Path.Combine(shared, ".incoming");
                if (Directory.Exists(incoming)) LibraryFolders.NoLink(incoming);
                Directory.CreateDirectory(incoming);
                var wrapper = Path.Combine(incoming, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(wrapper);
                var wrapped = Path.Combine(wrapper, "Song pack");
                Directory.Move(source, wrapped);
                try { PublishFolders(root, hash, wrapper, new() { ["Song pack"] = AvailableName(shared, "Song pack") }, default); }
                catch { Directory.Move(wrapped, source); throw; }
                finally { Directory.Delete(wrapper, false); }
                continue;
            }
            var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in Directory.GetDirectories(source))
            {
                var name = Path.GetFileName(child);
                mapping.Add(name, AvailableName(shared, name, mapping.Values));
            }
            if (mapping.Count == 0 || mapping.Keys.Any(name => name.StartsWith('.'))) continue;
            PublishFolders(root, hash, source, mapping, default);
            Directory.Delete(source, false);
        }
    }

    private static readonly uint[] crcTable = Enumerable.Range(0, 256).Select(value =>
    {
        uint crc = (uint)value;
        for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
        return crc;
    }).ToArray();
}
