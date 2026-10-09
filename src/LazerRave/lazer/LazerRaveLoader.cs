using osu.Game.Screens;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Select;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Screens;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;

namespace LazerRave.Lazer;

internal partial class LazerRaveLoader : Loader
{
    protected override MainMenu CreateMainMenu() => new LazerRaveMainMenu();
}

internal partial class LazerRaveMainMenu : MainMenu
{
    protected override bool AutomaticallyShowLogin => false;
    protected override bool FlattenPlayMenu => true;
    protected override Drawable CreatePlayIcon() => new CircularContainer
    {
        RelativeSizeAxes = Axes.Both,
        Masking = true,
        BorderThickness = 1.5f,
        BorderColour = Colour4.White,
        Child = new OsuSpriteText
        {
            Text = "LR",
            Font = OsuFont.TorusAlternate.With(size: 20, weight: FontWeight.SemiBold),
            Shadow = true,
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            UseFullGlyphHeight = false,
        },
    };
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
