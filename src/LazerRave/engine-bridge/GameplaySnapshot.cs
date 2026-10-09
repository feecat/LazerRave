using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace LazerRave.Bridge;

internal sealed record GameplaySnapshot(int ExScore, int Combo, int MaxCombo, int Misses, double Progress, int ClearType, bool Finished, bool Aborted)
{
    public int NormalScore { get; init; }
    public int Perfect { get; init; }
    public int Great { get; init; }
    public int Good { get; init; }
    public int Bad { get; init; }
    public int Poor { get; init; }
    public int TotalNotes { get; init; }
    public bool Eligible { get; init; }
    public static GameplaySnapshot Empty => new(0, 0, 0, 0, 0, 0, false, false);
    public static GameplaySnapshot Read(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 4096 });
        var root = XDocument.Load(reader).Root ?? throw new InvalidDataException("Missing score snapshot.");
        if (root.Name != "score" || (string?)root.Attribute("protocol") != "LAZERRAVE_SCORE_STREAM_V1") throw new InvalidDataException("Unsupported score snapshot.");
        int Number(string name) => int.Parse(root.Attribute(name)?.Value ?? throw new InvalidDataException("Missing score field: " + name), CultureInfo.InvariantCulture);
        var progress = double.Parse(root.Attribute("progress")?.Value ?? "", CultureInfo.InvariantCulture);
        var score = new GameplaySnapshot(Number("ex-score"), Number("combo"), Number("max-combo"), Number("misses"), progress, Number("clear-type"), Number("finished") == 1, Number("aborted") == 1);
        if (score.ExScore is < 0 or > 3000000 || score.Combo is < 0 or > 1000000 || score.MaxCombo is < 0 or > 1000000 || score.Misses is < 0 or > 1000000
            || !double.IsFinite(progress) || progress is < 0 or > 1 || score.ClearType is < 0 or > 9) throw new InvalidDataException("Invalid engine score snapshot.");
        int Optional(string name) => root.Attribute(name) is { } value ? int.Parse(value.Value, CultureInfo.InvariantCulture) : 0;
        return score with { NormalScore = Optional("normal-score"), Perfect = Optional("perfect"), Great = Optional("great"),
            Good = Optional("good"), Bad = Optional("bad"), Poor = Optional("poor"), TotalNotes = Optional("total-notes"), Eligible = Optional("eligible") == 1 };
    }
}
