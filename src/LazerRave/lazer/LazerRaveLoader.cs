using osu.Game.Screens;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Select;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Screens;

namespace LazerRave.Lazer;

internal partial class LazerRaveLoader : Loader
{
    protected override MainMenu CreateMainMenu() => new LazerRaveMainMenu();
}

internal partial class LazerRaveMainMenu : MainMenu
{
    protected override bool AutomaticallyShowLogin => false;
    protected override bool FlattenPlayMenu => true;
    protected override Drawable CreateSupporterDisplay() => Empty();
    protected override Drawable CreateMenuTipDisplay() => new LazerRaveVersionDisplay
    {
        Anchor = Anchor.TopCentre,
        Origin = Anchor.TopCentre,
    };
    [Resolved] private LazerRaveGame game { get; set; } = null!;
    protected override SongSelect CreateSongSelect() => new LazerRaveSongSelect();
    protected override void LoadComplete()
    {
        base.LoadComplete();
        Buttons.OnCustomMultiplayer = game.OpenMultiplayer;
        Buttons.OnBeatmapListing = Buttons.OnSolo;
        Buttons.ReturnToTopOnIdle = false;
    }

    public override void OnEntering(ScreenTransitionEvent e)
    {
        base.OnEntering(e);
        Buttons.State = ButtonSystemState.TopLevel;
    }
}
