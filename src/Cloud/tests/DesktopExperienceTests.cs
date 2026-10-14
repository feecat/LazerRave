using LazerRave.Bridge;
using Xunit;

namespace Cloud.Tests;

public sealed class DesktopExperienceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lazerrave-experience-" + Guid.NewGuid());
    public DesktopExperienceTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private Chart Chart(string name, int keys = 7, double bpm = 150)
    {
        string path = Path.Combine(root, name + ".bms");
        File.WriteAllText(path, "#TITLE " + name);
        return new(path, name, "Artist", keys, 10, 4, bpm, 100, null);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(7, true)]
    [InlineData(9, false)]
    [InlineData(10, false)]
    [InlineData(14, false)]
    public void ModernClientSupportsOnlySinglePlay(int keys, bool supported) => Assert.Equal(supported, SupportedKeyModes.Contains(keys));

    [Fact]
    public void FrontendBaseUsesNoteCountsNotDurationBgmMinesOrLongTails()
    {
        var chart = Chart("dominant", bpm: 999);
        File.WriteAllText(chart.Path, "#BPM 120\n#BPM01 200\n#STOP01 192000\n#LNOBJ ZZ\n#00101:01010101\n#00108:00010000\n#00109:01000000\n#00111:010001ZZ\n#00112:00000100\n#00116:00000100\n#00151:00000101\n#00152:01000001\n#001D1:01010101\n");
        var timeline = BmsTimeline.Read(chart, "auto");
        Assert.Equal(200, timeline.NoteBaseBpm);
        Assert.Equal(2, timeline.NotesPerBpm[120]);
        Assert.Equal(4, timeline.NotesPerBpm[200]);
        var profile = new ReadingSpeedProfile(true, 2, 1500) { NormalizeBase = true, BaseBpm = 150 };
        var settings = new FrontendSettings([]) { SpeedProfiles = new(profile, profile), PlayOptions = new Dictionary<string, int> { ["lane_cover"] = 1, ["cover"] = 95, ["hsfix"] = 4, ["base_speed"] = 200 } };
        var applied = settings.ForChart(chart);
        Assert.Equal(1.5, applied.Speed * applied.PlayOptions["base_speed"] / 100);
        Assert.Equal(0, applied.PlayOptions["hsfix"]);
        Assert.Equal(1500, settings.SpeedProfiles.Seven.Target);
        Assert.Equal(4, settings.PlayOptions["hsfix"]);
    }

    [Fact]
    public void FrontendBaseCombinesRepeatedBpmSegmentsAndUsesStableTies()
    {
        var chart = Chart("segments");
        File.WriteAllText(chart.Path, "#BPM 120\n#BPM01 200\n#BPM02 120\n#00011:01\n#00108:01\n#00111:0101\n#00208:02\n#00211:010101\n");
        var timeline = BmsTimeline.Read(chart, "auto");
        Assert.Equal(120, timeline.NoteBaseBpm);
        Assert.Equal(4, timeline.NotesPerBpm[120]);
        File.WriteAllText(chart.Path, "#BPM 120\n#BPM01 200\n#00011:01\n#00108:01\n#00111:01\n");
        Assert.Equal(120, BmsTimeline.Read(chart, "auto").NoteBaseBpm);
    }

    [Fact]
    public void FrontendBasePreservesTempoChangesAndRepresentsLowMultipliersWithoutClipping()
    {
        var chart = Chart("fast"); File.WriteAllText(chart.Path, "#BPM 1000\n#00011:0101\n");
        var profile = new ReadingSpeedProfile(false, 2, 600) { NormalizeBase = true, BaseBpm = 150 };
        var settings = new FrontendSettings([]) { SpeedProfiles = new(profile, profile) };
        var applied = settings.ForChart(chart);
        Assert.Equal(.3, applied.Speed * applied.PlayOptions["base_speed"] / 100, 8);
        Assert.Equal(0, applied.PlayOptions["hsfix"]);
        var disabled = profile with { NormalizeBase = false, Automatic = true };
        Assert.Equal(5, (settings with { SpeedProfiles = new(disabled, disabled) }).ForChart(chart).PlayOptions["hsfix"]);
    }

    [Fact]
    public void AutomaticSpeedUsesEngineMainBpmWithoutFlatteningTempoChanges()
    {
        var settings = new FrontendSettings([]) { SpeedProfiles = new(new(true, 3, 600), new(true, 2, 750)) };
        var five = settings.ForChart(Chart("five", 5, 120));
        var seven = settings.ForChart(Chart("seven", 7, 240));
        Assert.Equal(4, five.Speed);
        Assert.Equal(5, seven.Speed);
        Assert.Equal(5, five.PlayOptions["hsfix"]);
        Assert.NotEqual(4, seven.PlayOptions["hsfix"]);
        Assert.Equal(100, seven.PlayOptions["base_speed"]);
        Assert.Equal(3, settings.Speed);
        Assert.Empty(settings.PlayOptions);
    }

    [Fact]
    public void SpeedProfilesRespectCoverLiftAndClampWithoutChangingSavedPreferences()
    {
        var settings = new FrontendSettings([]) { SpeedProfiles = new(new(false, 3, 600), new(true, 2, 600)), PlayOptions = new Dictionary<string, int> { ["lane_cover"] = 1, ["cover"] = 25, ["lift_enabled"] = 1, ["lift"] = 25 } };
        var applied = settings.ForChart(Chart("covered"));
        Assert.Equal(2, applied.Speed);
        Assert.Equal(3, settings.ForChart(Chart("manual", 5)).Speed);
        Assert.Equal(600, settings.SpeedProfiles.Seven.Target);
        Assert.Equal(.5, (settings with { PlayOptions = new Dictionary<string, int> { ["lane_cover"] = 1, ["cover"] = 100 } }).ForChart(Chart("closed")).Speed);
    }

    [Fact]
    public void HiddenTwoPlayerPreferencesCannotAlterSinglePlayButRemainSaved()
    {
        var settings = new FrontendSettings([]) { PlayOptions = new Dictionary<string, int> { ["assist2"] = 1, ["dp_flip"] = 1, ["gauge2"] = 5 } };
        var applied = settings.ForChart(Chart("single"));
        Assert.Equal(0, applied.PlayOptions["assist2"]);
        Assert.Equal(0, applied.PlayOptions["dp_flip"]);
        Assert.Equal(0, applied.PlayOptions["gauge2"]);
        Assert.Equal(1, settings.PlayOptions["assist2"]);
        Assert.Equal(5, settings.PlayOptions["gauge2"]);
    }

    [Fact]
    public void LegacySpeedAndUnknownSettingsRemainCompatible()
    {
        string file = Path.Combine(root, "settings.toml");
        File.WriteAllText(file, "speed = 3.5\n[play]\nhsfix = 2\nfuture = 7\n");
        var settings = FrontendSettings.Read(file);
        Assert.False(settings.SpeedProfiles.Five.Automatic);
        Assert.Equal(3.5, settings.SpeedProfiles.Seven.Manual);
        Assert.Equal(3.5, settings.ForChart(Chart("legacy")).Speed);
        Assert.Equal(2, settings.ForChart(Chart("reference")).PlayOptions["hsfix"]);
        File.WriteAllText(file, "[reading_speed.five]\nautomatic = true\nmanual = 2.5\ntarget = 600\n[reading_speed.seven]\nautomatic = false\nmanual = 4.0\ntarget = 750\n");
        settings = FrontendSettings.Read(file);
        Assert.True(settings.SpeedProfiles.Five.Automatic);
        Assert.Equal(4, settings.SpeedProfiles.Seven.Manual);
        Assert.Throws<ArgumentException>(() => settings.ForChart(Chart("dp", 14)));
    }

    [Theory]
    [InlineData("manual = nan")]
    [InlineData("manual = 0.1")]
    [InlineData("target = 0")]
    [InlineData("automatic = 'yes'")]
    public void InvalidSpeedPreferencesAreRejected(string value)
    {
        string file = Path.Combine(root, "invalid.toml");
        File.WriteAllText(file, "[reading_speed.seven]\n" + value);
        Assert.Throws<InvalidDataException>(() => FrontendSettings.Read(file));
    }

    [Fact]
    public void FavoritesFollowContentAcrossMovesAndDoNotModifySongs()
    {
        var chart = Chart("favorite");
        string original = File.ReadAllText(chart.Path);
        string file = Path.Combine(root, "library.json");
        var library = new UserLibraryStore(file);
        library.ToggleFavorite([chart]);
        Assert.True(library.IsFavorite(chart));
        string moved = Path.Combine(root, "renamed.bms");
        File.Move(chart.Path, moved);
        var renamed = chart with { Path = moved };
        library = new(file);
        Assert.True(library.IsFavorite(renamed));
        Assert.Equal(original, File.ReadAllText(moved));
        library.ToggleFavorite([renamed]);
        Assert.False(new UserLibraryStore(file).IsFavorite(renamed));
    }

    [Fact]
    public void NewlyAvailableCatalogHashKeepsExistingFavorite()
    {
        var chart = Chart("metadata");
        var library = new UserLibraryStore(Path.Combine(root, "metadata.json"));
        library.ToggleFavorite([chart]);
        string md5 = Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(File.ReadAllBytes(chart.Path)));
        Assert.True(library.IsFavorite(chart with { Md5 = md5 }));
    }

    [Fact]
    public void ReadOnlyChecksNeverWriteUserPreferences()
    {
        string file = Path.Combine(root, "readonly.json");
        var library = new UserLibraryStore(file, readOnly: true);
        var chart = Chart("readonly");
        library.ToggleFavorite([chart]); library.RecordPlay(chart, DateTimeOffset.UtcNow);
        Assert.True(library.IsFavorite(chart));
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void RecentPlaysDeduplicatePersistAndAreBounded()
    {
        string file = Path.Combine(root, "recent.json");
        var library = new UserLibraryStore(file);
        var first = Chart("first");
        var second = Chart("second");
        var time = DateTimeOffset.UtcNow;
        library.RecordPlay(first, time);
        library.RecordPlay(second, time.AddMinutes(1));
        library.RecordPlay(first, time.AddMinutes(2));
        library = new(file);
        Assert.Equal(time.AddMinutes(2), library.LastPlayed(first));
        Assert.Equal(time.AddMinutes(1), library.LastPlayed(second));
        Assert.Equal(2, library.Recent.Count);
        for (int i = 0; i < 110; i++) library.RecordPlay(Chart("recent" + i), time.AddMinutes(3 + i));
        Assert.Equal(100, new UserLibraryStore(file).Recent.Count);
    }

    [Fact]
    public void CorruptLibraryDataCannotBeSilentlyOverwritten()
    {
        string file = Path.Combine(root, "broken.json");
        File.WriteAllText(file, "{broken");
        var library = new UserLibraryStore(file);
        Assert.NotNull(library.Error);
        Assert.Throws<InvalidDataException>(() => library.ToggleFavorite([Chart("song")]));
        Assert.Equal("{broken", File.ReadAllText(file));
    }

    [Fact]
    public void FailedLibraryWriteDoesNotChangeMemoryOrSavedData()
    {
        string file = Path.Combine(root, "locked.json");
        var library = new UserLibraryStore(file);
        var chart = Chart("safe");
        library.RecordPlay(chart, DateTimeOffset.UtcNow);
        var before = File.ReadAllBytes(file);
        if (!OperatingSystem.IsWindows()) return;
        using var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        var error = Record.Exception(() => library.ToggleFavorite([chart]));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.False(library.IsFavorite(chart));
        Assert.Equal(before, File.ReadAllBytes(file));
    }
}
