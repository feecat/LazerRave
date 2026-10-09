using System.Globalization;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using LazerRave.Bridge;

namespace LazerRave.Lazer;

internal sealed record PlayRecord(Guid Id, string ChartHash, string Title, string Player, DateTimeOffset PlayedAt,
    GameplaySnapshot Score, string Arrangement, string Gauge, string? ReplayPath)
{
    public bool Ranked => Score.Eligible && Score.Finished && !Score.Aborted;
    public static string ClearName(int clear) => clear switch { 1 => "Failed", 2 => "Easy", 3 => "Normal", 4 => "Hard", 5 => "Full combo", _ => "Finished" };
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
                records.Add(new(id, hash, Text("title"), Text("player"), DateTimeOffset.Parse(Text("date"), CultureInfo.InvariantCulture),
                    score, Text("arrangement"), Text("gauge"), File.Exists(replay) ? replay : null));
            }
            catch (Exception error) when (error is IOException or XmlException or FormatException or InvalidDataException or ArgumentException) { }
        }
        return records.OrderByDescending(record => record.PlayedAt).ToArray();
    }
    public PlayRecord? Save(Guid run, string hash, Chart chart, string player, FrontendSettings settings, GameplaySnapshot score)
    {
        if (!score.Finished || score.Aborted) return null;
        if (!IsHash(hash)) throw new ArgumentException("Invalid chart hash.");
        string gauge = PlayOptionCatalog.All.Single(option => option.Name == "gauge").Choices![settings.PlayOptions.GetValueOrDefault("gauge")].ToLowerInvariant();
        string replay = ReplayPath(run);
        var record = new PlayRecord(run, hash, chart.Title, player, DateTimeOffset.UtcNow, score, settings.Arrangement, gauge, File.Exists(replay) ? replay : null);
        var document = new XDocument(new XElement("play", new XAttribute("version", 1),
            new XAttribute("id", run), new XAttribute("title", chart.Title), new XAttribute("player", player),
            new XAttribute("date", record.PlayedAt.ToString("O")), new XAttribute("arrangement", record.Arrangement), new XAttribute("gauge", gauge),
            new XAttribute("ex", score.ExScore), new XAttribute("score", score.NormalScore), new XAttribute("combo", score.MaxCombo),
            new XAttribute("misses", score.Misses), new XAttribute("clear", score.ClearType), new XAttribute("ranked", score.Eligible ? 1 : 0),
            new XAttribute("perfect", score.Perfect), new XAttribute("great", score.Great), new XAttribute("good", score.Good),
            new XAttribute("bad", score.Bad), new XAttribute("poor", score.Poor), new XAttribute("notes", score.TotalNotes)));
        string folder = Path.Combine(root, "scores", hash); Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, run.ToString("N") + ".xml"), temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { document.Save(temporary); File.Move(temporary, target, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return record;
    }
    private static bool IsHash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}
