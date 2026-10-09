using LazerRave.Bridge;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Screens;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.OnlinePlay.Match.Components;
using osu.Game.Screens.Ranking;
using osu.Game.Screens.Ranking.Expanded.Accuracy;
using osu.Game.Screens.Ranking.Statistics;
using osuTK;
using osuTK.Graphics;
using static LazerRave.Lazer.LazerRaveText;

namespace LazerRave.Lazer;

internal partial class LazerRaveScoreDetailsScreen(ScoreInfo initialScore, BmsScoreDetails initialDetails, Chart chart, PlayRecord[] history, Action returned) : OsuScreen
{
    [Resolved] private LazerRaveGame game { get; set; } = null!;
    [Cached] private readonly OverlayColourProvider colours = new(OverlayColourScheme.Aquamarine);
    private Container panel = null!;
    private FillFlowContainer information = null!;
    private PurpleRoundedButton replay = null!;
    private BmsScoreDetails selected = null!;
    public override string Title => D("Score details").ToString();
    public override bool HideOverlaysOnEnter => true;
    public override bool? AllowGlobalTrackControl => false;
    public override bool DisallowExternalBeatmapRulesetChanges => true;

    protected override void LoadComplete()
    {
        base.LoadComplete();
        InternalChild = new GridContainer
        {
            RelativeSizeAxes = Axes.Both, Padding = new MarginPadding { Horizontal = 65, Top = 80, Bottom = 70 },
            ColumnDimensions = [new Dimension(GridSizeMode.Absolute, 400), new Dimension(GridSizeMode.Absolute, 35), new Dimension()],
            Content = new Drawable?[][] { [
                new OsuScrollContainer { RelativeSizeAxes = Axes.Both, Child = panel = new Container
                {
                    RelativeSizeAxes = Axes.X, Height = 640, Padding = new MarginPadding { Top = 35 },
                } }, null,
                new OsuScrollContainer { RelativeSizeAxes = Axes.Both, Child = information = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 14), Padding = new MarginPadding { Right = 15, Bottom = 20 },
                } },
            ] },
        };
        Present(initialScore, initialDetails);
    }
    private void Present(ScoreInfo score, BmsScoreDetails details)
    {
        selected = details; details.Apply(score);
        panel.Clear();
        panel.Add(new ScorePanel(score)
        {
            Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre, State = PanelState.Expanded,
            ExpandedContentFactory = value => new BmsExpandedScoreContent(value, details, chart),
        });
        information.Clear();
        information.Add(new SectionHeader(D("Score details")));
        string Text(int? number) => number?.ToString("N0") ?? "—";
        information.Add(new SimpleStatisticTable(2, new SimpleStatisticItem[]
        {
            Item("PGREAT", Text(details.Perfect)), Item("GREAT", Text(details.Great)), Item("GOOD", Text(details.Good)),
            Item("BAD", Text(details.Bad)), Item("POOR", Text(details.Poor)), Item("BP", Text(details.Bad is { } bad && details.Poor is { } poor ? bad + poor : null)),
            Item("SCORE", Text(details.NormalScore)), Item("MAX COMBO", score.MaxCombo.ToString("N0")),
            Item("Total notes", Text(details.TotalNotes)), Item("Clear", details.Clear),
            Item("Arrangement", details.Arrangement.ToUpperInvariant()), Item("Gauge", details.Gauge.ToUpperInvariant()),
            Item("Played at", score.Date == DateTimeOffset.UnixEpoch ? "—" : score.Date.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
            Item("Record", details.Record is { Ranked: false } ? D("Practice").ToString() : D("Ranked").ToString()),
        }));
        if (details.Record is { } playedRecord)
        {
            information.Add(new SimpleStatisticTable(2, new SimpleStatisticItem[]
            {
                Item("Speed", playedRecord.Speed?.ToString("0.00") ?? "—"), Item("Offset", playedRecord.Offset is { } offset ? $"{offset} ms" : "—"),
            }));
        }
        if (details.BestClear is not null || details.MinBp is not null)
        {
            information.Add(new SectionHeader(D("Personal best")));
            information.Add(new SimpleStatisticTable(2, new SimpleStatisticItem[] { Item("Best clear", details.BestClear ?? "—"), Item("Minimum BP", Text(details.MinBp)) }));
        }
        information.Add(replay = new PurpleRoundedButton
        {
            Text = D("Watch replay"), RelativeSizeAxes = Axes.X, Height = 45,
            Action = () =>
            {
                if (selected.ReplayPath is not { } path) return;
                var record = selected.Record;
                this.Exit(); game.QueueReplay(path, record);
            },
        });
        replay.Enabled.Value = details.ReplayPath is not null;
        if (details.Record?.ReplayError is { } replayError)
            information.Add(new OsuTextFlowContainer(text => text.Font = OsuFont.GetFont(size: 16)) { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Text = replayError, Colour = Color4.Orange });
        if (history.Length == 0) return;
        information.Add(new SectionHeader(D("Recent plays")));
        foreach (var record in history.Take(100))
            information.Add(new PurpleRoundedButton
            {
                RelativeSizeAxes = Axes.X, Height = 40,
                Text = $"{record.PlayedAt.ToLocalTime():MM-dd HH:mm} · {record.Player} · EX {record.Score.ExScore:N0} · {PlayRecord.ClearName(record.Score.ClearType)}",
                Action = () =>
                {
                    var value = BmsScoreDetails.CreateScore(initialScore.BeatmapInfo!, record.Player, record.Score.ExScore, record.Score.TotalNotes * 2, record.Score.MaxCombo, record.PlayedAt);
                    value.ID = record.Id;
                    if (record.Player == initialScore.User.Username) value.User = initialScore.User;
                    Present(value, BmsScoreDetails.From(record, history));
                },
            });
    }
    private static SimpleStatisticItem<string> Item(string name, string value) => new(D(name)) { Value = value, FontSize = 18, Margin = new MarginPadding { Vertical = 5 } };
    public override bool OnExiting(ScreenExitEvent e)
    {
        if (base.OnExiting(e)) return true;
        returned(); return false;
    }
}

internal partial class BmsExpandedScoreContent(ScoreInfo score, BmsScoreDetails details, Chart chart) : CompositeDrawable
{
    protected override void LoadComplete()
    {
        base.LoadComplete(); RelativeSizeAxes = Axes.Both;
        InternalChild = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical,
            Padding = new MarginPadding { Horizontal = 15, Top = 52 }, Spacing = new Vector2(0, 14),
            Children = new Drawable[]
            {
                new TruncatingSpriteText { Text = chart.Title, RelativeSizeAxes = Axes.X, Font = OsuFont.GetFont(size: 22, weight: FontWeight.SemiBold) },
                new TruncatingSpriteText { Text = chart.Artist, RelativeSizeAxes = Axes.X, Font = OsuFont.GetFont(size: 16) },
                new Container
                {
                    Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre, Size = new Vector2(230),
                    Children = new Drawable[]
                    {
                        new CircularProgress { RelativeSizeAxes = Axes.Both, InnerRadius = .14f, Progress = score.Accuracy, Colour = Color4.Aquamarine },
                        new RankText(score.Rank, score.CustomRankLabel) { Alpha = 1 },
                    },
                },
                new OsuSpriteText { Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre, Text = $"EX {score.TotalScore:N0} / {(details.TotalNotes is { } notes ? (notes * 2).ToString("N0") : "—")}", Font = OsuFont.Numeric.With(size: 30) },
                new OsuSpriteText { Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre, Text = $"{score.Accuracy:P2}", Font = OsuFont.GetFont(size: 24) },
                new OsuSpriteText { Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre, Text = details.Clear, Font = OsuFont.GetFont(size: 20, weight: FontWeight.SemiBold), Colour = Color4.LightGreen },
                new OsuSpriteText { Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre, Text = $"{chart.Keys}Key · Lv. {chart.Level}", Font = OsuFont.GetFont(size: 17) },
            },
        };
    }
}
