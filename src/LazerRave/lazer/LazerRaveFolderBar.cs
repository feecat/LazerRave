using static LazerRave.Lazer.LazerRaveText;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osuTK;

namespace LazerRave.Lazer;

internal partial class LazerRaveFolderBar : CompositeDrawable
{
    private readonly BmsBeatmapStore catalog;
    private readonly Action<string?> navigate;
    private FillFlowContainer trail = null!;

    public LazerRaveFolderBar(BmsBeatmapStore catalog, Action<string?> navigate)
    {
        this.catalog = catalog;
        this.navigate = navigate;
        Width = 420;
        Height = 50;
    }

    public float OccupiedWidth => Width;

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChild = new OsuScrollContainer(Direction.Horizontal)
        {
            RelativeSizeAxes = Axes.Both,
            ScrollbarVisible = false,
            Child = trail = new FillFlowContainer
            {
                Direction = FillDirection.Horizontal,
                AutoSizeAxes = Axes.Both,
                Spacing = new Vector2(4, 0),
            },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        Refresh();
    }

    public void Refresh()
    {
        if (!IsLoaded) return;
        trail.Clear();
        trail.Add(Chip(D("Library"), catalog.Directory is null, () => navigate(null)));
        foreach (var level in catalog.Ancestry)
        {
            trail.Add(new OsuSpriteText
            {
                Text = "›",
                Font = OsuFont.GetFont(size: 22),
                Alpha = 0.65f,
                Margin = new MarginPadding { Top = 12 },
            });
            trail.Add(Chip(level.Name, string.Equals(level.Path, catalog.Directory, StringComparison.OrdinalIgnoreCase), () => navigate(level.Path)));
        }
    }

    private Drawable Chip(LocalisableString label, bool current, Action action) => new OsuAnimatedButton
    {
        AutoSizeAxes = Axes.Both,
        Action = action,
        Masking = false,
        Child = new OsuSpriteText
        {
            Text = label,
            Font = OsuFont.GetFont(size: 22, weight: current ? FontWeight.Bold : FontWeight.SemiBold),
            Alpha = current ? 1f : 0.82f,
            Margin = new MarginPadding { Horizontal = 10, Vertical = 12 },
        },
    };
}
