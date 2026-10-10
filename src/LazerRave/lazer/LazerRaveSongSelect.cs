using static LazerRave.Lazer.LazerRaveText;
using osu.Framework.Localisation;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Graphics.UserInterface;
using osu.Game.Input.Bindings;
using osu.Game.Screens.Footer;
using osu.Game.Screens.Select;
using osuTK.Input;

namespace LazerRave.Lazer;

internal partial class LazerRaveSongSelect : SoloSongSelect
{
    [Resolved] private LazerRaveGame game { get; set; } = null!;
    public Func<BeatmapInfo, CancellationToken, Task>? ConfirmSelection { get; init; }
    public string ConfirmationText { get; init; } = "Use selected difficulty";
    public bool IsRoomSelection => ConfirmSelection is not null;
    public string? InitialFolder { get; init; }
    public override void OnEntering(ScreenTransitionEvent e)
    {
        base.OnEntering(e);
        if (InitialFolder is not null) game.FocusLibraryFolder(InitialFolder);
    }
    private bool confirming;
    private readonly CancellationTokenSource selectionLifetime = new();
    [Resolved] private LocalisationManager localisation { get; set; } = null!;
    public override string Title => localisation?.GetLocalisedString(D(IsRoomSelection ? "Choose song" : "Library")) ?? (IsRoomSelection ? "Choose song" : "Library");
    public override bool SupportsBeatmapManagement => false;
    public override bool? AllowGlobalTrackControl => false;
    public LazerRaveSongSelect() { ControlGlobalMusic = false; TopPadding = 80; }
    protected override bool IsBeatmapInScope(BeatmapInfo info) => game.IsChartVisible(info);

    /// <summary>
    /// Folder rows occupy the list at levels that hold no songs, so an empty beatmap match is not an
    /// empty list and the "no results" placeholder must not cover them.
    /// </summary>
    protected override bool SuppressNoResultsPlaceholder => game.CurrentFolders.Count > 0;

    /// <summary>
    /// Uses the LazerRave carousel so the current level's folders are listed as rows above the songs,
    /// matching the folder hierarchy the catalog browses rather than osu!'s flat beatmap list.
    /// </summary>
    protected override BeatmapCarousel CreateCarousel() => new LazerRaveBeatmapCarousel(
        () => game.CurrentFolders,
        path => game.EnterFolder(path))
    {
        BleedTop = FilterControl.HEIGHT_FROM_SCREEN_TOP + 5,
        BleedBottom = ScreenFooter.HEIGHT + 5,
        RelativeSizeAxes = Axes.Both,
        RequestPresentBeatmap = b => SelectAndRun(b, OnStart),
        RequestSelection = CarouselRequestSelection,
        RequestRecommendedSelection = CarouselRequestRecommendedSelection,
        NewItemsPresented = CarouselNewItemsPresented,
    };

    protected override void OnStart()
    {
        if (!IsRoomSelection) game.StartGame(Beatmap.Value.BeatmapInfo);
        else if (!confirming) _ = ConfirmRoomSelection();
    }
    private async Task ConfirmRoomSelection()
    {
        confirming = true;
        bool confirmed = false;
        game.SetLibraryMessage(D("Checking local song resources…"));
        try
        {
            await ConfirmSelection!(Beatmap.Value.BeatmapInfo, selectionLifetime.Token);
            confirmed = true;
            Schedule(() => { if (this.IsCurrentScreen()) this.Exit(); });
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Schedule(() => game.SetLibraryMessage(error.Message)); }
        finally { if (!confirmed) confirming = false; }
    }
    public override bool OnBackButton() => game.ParentFolder();
    public override IEnumerable<OsuMenuItem> GetForwardActions(BeatmapInfo beatmap)
    {
        yield return new OsuMenuItem(D(IsRoomSelection ? ConfirmationText : "Play"), MenuItemType.Highlighted, () => SelectAndRun(beatmap, OnStart)) { Icon = IsRoomSelection ? FontAwesome.Solid.Check : FontAwesome.Solid.Play };
    }
    public override IReadOnlyList<ScreenFooterButton> CreateFooterButtons() => base.CreateFooterButtons().SelectMany(button =>
        button is FooterButtonMods
            ? new ScreenFooterButton[] { new LazerRavePlayButton("Gauge"), new LazerRavePlayButton("Speed"), new LazerRavePlayButton("Lanes") }
            : new[] { button }).ToArray();
    public override bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
    {
        if (!this.IsCurrentScreen()) return false;
        if (e.Action == GlobalAction.Select) { SelectAndRun(Beatmap.Value.BeatmapInfo, OnStart); return true; }
        if (e.Action == GlobalAction.Back) return game.ParentFolder();
        return base.OnPressed(e);
    }
    public void RefreshKeyFilter()
    {
        if (!IsLoaded) return;
        FilterControl.ApplyRequiredCriteria = criteria =>
        {
            game.SetLibrarySearch(criteria.SearchText);
            game.SetLibraryLevelRange(FilterControl.DifficultyMinimum, FilterControl.DifficultyMaximum);
            criteria.UserStarDifficulty.Min = null; criteria.UserStarDifficulty.Max = null;
            if (game.Keys == 0) return;
            criteria.CircleSize.Min = game.Keys; criteria.CircleSize.Max = game.Keys;
            criteria.CircleSize.IsLowerInclusive = true; criteria.CircleSize.IsUpperInclusive = true;
        };
        RefreshFilter();
    }
    protected override BeatmapDetailsArea CreateDetailsArea() => new LazerRaveRankingArea();
    protected override FilterControl CreateFilterControl() => new FilterControl { BmsDifficulty = true, MaximumBmsLevel = () => game.MaximumChartLevel };
    protected override void LoadComplete() { base.LoadComplete(); FilterControl.UseBmsFilters(); RefreshKeyFilter(); }
    public void ClearSearch() => FilterControl.Search(string.Empty);
    protected override bool OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Key.Delete) return true;
        if (e.Key == Key.BackSpace) { game.ParentFolder(); return true; }
        return base.OnKeyDown(e);
    }
    public override bool OnExiting(ScreenExitEvent e)
    {
        if (base.OnExiting(e)) return true;
        selectionLifetime.Cancel();
        return false;
    }
    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing) selectionLifetime.Cancel();
        base.Dispose(isDisposing);
    }
}
