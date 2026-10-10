using System.Security.Cryptography;
using System.Text;
using LazerRave.Bridge;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Database;
using osu.Game.Models;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Skinning;

namespace LazerRave.Lazer;

internal partial class BmsBeatmapStore : BeatmapStore
{
    private readonly object gate = new();
    private bool initialized;
    private readonly BindableList<BeatmapSetInfo> visible = new();
    private readonly Dictionary<Guid, Chart> charts = new();
    private readonly Dictionary<Guid, WorkingBeatmap> working = new();
    private readonly Dictionary<string, BeatmapSetInfo> sets = new(StringComparer.OrdinalIgnoreCase);
    private readonly RulesetInfo ruleset = new ManiaRuleset().RulesetInfo;
    public SongLibrary Library { get; private set; } = new();
    public string? Directory { get; private set; }
    public int Keys { get; private set; } = 7;
    public string Query { get; private set; } = "";
    public int DifficultyFilter { get; private set; }
    public int? LevelFilter { get; private set; }
    public double? LevelMinimum { get; private set; }
    public double? LevelMaximum { get; private set; }
    public void SetLevelRange(double? minimum, double? maximum)
    {
        lock (gate)
        {
            if (LevelMinimum == minimum && LevelMaximum == maximum) return;
            LevelMinimum = minimum; LevelMaximum = maximum; FilterCore(Directory, Keys);
        }
    }
    private bool Matches(Chart chart) => (Keys == 0 || Keys == chart.Keys) &&
        (DifficultyFilter == 0 || chart.Difficulty == DifficultyFilter) && (LevelFilter is null || chart.Level == LevelFilter) &&
        (LevelMinimum is null || chart.Level >= LevelMinimum) && (LevelMaximum is null || chart.Level <= LevelMaximum);
    public void SetSearch(string query)
    {
        lock (gate)
        {
            query = query.Trim(); if (Query == query) return;
            Query = query; FilterCore(Directory, Keys);
        }
    }
    public void SetDifficulty(int difficulty, int? level)
    { lock (gate) { DifficultyFilter = difficulty; LevelFilter = level; FilterCore(Directory, Keys); } }

    public override IBindableList<BeatmapSetInfo> GetBeatmapSets(CancellationToken? cancellationToken) => visible;
    public static int Columns(int keys) => keys switch { 5 or 7 => keys + 1, 10 or 14 => keys + 2, _ => keys };
    public static Guid StableId(string path) => new(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())).AsSpan(0, 16));

    public void Replace(SongLibrary library, string[] roots) { lock (gate) ReplaceCore(library, roots); }
    private void ReplaceCore(SongLibrary library, string[] roots)
    {
        Library = library;
        charts.Clear(); sets.Clear(); working.Clear();
        foreach (var song in library.Songs)
        {
            var set = new BeatmapSetInfo { ID = StableId(song.Directory), Hash = StableId(song.Directory).ToString("N"), DateAdded = DateTimeOffset.UnixEpoch };
            foreach (var chart in song.Charts)
            {
                var id = StableId(chart.Path);
                charts[id] = chart;
                set.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = id.ToString("N") }, chart.Path));
                set.Beatmaps.Add(new BeatmapInfo(ruleset)
                {
                    ID = id, BeatmapSet = set, Hash = id.ToString("N"),
                    MD5Hash = chart.Md5 is { Length: 32 } md5 && md5.All(Uri.IsHexDigit) ? md5.ToLowerInvariant() : id.ToString("N"),
                    DifficultyName = chart.DifficultyText,
                    BPM = chart.Bpm, StarRating = 0,
                    Difficulty = new BeatmapDifficulty { CircleSize = chart.Keys },
                    Metadata = new BeatmapMetadata
                    {
                        Title = chart.DisplayTitle, TitleUnicode = chart.DisplayTitle, Artist = chart.Artist, ArtistUnicode = chart.Artist,
                        Source = Path.GetFileName(song.Directory), Tags = $"{song.Directory} {Path.GetFileName(chart.Path)} {chart.FullTitle} {chart.Label} Lv.{chart.Level}", PreviewTime = 0,
                        AudioFile = song.Directory, BackgroundFile = chart.Path,
                    },
                });
            }
            sets[song.Directory] = set;
        }
        bool directoryRemoved = Directory is not null && !library.Folders.Any(f => f.Path.Equals(Directory, StringComparison.OrdinalIgnoreCase));
        if (directoryRemoved)
            Directory = null;
        if ((!initialized || directoryRemoved) && Directory is null && roots.Length == 1)
            Directory = roots.Select(root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)))
                .FirstOrDefault(root => library.Folders.Any(f => f.Path.Equals(root, StringComparison.OrdinalIgnoreCase)));
        initialized = true;
        Filter(Directory, Keys);
    }

    private Entry[] EntriesHere => Library.Browse(Directory, Query, Keys == 0 ? null : Keys, 0)
        .Where(entry => entry.Song is { } song ? song.Charts.Any(Matches) : Library.Songs.Any(song =>
            (song.Directory.Equals(entry.Id, StringComparison.OrdinalIgnoreCase) || SongLibrary.ContainsPath(entry.Id, song.Directory)) && song.Charts.Any(Matches))).ToArray();
    public IEnumerable<Folder> ChildFolders
    {
        get
        {
            lock (gate) return (Query.Length > 0 ? Array.Empty<Entry>() : EntriesHere).Where(entry => entry.IsFolder)
                .Select(entry => Library.Folders.Single(folder => folder.Path.Equals(entry.Id, StringComparison.OrdinalIgnoreCase))).ToArray();
        }
    }
    /// <summary>
    /// The folder segments from the library root down to <see cref="Directory"/>, root first.
    /// Empty at the library overview.
    /// </summary>
    public Folder[] Ancestry { get { lock (gate) return Library.Breadcrumbs(Directory); } }
    /// <summary>
    /// Songs in this directory and its immediate song folders. Deeper collections remain folders.
    /// </summary>
    public IEnumerable<Song> SongsHere
    {
        get { lock (gate) return EntriesHere.Where(entry => entry.Song is not null).Select(entry => entry.Song!).ToArray(); }
    }
    public bool HasParent => Directory is not null;
    public bool IsVisible(BeatmapInfo info)
    {
        lock (gate) return charts.TryGetValue(info.ID, out var chart) && Matches(chart)
            && visible.Any(set => set.Beatmaps.Any(candidate => candidate.ID == info.ID));
    }
    public Chart? ChartFor(BeatmapInfo info) { lock (gate) return charts.GetValueOrDefault(info.ID); }
    public void Filter(string? directory, int keys) { lock (gate) FilterCore(directory, keys); }
    private void FilterCore(string? directory, int keys)
    {
        Directory = string.IsNullOrEmpty(directory) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)); Keys = keys;
        var selected = (Query.Length > 0 ? Library.Songs.Where(song => song.Charts.Any(Matches)) : SongsHere).Select(song =>
        {
            var source = sets[song.Directory];
            var filtered = new BeatmapSetInfo { ID = source.ID, Hash = source.Hash, DateAdded = source.DateAdded };
            foreach (var info in source.Beatmaps.Where(info => Matches(charts[info.ID]))) filtered.Beatmaps.Add(info);
            foreach (var file in source.Files) filtered.Files.Add(file);
            return filtered;
        }).ToArray();
        visible.Clear(); visible.AddRange(selected);
    }

    public WorkingBeatmap? Resolve(BeatmapInfo info, AudioManager audio, TextureStore textures, string encoding)
    { lock (gate) return ResolveCore(info, audio, textures, encoding); }
    private WorkingBeatmap? ResolveCore(BeatmapInfo info, AudioManager audio, TextureStore textures, string encoding)
    {
        if (!charts.TryGetValue(info.ID, out var chart)) return null;
        if (!working.TryGetValue(info.ID, out var value))
            working[info.ID] = value = new BmsWorkingBeatmap(info, chart, audio, textures, encoding);
        return value;
    }

    private sealed class BmsWorkingBeatmap(BeatmapInfo info, Chart chart, AudioManager audio, TextureStore textures, string encoding)
        : WorkingBeatmap(info, audio)
    {
        private readonly Lazy<PreviewPlan> media = new(() => PreviewPlan.Read(chart, encoding));
        private readonly Lazy<BmsTimeline> timeline = new(() => BmsTimeline.Read(chart, encoding));
        protected override IBeatmap GetBeatmap()
        {
            var value = new ManiaBeatmap(new StageDefinition(Columns(chart.Keys))) { BeatmapInfo = BeatmapInfo };
            BeatmapInfo.Length = timeline.Value.Length;
            foreach (var tempo in timeline.Value.Tempos)
                value.ControlPointInfo.Add(tempo.Time, new TimingControlPoint { BeatLength = 60000 / tempo.Bpm });
            return value;
        }
        public override Texture GetBackground() => media.Value.Cover is { } cover ? textures.Get(cover) : null!;
        protected override Track GetBeatmapTrack() => GetVirtualTrack(Math.Max(1, timeline.Value.Length));
        protected override ISkin GetSkin() => null!;
        public override Stream GetStream(string storagePath) => Stream.Null;
    }
}
