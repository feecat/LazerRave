using System.IO.Compression;
using System.Text;
using LazerRave.Content;
using Xunit;

namespace Cloud.Tests;

public sealed class SongTransferTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lazerrave-transfer-" + Guid.NewGuid());
    private string Song => Path.Combine(root, "song");
    public SongTransferTests()
    {
        Directory.CreateDirectory(Song);
        File.WriteAllText(Path.Combine(Song, "normal.bms"), "#TITLE Fixture\n#WAV01 tone.wav\n#00118:01\n", Encoding.UTF8);
        File.WriteAllText(Path.Combine(Song, "another.bms"), "#TITLE Fixture\n#WAV01 tone.wav\n#00118:0101\n", Encoding.UTF8);
        File.WriteAllBytes(Path.Combine(Song, "tone.wav"), new byte[4096]);
    }
    [Fact]
    public async Task ResourceIdentityIgnoresOrderAndSelectionButTracksResources()
    {
        var normal = await SongTransferFiles.Inspect(Path.Combine(Song, "normal.bms"), default);
        var another = await SongTransferFiles.Inspect(Path.Combine(Song, "another.bms"), default);
        Assert.Equal(normal.ContentSha256, another.ContentSha256);
        Assert.NotEqual(normal.ChartSha256, another.ChartSha256);
        Assert.Equal(normal.ContentSha256, SongContent.Identity(normal.Files.Reverse()));
        File.WriteAllBytes(Path.Combine(Song, "tone.wav"), new byte[4097]);
        Assert.NotEqual(normal.ContentSha256, (await SongTransferFiles.Inspect(Path.Combine(Song, "normal.bms"), default)).ContentSha256);
    }
    [Fact]
    public async Task RoundTripRetainsDifficultiesAndReusesInstalledContent()
    {
        var chart = Path.Combine(Song, "normal.bms");
        var manifest = await SongTransferFiles.Inspect(chart, default);
        var zip = await SongTransferFiles.Pack(chart, manifest, Path.Combine(root, "uploads"), default);
        var charts = await ArchiveInspector.Inspect(zip, new(), default, manifest);
        Assert.Equal(2, charts.Count);
        var shared = Path.Combine(root, "client", "Shared");
        var path = await SongTransferFiles.Install(zip, manifest, shared, default);
        Assert.True(File.Exists(path)); Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "another.bms")));
        Assert.Equal(path, await SongTransferFiles.Install(zip, manifest, shared, default));
        Assert.Equal(path, await SongTransferFiles.Find(manifest, new[] { Path.GetDirectoryName(path)! }, default));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(shared, ".incoming")));
    }
    [Fact]
    public async Task MissingAudioAndChangedResourcesAreRejected()
    {
        var chart = Path.Combine(Song, "normal.bms");
        var manifest = await SongTransferFiles.Inspect(chart, default);
        File.WriteAllBytes(Path.Combine(Song, "tone.wav"), new byte[4097]);
        await Assert.ThrowsAsync<InvalidDataException>(() => SongTransferFiles.Pack(chart, manifest, Path.Combine(root, "uploads"), default));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "uploads")));
        File.Delete(Path.Combine(Song, "tone.wav"));
        await Assert.ThrowsAsync<InvalidDataException>(() => SongTransferFiles.Inspect(chart, default));
    }
    [Fact]
    public async Task DamagedManifestCannotInstallOrPassServerValidation()
    {
        var chart = Path.Combine(Song, "normal.bms");
        var manifest = await SongTransferFiles.Inspect(chart, default);
        var zip = await SongTransferFiles.Pack(chart, manifest, Path.Combine(root, "uploads"), default);
        var files = manifest.Files.Select(f => f.Path.EndsWith(".wav") ? f with { Sha256 = new string('a', 64) } : f).ToArray();
        var damaged = manifest with { Files = files, ContentSha256 = SongContent.Identity(files) };
        await Assert.ThrowsAsync<ApiError>(() => ArchiveInspector.Inspect(zip, new(), default, damaged));
        var shared = Path.Combine(root, "Shared");
        await Assert.ThrowsAsync<InvalidDataException>(() => SongTransferFiles.Install(zip, damaged, shared, default));
        Assert.False(Directory.Exists(Path.Combine(shared, damaged.ContentSha256)));
    }
    [Fact]
    public void ManifestRejectsTraversalAndCaseConflicts()
    {
        Assert.Throws<InvalidDataException>(() => SongContent.SafePath("../song.bms"));
        Assert.Throws<InvalidDataException>(() => SongContent.SafePath("C:/song.bms"));
        var files = new[] { new SongFile("song.bms", 1, new string('a', 64)), new SongFile("SONG.BMS", 1, new string('a', 64)) };
        Assert.Throws<InvalidDataException>(() => SongContent.Validate(new(1, "song.bms", new string('a', 64), SongContent.Identity(files), files)));
        Assert.Equal(TimeSpan.FromHours(2), SongContent.Lifetime);
    }
    public void Dispose() => Directory.Delete(root, true);
}
