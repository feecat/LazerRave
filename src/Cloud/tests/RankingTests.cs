using Cloud;
using Xunit;

namespace Cloud.Tests;

public sealed class RankingTests
{
    private static ScoreInput Valid => new(Guid.NewGuid(), Guid.NewGuid(), "openlr2-v1", "off", "normal", 10, 5, 3, 2, 1, 10, "normal");
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
        foreach (var score in new[] { Valid with { Perfect = -1 }, Valid with { MaxCombo = 100 }, Valid with { Arrangement = "invalid" }, Valid with { Ruleset = "other" } })
            Assert.Throws<ApiError>(() => Ranking.Validate(score));
    }
}
