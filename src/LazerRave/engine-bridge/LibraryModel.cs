using System.Globalization;
using System.Xml.Linq;
using Tomlyn;
using Tomlyn.Model;

namespace LazerRave.Bridge;

internal sealed record FrontendSettings(string[] Roots, double Speed = 2, int Offset = 0,
    string Arrangement = "off", string Encoding = "auto", int Width = 1024, int Height = 768,
    string Player = "Player", string? Avatar = null, int FrameLimit = 0, string RenderProfile = "baseline", string Presentation = "embedded")
{
    public IReadOnlyDictionary<string, int> PlayOptions { get; init; } = new Dictionary<string, int>();
    public static FrontendSettings Read(string path)
    {
        if (!File.Exists(path)) return new([]);
        var table = Toml.ToModel(File.ReadAllText(path));
        object? Get(string key) => table.TryGetValue(key, out var value) ? value : null;
        double Number(string key, double fallback) => Get(key) is { } n ? Convert.ToDouble(n, CultureInfo.InvariantCulture) : fallback;
        string Text(string key, string fallback) => Get(key) as string ?? fallback;
        var play = Get("play");
        if (play is not null && play is not TomlTable)
            throw new InvalidDataException("Play settings must be a TOML table.");
        var roots = (Get("directories") as TomlArray ?? []).OfType<string>().Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var settings = new FrontendSettings(roots, Number("speed", 2), (int)Number("offset", 0),
            Text("arrangement", "off"), Text("chart_encoding", "auto"), (int)Number("window_width", 1024),
            (int)Number("window_height", 768), Text("display_name", "Player"), Get("avatar_path") as string, (int)Number("game_frame_limit", 0), Text("game_render_profile", "baseline"), Text("game_presentation", "embedded"))
        {
            PlayOptions = PlayOptionCatalog.Read(play as TomlTable),
        };
        if (settings.Speed is < .5 or > 10 || !double.IsFinite(settings.Speed) || Math.Abs(settings.Offset) > 1000
            || settings.Width is < 320 or > 7680 || settings.Height is < 240 or > 4320
            || settings.FrameLimit is < -1 or > 1000 || settings.FrameLimit is > 0 and < 30
            || !RenderProfiles.Labels.ContainsKey(settings.RenderProfile)
            || !new[] { "embedded", "standalone" }.Contains(settings.Presentation)
            || !PlayOptionCatalog.Arrangements.Contains(settings.Arrangement)
            || !new[] { "auto", "utf-8", "cp932", "gb18030" }.Contains(settings.Encoding))
            throw new InvalidDataException("Invalid LazerRave play settings.");
        return settings;
    }
    public static string SharedPath => ApplicationPaths.Settings;
}
internal static class RenderProfiles
{
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        ["baseline"] = "A - Baseline",
        ["uncapped"] = "B - Engine unlimited",
        ["vsync"] = "C - Engine VSync",
        ["directshow"] = "D - DirectShow decoder",
        ["discard"] = "E - D3D11 blit",
        ["no-bga"] = "F - BGA disabled",
        ["rgb-video"] = "G - RGB video surface",
    };
}
internal sealed record Chart(string Path, string Title, string Artist, int Keys, int Level, int Difficulty,
    double Bpm, int Notes, int? Score)
{
    public string Label => Difficulty switch { 1 => "BEGINNER", 2 => "NORMAL", 3 => "HYPER", 4 => "ANOTHER", 5 => "INSANE", _ => "UNKNOWN" };
}
internal sealed record Song(string Directory, string Title, string Artist, Chart[] Charts);
internal sealed record Folder(string Path, string? Parent, string Name);
internal sealed record Entry(string Id, string Title, Song? Song)
{
    public bool IsFolder => Song is null;
}
internal sealed class SongLibrary
{
    public Song[] Songs { get; private init; } = [];
    public Folder[] Folders { get; private init; } = [];
    public static bool ContainsPath(string root, string path) => Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static SongLibrary Parse(XDocument document, string runtime, string[] selectedRoots)
    {
        var response = document.Root ?? throw new InvalidDataException("Empty catalog response.");
        if ((string?)response.Attribute("status") != "ok") throw new InvalidDataException((string?)response.Element("message") ?? "Catalog failed.");
        var roots = selectedRoots.Select(Path.TrimEndingDirectorySeparator).ToArray();
        var songs = new Dictionary<string, List<Chart>>(StringComparer.OrdinalIgnoreCase);
        var folders = new Dictionary<string, Folder>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in response.Elements("chart"))
        {
            var path = Path.GetFullPath((string?)row.Attribute("path") ?? "", runtime);
            var root = roots.Where(root => ContainsPath(root, path)).OrderByDescending(root => root.Length).FirstOrDefault();
            if (root is null || !File.Exists(path)) continue;
            int Int(string key) => int.TryParse((string?)row.Attribute(key), out var n) ? n : 0;
            var chart = new Chart(path, (string?)row.Attribute("title") ?? "", (string?)row.Attribute("artist") ?? "", Int("keys"),
                Int("level"), Int("difficulty"), double.TryParse((string?)row.Attribute("bpm"), CultureInfo.InvariantCulture, out var bpm) ? bpm : 0,
                Int("notes"), row.Attribute("score") is null ? null : Int("score"));
            var directory = Path.GetDirectoryName(path)!;
            if (!songs.TryGetValue(directory, out var charts)) songs[directory] = charts = [];
            if (!charts.Any(existing => existing.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) charts.Add(chart);
            var current = directory;
            while (true)
            {
                var isRoot = current.Equals(root, StringComparison.OrdinalIgnoreCase);
                var parent = isRoot ? null : Path.GetDirectoryName(current);
                folders[current] = new(current, parent, Path.GetFileName(current));
                if (isRoot || parent is null) break;
                current = parent;
            }
        }
        return new()
        {
            Folders = folders.Values.OrderBy(folder => folder.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            Songs = songs.Select(group =>
            {
                var charts = group.Value.OrderBy(chart => chart.Level).ToArray();
                return new Song(group.Key, charts[0].Title, charts[0].Artist, charts);
            }).OrderBy(song => song.Directory, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }
    public Entry[] Browse(string? directory, string query, int? keys, int sort)
    {
        bool Matches(Song song) => song.Charts.Any(chart => keys is null || chart.Keys == keys)
            && (song.Title + " " + song.Artist + " " + song.Directory).Contains(query, StringComparison.OrdinalIgnoreCase);
        var matching = Songs.Where(Matches).ToArray();
        IEnumerable<Entry> entries;
        if (query.Length > 0) entries = matching.Select(song => new Entry(song.Directory, song.Title, song));
        else
        {
            var result = matching.Where(song => string.Equals(song.Directory, directory, StringComparison.OrdinalIgnoreCase))
                .Select(song => new Entry(song.Directory, song.Title, song)).ToList();
            foreach (var folder in Folders.Where(folder => string.Equals(folder.Parent, directory, StringComparison.OrdinalIgnoreCase)))
            {
                if (!matching.Any(song => song.Directory.Equals(folder.Path, StringComparison.OrdinalIgnoreCase) || ContainsPath(folder.Path, song.Directory))) continue;
                var song = matching.FirstOrDefault(song => song.Directory.Equals(folder.Path, StringComparison.OrdinalIgnoreCase));
                var hasChildren = Folders.Any(child => string.Equals(child.Parent, folder.Path, StringComparison.OrdinalIgnoreCase));
                result.Add(new(folder.Path, song is not null && !hasChildren ? song.Title : folder.Name, hasChildren ? null : song));
            }
            entries = result;
        }
        return entries.OrderBy(entry => !entry.IsFolder).ThenBy(entry => sort switch
        {
            1 => entry.Title,
            2 => entry.Song?.Charts.Where(chart => keys is null || chart.Keys == keys).Min(chart => chart.Level).ToString("D3") ?? "",
            _ => entry.Id
        }, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public Folder[] Breadcrumbs(string? directory)
    {
        var result = new List<Folder>();
        while (directory is not null)
        {
            var folder = Folders.FirstOrDefault(folder => folder.Path.Equals(directory, StringComparison.OrdinalIgnoreCase));
            if (folder is null || result.Contains(folder)) break;
            result.Add(folder); directory = folder.Parent;
        }
        result.Reverse(); return result.ToArray();
    }
}
internal sealed class SelectionModel(SongLibrary library)
{
    public SongLibrary Library { get; set; } = library;
    public string? Directory { get; private set; }
    public string Query { get; set; } = "";
    public int? Keys { get; set; } = 7;
    public int Sort { get; set; }
    public Entry[] Entries { get; private set; } = [];
    public int Selected { get; private set; }
    public Entry? Current => Entries.ElementAtOrDefault(Selected);
    private readonly Dictionary<string, string> choices = new(StringComparer.OrdinalIgnoreCase);
    public Chart[] Difficulties => Current?.Song?.Charts.Where(chart => Keys is null || chart.Keys == Keys).ToArray() ?? [];
    public Chart? Chart => Difficulties.FirstOrDefault(chart => choices.TryGetValue(Current!.Id, out var path) && path == chart.Path) ?? Difficulties.FirstOrDefault();
    public void Refresh()
    {
        var previous = Current?.Id;
        Entries = Library.Browse(Directory, Query, Keys, Sort);
        Selected = Math.Max(0, Array.FindIndex(Entries, entry => entry.Id == previous));
    }
    public void Select(int index) => Selected = Math.Clamp(index, 0, Math.Max(0, Entries.Length - 1));
    public void Move(int delta) => Select(Selected + delta);
    public void Choose(Chart chart) { if (Current is not null && Difficulties.Contains(chart)) choices[Current.Id] = chart.Path; }
    public void StepDifficulty(int delta)
    {
        var charts = Difficulties; if (charts.Length == 0) return;
        Choose(charts[Math.Clamp(Array.IndexOf(charts, Chart!) + delta, 0, charts.Length - 1)]);
    }
    public void Browse(string? directory)
    {
        Directory = directory; Query = ""; Entries = []; Refresh();
    }
    public void Back()
    {
        if (Query.Length > 0) { Query = ""; Refresh(); return; }
        var leaving = Directory;
        Browse(Library.Folders.FirstOrDefault(folder => folder.Path == Directory)?.Parent);
        var index = Array.FindIndex(Entries, entry => entry.Id == leaving);
        if (index >= 0) Select(index);
    }
}
