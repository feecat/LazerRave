using osu.Game.Screens;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Select;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Screens;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Framework.Graphics.Sprites;
using osuTK.Graphics;
using osuTK.Input;
using static LazerRave.Lazer.LazerRaveText;

namespace LazerRave.Lazer;

internal partial class LazerRaveLoader : Loader
{
    protected override MainMenu CreateMainMenu() => new LazerRaveMainMenu();
}

internal partial class LazerRaveMainMenu : MainMenu
{
    protected override bool AutomaticallyShowLogin => false;
    protected override bool FlattenPlayMenu => true;
    protected override osu.Framework.Localisation.LocalisableString BrowseText => D("Song packs");
    protected override MainMenuButton CreateSecondaryMenuButton() => new(
        D("Native LR2"), "button-default-select", FontAwesome.Solid.Desktop,
        new Color4(52, 105, 127, 255), (_, _) => game.OpenClassicGame(), Key.N)
    {
        Padding = new MarginPadding { Right = ButtonSystem.WEDGE_WIDTH },
    };
    protected override Drawable CreatePlayIcon() => new Container
    {
        RelativeSizeAxes = Axes.Both,
        Children = new Drawable[]
        {
            new CircularProgress
            {
                RelativeSizeAxes = Axes.Both,
                Progress = 1,
                InnerRadius = 0.09f,
                Colour = Colour4.White,
            },
            new OsuSpriteText
            {
                Text = "LR",
                Font = OsuFont.TorusAlternate.With(size: 20, weight: FontWeight.Medium),
                Shadow = true,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                UseFullGlyphHeight = false,
            },
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
        Buttons.OnBeatmapListing = game.OpenSongPacks;
        Buttons.ReturnToTopOnIdle = false;
    }

    public override void OnEntering(ScreenTransitionEvent e)
    {
        base.OnEntering(e);
        Buttons.State = ButtonSystemState.TopLevel;
    }
}
