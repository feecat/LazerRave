using System.Globalization;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using LazerRave.Bridge;

namespace LazerRave.Lazer;

internal sealed record PlayRecord(Guid Id, string ChartHash, string Title, string Player, DateTimeOffset PlayedAt,
    GameplaySnapshot Score, string Arrangement, string Gauge, string? ReplayPath)
{
    public string? ReplayHash { get; init; }
    public long? ReplaySize { get; init; }
    public string? ReplayError { get; init; }
    public double? Speed { get; init; }
    public int? Offset { get; init; }
    public IReadOnlyDictionary<string, int>? PlayOptions { get; init; }
    public FrontendSettings PlaybackSettings(FrontendSettings current) => PlayOptions is null ? current : current with
        { Speed = Speed ?? current.Speed, Offset = Offset ?? current.Offset, Arrangement = Arrangement, PlayOptions = PlayOptions };
    public bool Ranked => Score.Eligible && Score.Finished && !Score.Aborted;
    public static string ClearName(int clear) => clear switch { 1 => "FAILED", 2 => "EASY CLEAR", 3 => "CLEAR", 4 => "HARD CLEAR", 5 => "FULL COMBO", _ => "FINISHED" };
}

internal sealed class PlayRecordStore(string root)
{
    public static PlayRecord? PersonalBest(IEnumerable<PlayRecord> records, string player) => records.Where(record => record.Ranked && record.Player == player)
        .OrderByDescending(record => record.Score.ExScore).ThenBy(record => record.PlayedAt).FirstOrDefault();
    public string ReplayPath(Guid run) => Path.Combine(root, "replays", run.ToString("N") + ".lr2rep");
    public static async Task<string> HashChart(string path, CancellationToken cancellation = default)
    {
        await using var input = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellation));
    }
    public PlayRecord[] Read(string hash)
    {
        if (!IsHash(hash)) throw new ArgumentException("Invalid chart hash.");
        string folder = Path.Combine(root, "scores", hash);
        if (!Directory.Exists(folder)) return [];
        var records = new List<PlayRecord>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.xml"))
        {
            try
            {
                using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 16384 });
                var row = XDocument.Load(reader).Root ?? throw new InvalidDataException("Empty record.");
                if (row.Name != "play" || (string?)row.Attribute("version") != "1") continue;
                string Text(string name) => row.Attribute(name)?.Value ?? throw new InvalidDataException("Missing record field.");
                int Number(string name) => int.Parse(Text(name), CultureInfo.InvariantCulture);
                Guid id = Guid.Parse(Text("id"));
                string replay = ReplayPath(id);
                var score = new GameplaySnapshot(Number("ex"), 0, Number("combo"), Number("misses"), 1, Number("clear"), true, false)
                {
                    NormalScore = Number("score"), Perfect = Number("perfect"), Great = Number("great"), Good = Number("good"),
                    Bad = Number("bad"), Poor = Number("poor"), TotalNotes = Number("notes"), Eligible = Number("ranked") == 1,
                };
                var options = row.Element("play-options")?.Elements("option").ToDictionary(option => (string)option.Attribute("name")!, option => (int)option.Attribute("value")!);
                if (options is not null) PlayOptionCatalog.Validate(options);
                string? replayError = (string?)row.Attribute("replay-error");
                records.Add(new(id, hash, Text("title"), Text("player"), DateTimeOffset.Parse(Text("date"), CultureInfo.InvariantCulture),
                    score, Text("arrangement"), Text("gauge"), replayError is null && File.Exists(replay) ? replay : null)
                {
                    ReplayHash = (string?)row.Attribute("replay-hash"), ReplaySize = (long?)row.Attribute("replay-size"),
                    ReplayError = replayError,
                    Speed = (double?)row.Attribute("speed"), Offset = (int?)row.Attribute("offset"), PlayOptions = options,
                });
            }
            catch (Exception error) when (error is IOException or XmlException or FormatException or InvalidDataException or ArgumentException or OverflowException) { }
        }
        return records.OrderByDescending(record => record.PlayedAt).ToArray();
    }
    public PlayRecord? Save(Guid run, string hash, Chart chart, string player, FrontendSettings settings, GameplaySnapshot score)
    {
        if (!score.Finished || score.Aborted) return null;
        if (!IsHash(hash)) throw new ArgumentException("Invalid chart hash.");
        string gauge = PlayOptionCatalog.All.Single(option => option.Name == "gauge").Choices![settings.PlayOptions.GetValueOrDefault("gauge")].ToLowerInvariant();
        string replay = ReplayPath(run);
        (string Hash, long Size)? fingerprint = null;
        string? replayError = null;
        try { if (File.Exists(replay)) fingerprint = ReplayFile.Validate(replay); }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException) { replayError = error.Message; }
        var record = new PlayRecord(run, hash, chart.Title, player, DateTimeOffset.UtcNow, score, settings.Arrangement, gauge, fingerprint is null ? null : replay)
        { ReplayHash = fingerprint?.Hash, ReplaySize = fingerprint?.Size, ReplayError = replayError, Speed = settings.Speed, Offset = settings.Offset, PlayOptions = new Dictionary<string, int>(settings.PlayOptions) };
        var document = new XDocument(new XElement("play", new XAttribute("version", 1),
            new XAttribute("id", run), new XAttribute("title", chart.Title), new XAttribute("player", player),
            new XAttribute("date", record.PlayedAt.ToString("O")), new XAttribute("arrangement", record.Arrangement), new XAttribute("gauge", gauge),
            new XAttribute("ex", score.ExScore), new XAttribute("score", score.NormalScore), new XAttribute("combo", score.MaxCombo),
            new XAttribute("misses", score.Misses), new XAttribute("clear", score.ClearType), new XAttribute("ranked", score.Eligible ? 1 : 0),
            new XAttribute("perfect", score.Perfect), new XAttribute("great", score.Great), new XAttribute("good", score.Good),
            new XAttribute("bad", score.Bad), new XAttribute("poor", score.Poor), new XAttribute("notes", score.TotalNotes),
            new XAttribute("speed", settings.Speed), new XAttribute("offset", settings.Offset),
            new XElement("play-options", settings.PlayOptions.Select(option => new XElement("option", new XAttribute("name", option.Key), new XAttribute("value", option.Value))))));
        if (fingerprint is { } saved) document.Root!.Add(new XAttribute("replay-hash", saved.Hash), new XAttribute("replay-size", saved.Size));
        if (replayError is not null) document.Root!.Add(new XAttribute("replay-error", replayError));
        string folder = Path.Combine(root, "scores", hash); Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, run.ToString("N") + ".xml"), temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { document.Save(temporary); File.Move(temporary, target, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return record;
    }
    private static bool IsHash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}
