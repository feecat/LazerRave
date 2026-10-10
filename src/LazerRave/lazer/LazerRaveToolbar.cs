using static LazerRave.Lazer.LazerRaveText;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays.Toolbar;

namespace LazerRave.Lazer;

internal partial class LazerRaveToolbar : Toolbar
{
    protected override Drawable CreateIdentityDisplay() => new OsuSpriteText
    {
        Text = "LazerRave",
        Font = OsuFont.GetFont(size: 20, weight: FontWeight.Bold),
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        X = 14,
    };

    protected override Drawable[] CreateRightButtons() =>
    [
        new WebsiteButton("Website", FontAwesome.Solid.Globe, "/"),
        new SongPacksButton(),
        new WebsiteButton("Download", FontAwesome.Solid.Download, "/download"),
        new WebsiteButton("GitHub", FontAwesome.Brands.Github, "https://github.com/feecat/LazerRave"),
        CreateUserButton(), new ToolbarClock(), new ToolbarNotificationButton(),
    ];
    private partial class SongPacksButton : ToolbarButton
    {
        [Resolved] private LazerRaveGame game { get; set; } = null!;
        public SongPacksButton() { TooltipMain = D("Song packs"); SetIcon(FontAwesome.Solid.FolderOpen); Action = () => game.OpenSongPacks(); }
    }

    private partial class WebsiteButton : ToolbarButton
    {
        [Resolved] private LazerRaveGame game { get; set; } = null!;
        protected override Anchor TooltipAnchor => Anchor.TopRight;

        public WebsiteButton(string label, IconUsage icon, string path)
        {
            TooltipMain = D(label);
            SetIcon(icon);
            Action = () => game.OpenCloudWebsite(path.StartsWith('/') ? "https://lazerrave.com" + path : path);
        }
    }
}
