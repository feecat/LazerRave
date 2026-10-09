using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics.Colour;
using osu.Game.Screens.Menu;

namespace LazerRave.Lazer;

internal partial class LazerRaveLogo : OsuLogo
{
    protected override string TextureName => "Branding/logo";

    protected override ColourInfo BackgroundColour => ColourInfo.GradientVertical(
        Color4Extensions.FromHex("234864"), Color4Extensions.FromHex("070e1a"));

    protected override ColourInfo TriangleColour => ColourInfo.GradientVertical(
        Color4Extensions.FromHex("52d6ff"), Color4Extensions.FromHex("187cb7"));
}
