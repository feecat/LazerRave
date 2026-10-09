using static LazerRave.Lazer.LazerRaveText;
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
    public override string Title => "Library";
    public override bool SupportsBeatmapManagement => false;
    public override bool? AllowGlobalTrackControl => false;
    public LazerRaveSongSelect() { ControlGlobalMusic = false; TopPadding = 80; }
    protected override bool IsBeatmapInScope(BeatmapInfo info) => game.IsChartVisible(info);
    protected override void OnStart() => game.StartGame(Beatmap.Value.BeatmapInfo);
    public override bool OnBackButton() => game.ParentFolder();
    public override IEnumerable<OsuMenuItem> GetForwardActions(BeatmapInfo beatmap)
    {
        yield return new OsuMenuItem(D("Play"), MenuItemType.Highlighted, () => SelectAndRun(beatmap, OnStart)) { Icon = FontAwesome.Solid.Play };
    }
    public override IReadOnlyList<ScreenFooterButton> CreateFooterButtons() => base.CreateFooterButtons().Select(button =>
        button is FooterButtonMods ? new ScreenFooterButton { Text = "OpenLR2", Icon = FontAwesome.Solid.Keyboard, Action = game.ToggleSettings } : button)
        .Append(new ScreenFooterButton { Text = D("Rescan"), Icon = FontAwesome.Solid.Sync, Action = game.Rescan }).ToArray();
    public override bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
    {
        if (!this.IsCurrentScreen()) return false;
        if (e.Action == GlobalAction.Select) { SelectAndRun(Beatmap.Value.BeatmapInfo, OnStart); return true; }
        if (e.Action == GlobalAction.Back) return game.ParentFolder();
        if (e.Action == GlobalAction.ToggleModSelection) { game.ToggleSettings(); return true; }
        return base.OnPressed(e);
    }
    public void RefreshKeyFilter()
    {
        if (!IsLoaded) return;
        FilterControl.ApplyRequiredCriteria = criteria =>
        {
            if (game.Keys == 0) return;
            int columns = BmsBeatmapStore.Columns(game.Keys);
            criteria.CircleSize.Min = columns; criteria.CircleSize.Max = columns;
            criteria.CircleSize.IsLowerInclusive = true; criteria.CircleSize.IsUpperInclusive = true;
        };
        RefreshFilter();
    }
    protected override void LoadComplete() { base.LoadComplete(); RefreshKeyFilter(); }
    protected override bool OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Key.Delete) return true;
        if (e.Key == Key.BackSpace) { game.ParentFolder(); return true; }
        return base.OnKeyDown(e);
    }
}
