using osu.Game.Screens;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Select;
using osu.Framework.Allocation;

namespace LazerRave.Lazer;

internal partial class LazerRaveLoader : Loader
{
    protected override MainMenu CreateMainMenu() => new LazerRaveMainMenu();
}

internal partial class LazerRaveMainMenu : MainMenu
{
    [Resolved] private LazerRaveGame game { get; set; } = null!;
    protected override SongSelect CreateSongSelect() => new LazerRaveSongSelect();
    protected override void LoadComplete() { base.LoadComplete(); Buttons.OnCustomMultiplayer = game.OpenMultiplayer; }
}
