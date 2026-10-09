using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LazerRave.Content;

public sealed record SongFile(string Path, long Size, string Sha256);
public sealed record SongManifest(int Version, string ChartPath, string ChartSha256, string ContentSha256, SongFile[] Files);

public static class SongContent
{
    public const long MaxArchiveBytes = 128L * 1024 * 1024;
    public const long MaxExpandedBytes = 512L * 1024 * 1024;
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);
    public const int ChunkBytes = 8 * 1024 * 1024;
    public static bool IsHash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static bool IsChart(string path) => new[] { ".bms", ".bme", ".bml", ".pms" }.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());
    public static bool IsResource(string path) => IsChart(path) || new[] { ".wav", ".ogg", ".mp3", ".flac", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".avi", ".mpg", ".mpeg", ".mp4", ".wmv", ".webm", ".txt" }.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

    public static string SafePath(string value)
    {
        var normalized = value.Replace('\\', '/');
        if (normalized.Length is 0 or > 240 || normalized.StartsWith('/') || normalized.EndsWith('/') ||
            normalized.Split('/').Any(s => s.Length == 0 || s is "." or ".." || s.EndsWith('.') || s.EndsWith(' ') ||
                s.Any(c => c < 32 || ":*?\"<>|".Contains(c)) || Regex.IsMatch(s, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\\.)", RegexOptions.IgnoreCase)))
            throw new InvalidDataException("Unsafe song resource path.");
        return normalized;
    }
    public static string Identity(IEnumerable<SongFile> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
            hash.AppendData(Encoding.UTF8.GetBytes(file.Path.Normalize(NormalizationForm.FormC).ToUpperInvariant() + "\n" + file.Size.ToString(CultureInfo.InvariantCulture) + "\n" + file.Sha256 + "\n"));
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    public static void Validate(SongManifest manifest, long expandedLimit = MaxExpandedBytes)
    {
        if (manifest.Version != 1 || manifest.Files is not { Length: > 0 and <= 10000 } || !IsHash(manifest.ChartSha256) || !IsHash(manifest.ContentSha256))
            throw new InvalidDataException("Invalid song manifest.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var file in manifest.Files)
        {
            if (file is null || SafePath(file.Path) != file.Path || !IsResource(file.Path) || !IsHash(file.Sha256) || file.Size < 0 ||
                !names.Add(file.Path.Normalize(NormalizationForm.FormC)) || (IsChart(file.Path) && file.Size > 5 * 1024 * 1024))
                throw new InvalidDataException("Invalid or duplicate song resource.");
            expanded = checked(expanded + file.Size);
            if (expanded > expandedLimit) throw new InvalidDataException("Song resources exceed the expanded size limit.");
        }
        foreach (var path in names)
            for (var parent = path.Contains('/') ? path[..path.LastIndexOf('/')] : ""; parent.Length > 0; parent = parent.Contains('/') ? parent[..parent.LastIndexOf('/')] : "")
                if (names.Contains(parent)) throw new InvalidDataException("Conflicting file and directory paths.");
        if (!IsChart(manifest.ChartPath) || manifest.ChartPath.Contains('/') ||
            !manifest.Files.Any(f => f.Path == manifest.ChartPath && f.Sha256 == manifest.ChartSha256) || Identity(manifest.Files) != manifest.ContentSha256)
            throw new InvalidDataException("Selected chart or resource identity does not match the manifest.");
    }

    public static async Task<string> HashFile(string path, CancellationToken cancellation)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, cancellation)).ToLowerInvariant();
    }
    public static string Resolve(string root, string relative)
    {
        var prefix = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(root)) + System.IO.Path.DirectorySeparatorChar;
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(prefix, SafePath(relative).Replace('/', System.IO.Path.DirectorySeparatorChar)));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Song resource escapes the target directory.");
        return path;
    }
}
