using LazerRave.Bridge;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Beatmaps;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Leaderboards;
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
    private readonly Dictionary<Guid, BmsScoreDetails> details = new();
    private PlayRecord[] history = [];
    private readonly Dictionary<Guid, string> replayPaths = new();
    private bool submitting;

    public override void RefetchScores()
    {
        request?.Cancel(); request?.Dispose(); request = new CancellationTokenSource();
        selected = game.SelectedChart;
        records.Clear(); replayPaths.Clear(); details.Clear(); history = [];
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
                var local = await Task.Run(() => game.Records.Read(hash), cancellation);
                Schedule(() =>
                {
                    if (cancellation.IsCancellationRequested) return;
                    history = local;
                    var scores = local.Where(record => record.Ranked).GroupBy(record => record.Player).Select(group =>
                        group.OrderByDescending(record => record.Score.ExScore).ThenBy(record => record.PlayedAt).First()).Select(record =>
                    {
                        records[record.Id] = record;
                        if (record.ReplayPath is { } replay) replayPaths[record.Id] = replay;
                        var score = Score(info, record.Player, record.Score.ExScore, record.Score.TotalNotes * 2, record.Score.MaxCombo, record.PlayedAt);
                        score.ID = record.Id;
                        details[score.ID] = BmsScoreDetails.From(record, local);
                        details[score.ID].Apply(score);
                        return score;
                    }).ToList();
                    string player = game.Cloud.User?.Username ?? game.PlaySettings.Value.Player;
                    if (chart.Score is { } legacyScore && !scores.Any(score => score.User.Username == player && score.TotalScore >= legacyScore))
                    {
                        var legacy = Score(info, player, legacyScore, chart.Notes * 2, 0, DateTimeOffset.UnixEpoch);
                        scores.RemoveAll(score => score.User.Username == player);
                        scores.Add(legacy);
                        details[legacy.ID] = new(null, null, null, null, null, null, chart.Notes, "—", "—", "—") { LegacyReplayPath = chart.LegacyReplayPath };
                        if (chart.LegacyReplayPath is { } replay && File.Exists(replay)) replayPaths[legacy.ID] = replay;
                    }
                    foreach (var score in scores) score.Position = 1 + scores.Count(other => other.TotalScore > score.TotalScore);
                    var best = scores.Where(score => score.User.Username == player).OrderByDescending(score => score.TotalScore).FirstOrDefault();
                    IEnumerable<ScoreInfo> ordered = sorting switch
                    {
                        LeaderboardSortMode.Date => scores.OrderByDescending(score => score.Date),
                        LeaderboardSortMode.Accuracy => scores.OrderByDescending(score => score.Accuracy),
                        LeaderboardSortMode.MaxCombo => scores.OrderByDescending(score => score.MaxCombo),
                        LeaderboardSortMode.Misses => scores.OrderBy(score => details[score.ID].MinBp ?? int.MaxValue),
                        _ => scores.OrderByDescending(score => score.TotalScore).ThenBy(score => score.Date),
                    };
                    SetScores(ordered.ToArray(), best, scores.Count);
                });
            }
            else
            {
                var target = await game.Cloud.FindRankingChart(chart, hash, cancellation);
                if (target is null) { Schedule(() => { if (!cancellation.IsCancellationRequested) SetState(LeaderboardState.BeatmapUnavailable); }); return; }
                var ranking = await game.Cloud.ChartRanking(target.Id, "all", "all", 1, cancellation);
                var local = await Task.Run(() => game.Records.Read(hash), cancellation);
                Schedule(() =>
                {
                    if (cancellation.IsCancellationRequested) return;
                    history = local;
                    var scores = ranking.Select(rank =>
                    {
                        var score = Score(info, rank.Username, rank.ExScore, rank.ScoreMax ?? chart.Notes * 2, rank.MaxCombo, rank.CreatedAt);
                        if (rank.Id != Guid.Empty) score.ID = rank.Id;
                        score.Position = (int)Math.Min(int.MaxValue, rank.Rank);
                        var record = local.FirstOrDefault(record => record.Id == rank.ClientRunId && record.Player == rank.Username);
                        details[score.ID] = new(rank.NormalScore, rank.Perfect, rank.Great, rank.Good, rank.Bad, rank.Poor,
                            rank.ScoreMax is { } maximum ? maximum / 2 : chart.Notes, ClearName(rank.Clear), rank.Arrangement, rank.Gauge,
                            rank.BestClear is { } clear ? ClearName(clear) : null, rank.MinMisses, record);
                        details[score.ID].Apply(score);
                        if (record is not null)
                        {
                            records[score.ID] = record;
                            if (record.ReplayPath is { } replay) replayPaths[score.ID] = replay;
                        }
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
        var score = BmsScoreDetails.CreateScore(info, player, exScore, maximum, combo, date);
        if (player == game.Cloud.User?.Username)
            score.User = new APIUser { Id = 0, Username = player, AvatarUrl = game.Cloud.AvatarUri?.AbsoluteUri };
        return score;
    }
    private static string ClearName(string clear) => clear switch
        { "failed" => "FAILED", "easy" => "EASY CLEAR", "normal" => "CLEAR", "hard" => "HARD CLEAR", "full-combo" => "FULL COMBO", "perfect" => "PERFECT", _ => clear.ToUpperInvariant() };

    protected override BeatmapLeaderboardScore CreateScoreDrawable(ScoreInfo score, int? rank, BeatmapLeaderboardScore.HighlightType? highlight, bool personalBest)
    {
        var chart = selected!;
        var menu = new List<MenuItem>();
        records.TryGetValue(score.ID, out var record);
        var scoreDetails = details[score.ID];
        var plays = history;
        Action showDetails = () => game.ShowScoreDetails(score, scoreDetails, chart, plays);
        menu.Add(new OsuMenuItem(D("Score details"), MenuItemType.Standard, showDetails));
        Action? replay = null;
        if (replayPaths.TryGetValue(score.ID, out string? path))
        {
            replay = () => { if (Equals(game.SelectedChart, chart)) game.WatchReplay(path, record); };
            menu.Add(new OsuMenuItem(D("Watch replay"), MenuItemType.Standard, replay));
        }
        if (record is not null && record.Player == game.Cloud.User?.Username)
        {
            menu.Add(new OsuMenuItem(D("Submit score to public IR"), MenuItemType.Standard, () => { if (!submitting) _ = Submit(chart, record); }));
            menu.Add(new OsuMenuItem(D("Submit to private ranking"), MenuItemType.Standard, () => { if (!submitting) _ = Submit(chart, record, true); }));
        }
        return new BeatmapLeaderboardScore(score)
        {
            Rank = rank, Highlight = highlight, DisplayScore = new Bindable<string>($"EX {score.TotalScore:N0}"),
            Action = showDetails, ShowReplay = replay is null ? null : _ => replay(), ContextMenuItemsOverride = menu.ToArray(),
        };
    }

    private async Task Submit(Chart chart, PlayRecord record, bool privateRanking = false)
    {
        submitting = true;
        game.SetLibraryMessage(D("Submitting…"));
        try
        {
            await game.Cloud.SubmitRecord(chart, record, request!.Token, privateRanking);
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
