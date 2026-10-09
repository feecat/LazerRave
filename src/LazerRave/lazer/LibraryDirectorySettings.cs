using System.Collections.Specialized;
using static LazerRave.Lazer.LazerRaveText;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Settings;

namespace LazerRave.Lazer;

internal partial class LibraryDirectorySettings(DesktopSettings settings) : SettingsSubsection
{
    protected override LocalisableString Header => D("Directories");
    private readonly FillFlowContainer rows = new()
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Direction = FillDirection.Vertical,
        Spacing = new osuTK.Vector2(0, 12),
    };

    [BackgroundDependencyLoader]
    private void load()
    {
        Children = new Drawable[]
        {
            new OsuSpriteText
            {
                Text = D("Relative paths start in the application folder."),
                Font = OsuFont.GetFont(size: 15),
                Margin = new MarginPadding { Left = 20, Bottom = 8 },
            },
            rows,
            new RoundedButton
            {
                Text = D("Add folder"), RelativeSizeAxes = Axes.X, Height = 40,
                Action = () => settings.Roots.Add(""),
            },
        };
        RebuildRows();
        settings.Roots.CollectionChanged += RootsChanged;
    }

    private void RootsChanged(object? sender, NotifyCollectionChangedEventArgs change)
    {
        if (change.Action != NotifyCollectionChangedAction.Replace) Schedule(RebuildRows);
    }

    private void RebuildRows()
    {
        rows.Clear();
        for (int i = 0; i < settings.Roots.Count; i++)
        {
            int index = i;
            var value = new Bindable<string>(settings.Roots[index]);
            value.BindValueChanged(change => settings.Roots[index] = change.NewValue);
            rows.Add(new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y,
                        Padding = new MarginPadding { Right = 54 },
                        Child = new SettingsTextBox { LabelText = D("Folder path"), Current = value, ShowsDefaultIndicator = false },
                    },
                    new RoundedButton
                    {
                        Text = "−", Width = 40, Height = 32, Y = 22,
                        Anchor = Anchor.TopRight, Origin = Anchor.TopRight,
                        Action = () => settings.Roots.RemoveAt(index),
                    },
                },
            });
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        settings.Roots.CollectionChanged -= RootsChanged;
        base.Dispose(isDisposing);
    }
}
