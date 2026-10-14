using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LazerRave.Bridge;

internal sealed class UserLibraryStore
{
    private readonly string path;
    private readonly bool readOnly;
    private readonly object gate = new();
    private readonly Dictionary<string, (long Length, DateTime Modified, string Hash)> hashes = new(StringComparer.OrdinalIgnoreCase);
    private JsonObject document = new();
    private string? savedText;
    private HashSet<string> favorites = new(StringComparer.Ordinal);
    private Dictionary<string, DateTimeOffset> recent = new(StringComparer.Ordinal);
    public string? Error { get; private set; }
    public event Action? Changed;
    public IReadOnlyDictionary<string, DateTimeOffset> Recent { get { lock (gate) return new Dictionary<string, DateTimeOffset>(recent); } }
    public UserLibraryStore(string path, bool readOnly = false)
    {
        this.path = path; this.readOnly = readOnly;
        try
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Library preferences are too large.");
            savedText = File.ReadAllText(path);
            document = JsonNode.Parse(savedText) as JsonObject ?? throw new InvalidDataException("Invalid library preferences.");
            if (document["version"]?.GetValue<int>() != 1) throw new InvalidDataException("Unsupported library preferences version.");
            favorites = document["favorites"]?.Deserialize<string[]>()?.ToHashSet(StringComparer.Ordinal) ?? [];
            recent = document["recent"]?.Deserialize<Dictionary<string, DateTimeOffset>>() ?? [];
            if (favorites.Any(key => !ValidIdentity(key)) || recent.Keys.Any(key => !ValidIdentity(key))) throw new InvalidDataException("Invalid library chart identity.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        { Error = error.Message; }
    }
    private static bool ValidIdentity(string? value) => value is not null && (value.StartsWith("sha256:", StringComparison.Ordinal) && value.Length == 71 && value[7..].All(Uri.IsHexDigit)
        || value.StartsWith("md5:", StringComparison.Ordinal) && value.Length == 36 && value[4..].All(Uri.IsHexDigit));
    private string Identity(Chart chart)
    {
        if (chart.Md5 is { Length: 32 } md5 && md5.All(Uri.IsHexDigit)) return "md5:" + md5.ToLowerInvariant();
        var file = new FileInfo(chart.Path);
        if (hashes.TryGetValue(chart.Path, out var cached) && cached.Length == file.Length && cached.Modified == file.LastWriteTimeUtc) return cached.Hash;
        using var input = file.OpenRead();
        string identity = "md5:" + Convert.ToHexStringLower(MD5.HashData(input));
        hashes[chart.Path] = (file.Length, file.LastWriteTimeUtc, identity);
        return identity;
    }
    private string? AvailableIdentity(Chart chart)
    {
        try { return Identity(chart); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }
    public bool IsFavorite(Chart chart) { lock (gate) return favorites.Count > 0 && AvailableIdentity(chart) is { } identity && favorites.Contains(identity); }
    public DateTimeOffset? LastPlayed(Chart chart) { lock (gate) return recent.Count > 0 && AvailableIdentity(chart) is { } identity && recent.TryGetValue(identity, out var date) ? date : null; }
    public void ToggleFavorite(IEnumerable<Chart> charts)
    {
        lock (gate)
        {
            EnsureWritable();
            var identities = charts.Where(chart => SupportedKeyModes.Contains(chart.Keys)).Select(Identity).Distinct().ToArray();
            if (identities.Length == 0) return;
            var next = new HashSet<string>(favorites, StringComparer.Ordinal);
            bool remove = identities.Any(next.Contains);
            foreach (var identity in identities) { if (remove) next.Remove(identity); else next.Add(identity); }
            Save(next, recent);
        }
        Changed?.Invoke();
    }
    public void RecordPlay(Chart chart, DateTimeOffset playedAt)
    {
        if (!SupportedKeyModes.Contains(chart.Keys)) return;
        lock (gate)
        {
            EnsureWritable();
            string identity = Identity(chart);
            var next = new Dictionary<string, DateTimeOffset>(recent);
            if (next.TryGetValue(identity, out var previous) && previous >= playedAt) return;
            next[identity] = playedAt.ToUniversalTime();
            next = next.OrderByDescending(pair => pair.Value).Take(100).ToDictionary(pair => pair.Key, pair => pair.Value);
            Save(favorites, next);
        }
        Changed?.Invoke();
    }
    private void EnsureWritable()
    {
        if (Error is not null) throw new InvalidDataException("Library preferences could not be loaded; the original file is retained. " + Error);
        if (!readOnly && (File.Exists(path) ? File.ReadAllText(path) : null) != savedText) throw new IOException("Library preferences changed outside the application. Restart before saving.");
    }
    private void Save(HashSet<string> nextFavorites, Dictionary<string, DateTimeOffset> nextRecent)
    {
        var next = (JsonObject)document.DeepClone();
        next["version"] = 1;
        next["favorites"] = JsonSerializer.SerializeToNode(nextFavorites.Order(StringComparer.Ordinal));
        next["recent"] = JsonSerializer.SerializeToNode(nextRecent);
        string text = next.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        if (!readOnly)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, text); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        favorites = new(nextFavorites, StringComparer.Ordinal); recent = new(nextRecent); document = next; savedText = text;
    }
}
