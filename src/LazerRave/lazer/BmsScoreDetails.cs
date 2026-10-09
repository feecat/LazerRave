using LazerRave.Bridge;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace LazerRave.Lazer;

internal sealed record BmsScoreDetails(int? NormalScore, int? Perfect, int? Great, int? Good, int? Bad, int? Poor,
    int? TotalNotes, string Clear, string Arrangement, string Gauge, string? BestClear = null, int? MinBp = null, PlayRecord? Record = null)
{
    public string? LegacyReplayPath { get; init; }
    public string? ReplayPath => Record?.ReplayPath ?? (LegacyReplayPath is { } path && File.Exists(path) ? path : null);
    public static BmsScoreDetails From(PlayRecord record, IEnumerable<PlayRecord> history)
    {
        var personal = history.Where(value => value.Ranked && value.Player == record.Player).ToArray();
        var score = record.Score;
        int bestClear = personal.Select(value => value.Score.ClearType).DefaultIfEmpty(score.ClearType).Max();
        return new(score.NormalScore, score.Perfect, score.Great, score.Good, score.Bad, score.Poor, score.TotalNotes,
            PlayRecord.ClearName(score.ClearType), record.Arrangement, record.Gauge, PlayRecord.ClearName(bestClear),
            personal.Length == 0 ? null : personal.Min(value => value.Score.Bad + value.Score.Poor), record);
    }
    public void Apply(ScoreInfo score)
    {
        if (Perfect is null) return;
        score.Statistics = new Dictionary<HitResult, int>
        {
            [HitResult.Perfect] = Perfect.Value, [HitResult.Great] = Great ?? 0,
            [HitResult.Good] = Good ?? 0, [HitResult.Meh] = Bad ?? 0, [HitResult.Miss] = Poor ?? 0,
        };
        score.CustomStatistics = new[]
        {
            new HitResultDisplayStatistic(HitResult.Perfect, Perfect.Value, null, "PGREAT"),
            new HitResultDisplayStatistic(HitResult.Great, Great ?? 0, null, "GREAT"),
            new HitResultDisplayStatistic(HitResult.Good, Good ?? 0, null, "GOOD"),
            new HitResultDisplayStatistic(HitResult.Meh, Bad ?? 0, null, "BAD"),
            new HitResultDisplayStatistic(HitResult.Miss, Poor ?? 0, null, "POOR"),
        };
    }
    public static ScoreInfo CreateScore(BeatmapInfo info, string player, int exScore, int maximum, int combo, DateTimeOffset date)
    {
        int grade = maximum > 0 ? (int)Math.Clamp((long)exScore * 9 / maximum, 0, 8) : -1;
        return new ScoreInfo(info, info.Ruleset, new RealmUser { OnlineID = 0, Username = player })
        {
            TotalScore = exScore, TotalScoreWithoutMods = exScore, LegacyTotalScore = exScore,
            MaxCombo = combo, Accuracy = maximum > 0 ? Math.Clamp((double)exScore / maximum, 0, 1) : 0,
            Date = date, Ranked = true,
            CustomRankLabel = grade switch { 8 => "AAA", 7 => "AA", 6 => "A", 5 => "B", 4 => "C", 3 => "D", 2 => "E", 1 or 0 => "F", _ => "—" },
            Rank = grade switch { 8 => ScoreRank.X, 7 => ScoreRank.S, 6 => ScoreRank.A, 5 => ScoreRank.B, 4 => ScoreRank.C, 3 or 2 => ScoreRank.D, _ => ScoreRank.F },
        };
    }
}
