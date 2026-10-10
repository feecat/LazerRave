using System.IO.Compression;
using LazerRave.Content;
using LazerRave.Lazer;
using Xunit;

namespace Cloud.Tests;

public sealed class SongPackFilesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lazerrave-pack-" + Guid.NewGuid());
    private string Zip(params (string Path, string Text)[] files)
    {
        Directory.CreateDirectory(root);
        var zip = Path.Combine(root, Guid.NewGuid() + ".zip");
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var file in files)
        {
            using var writer = new StreamWriter(archive.CreateEntry(file.Path).Open());
            writer.Write(file.Text);
        }
        return zip;
    }

    [Fact]
    public async Task ImportKeepsFoldersAndDifficultiesAndReimportsWithoutOverwriting()
    {
        var zip = Zip(("Pack/Song/normal.bms", "#TITLE Test"), ("Pack/Song/another.bms", "#TITLE Test"), ("Pack/Song/resources/tone.ogg", "audio"));
        var application = Path.Combine(root, "app"); string? imported = null; bool progress = false;
        await using var client = new CloudClient(() => [], () => "auto", path => { imported = path; return Task.CompletedTask; }, application);
        client.Changed += () => progress |= client.Progress?.Stage == "Installing";
        await client.ImportPack(zip, default);
        Assert.StartsWith(LibraryFolders.Shared(application) + Path.DirectorySeparatorChar, imported!);
        Assert.Equal("audio", File.ReadAllText(Path.Combine(imported!, "Pack", "Song", "resources", "tone.ogg")));
        Assert.True(File.Exists(Path.Combine(imported!, "Pack", "Song", "another.bms")));
        Assert.True(progress); Assert.False(client.Busy);
        Assert.Equal(imported, await SongPackFiles.Install(zip, application, default));
        File.WriteAllText(Path.Combine(imported!, "Pack", "Song", "normal.bms"), "modified");
        await Assert.ThrowsAsync<InvalidDataException>(() => SongPackFiles.Install(zip, application, default));
        Assert.Equal("modified", File.ReadAllText(Path.Combine(imported!, "Pack", "Song", "normal.bms")));
    }

    [Theory]
    [InlineData("../escaped.bms")]
    [InlineData("/escaped.bms")]
    [InlineData("song/NUL.bms")]
    [InlineData("song/tool.exe")]
    [InlineData("song/chart.bms:stream")]
    public async Task UnsafePacksNeverAppearInTheLibrary(string unsafePath)
    {
        var zip = Zip(("normal.bms", "#TITLE Test"), (unsafePath, "invalid"));
        var application = Path.Combine(root, "app");
        await Assert.ThrowsAsync<InvalidDataException>(() => SongPackFiles.Install(zip, application, default));
        Assert.Empty(Directory.GetDirectories(LibraryFolders.Shared(application), "pack-*"));
        Assert.Empty(Directory.GetDirectories(Path.Combine(LibraryFolders.Shared(application), ".incoming")));
        Assert.False(File.Exists(Path.Combine(root, "escaped.bms")));
    }

    [Theory]
    [InlineData("Song/normal.bms", "song/NORMAL.bms")]
    [InlineData("song/file.txt", "song/file.txt/normal.bms")]
    public async Task ConflictingWindowsPathsAreRejected(string first, string second)
    {
        var zip = Zip((first, "#TITLE Test"), (second, "#TITLE Test"));
        await Assert.ThrowsAsync<InvalidDataException>(() => SongPackFiles.Install(zip, Path.Combine(root, "app"), default));
    }

    [Fact]
    public async Task InterruptedImportsDoNotExposePartialSongs()
    {
        var zip = Zip(("song/normal.bms", "#TITLE Test"), ("song/audio.ogg", "audio"));
        var application = Path.Combine(root, "app");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SongPackFiles.Install(zip, application, cancelled.Token));
        Assert.Empty(Directory.GetDirectories(LibraryFolders.Shared(application), "pack-*"));
    }

    [Fact]
    public async Task SymbolicLinksAndResourceOnlyArchivesAreRejected()
    {
        var application = Path.Combine(root, "app");
        var resourceOnly = Zip(("song/audio.ogg", "audio"));
        await Assert.ThrowsAsync<InvalidDataException>(() => SongPackFiles.Install(resourceOnly, application, default));
        var linked = Zip(("song/normal.bms", "#TITLE Test"));
        using (var archive = ZipFile.Open(linked, ZipArchiveMode.Update)) archive.Entries[0].ExternalAttributes = unchecked((int)0xA0000000);
        await Assert.ThrowsAsync<InvalidDataException>(() => SongPackFiles.Install(linked, application, default));
        Assert.Empty(Directory.GetDirectories(LibraryFolders.Shared(application), "pack-*"));
    }

    [Theory]
    [InlineData("https://lazerrave.com/packs/3e37f380-2d12-4d7d-b630-e163a80224c3")]
    [InlineData("https://lazerrave.com/api/packs/3e37f380-2d12-4d7d-b630-e163a80224c3/download")]
    public void BothWebsiteLinkFormsResolve(string link) => Assert.Equal(Guid.Parse("3e37f380-2d12-4d7d-b630-e163a80224c3"), CloudClient.PackId(link, new Uri("https://lazerrave.com")));

    [Theory]
    [InlineData("https://example.com/packs/3e37f380-2d12-4d7d-b630-e163a80224c3")]
    [InlineData("http://lazerrave.com/packs/3e37f380-2d12-4d7d-b630-e163a80224c3")]
    [InlineData("https://lazerrave.com/packs/3e37f380-2d12-4d7d-b630-e163a80224c3?redirect=https://example.com")]
    public void DownloadLinksCannotChangeTheServer(string link) => Assert.Throws<ArgumentException>(() => CloudClient.PackId(link, new Uri("https://lazerrave.com")));

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
