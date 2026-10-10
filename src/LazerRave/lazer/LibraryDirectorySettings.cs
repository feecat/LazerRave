using System.Collections.Specialized;
using LazerRave.Content;
using LazerRave.Bridge;
using static LazerRave.Lazer.LazerRaveText;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Framework.Platform;
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
    private readonly Bindable<string> packLink = new("");
    private readonly Bindable<FileInfo?> packFile = new();
    private CloudClient? cloud;
    private LazerRaveGame game = null!;
    private GameHost host = null!;
    private RoundedButton download = null!, import = null!, cancel = null!;
    private readonly OsuSpriteText transferStatus = new()
    {
        Font = OsuFont.GetFont(size: 15), Margin = new MarginPadding { Left = 20, Bottom = 8 },
    };

    [BackgroundDependencyLoader]
    private void load(LazerRaveGame game, GameHost host)
    {
        this.game = game;
        this.host = host;
        cloud = game.Cloud;
        Children = new Drawable[]
        {
            new OsuSpriteText
            {
                Text = D("Relative paths start in the application folder."),
                Font = OsuFont.GetFont(size: 15),
                Margin = new MarginPadding { Left = 20, Bottom = 8 },
            },
            rows,
            new RoundedButton { Text = D("Browse song packs"), RelativeSizeAxes = Axes.X, Height = 40, Action = game.OpenSongPacks },
            new RoundedButton
            {
                Text = D("Add folder"), RelativeSizeAxes = Axes.X, Height = 40,
                Action = () => settings.Roots.Add(""),
            },
            new OsuSpriteText
            {
                Text = D("Song packs install to BMS/Shared."), Font = OsuFont.GetFont(size: 15),
                Margin = new MarginPadding { Left = 20, Top = 16, Bottom = 8 },
            },
            new SettingsTextBox { LabelText = D("Song pack link"), Current = packLink, ShowsDefaultIndicator = false },
            download = new RoundedButton
            {
                Text = D("Download song pack"), RelativeSizeAxes = Axes.X, Height = 40,
                Action = () => _ = Transfer(() => cloud.DownloadPack(packLink.Value, CancellationToken.None), game),
            },
            new FormFileSelector(".zip")
            {
                Caption = D("Song pack ZIP"), PlaceholderText = D("Choose file"), Current = packFile,
                Margin = new MarginPadding { Top = 12 },
            },
            import = new RoundedButton
            {
                Text = D("Import song pack ZIP"), RelativeSizeAxes = Axes.X, Height = 40,
                Action = () => _ = Transfer(() => cloud.ImportPack(packFile.Value!.FullName, CancellationToken.None), game),
            },
            transferStatus,
            cancel = new RoundedButton
            {
                Text = D("Cancel transfer"), RelativeSizeAxes = Axes.X, Height = 40, Action = cloud.CancelTransfer,
            },
        };
        RebuildRows();
        settings.Roots.CollectionChanged += RootsChanged;
        packLink.BindValueChanged(_ => TransferChanged());
        packFile.BindValueChanged(_ => TransferChanged());
        cloud.Changed += TransferChanged;
        RefreshTransfer();
    }

    private async Task Transfer(Func<Task> action, LazerRaveGame game)
    {
        try { await action(); }
        catch (Exception error)
        {
            Schedule(() => { game.SetLibraryMessage(error.Message); transferStatus.Text = error.Message; });
        }
    }

    private void TransferChanged() => Scheduler.AddOnce(RefreshTransfer);
    private void RefreshTransfer()
    {
        if (cloud is null || IsDisposed) return;
        download.Enabled.Value = !cloud.Busy && !string.IsNullOrWhiteSpace(packLink.Value);
        import.Enabled.Value = !cloud.Busy && packFile.Value is not null;
        cancel.Alpha = cloud.Busy ? 1 : 0;
        cancel.Enabled.Value = cloud.Busy;
        transferStatus.Text = cloud.Progress is { } progress
            ? LocalisableString.Format("{0} · {1:0}%", D(progress.Stage), progress.Fraction * 100)
            : D(cloud.Status);
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
            if (settings.Roots[index] == LibraryFolders.DefaultRoot)
            {
                rows.Add(new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new osuTK.Vector2(0, 5),
                    Padding = new MarginPadding { Horizontal = 20 },
                    Children = new Drawable[]
                    {
                        new OsuSpriteText { Text = D("Default library"), Font = OsuFont.GetFont(size: 15) },
                        new OsuSpriteText { Text = LibraryFolders.DefaultRoot, Font = OsuFont.GetFont(size: 18) },
                    },
                });
                continue;
            }
            var value = new Bindable<string>(settings.Roots[index]);
            value.BindValueChanged(change => settings.Roots[index] = change.NewValue);
            RoundedButton browse;
            var row = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new osuTK.Vector2(0, 5),
                Padding = new MarginPadding { Horizontal = 20 },
                Children = new Drawable[]
                {
                    new OsuSpriteText { Text = D("Folder path"), Font = OsuFont.GetFont(size: 15) },
                    new Container
                    {
                        RelativeSizeAxes = Axes.X, Height = 40,
                        Children = new Drawable[]
                        {
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both, Padding = new MarginPadding { Right = 144 },
                                Child = new OutlinedTextBox { RelativeSizeAxes = Axes.X, Height = 40, Current = value, CommitOnFocusLost = true },
                            },
                            new FillFlowContainer
                            {
                                Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight,
                                Direction = FillDirection.Horizontal, Size = new osuTK.Vector2(136, 40),
                                Spacing = new osuTK.Vector2(8, 0),
                                Children = new Drawable[]
                                {
                                    browse = new RoundedButton { Text = D("Browse"), TooltipText = D("Choose folder"), Width = 88, Height = 40 },
                                    new RoundedButton
                                    {
                                        Text = "−", TooltipText = D("Remove folder"), Width = 40, Height = 40,
                                        Action = () => settings.Roots.RemoveAt(index),
                                    },
                                },
                            },
                        },
                    },
                },
            };
            browse.Action = () => _ = Browse(row, browse, value);
            rows.Add(row);
        }
    }

    private async Task Browse(Drawable row, RoundedButton button, Bindable<string> value)
    {
        button.Enabled.Value = false;
        try
        {
            var initial = AppContext.BaseDirectory;
            try
            {
                var candidate = ApplicationPaths.ResolveLibraryRoot(value.Value);
                if (Directory.Exists(candidate)) initial = candidate;
            }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { }
            var selected = await SystemFolderPicker.Show(host, initial);
            if (selected is not null)
            {
                var resolved = Path.GetFullPath(selected);
                if (!Directory.Exists(resolved)) throw new DirectoryNotFoundException(resolved);
                var application = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
                var path = resolved.Equals(application, StringComparison.OrdinalIgnoreCase) ? @".\"
                    : resolved.StartsWith(application + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        ? @".\" + Path.GetRelativePath(application, resolved) : resolved;
                Schedule(() => { if (!IsDisposed && rows.Children.Contains(row)) value.Value = path; });
            }
        }
        catch (Exception error) { Schedule(() => { if (!IsDisposed) game.SetLibraryMessage(error.Message); }); }
        finally { Schedule(() => { if (!IsDisposed && rows.Children.Contains(row)) button.Enabled.Value = true; }); }
    }

    protected override void Dispose(bool isDisposing)
    {
        settings.Roots.CollectionChanged -= RootsChanged;
        if (cloud is not null) cloud.Changed -= TransferChanged;
        base.Dispose(isDisposing);
    }
}
