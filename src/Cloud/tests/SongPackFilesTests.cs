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
        Assert.Equal(Path.Combine(LibraryFolders.Shared(application), "Pack"), imported);
        Assert.Equal("audio", File.ReadAllText(Path.Combine(imported!, "Song", "resources", "tone.ogg")));
        Assert.True(File.Exists(Path.Combine(imported!, "Song", "another.bms")));
        Assert.True(progress); Assert.False(client.Busy);
        Assert.Equal(imported, await SongPackFiles.Install(zip, application, default));
        File.WriteAllText(Path.Combine(imported!, "Song", "normal.bms"), "modified");
        await Assert.ThrowsAsync<InvalidDataException>(() => SongPackFiles.Install(zip, application, default));
        Assert.Equal("modified", File.ReadAllText(Path.Combine(imported!, "Song", "normal.bms")));
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
        Assert.Empty(Directory.GetFiles(LibraryFolders.Shared(application), "*.bms", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetDirectories(Path.Combine(LibraryFolders.Shared(application), ".incoming")));
        Assert.False(File.Exists(Path.Combine(root, "escaped.bms")));
    }

    [Fact]
    public async Task SingleSongIsInstalledDirectlyAndRetainsItsName()
    {
        var zip = Zip(("[0002]Pure Ruby/normal.bms", "#TITLE Pure Ruby"), ("[0002]Pure Ruby/tone.ogg", "audio"));
        var application = Path.Combine(root, "app");
        var installed = await SongPackFiles.Install(zip, application, default);
        Assert.Equal(Path.Combine(LibraryFolders.Shared(application), "[0002]Pure Ruby"), installed);
        Assert.Empty(Directory.GetDirectories(LibraryFolders.Shared(application), "pack-*"));
        Assert.Equal(installed, SongPackFiles.InstalledDirectory(application, await SongContent.HashFile(zip, default)));
    }

    [Fact]
    public async Task ConflictingSongsKeepBothVersionsAndRepeatedImportUsesTheSameFolder()
    {
        var first = Zip(("Song/normal.bms", "first"));
        var second = Zip(("Song/normal.bms", "second"));
        var application = Path.Combine(root, "app");
        var a = await SongPackFiles.Install(first, application, default);
        var b = await SongPackFiles.Install(second, application, default);
        Assert.NotEqual(a, b); Assert.EndsWith("Song (2)", b);
        Assert.Equal("first", File.ReadAllText(Path.Combine(a, "normal.bms")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(b, "normal.bms")));
        Assert.Equal(b, await SongPackFiles.Install(second, application, default));
    }

    [Fact]
    public async Task MultiSongPackHasNoAddedOuterFolder()
    {
        var zip = Zip(("One/normal.bms", "one"), ("Two/normal.bms", "two"));
        var application = Path.Combine(root, "app");
        Assert.Equal(LibraryFolders.Shared(application), await SongPackFiles.Install(zip, application, default));
        Assert.True(File.Exists(Path.Combine(LibraryFolders.Shared(application), "One", "normal.bms")));
        Assert.True(File.Exists(Path.Combine(LibraryFolders.Shared(application), "Two", "normal.bms")));
    }

    [Fact]
    public async Task MultiSongNameConflictsReserveUniqueDestinationsBeforeMoving()
    {
        var application = Path.Combine(root, "app");
        await SongPackFiles.Install(Zip(("Song/normal.bms", "original")), application, default);
        await SongPackFiles.Install(Zip(("Song/normal.bms", "second"), ("Song (2)/normal.bms", "third")), application, default);
        var shared = LibraryFolders.Shared(application);
        Assert.Equal("original", File.ReadAllText(Path.Combine(shared, "Song", "normal.bms")));
        Assert.Equal(3, Directory.GetFiles(shared, "normal.bms", SearchOption.AllDirectories).Length);
        Assert.Contains("second", Directory.GetFiles(shared, "normal.bms", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.Contains("third", Directory.GetFiles(shared, "normal.bms", SearchOption.AllDirectories).Select(File.ReadAllText));
    }

    [Fact]
    public async Task FlatArchiveUsesTheSuppliedSongName()
    {
        var zip = Zip(("normal.bms", "#TITLE Halcyon"), ("tone.ogg", "audio"));
        var application = Path.Combine(root, "app");
        var installed = await SongPackFiles.Install(zip, application, default, title: "Halcyon");
        Assert.Equal(Path.Combine(LibraryFolders.Shared(application), "Halcyon"), installed);
    }

    [Fact]
    public async Task LegacyHashFolderMigratesAndCanBeReimported()
    {
        var zip = Zip(("Song/normal.bms", "#TITLE Test"), ("Song/tone.ogg", "audio"));
        var application = Path.Combine(root, "app");
        var hash = await SongContent.HashFile(zip, default);
        var legacy = Path.Combine(LibraryFolders.Shared(application), "pack-" + hash);
        ZipFile.ExtractToDirectory(zip, legacy);
        LibraryFolders.Ensure(application);
        Assert.False(Directory.Exists(legacy));
        var installed = Path.Combine(LibraryFolders.Shared(application), "Song");
        Assert.Equal("audio", File.ReadAllText(Path.Combine(installed, "tone.ogg")));
        Assert.Equal(installed, await SongPackFiles.Install(zip, application, default));
    }

    [Fact]
    public void UnwritableMigrationRecordDoesNotPreventStartupOrLoseSongs()
    {
        var application = Path.Combine(root, "app");
        var legacy = Path.Combine(LibraryFolders.Shared(application), "pack-" + new string('a', 64), "Song");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "normal.bms"), "original");
        Directory.CreateDirectory(Path.Combine(application, "userdata"));
        File.WriteAllText(Path.Combine(application, "userdata", "song-packs"), "unavailable");
        LibraryFolders.Ensure(application);
        Assert.Equal("original", File.ReadAllText(Path.Combine(legacy, "normal.bms")));
        Assert.False(Directory.Exists(Path.Combine(LibraryFolders.Shared(application), "Song")));
    }

    [Fact]
    public async Task FlatLegacyArchiveMigratesAndCanBeReimportedWithItsTitle()
    {
        var zip = Zip(("normal.bms", "#TITLE Test"), ("tone.ogg", "audio"));
        var application = Path.Combine(root, "app");
        var hash = await SongContent.HashFile(zip, default);
        var legacy = Path.Combine(LibraryFolders.Shared(application), "pack-" + hash);
        ZipFile.ExtractToDirectory(zip, legacy);
        LibraryFolders.Ensure(application);
        Assert.False(Directory.Exists(legacy));
        var installed = SongPackFiles.InstalledDirectory(application, hash);
        Assert.Equal("audio", File.ReadAllText(Path.Combine(installed!, "tone.ogg")));
        Assert.Equal(installed, await SongPackFiles.Install(zip, application, default, title: "Test"));
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
        Assert.Empty(Directory.GetFiles(LibraryFolders.Shared(application), "*.bms", SearchOption.AllDirectories));
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
        Assert.Empty(Directory.GetFiles(LibraryFolders.Shared(application), "*.bms", SearchOption.AllDirectories));
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
