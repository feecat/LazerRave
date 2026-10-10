using System.Xml.Linq;
using LazerRave.Bridge;
using Xunit;

namespace Cloud.Tests;

public sealed class SongLibraryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lazerrave-library-" + Guid.NewGuid());
    private XElement Row(string file, string title, string? subtitle, int keys, int difficulty, int level)
    {
        string path = Path.Combine(root, "Pack", "Song", file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "#TITLE " + title);
        var row = new XElement("chart", new XAttribute("path", path), new XAttribute("title", title),
            new XAttribute("artist", "Artist"), new XAttribute("keys", keys), new XAttribute("level", level),
            new XAttribute("difficulty", difficulty), new XAttribute("md5", new string('a', 32)), new XAttribute("score", 123));
        if (subtitle is not null) row.Add(new XAttribute("subtitle", subtitle));
        return row;
    }
    private SongLibrary Read(params XElement[] rows) => SongLibrary.Parse(
        new XDocument(new XElement("lazerrave", new XAttribute("status", "ok"), rows)), root, [root]);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    [Fact]
    public void NativeCustomDifficultiesKeepOneSongAndRemainSearchable()
    {
        var library = Read(Row("a.bms", "melody_express", "[express]", 7, 3, 11),
            Row("b.bms", "melody_express", "[horse]", 7, 1, 3),
            Row("c.bms", "melody_express", "[express14]", 14, 3, 12));
        var song = Assert.Single(library.Songs);
        Assert.Equal("melody_express", song.Title);
        Assert.Equal(new[] { "HORSE", "EXPRESS", "EXPRESS" }, song.Charts.Select(chart => chart.Label));
        Assert.All(song.Charts, chart => { Assert.Equal("melody_express", chart.DisplayTitle); Assert.Equal(123, chart.Score); Assert.Equal(new string('a', 32), chart.Md5); });
        Assert.Single(library.Browse(null, "horse", 7, 0));
        Assert.Empty(library.Browse(null, "horse", 14, 0));
        var selection = new SelectionModel(library) { Keys = 7, Query = "express" };
        selection.Refresh();
        Assert.Equal(2, selection.Difficulties.Length);
        selection.StepDifficulty(1);
        Assert.Equal("EXPRESS", selection.Chart!.Label);
        Assert.Equal("7Key · EXPRESS · Lv.11", selection.Chart.DifficultyText);
        Assert.EndsWith("a.bms", selection.Chart.Path);
    }

    [Fact]
    public void OldCatalogResponsesUseTitleLabelsOrNumericFallback()
    {
        var song = Assert.Single(Read(Row("a.bms", "Song [7 Another]", null, 7, 2, 9),
            Row("b.bms", "Song", null, 7, 3, 5)).Songs);
        Assert.Equal("Song", song.Title);
        Assert.Equal(new[] { "HYPER", "ANOTHER" }, song.Charts.Select(chart => chart.Label));
        Assert.Equal(new[] { 3, 2 }, song.Charts.Select(chart => chart.Difficulty));
    }

    [Fact]
    public void VersionSubtitlesAndUnknownCategoriesArePreserved()
    {
        var song = Assert.Single(Read(Row("a.bms", "Song", "(bms edit)", 7, 3, 1),
            Row("b.bms", "Song", "(bms edit)", 7, 0, 2)).Songs);
        Assert.Equal("Song (bms edit)", song.Title);
        Assert.Equal(new[] { "HYPER", "UNKNOWN" }, song.Charts.Select(chart => chart.Label));
        Assert.All(song.Charts, chart => Assert.Equal("Song (bms edit)", chart.FullTitle));
    }
}
