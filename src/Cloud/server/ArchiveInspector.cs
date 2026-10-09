using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Buffers.Binary;
using LazerRave.Content;

namespace Cloud;

public sealed record ChartMetadata(string Path, string Sha256, string Md5, string Title, string Artist, string Difficulty, int Keys, int Level);

public static class ArchiveInspector
{
    private static readonly HashSet<string> extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bms", ".bme", ".bml", ".pms", ".wav", ".ogg", ".mp3", ".flac", ".png", ".jpg", ".jpeg", ".bmp", ".gif",
        ".avi", ".mpg", ".mpeg", ".mp4", ".wmv", ".webm", ".txt"
    };
    public static string SafeName(ZipArchiveEntry entry)
    {
        var name = entry.FullName.Replace('\\', '/');
        var segments = name.TrimEnd('/').Split('/');
        int attributes = entry.ExternalAttributes;
        if (name.Length is 0 or > 240 || name.StartsWith('/') || segments.Any(s => s.Length == 0 || s is "." or ".." ||
            s.Any(c => c < 32 || ":*?\"<>|".Contains(c)) || s.EndsWith('.') || s.EndsWith(' ') ||
            Regex.IsMatch(s, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\\.)", RegexOptions.IgnoreCase)) ||
            ((attributes >> 16) & 0xF000) is 0xA000 || (attributes & 0x400) != 0)
            throw new ApiError(400, "ZIP contains an unsafe path or link.");
        return name;
    }

    public static async Task<List<ChartMetadata>> Inspect(string path, CloudOptions limits, CancellationToken cancellation, SongManifest? manifest = null)
    {
        if (manifest is not null) SongContent.Validate(manifest, limits.MaxExpandedBytes);
        var expected = manifest?.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        int checkedFiles = 0;
        CheckDirectory(path);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count is 0 or > 10000) throw new ApiError(400, "ZIP must contain 1–10,000 entries.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        var charts = new List<ChartMetadata>();
        byte[] buffer = new byte[64 * 1024];
        foreach (var entry in archive.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            var name = SafeName(entry);
            if (!names.Add(name.TrimEnd('/'))) throw new ApiError(400, "ZIP contains duplicate paths under Windows case rules.");
            bool directory = name.EndsWith('/');
            var normalized = name.TrimEnd('/');
            for (var parent = normalized.Contains('/') ? normalized[..normalized.LastIndexOf('/')] : ""; parent.Length > 0;
                 parent = parent.Contains('/') ? parent[..parent.LastIndexOf('/')] : "") directories.Add(parent);
            if (directory) { directories.Add(normalized); continue; }
            files.Add(normalized);
            var extension = System.IO.Path.GetExtension(name);
            if (expected is not null && (!expected.TryGetValue(name, out var declared) || declared.Size != entry.Length))
                throw new ApiError(400, "ZIP does not match the resource manifest.");
            if (!extensions.Contains(extension)) throw new ApiError(400, $"Unsupported file type: {extension}");
            bool chart = new[] { ".bms", ".bme", ".bml", ".pms" }.Contains(extension.ToLowerInvariant());
            if (entry.Length > limits.MaxExpandedBytes || (chart && entry.Length > 5 * 1024 * 1024)) throw new ApiError(400, "ZIP entry exceeds the size limit.");
            expanded = checked(expanded + entry.Length);
            if (expanded > limits.MaxExpandedBytes) throw new ApiError(400, "Expanded ZIP exceeds the size limit.");
            await using var input = entry.Open();
            using var metadata = chart ? new MemoryStream() : null;
            using var resourceHash = manifest is not null ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
            long read = 0;
            uint crc = uint.MaxValue;
            int count;
            while ((count = await input.ReadAsync(buffer, cancellation)) > 0)
            {
                read += count;
                resourceHash?.AppendData(buffer, 0, count);
                for (int i = 0; i < count; i++) crc = crcTable[(crc ^ buffer[i]) & 255] ^ (crc >> 8);
                if (read > entry.Length) throw new ApiError(400, "ZIP entry size is inconsistent.");
                if (metadata is not null) await metadata.WriteAsync(buffer.AsMemory(0, count), cancellation);
            }
            if (read != entry.Length) throw new ApiError(400, "ZIP entry is truncated.");
            if (~crc != entry.Crc32) throw new ApiError(400, "ZIP entry checksum is invalid.");
            if (expected is not null && Convert.ToHexString(resourceHash!.GetHashAndReset()).ToLowerInvariant() != expected[name].Sha256)
                throw new ApiError(400, "ZIP resource checksum does not match the manifest.");
            checkedFiles++;
            if (metadata is not null) charts.Add(Parse(name, metadata.ToArray()));
        }
        if (files.Overlaps(directories)) throw new ApiError(400, "ZIP contains conflicting file and directory paths.");
        if (charts.Count == 0) throw new ApiError(400, "ZIP contains no supported BMS charts.");
        if (manifest is not null && (checkedFiles != manifest.Files.Length || !charts.Any(c => c.Path == manifest.ChartPath && c.Sha256 == manifest.ChartSha256)))
            throw new ApiError(400, "ZIP is missing a declared resource or the selected chart.");
        return charts;
    }

    private static readonly uint[] crcTable = Enumerable.Range(0, 256).Select(value =>
    {
        uint crc = (uint)value;
        for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
        return crc;
    }).ToArray();

    private static void CheckDirectory(string path)
    {
        using var input = File.OpenRead(path);
        if (input.Length < 22) throw new ApiError(400, "Invalid ZIP archive.");
        byte[] tail = new byte[(int)Math.Min(input.Length, 65557)];
        input.Position = input.Length - tail.Length; input.ReadExactly(tail);
        int end = -1;
        for (int i = tail.Length - 22; i >= 0; i--)
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) == 0x06054b50 &&
                i + 22 + BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20)) == tail.Length) { end = i; break; }
        if (end < 0) throw new ApiError(400, "ZIP end record is missing.");
        var record = tail.AsSpan(end);
        int count = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
        uint bytes = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
        if (count is 0 or > 10000 || bytes > 8 * 1024 * 1024 || (long)offset + bytes > input.Length - 22 ||
            BinaryPrimitives.ReadUInt16LittleEndian(record[4..]) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(record[6..]) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(record[8..]) != count)
            throw new ApiError(400, "Unsupported ZIP directory. Split and ZIP64 archives are not accepted.");
        input.Position = offset;
        byte[] header = new byte[46];
        for (int entry = 0; entry < count; entry++)
        {
            input.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x02014b50 ||
                (BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8)) & 1) != 0)
                throw new ApiError(400, "Invalid or encrypted ZIP directory.");
            input.Position += BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28)) +
                BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(30)) + BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(32));
            if (input.Position > (long)offset + bytes) throw new ApiError(400, "ZIP directory length is invalid.");
        }
        if (input.Position != (long)offset + bytes) throw new ApiError(400, "ZIP entry count is inconsistent.");
    }

    private static ChartMetadata Parse(string path, byte[] bytes)
    {
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException) { text = Encoding.GetEncoding(932).GetString(bytes); }
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n'))
        {
            var match = Regex.Match(line.Trim(), "^#(TITLE|ARTIST|SUBTITLE|PLAYLEVEL|PLAYER|DIFFICULTY)\\s+(.+)$", RegexOptions.IgnoreCase);
            if (match.Success) fields[match.Groups[1].Value] = match.Groups[2].Value.Trim();
        }
        string Field(string key, int max, string fallback = "") => (fields.GetValueOrDefault(key) ?? fallback)[..Math.Min(max, (fields.GetValueOrDefault(key) ?? fallback).Length)];
        int keys = Regex.IsMatch(text, "^#\\d{3}(?:18|19|28|29|58|59|68|69):", RegexOptions.Multiline) ? 7 : 5;
        if (fields.GetValueOrDefault("PLAYER") is "2" or "3" || Regex.IsMatch(text, "^#\\d{3}(?:2[1-9]|6[1-9]):", RegexOptions.Multiline)) keys *= 2;
        if (Path.GetExtension(path).Equals(".pms", StringComparison.OrdinalIgnoreCase)) keys = 9;
        int level = int.TryParse(fields.GetValueOrDefault("PLAYLEVEL"), out var parsed) ? Math.Clamp(parsed, 0, 999) : 0;
        return new(path, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(),
            Field("TITLE", 200, Path.GetFileNameWithoutExtension(path)), Field("ARTIST", 200), Field("SUBTITLE", 120, fields.GetValueOrDefault("DIFFICULTY") ?? ""), keys, level);
    }
}
