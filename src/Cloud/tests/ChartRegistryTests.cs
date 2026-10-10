using Cloud;
using Xunit;

namespace Cloud.Tests;

public sealed class ChartRegistryTests
{
    private static ChartRegistration Valid => new(new string('A', 64), new string('B', 32), "  Fixture  ", "Artist", "HYPER", 7, 10);
    [Fact]
    public void RegistrationNormalizesIdentityWithoutRequiringAResourceArchive()
    {
        var normalized = ChartRegistry.Validate(Valid);
        Assert.Equal(new string('a', 64), normalized.Sha256);
        Assert.Equal(new string('b', 32), normalized.Md5);
        Assert.Equal("Fixture", normalized.Title);
        Assert.Equal("public", normalized.Visibility);
        ChartRegistry.Validate(Valid with { Visibility = "restricted", Bpm = 180, LengthMs = 150000 });
    }
    [Fact]
    public void RejectsMalformedIdentityAndUnboundedMetadata()
    {
        foreach (var input in new[] { Valid with { Sha256 = "x" }, Valid with { Md5 = "x" }, Valid with { Title = " " }, Valid with { Keys = 6 }, Valid with { Level = -1 },
            Valid with { Visibility = "hidden" }, Valid with { Bpm = double.PositiveInfinity }, Valid with { LengthMs = double.NaN }, Valid with { LengthMs = 86400001 } })
            Assert.Throws<ApiError>(() => ChartRegistry.Validate(input));
    }
}
