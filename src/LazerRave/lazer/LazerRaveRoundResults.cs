using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Screens;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Screens;
using osu.Game.Screens.OnlinePlay.Match.Components;
using osuTK;

namespace LazerRave.Lazer;

internal partial class LazerRaveRoundResults(CloudClient client, Guid match, string title, string? error) : OsuScreen
{
    public override string Title => "Multiplayer results";
    public override bool HideOverlaysOnEnter => true;
    public override bool? AllowGlobalTrackControl => false;
    protected override void LoadComplete()
    {
        base.LoadComplete();
        AddInternal(new GridContainer
        {
            RelativeSizeAxes = Axes.Both, Padding = new MarginPadding { Horizontal = 80, Top = 90, Bottom = 70 },
            RowDimensions = [new Dimension(GridSizeMode.Absolute, 55), new Dimension(), new Dimension(GridSizeMode.Absolute, 65)],
            Content = new Drawable?[][]
            {
                [new TruncatingSpriteText { Text = title, RelativeSizeAxes = Axes.X, Font = OsuFont.GetFont(size: 30) }],
                [new CloudLeaderboard(client, match, false) { RelativeSizeAxes = Axes.Both }],
                [new Container { RelativeSizeAxes = Axes.Both, Children = new Drawable[]
                {
                    new PurpleRoundedButton { Text = "Return to room", Size = new Vector2(200, 45), Anchor = Anchor.BottomRight, Origin = Anchor.BottomRight, Action = () => this.Exit() },
                    new TruncatingSpriteText { Text = error ?? "", RelativeSizeAxes = Axes.X, Width = .7f, Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Font = OsuFont.GetFont(size: 16) },
                } }],
            },
        });
    }
}
