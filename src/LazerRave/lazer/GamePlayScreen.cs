using static LazerRave.Lazer.LazerRaveText;
using LazerRave.Bridge;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Screens;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Screens;
using osuTK;
using osuTK.Graphics;

namespace LazerRave.Lazer;

internal partial class GamePlayScreen(EngineBridge bridge, FrontendSettings settings, Chart chart, NativeGameViewport viewport, Action<string?> returned) : OsuScreen
{
    private readonly CancellationTokenSource session = new();
    private readonly Container area = new() { RelativeSizeAxes = Axes.Both };
    private OsuSpriteText status = null!;
    private bool finished;
    public PlayRecordStore? Records { get; init; }
    public string RecordPlayer { get; init; } = "Player";
    public string? ReplaySource { get; init; }
    public PlayRecord? ReplayRecord { get; init; }
    public string? ExpectedChartHash { get; init; }
    public CloudClient? MultiplayerClient { get; init; }
    public Guid? MatchId { get; init; }
    public override string Title => chart.Title;
    public override bool ShowFooter => false;
    public override bool CursorVisible => settings.Presentation == "standalone";
    public override bool HideOverlaysOnEnter => true;
    public override bool? AllowGlobalTrackControl => false;
    protected override osu.Game.Overlays.OverlayActivation InitialOverlayActivationMode => osu.Game.Overlays.OverlayActivation.Disabled;
    public override float BackgroundParallaxAmount => 0;
    protected override void LoadComplete()
    {
        base.LoadComplete();
        AddInternal(new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black });
        if (MultiplayerClient is { } client && MatchId is { } match)
        {
            AddInternal(new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                ColumnDimensions = [new Dimension(), new Dimension(GridSizeMode.Absolute, 290)],
                Content = new Drawable?[][] { [area, new CloudLeaderboard(client, match, true) { RelativeSizeAxes = Axes.Both }] },
            });
        }
        else AddInternal(area);
        AddInternal(status = new OsuSpriteText { Text = D("Loading…"), Position = new Vector2(24, 24), Font = OsuFont.GetFont(size: 17) });
    }
    public override void OnEntering(ScreenTransitionEvent e) { base.OnEntering(e); _ = RunAsync(); }
    private PixelRect Bounds()
    {
        var box = area.ScreenSpaceDrawQuad.AABB;
        return new(box.X, box.Y, Math.Max(1, box.Width), Math.Max(1, box.Height));
    }
    private async Task RunAsync()
    {
        string? failure = null;
        await using var scores = MultiplayerClient is { } client && MatchId is { } match ? new CloudScoreSession(client, match) : null;
        var run = Guid.NewGuid();
        GameplaySnapshot latest = GameplaySnapshot.Empty;
        string? hash = null;
        void Score(GameplaySnapshot value)
        {
            latest = value; scores?.Update(value);
            if (scores is not null && value.Finished && !value.Aborted) Schedule(() => session.Cancel());
        }
        Action<GameplaySnapshot> report = Score;
        try
        {
            hash = await PlayRecordStore.HashChart(chart.Path, session.Token);
            if ((ReplayRecord?.ChartHash ?? ExpectedChartHash) is { } expected && !string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The BMS chart has changed or does not match this replay or round.");
            var playSettings = ReplayRecord?.PlaybackSettings(settings) ?? settings;
            if (ReplaySource is not null) ReplayFile.Validate(ReplaySource, ReplayRecord?.ReplayHash, ReplayRecord?.ReplaySize);
            if (settings.Presentation == "standalone")
            {
                Schedule(() => status.Text = D("Playing in a separate window…"));
                await bridge.Play(playSettings, chart, session.Token, progress: report, replaySource: ReplaySource, replayDestination: ReplaySource is null ? Records?.ReplayPath(run) : null);
            }
            else
            {
                var target = await viewport.OpenAsync(Bounds(), settings.Width, settings.Height, session.Token);
                await bridge.Play(playSettings, chart, session.Token, target, (window, process) =>
                {
                    viewport.BindEngine(window, process);
                    Schedule(() => status.Hide());
                }, report, ReplaySource, ReplaySource is null ? Records?.ReplayPath(run) : null);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { failure = error.Message; }
        finally
        {
            try { if (settings.Presentation == "embedded") await viewport.CloseAsync(); }
            catch (Exception error) { failure ??= error.Message; }
            if (ReplaySource is null && Records is not null && hash is not null && latest.Finished && !latest.Aborted)
            {
                try
                {
                    var saved = Records.Save(run, hash, chart, RecordPlayer, settings, latest);
                    if (saved?.ReplayError is { } replayError) failure ??= "Play record saved; replay unavailable: " + replayError;
                }
                catch (Exception error) { failure ??= "Cannot save play record: " + error.Message; }
            }
            if (scores is not null)
            {
                try { await scores.Finish(); }
                catch (Exception error) { failure ??= "Result upload failed: " + error.Message; }
            }
            if (!IsDisposed) Schedule(() => { finished = true; this.Exit(); returned(failure); });
        }
    }
    public void RequestReturn() { if (!finished) { status.Text = D("Returning…"); session.Cancel(); } }
    public override bool OnBackButton() { RequestReturn(); return true; }
    public override bool OnExiting(ScreenExitEvent e) { if (!finished) { RequestReturn(); return true; } return base.OnExiting(e); }
    protected override void Update() { base.Update(); if (!finished && settings.Presentation == "embedded") viewport.SetBounds(Bounds(), settings.Width, settings.Height); }
    protected override void Dispose(bool isDisposing) { if (isDisposing && !IsDisposed) session.Cancel(); base.Dispose(isDisposing); }
}
