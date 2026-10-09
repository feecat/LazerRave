using osu.Game.Screens;
using osu.Game.Screens.Menu;
using osu.Game.Screens.Select;

namespace LazerRave.Lazer;

internal partial class LazerRaveLoader : Loader
{
    protected override MainMenu CreateMainMenu() => new LazerRaveMainMenu();
}

internal partial class LazerRaveMainMenu : MainMenu
{
    protected override SongSelect CreateSongSelect() => new LazerRaveSongSelect();
}
