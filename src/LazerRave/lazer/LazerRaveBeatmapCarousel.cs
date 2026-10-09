using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Pooling;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Graphics;
using osu.Game.Graphics.Carousel;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osu.Game.Screens.Select;
using osuTK;
using osuTK.Graphics;
using osu.Game.Screens.Select.Filter;

namespace LazerRave.Lazer;

/// <summary>
/// Carousel model for a library folder listed alongside beatmaps, carrying the directory to descend into.
/// </summary>
internal sealed record FolderDefinition(string Name, string Path);

/// <summary>
/// Renders a <see cref="FolderDefinition"/> as a carousel row styled like a beatmap panel, so folders are
/// browsed the same way songs are: select the row, then activate it to enter the folder.
/// </summary>
internal partial class FolderPanel : Panel
{
    public const float HEIGHT = 90;

    private OsuSpriteText nameText = null!;

    [BackgroundDependencyLoader]
    private void load(OverlayColourProvider colourProvider)
    {
        Height = HEIGHT;
        Background = new Box
        {
            RelativeSizeAxes = Axes.Both,
            Colour = colourProvider.Background4,
        };

        AccentColour = new Color4(240, 188, 87, 255);
        Icon = new Container
        {
            Size = new Vector2(72, HEIGHT),
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = AccentColour.Value,
                    Alpha = 0.16f,
                },
                new Box { RelativeSizeAxes = Axes.Y, Width = 5, Colour = AccentColour.Value },
                new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Icon = FontAwesome.Solid.Folder,
                    Size = new Vector2(40),
                    Colour = AccentColour.Value,
                },
            },
        };

        Content.RelativeSizeAxes = Axes.Both;

        Content.Children = new Drawable[]
        {
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Left = 18, Right = 70 },
                Child = nameText = new TruncatingSpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    RelativeSizeAxes = Axes.X,
                    Font = OsuFont.GetFont(size: 26, weight: FontWeight.SemiBold),
                    Colour = AccentColour.Value,
                    UseFullGlyphHeight = false,
                },
            },
            new SpriteIcon
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                X = -40,
                Icon = FontAwesome.Solid.ChevronRight,
                Size = new Vector2(18),
                Colour = AccentColour.Value,
            },
        };
    }

    protected override void PrepareForUse()
    {
        base.PrepareForUse();
        nameText.Text = ((FolderDefinition)Item!.Model).Name;
    }

    public override MenuItem[]? ContextMenuItems => null;
}

/// <summary>
/// Song select carousel which also lists the folders of the level being browsed, above the beatmaps.
/// Activating a folder row descends into that folder instead of starting a beatmap.
/// </summary>
internal partial class LazerRaveBeatmapCarousel : BeatmapCarousel
{
    private readonly Func<IReadOnlyList<FolderDefinition>> getFolders;
    private readonly Action<string> enterFolder;

    private readonly DrawablePool<FolderPanel> folderPanelPool = new DrawablePool<FolderPanel>(10);

    public LazerRaveBeatmapCarousel(Func<IReadOnlyList<FolderDefinition>> getFolders, Action<string> enterFolder)
    {
        this.getFolders = getFolders;
        this.enterFolder = enterFolder;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        AddInternal(folderPanelPool);
    }

    protected override IEnumerable<CarouselItem> GetPinnedItems() =>
        getFolders().Select(folder => new CarouselItem(folder)
        {
            DrawHeight = FolderPanel.HEIGHT,
        });

    protected override bool CheckModelEquality(object? x, object? y) =>
        x is FolderDefinition first && y is FolderDefinition second
            ? string.Equals(first.Path, second.Path, StringComparison.OrdinalIgnoreCase)
            : base.CheckModelEquality(x, y);

    /// <summary>
    /// A song stays one set regardless of the selected sort order.
    /// </summary>
    public override void Filter(FilterCriteria criteria, bool showLoadingImmediately = false)
    {
        criteria.ForceBeatmapSetsGroupedTogether = true;
        criteria.Group = GroupMode.None;
        base.Filter(criteria, showLoadingImmediately);
    }

    protected override void FindCarouselItemsForSelection(ref Selection keyboardSelection, ref Selection selection, IList<CarouselItem> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (CheckModelEquality(items[i].Model, keyboardSelection.Model!))
                keyboardSelection = keyboardSelection with { CarouselItem = items[i], Index = i };
            if (CheckModelEquality(items[i].Model, selection.Model!))
                selection = selection with { CarouselItem = items[i], Index = i };
        }

        if (keyboardSelection.CarouselItem is not null) return;
        if (selection.CarouselItem is not null)
        {
            keyboardSelection = selection;
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].IsVisible && items[i].Model is FolderDefinition)
            {
                keyboardSelection = selection = new Selection(items[i].Model, items[i], null, i);
                ScrollToSelection(immediate: true);
                break;
            }
        }
    }

    protected override bool CheckValidForSetSelection(CarouselItem item) => item.Model is FolderDefinition || base.CheckValidForSetSelection(item);

    protected override void HandleItemActivated(CarouselItem item)
    {
        if (item.Model is FolderDefinition folder)
        {
            enterFolder(folder.Path);
            return;
        }

        base.HandleItemActivated(item);
    }

    protected override Drawable GetDrawableForDisplay(CarouselItem item)
    {
        if (item.Model is FolderDefinition)
            return folderPanelPool.Get();

        return base.GetDrawableForDisplay(item);
    }
}
