using System.IO.Compression;
using Cloud;
using Xunit;

namespace Cloud.Tests;

public sealed class ArchiveTests
{
    private static string Zip(params (string Name, string Text, int Attributes)[] entries)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var item in entries)
        {
            var entry = archive.CreateEntry(item.Name); entry.ExternalAttributes = item.Attributes;
            using var writer = new StreamWriter(entry.Open()); writer.Write(item.Text);
        }
        return path;
    }
    [Fact]
    public async Task ValidArchiveProducesContentIdentityAndMetadata()
    {
        var path = Zip(("曲目/normal.bms", "#TITLE 桜華月\n#ARTIST Fixture\n#PLAYLEVEL 10\n#00119:0100\n", 0), ("曲目/sound.wav", "fixture", 0));
        try
        {
            var charts = await ArchiveInspector.Inspect(path, new(), CancellationToken.None);
            var chart = Assert.Single(charts); Assert.Equal(7, chart.Keys); Assert.Equal(10, chart.Level);
            Assert.Equal("桜華月", chart.Title); Assert.Equal(64, chart.Sha256.Length); Assert.Equal(32, chart.Md5.Length);
        }
        finally { File.Delete(path); }
    }
    [Theory]
    [InlineData("../escape.bms")]
    [InlineData("C:/escape.bms")]
    [InlineData("/escape.bms")]
    [InlineData("CON.bms")]
    [InlineData("folder/trailing .bms/../song.bms")]
    [InlineData("script.exe")]
    public async Task UnsafeEntriesAreRejected(string name)
    {
        var path = Zip((name, "#TITLE Fixture", 0));
        try { await Assert.ThrowsAsync<ApiError>(() => ArchiveInspector.Inspect(path, new(), CancellationToken.None)); }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task LinksAndCaseConflictsAreRejected()
    {
        foreach (var entries in new[] {
            new[] { ("a.bms", "#TITLE Fixture", 0xA000 << 16) },
            new[] { ("a.bms", "#TITLE Fixture", 0), ("A.bms", "#TITLE Other", 0) },
            new[] { ("a.bms", "#TITLE Fixture", 0), ("a.bms/nested.bms", "#TITLE Other", 0) },
        })
        {
            var path = Zip(entries);
            try { await Assert.ThrowsAsync<ApiError>(() => ArchiveInspector.Inspect(path, new(), CancellationToken.None)); }
            finally { File.Delete(path); }
        }
    }
    [Fact]
    public async Task ExpandedQuotaAndMissingChartsAreRejected()
    {
        var path = Zip(("a.bms", new string('x', 10000), 0));
        try { await Assert.ThrowsAsync<ApiError>(() => ArchiveInspector.Inspect(path, new() { MaxExpandedBytes = 100 }, CancellationToken.None)); }
        finally { File.Delete(path); }
        path = Zip(("sound.wav", "fixture", 0));
        try { await Assert.ThrowsAsync<ApiError>(() => ArchiveInspector.Inspect(path, new(), CancellationToken.None)); }
        finally { File.Delete(path); }
    }
}
