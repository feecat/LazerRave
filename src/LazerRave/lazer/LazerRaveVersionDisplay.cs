using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;

namespace LazerRave.Lazer;

internal partial class LazerRaveVersionDisplay : CompositeDrawable
{
    public LazerRaveVersionDisplay()
    {
        AutoSizeAxes = Axes.Both;
        InternalChildren =
        [
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 10,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4Extensions.FromHex("171A1C"),
                    Alpha = 0.75f,
                },
            },
            new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Margin = new MarginPadding { Horizontal = 16, Vertical = 10 },
                Spacing = new Vector2(12, 0),
                Children =
                [
                    new OsuSpriteText
                    {
                        Text = ClientVersion.DisplayName,
                        Font = OsuFont.GetFont(size: 20, weight: FontWeight.SemiBold),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                    },
                    new Container
                    {
                        AutoSizeAxes = Axes.Both,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Masking = true,
                        CornerRadius = 5,
                        Children =
                        [
                            new Box { RelativeSizeAxes = Axes.Both, Colour = Color4Extensions.FromHex("F2B86B") },
                            new OsuSpriteText
                            {
                                Text = ClientVersion.Stage,
                                Font = OsuFont.GetFont(size: 16, weight: FontWeight.Bold),
                                Colour = Color4Extensions.FromHex("171A1C"),
                                Margin = new MarginPadding { Horizontal = 8, Vertical = 3 },
                            },
                        ],
                    },
                ],
            },
        ];
    }
}
