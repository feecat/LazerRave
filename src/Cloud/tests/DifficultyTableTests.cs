using Cloud;
using Xunit;

namespace Cloud.Tests;

public sealed class DifficultyTableTests
{
    private static TableInput Valid => new("BMS table", "★", "", "https://example.com/table", [new(new string('A', 32), " 1 ", "Chart")]);
    [Fact]
    public void NormalizesHashesAndLevelsWithoutRequiringAnUploadedSong()
    {
        var result = DifficultyTables.Validate(Valid);
        Assert.Equal(new string('a', 32), result.Entries[0].Md5);
        Assert.Equal("1", result.Entries[0].Level);
    }
    [Fact]
    public void RejectsDuplicateHashesAndInvalidLevels()
    {
        Assert.Throws<ApiError>(() => DifficultyTables.Validate(Valid with { Entries = [Valid.Entries[0], Valid.Entries[0] with { Md5 = new string('a', 32) }] }));
        Assert.Throws<ApiError>(() => DifficultyTables.Validate(Valid with { Entries = [Valid.Entries[0] with { Level = " " }] }));
        Assert.Throws<ApiError>(() => DifficultyTables.Validate(Valid with { Entries = [Valid.Entries[0] with { Md5 = "invalid" }] }));
    }
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows")]
    [InlineData("https://user:password@example.com")]
    public void RejectsExecutableOrCredentialBearingLinks(string link)
    {
        Assert.Throws<ApiError>(() => DifficultyTables.Validate(Valid with { SourceUrl = link }));
        Assert.Throws<ApiError>(() => DifficultyTables.Validate(Valid with { Entries = [Valid.Entries[0] with { Url = link }] }));
    }
}
