using LazerRave.Content;
using Xunit;

namespace Cloud.Tests;

public sealed class SongTitleParserTests
{
    private static SongChart Chart(string title, string difficulty = "", int keys = 7, string artist = "Artist", Guid[]? packs = null, string scope = "community") =>
        new(Guid.NewGuid(), title, artist, difficulty, keys, scope, packs);

    [Theory]
    [InlineData("Cassiopeia [7 Normal]", 7, "Cassiopeia", "NORMAL")]
    [InlineData("La nyam -europa mix- (5 main)", 5, "La nyam -europa mix-", "MAIN")]
    [InlineData("Song [5key//Hard]", 5, "Song", "HARD")]
    [InlineData("Song (14 easy)", 14, "Song", "EASY")]
    [InlineData("Song [SP Imaginary]", 7, "Song", "IMAGINARY")]
    [InlineData("Song [N7]", 7, "Song", "NORMAL")]
    [InlineData("Song［7 Normal］", 7, "Song", "NORMAL")]
    [InlineData("Song [HYPER] [7key]", 7, "Song", "HYPER")]
    [InlineData("Angel Wing-DP ANOTHER-", 14, "Angel Wing", "ANOTHER")]
    [InlineData("ERIS DP HYPER", 14, "ERIS", "HYPER")]
    [InlineData("Zianio<normal>", 7, "Zianio", "NORMAL")]
    [InlineData("Ende-A-", 7, "Ende", "ANOTHER")]
    [InlineData("Song [Another 7key]", 7, "Song", "ANOTHER")]
    [InlineData("Song [Imaginary DP]", 14, "Song", "IMAGINARY")]
    public void ParsesKeyQualifiedAndStandardLabels(string title, int keys, string song, string label)
    {
        var result = SongTitleParser.Classify([Chart(title, keys: keys)]).Single();
        Assert.Equal(song, result.Title);
        Assert.Equal(label, result.Difficulty);
    }

    [Fact]
    public void CustomLabelsUseSiblingEvidenceRatherThanANameWhitelist()
    {
        var result = SongTitleParser.Classify([
            Chart("melody_express[express]", "3"), Chart("melody_express[horse]", "1"),
            Chart("melody_express[steam]", "2"), Chart("melody_express[express14]", "3", 14),
            Chart("melody_express [super express]", "4")]);
        Assert.All(result, chart => Assert.Equal("melody_express", chart.Title));
        Assert.Equal(new[] { "EXPRESS", "HORSE", "STEAM", "EXPRESS", "SUPER EXPRESS" }, result.Select(c => c.Difficulty));
    }

    [Fact]
    public void SamePackChartCreditsCanJoinTheirOriginalArtist()
    {
        Guid pack = Guid.NewGuid();
        var result = SongTitleParser.Classify([
            Chart("overclocked[normal]", "2", artist: "crock", packs: [pack]),
            Chart("overclocked -over heat-", "5", artist: "crock / Lilith", packs: [pack])]);
        Assert.All(result, chart => { Assert.Equal("overclocked", chart.Title); Assert.Equal("crock", chart.Artist); });
        Assert.Equal("OVER HEAT", result[1].Difficulty);
        var unrelated = SongTitleParser.Classify([
            Chart("Song [NORMAL]", artist: "A"), Chart("Song [ANOTHER]", artist: "A / B")]);
        Assert.Equal(new[] { "A", "A / B" }, unrelated.Select(c => c.Artist));
    }

    [Theory]
    [InlineData("Song (bms edit) [HYPER]", "Song (bms edit)")]
    [InlineData("Song [Remix] [7 Another]", "Song [Remix]")]
    [InlineData("Song -europa mix- (7 main)", "Song -europa mix-")]
    [InlineData("Song [Version 2]", "Song [Version 2]")]
    [InlineData("Song -the solitary melody-", "Song -the solitary melody-")]
    [InlineData("Song [Unknown subtitle]", "Song [Unknown subtitle]")]
    [InlineData("Angel Wing<LONG>-Hyper-", "Angel Wing<LONG>")]
    [InlineData("Symphony 5", "Symphony 5")]
    [InlineData("Song [v2.0] [NORMAL]", "Song [v2.0]")]
    public void KeepsVersionsAndAmbiguousSingleSubtitles(string title, string expected)
    {
        Assert.Equal(expected, SongTitleParser.Classify([Chart(title)]).Single().Title);
    }

    [Fact]
    public void PrivateChartsCannotSupplyContextToPublicGrouping()
    {
        var result = SongTitleParser.Classify([
            Chart("Song [Forest]"), Chart("Song [City]", scope: "user:private")]);
        Assert.Equal(new[] { "Song [Forest]", "Song [City]" }, result.Select(c => c.Title));
    }

    [Fact]
    public void UsesNumericMetadataWhenTitleHasNoDifficultyLabel()
    {
        Assert.Equal("INSANE", SongTitleParser.Classify([Chart("Song", "5")]).Single().Difficulty);
        Assert.Equal("ANOTHER", SongTitleParser.Classify([Chart("Song", "[ANOTHER]")]).Single().Difficulty);
        Assert.Equal("JET", SongTitleParser.Classify([Chart("Song [JET]", "JET")]).Single().Difficulty);
    }

    [Fact]
    public void RemovesExplicitChartCreditsButRetainsFullOriginalMetadata()
    {
        var input = Chart("Pure Ruby [ANOTHER]", artist: "SHIKI obj:ucc");
        Assert.Equal("SHIKI", SongTitleParser.Classify([input]).Single().Artist);
        Assert.Equal("SHIKI obj:ucc", input.Artist);
        Assert.Equal("xi", SongTitleParser.Classify([Chart("FREEDOM DiVE [Another]", artist: "xi (obj:k)")]).Single().Artist);
    }

    [Fact]
    public void StripsCustomChartsWithoutRemovingARepeatedSongSubtitle()
    {
        var result = SongTitleParser.Classify([
            Chart("Song -Forcing breakthrough-"),
            Chart("Song -Forcing breakthrough- [Forest]"),
            Chart("Song -Forcing breakthrough- [City]")]);
        Assert.All(result, chart => Assert.Equal("Song -Forcing breakthrough-", chart.Title));
        Assert.Equal(new[] { "UNKNOWN", "FOREST", "CITY" }, result.Select(c => c.Difficulty));
    }

    [Theory]
    [InlineData("[express]", "EXPRESS")]
    [InlineData("-over heat-", "OVER HEAT")]
    [InlineData("(5 main)", "MAIN")]
    [InlineData("JET", "JET")]
    [InlineData("【極】", "極")]
    [InlineData("[7 Normal]", "NORMAL")]
    public void NativeSubtitleOverridesNumericCategory(string subtitle, string expected)
    {
        var input = Chart("Song", "4", keys: subtitle.Contains("5 main") ? 5 : 7) with { Subtitle = subtitle };
        var result = SongTitleParser.Classify([input]).Single();
        Assert.Equal("Song", result.Title);
        Assert.Equal(expected, result.Difficulty);
    }

    [Fact]
    public void NativeVersionSubtitleIsRestoredIntoTheSongTitle()
    {
        var result = SongTitleParser.Classify([Chart("Song", "3") with { Subtitle = "(bms edit)" }]).Single();
        Assert.Equal("Song (bms edit)", result.Title);
        Assert.Equal("HYPER", result.Difficulty);
        Assert.Equal("Song [JET]", SongTitleParser.FullTitle("Song [JET]", "[JET]"));
    }

    [Theory]
    [InlineData("[JET]")]
    [InlineData("JET")]
    public void ExplicitSubtitleTakesPrecedenceOverInnerTitleAndNumericLabels(string subtitle)
    {
        var result = SongTitleParser.Classify([Chart("Song [NORMAL]", "4") with { Subtitle = subtitle }]).Single();
        Assert.Equal("Song", result.Title);
        Assert.Equal("JET", result.Difficulty);
    }

    [Fact]
    public void NativeRepeatedSongSubtitleIsNotUsedAsDifficulty()
    {
        var result = SongTitleParser.Classify([
            Chart("Song", "2") with { Subtitle = "-Forcing breakthrough-" },
            Chart("Song", "4") with { Subtitle = "-Forcing breakthrough-" }]);
        Assert.All(result, chart => Assert.Equal("Song -Forcing breakthrough-", chart.Title));
        Assert.Equal(new[] { "NORMAL", "ANOTHER" }, result.Select(c => c.Difficulty));
    }
}
