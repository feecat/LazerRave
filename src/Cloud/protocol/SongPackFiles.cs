using System.IO.Compression;

namespace LazerRave.Content;

public static class SongPackFiles
{
    public static async Task<string> Install(string zip, string applicationRoot, CancellationToken cancellation, IProgress<TransferProgress>? progress = null)
    {
        if (new FileInfo(zip).Length is <= 0 or > SongContent.MaxArchiveBytes)
            throw new InvalidDataException("The ZIP must be within the 128 MiB download limit.");
        LibraryFolders.Ensure(applicationRoot);
        var shared = LibraryFolders.Shared(applicationRoot);
        var destination = Path.Combine(shared, "pack-" + await SongContent.HashFile(zip, cancellation));
        if (Directory.Exists(destination)) LibraryFolders.NoLink(destination);
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
            if (Directory.Exists(destination))
            {
                foreach (var name in files)
                {
                    var existing = SongContent.Resolve(destination, name);
                    for (var parent = Path.GetDirectoryName(existing); parent is not null && parent.StartsWith(destination, StringComparison.OrdinalIgnoreCase); parent = Path.GetDirectoryName(parent))
                        LibraryFolders.NoLink(parent);
                    if (!File.Exists(existing)) throw new InvalidDataException("An installed pack is incomplete. Restore it before importing again.");
                    LibraryFolders.NoLink(existing);
                    if (await SongContent.HashFile(existing, cancellation) != await SongContent.HashFile(SongContent.Resolve(staging, name), cancellation))
                        throw new InvalidDataException("An installed pack was modified. Existing files were preserved.");
                }
            }
            else Directory.Move(staging, destination);
            return destination;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    private static readonly uint[] crcTable = Enumerable.Range(0, 256).Select(value =>
    {
        uint crc = (uint)value;
        for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
        return crc;
    }).ToArray();
}
