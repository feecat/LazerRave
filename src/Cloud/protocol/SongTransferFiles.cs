using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace LazerRave.Content;

public sealed record TransferProgress(string Stage, long Completed, long Total, double BytesPerSecond = 0)
{
    public double Fraction => Total > 0 ? Math.Clamp((double)Completed / Total, 0, 1) : 0;
}

public static class SongTransferFiles
{
    private static void NoLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked song resources cannot be shared.");
    }
    private static IEnumerable<string> Files(string directory)
    {
        NoLink(directory);
        foreach (var file in Directory.EnumerateFiles(directory)) { NoLink(file); if (SongContent.IsResource(file)) yield return file; }
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            NoLink(child);
            foreach (var file in Files(child)) yield return file;
        }
    }
    public static async Task<SongManifest> Inspect(string chartPath, CancellationToken cancellation, IProgress<TransferProgress>? progress = null, string encoding = "auto")
    {
        chartPath = Path.GetFullPath(chartPath);
        if (!SongContent.IsChart(chartPath)) throw new InvalidDataException("Select a BMS chart to share.");
        var root = Path.GetDirectoryName(chartPath)!;
        var paths = Files(root).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length is 0 or > 10000) throw new InvalidDataException("A song may contain at most 10,000 resource files.");
        var total = paths.Sum(p => new FileInfo(p).Length);
        if (total > SongContent.MaxExpandedBytes) throw new InvalidDataException("The song exceeds the 512 MiB expanded limit.");
        var files = new List<SongFile>(); long done = 0;
        foreach (var path in paths)
        {
            cancellation.ThrowIfCancellationRequested();
            var relative = SongContent.SafePath(Path.GetRelativePath(root, path));
            var size = new FileInfo(path).Length;
            if (SongContent.IsChart(relative) && size > 5 * 1024 * 1024) throw new InvalidDataException("A BMS chart exceeds the 5 MiB limit.");
            files.Add(new(relative, size, await SongContent.HashFile(path, cancellation)));
            done += size; progress?.Report(new("Checking resources", done, total));
        }
        var selected = files.Single(f => f.Path == Path.GetFileName(chartPath));
        var manifest = new SongManifest(1, selected.Path, selected.Sha256, SongContent.Identity(files), files.ToArray());
        SongContent.Validate(manifest);
        return manifest;
    }
    public static async Task<string> Pack(string chartPath, SongManifest manifest, string cache, CancellationToken cancellation, IProgress<TransferProgress>? progress = null)
    {
        SongContent.Validate(manifest);
        Directory.CreateDirectory(cache); NoLink(cache);
        var destination = Path.Combine(cache, manifest.ContentSha256 + ".zip");
        if (File.Exists(destination)) return destination;
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        long done = 0, total = manifest.Files.Sum(f => f.Size);
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                using var archive = new ZipArchive(output, ZipArchiveMode.Create, true, Encoding.UTF8);
                foreach (var file in manifest.Files)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var path = SongContent.Resolve(Path.GetDirectoryName(chartPath)!, file.Path); NoLink(path);
                    await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
                    if (source.Length != file.Size) throw new InvalidDataException("Song resources changed. Select the song again.");
                    var entry = archive.CreateEntry(file.Path, CompressionLevel.SmallestSize);
                    entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    await using var target = entry.Open();
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[65536]; int read;
                    while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
                    {
                        hash.AppendData(buffer, 0, read); await target.WriteAsync(buffer.AsMemory(0, read), cancellation);
                        done += read; progress?.Report(new("Compressing", done, total));
                    }
                    if (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != file.Sha256) throw new InvalidDataException("Song resources changed during compression.");
                    if (output.Length > SongContent.MaxArchiveBytes) throw new InvalidDataException("Compressed song exceeds the 128 MiB upload limit.");
                }
            }
            if (new FileInfo(temporary).Length > SongContent.MaxArchiveBytes) throw new InvalidDataException("Compressed song exceeds the 128 MiB upload limit.");
            File.Move(temporary, destination, true); return destination;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static async Task<string?> Find(SongManifest manifest, IEnumerable<string> songDirectories, CancellationToken cancellation)
    {
        SongContent.Validate(manifest);
        foreach (var root in songDirectories.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellation.ThrowIfCancellationRequested();
            bool matched = true;
            foreach (var file in manifest.Files)
            {
                var path = SongContent.Resolve(root, file.Path);
                if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length != file.Size || await SongContent.HashFile(path, cancellation) != file.Sha256)
                { matched = false; break; }
            }
            if (matched) return SongContent.Resolve(root, manifest.ChartPath);
        }
        return null;
    }
    public static async Task<string> Install(string zip, SongManifest manifest, string shared, CancellationToken cancellation, IProgress<TransferProgress>? progress = null)
    {
        SongContent.Validate(manifest);
        Directory.CreateDirectory(shared); NoLink(shared);
        var destination = Path.Combine(shared, manifest.ContentSha256);
        if (Directory.Exists(destination))
            return await Find(manifest, new[] { destination }, cancellation) ?? throw new InvalidDataException("The existing Shared song is damaged. Remove it before downloading again.");
        var incoming = Path.Combine(shared, ".incoming");
        Directory.CreateDirectory(incoming); NoLink(incoming);
        var staging = Path.Combine(incoming, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var expected = manifest.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var checkedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long completed = 0, total = manifest.Files.Sum(f => f.Size);
        try
        {
            using var archive = ZipFile.OpenRead(zip);
            if (archive.Entries.Count is 0 or > 10000) throw new InvalidDataException("Unsupported ZIP entry count.");
            foreach (var entry in archive.Entries)
            {
                cancellation.ThrowIfCancellationRequested();
                var relative = SongContent.SafePath(entry.FullName.TrimEnd('/'));
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & 0x400) != 0)
                    throw new InvalidDataException("Linked ZIP resources are not accepted.");
                if (entry.FullName.EndsWith('/')) continue;
                if (!expected.TryGetValue(relative, out var file) || file.Size != entry.Length || !checkedFiles.Add(relative)) throw new InvalidDataException("ZIP does not match the song manifest.");
                var path = SongContent.Resolve(staging, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using var source = entry.Open();
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[65536]; int read; long size = 0;
                while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
                {
                    size += read; if (size > file.Size) throw new InvalidDataException("Expanded ZIP exceeds declared size.");
                    hash.AppendData(buffer, 0, read); await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    completed += read; progress?.Report(new("Installing", completed, total));
                }
                if (size != file.Size || Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() != file.Sha256) throw new InvalidDataException("Downloaded resource checksum is invalid.");
            }
            if (checkedFiles.Count != expected.Count) throw new InvalidDataException("ZIP is missing song resources.");
            Directory.Move(staging, destination);
            return SongContent.Resolve(destination, manifest.ChartPath);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
