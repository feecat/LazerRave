using Cloud;
using Xunit;

namespace Cloud.Tests;

public sealed class RankingTests
{
    private static ScoreInput Valid => new(Guid.NewGuid(), Guid.NewGuid(), "openlr2-v1", "off", "normal", 10, 5, 3, 2, 1, 10, "normal");
    [Fact]
    public void RankingFiltersAcceptAggregateAndSpecificOptionsButRejectUnknownValues()
    {
        Ranking.ValidateFilter("all", "all");
        foreach (var arrangement in Ranking.Arrangements)
            foreach (var gauge in Ranking.Gauges) Ranking.ValidateFilter(arrangement, gauge, 10000);
        Assert.Throws<ApiError>(() => Ranking.ValidateFilter("invalid", "all"));
        Assert.Throws<ApiError>(() => Ranking.ValidateFilter("all", "invalid"));
        Assert.Throws<ApiError>(() => Ranking.ValidateFilter("all", "all", 0));
        Assert.Throws<ApiError>(() => Ranking.ValidateFilter("all", "all", 10001));
    }
    [Fact]
    public void AssistedAndAutoplayScoresAreExcluded()
    {
        Ranking.Validate(Valid);
        foreach (var score in new[] { Valid with { Autoplay = true }, Valid with { Assist = true }, Valid with { Modifiers = true } })
            Assert.Throws<ApiError>(() => Ranking.Validate(score));
    }
    [Fact]
    public void NegativeJudgementsAndImpossibleCombosAreRejected()
    {
        foreach (var score in new[] { Valid with { Perfect = -1 }, Valid with { MaxCombo = 100 }, Valid with { Arrangement = "invalid" }, Valid with { Ruleset = "other" }, Valid with { NormalScore = -1 } })
            Assert.Throws<ApiError>(() => Ranking.Validate(score));
    }
    [Fact]
    public void RejectsInconsistentMaximumAndInvalidInputMetadata()
    {
        Ranking.Validate(Valid with { ScoreMax = 50, InputType = "keyboard" });
        foreach (var score in new[] { Valid with { ScoreMax = 10 }, Valid with { InputType = "invalid" }, Valid with { Comment = new string('x', 201) } })
            Assert.Throws<ApiError>(() => Ranking.Validate(score));
    }
}
