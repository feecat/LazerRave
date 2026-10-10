using LazerRave.Content;
using Xunit;

namespace Cloud.Tests;

public sealed class LibraryFoldersTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "lazerrave-library-" + Guid.NewGuid());

    [Fact]
    public void RequiredRootIsRestoredAndAliasesDoNotDuplicateTheLibrary()
    {
        Assert.Equal(new[] { LibraryFolders.DefaultRoot }, LibraryFolders.NormalizeRoots([], root));
        Assert.Equal(new[] { LibraryFolders.DefaultRoot, "Other" }, LibraryFolders.NormalizeRoots(
            ["BMS", @".\BMS", LibraryFolders.Bms(root), @"BMS\Shared", "Shared", "Other", Path.Combine(root, "Other"), " "], root));
    }

    [Fact]
    public void MissingDirectoriesAreCreatedAgainBeforeUse()
    {
        LibraryFolders.Ensure(root);
        Assert.True(Directory.Exists(LibraryFolders.Shared(root)));
        Directory.Delete(LibraryFolders.Bms(root), true);
        LibraryFolders.Ensure(root);
        Assert.True(Directory.Exists(LibraryFolders.Shared(root)));
        Assert.False(Directory.Exists(Path.Combine(root, "Shared")));
    }

    [Fact]
    public void LegacyDownloadsMoveUnderBmsWithResourcesIntact()
    {
        var legacy = Path.Combine(root, "Shared", "song", "sound");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "audio.ogg"), "fixture");
        LibraryFolders.Ensure(root);
        Assert.Equal("fixture", File.ReadAllText(Path.Combine(LibraryFolders.Shared(root), "song", "sound", "audio.ogg")));
        Assert.False(Directory.Exists(Path.Combine(root, "Shared")));
        LibraryFolders.Ensure(root);
    }

    [Fact]
    public void MigrationPreservesBothVersionsOnNameConflicts()
    {
        var legacy = Path.Combine(root, "Shared", "song");
        var current = Path.Combine(LibraryFolders.Shared(root), "song");
        Directory.CreateDirectory(legacy); Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(legacy, "normal.bms"), "old");
        File.WriteAllText(Path.Combine(current, "normal.bms"), "new");
        LibraryFolders.Ensure(root);
        Assert.Equal("new", File.ReadAllText(Path.Combine(current, "normal.bms")));
        var moved = Assert.Single(Directory.GetDirectories(LibraryFolders.Shared(root), "song-legacy-*"));
        Assert.Equal("old", File.ReadAllText(Path.Combine(moved, "normal.bms")));
        Assert.False(Directory.Exists(Path.Combine(root, "Shared")));
    }

    [Fact]
    public void IncomingMigrationStaysOutsideTheScannedLibrary()
    {
        var legacy = Path.Combine(root, "Shared", ".incoming");
        var current = Path.Combine(LibraryFolders.Shared(root), ".incoming");
        Directory.CreateDirectory(legacy); Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(legacy, "normal.bms"), "partial");
        File.WriteAllText(Path.Combine(current, "normal.bms"), "current");
        LibraryFolders.Ensure(root);
        Assert.Equal("current", File.ReadAllText(Path.Combine(current, "normal.bms")));
        var moved = Assert.Single(Directory.GetDirectories(current, "legacy-*"));
        Assert.Equal("partial", File.ReadAllText(Path.Combine(moved, "normal.bms")));
        Assert.Single(Directory.GetDirectories(LibraryFolders.Shared(root)));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
