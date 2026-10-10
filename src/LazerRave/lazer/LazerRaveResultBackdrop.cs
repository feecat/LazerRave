using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK.Graphics;

namespace LazerRave.Lazer;

internal partial class LazerRaveResultBackdrop : Container
{
    public LazerRaveResultBackdrop()
    {
        RelativeSizeAxes = Axes.Both;
        Depth = 1;
        Padding = new MarginPadding { Horizontal = 40, Top = 60, Bottom = 45 };
        Child = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Masking = true,
            CornerRadius = 16,
            Child = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(0.035f, 0.05f, 0.075f, 0.8f),
            },
        };
    }
}
