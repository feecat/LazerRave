using LazerRave.Bridge;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Beatmaps;
using osu.Game.Graphics.UserInterface;
using osu.Game.Models;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Leaderboards;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play.Leaderboards;
using osu.Game.Screens.Select;
using static LazerRave.Lazer.LazerRaveText;

namespace LazerRave.Lazer;

internal partial class LazerRaveRankingArea : BeatmapDetailsArea
{
    protected override Header CreateHeader() => new Header { RankingOnly = true };
    protected override BeatmapLeaderboardWedge CreateLeaderboard() => new LazerRaveLeaderboardWedge();
}

internal partial class LazerRaveLeaderboardWedge : BeatmapLeaderboardWedge
{
    [Resolved] private LazerRaveGame game { get; set; } = null!;
    [Resolved] private IBindable<WorkingBeatmap> beatmap { get; set; } = null!;
    private CancellationTokenSource? request;
    private Chart? selected;
    private readonly Dictionary<Guid, PlayRecord> records = new();
    private readonly Dictionary<Guid, string> replayPaths = new();
    private bool submitting;

    public override void RefetchScores()
    {
        request?.Cancel(); request?.Dispose(); request = new CancellationTokenSource();
        selected = game.SelectedChart;
        records.Clear(); replayPaths.Clear();
        SetScores(Array.Empty<ScoreInfo>());
        if (selected is null) { SetState(LeaderboardState.NoneSelected); return; }
        SetState(LeaderboardState.Retrieving);
        _ = Fetch(selected, beatmap.Value.BeatmapInfo, Scope.Value, Sorting.Value, request.Token);
    }

    protected override void Update()
    {
        base.Update();
        if (!Equals(selected, game.SelectedChart)) RefetchScores();
    }

    private async Task Fetch(Chart chart, BeatmapInfo info, BeatmapLeaderboardScope scope, LeaderboardSortMode sorting, CancellationToken cancellation)
    {
        try
        {
            string hash = await PlayRecordStore.HashChart(chart.Path, cancellation);
            if (scope == BeatmapLeaderboardScope.Local)
            {
                var local = await Task.Run(() => game.Records.Read(hash).Where(record => record.Ranked).ToArray(), cancellation);
                Schedule(() =>
                {
                    if (cancellation.IsCancellationRequested) return;
                    var scores = local.Select(record =>
                    {
                        records[record.Id] = record;
                        if (record.ReplayPath is { } replay) replayPaths[record.Id] = replay;
                        var score = Score(info, record.Player, record.Score.ExScore, record.Score.TotalNotes * 2, record.Score.MaxCombo, record.PlayedAt);
                        score.ID = record.Id;
                        score.Statistics = new Dictionary<HitResult, int>
                        {
                            [HitResult.Perfect] = record.Score.Perfect, [HitResult.Great] = record.Score.Great,
                            [HitResult.Good] = record.Score.Good, [HitResult.Meh] = record.Score.Bad, [HitResult.Miss] = record.Score.Poor,
                        };
                        return score;
                    }).ToList();
                    string player = game.Cloud.User?.Username ?? game.PlaySettings.Value.Player;
                    if (chart.Score is { } legacyScore && !scores.Any(score => score.User.Username == player && score.TotalScore >= legacyScore))
                    {
                        var legacy = Score(info, player, legacyScore, chart.Notes * 2, 0, DateTimeOffset.UnixEpoch);
                        scores.Add(legacy);
                        if (chart.LegacyReplayPath is { } replay && File.Exists(replay)) replayPaths[legacy.ID] = replay;
                    }
                    foreach (var score in scores) score.Position = 1 + scores.Count(other => other.TotalScore > score.TotalScore);
                    var best = scores.Where(score => score.User.Username == player).OrderByDescending(score => score.TotalScore).FirstOrDefault();
                    IEnumerable<ScoreInfo> ordered = sorting switch
                    {
                        LeaderboardSortMode.Date => scores.OrderByDescending(score => score.Date),
                        LeaderboardSortMode.Accuracy => scores.OrderByDescending(score => score.Accuracy),
                        LeaderboardSortMode.MaxCombo => scores.OrderByDescending(score => score.MaxCombo),
                        LeaderboardSortMode.Misses => scores.OrderBy(score => score.Statistics.GetValueOrDefault(HitResult.Miss)),
                        _ => scores.OrderByDescending(score => score.TotalScore).ThenBy(score => score.Date),
                    };
                    SetScores(ordered.ToArray(), best, scores.Count);
                });
            }
            else
            {
                var target = await game.Cloud.FindRankingChart(chart, hash, cancellation);
                if (target is null) { Schedule(() => { if (!cancellation.IsCancellationRequested) SetState(LeaderboardState.BeatmapUnavailable); }); return; }
                var settings = game.PlaySettings.Value;
                string gauge = PlayOptionCatalog.All.Single(option => option.Name == "gauge").Choices![settings.PlayOptions.GetValueOrDefault("gauge")].ToLowerInvariant();
                var ranking = await game.Cloud.ChartRanking(target.Id, settings.Arrangement, gauge, 1, cancellation);
                Schedule(() =>
                {
                    if (cancellation.IsCancellationRequested) return;
                    var scores = ranking.Select(rank =>
                    {
                        var score = Score(info, rank.Username, rank.ExScore, rank.ScoreMax ?? chart.Notes * 2, rank.MaxCombo, rank.CreatedAt);
                        score.Position = (int)Math.Min(int.MaxValue, rank.Rank);
                        score.Statistics[HitResult.Miss] = rank.Misses;
                        return score;
                    }).ToArray();
                    SetScores(scores, scores.FirstOrDefault(score => score.User.Username == game.Cloud.User?.Username));
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Schedule(() =>
            {
                if (cancellation.IsCancellationRequested) return;
                SetState(LeaderboardState.NetworkFailure);
                game.SetLibraryMessage(error.Message);
            });
        }
    }

    private ScoreInfo Score(BeatmapInfo info, string player, int exScore, int maximum, int combo, DateTimeOffset date)
    {
        double accuracy = maximum > 0 ? Math.Clamp((double)exScore / maximum, 0, 1) : 0;
        int grade = maximum > 0 ? (int)Math.Clamp((long)exScore * 9 / maximum, 0, 8) : -1;
        var score = new ScoreInfo(info, info.Ruleset, new RealmUser { OnlineID = 0, Username = player })
        {
            TotalScore = exScore, TotalScoreWithoutMods = exScore, LegacyTotalScore = exScore,
            MaxCombo = combo, Accuracy = accuracy, Date = date, Ranked = true,
            CustomRankLabel = grade switch { 8 => "AAA", 7 => "AA", 6 => "A", 5 => "B", 4 => "C", 3 => "D", 2 => "E", 1 or 0 => "F", _ => "—" },
            Rank = grade switch { 8 => ScoreRank.X, 7 => ScoreRank.S, 6 => ScoreRank.A, 5 => ScoreRank.B, 4 => ScoreRank.C, 3 or 2 => ScoreRank.D, _ => ScoreRank.F },
        };
        if (player == game.Cloud.User?.Username)
            score.User = new APIUser { Id = 0, Username = player, AvatarUrl = game.Cloud.AvatarUri?.AbsoluteUri };
        return score;
    }

    protected override BeatmapLeaderboardScore CreateScoreDrawable(ScoreInfo score, int? rank, BeatmapLeaderboardScore.HighlightType? highlight, bool personalBest)
    {
        var chart = selected!;
        var menu = new List<MenuItem>();
        Action? replay = null;
        if (replayPaths.TryGetValue(score.ID, out string? path))
        {
            replay = () => { if (Equals(game.SelectedChart, chart)) game.WatchReplay(path); };
            menu.Add(new OsuMenuItem(D("Watch replay"), MenuItemType.Standard, replay));
        }
        if (records.TryGetValue(score.ID, out var record) && record.Player == game.Cloud.User?.Username)
            menu.Add(new OsuMenuItem(D("Submit score"), MenuItemType.Standard, () => { if (!submitting) _ = Submit(chart, record); }));
        return new BeatmapLeaderboardScore(score)
        {
            Rank = rank, Highlight = highlight, DisplayScore = new Bindable<string>($"EX {score.TotalScore:N0}"),
            Action = replay, ShowReplay = replay is null ? null : _ => replay(), ContextMenuItemsOverride = menu.ToArray(),
        };
    }

    private async Task Submit(Chart chart, PlayRecord record)
    {
        submitting = true;
        game.SetLibraryMessage(D("Submitting…"));
        try
        {
            await game.Cloud.SubmitRecord(chart, record, request!.Token);
            Schedule(() => game.SetLibraryMessage(D("Score submitted")));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Schedule(() => game.SetLibraryMessage(error.Message)); }
        finally { submitting = false; }
    }

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing) { request?.Cancel(); request?.Dispose(); }
        base.Dispose(isDisposing);
    }
}
