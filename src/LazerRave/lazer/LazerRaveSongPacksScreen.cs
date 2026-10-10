using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Framework.Screens;
using osu.Framework.Threading;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Screens;
using osuTK;
using static LazerRave.Lazer.LazerRaveText;

namespace LazerRave.Lazer;

internal partial class LazerRaveSongPacksScreen(CloudClient client) : OsuScreen
{
    [Resolved] private LazerRaveGame game { get; set; } = null!;
    [Cached] private readonly OverlayColourProvider colours = new(OverlayColourScheme.Aquamarine);
    public override string Title => D("Song packs").ToString();
    public override bool HideOverlaysOnEnter => true;
    public override bool? AllowGlobalTrackControl => false;
    private readonly Bindable<string> query = new("");
    private readonly Bindable<int> keys = new(0);
    private readonly Bindable<string> sort = new("title");
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? request;
    private ScheduledDelegate? delayed;
    private CloudPackPage? catalog;
    private int page = 1, dirty;
    private Guid? downloading;
    private string error = "";
    private bool loading;
    private FillFlowContainer list = null!;
    private OsuSpriteText status = null!, pagination = null!;
    private Box progress = null!;
    private RoundedButton previous = null!, next = null!, cancel = null!;
    private readonly List<(CloudSongPack Pack, RoundedButton Button)> actions = [];

    protected override void LoadComplete()
    {
        base.LoadComplete();
        InternalChild = new Container
        {
            RelativeSizeAxes = Axes.Both, Padding = new MarginPadding { Horizontal = 65, Top = 85, Bottom = 75 },
            Children = new Drawable[]
            {
                new OsuSpriteText { Text = D("Song packs"), Font = OsuFont.GetFont(size: 32, weight: FontWeight.Bold) },
                new GridContainer
                {
                    Y = 44, RelativeSizeAxes = Axes.X, Height = 76,
                    ColumnDimensions = [new Dimension(), new Dimension(GridSizeMode.Absolute, 180), new Dimension(GridSizeMode.Absolute, 210), new Dimension(GridSizeMode.Absolute, 115)],
                    Content = new Drawable?[][] { [
                        new SettingsTextBox { LabelText = D("Search song packs"), Current = query, ShowsDefaultIndicator = false },
                        new KeysDropdown { LabelText = D("Keys"), Items = new[] { 0, 5, 7, 9, 10, 14 }, Current = keys, ShowsDefaultIndicator = false },
                        new SortDropdown { LabelText = D("Sort by"), Items = new[] { "title", "newest", "difficulty" }, Current = sort, ShowsDefaultIndicator = false },
                        new RoundedButton { Text = D("Refresh"), Width = 105, Height = 40, Y = 26, Action = () => _ = LoadPage() },
                    ] },
                },
                status = new OsuSpriteText { Y = 122, Font = OsuFont.GetFont(size: 18) },
                new Container { Y = 148, RelativeSizeAxes = Axes.X, Height = 4, Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = colours.Background3 },
                    progress = new Box { RelativeSizeAxes = Axes.Both, Width = 0, Colour = colours.Highlight1 },
                } },
                new Container
                {
                    RelativeSizeAxes = Axes.Both, Padding = new MarginPadding { Top = 166, Bottom = 56 },
                    Child = new OsuScrollContainer { RelativeSizeAxes = Axes.Both, Child = list = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 12), Padding = new MarginPadding { Right = 15, Bottom = 15 },
                    } },
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft, AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal, Spacing = new Vector2(12, 0),
                    Children = new Drawable[]
                    {
                        previous = new RoundedButton { Text = D("Previous"), Width = 120, Height = 40, Action = () => { page--; _ = LoadPage(); } },
                        pagination = new OsuSpriteText { Font = OsuFont.GetFont(size: 18), Margin = new MarginPadding { Top = 10 } },
                        next = new RoundedButton { Text = D("Next"), Width = 120, Height = 40, Action = () => { page++; _ = LoadPage(); } },
                    },
                },
                cancel = new RoundedButton
                {
                    Anchor = Anchor.BottomRight, Origin = Anchor.BottomRight, Text = D("Cancel transfer"), Width = 170, Height = 40, Action = client.CancelTransfer,
                },
            },
        };
        query.BindValueChanged(_ => Reload()); keys.BindValueChanged(_ => Reload()); sort.BindValueChanged(_ => Reload());
        client.Changed += Changed;
        _ = LoadPage();
    }

    private void Reload()
    {
        delayed?.Cancel(); request?.Cancel(); page = 1;
        delayed = Scheduler.AddDelayed(() => _ = LoadPage(), 300);
    }
    private void Changed() => Interlocked.Exchange(ref dirty, 1);
    private async Task LoadPage()
    {
        request?.Cancel(); request?.Dispose(); request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var current = request; loading = true; error = ""; Changed();
        try
        {
            var result = await client.BrowsePacks(query.Value, keys.Value, sort.Value, page, current.Token);
            Schedule(() => { if (request == current && !current.IsCancellationRequested) { catalog = result; Present(); } });
        }
        catch (OperationCanceledException) { }
        catch (Exception failure) { Schedule(() => { if (request == current) error = failure.Message; }); }
        finally { Schedule(() => { if (request == current) { loading = false; Changed(); } }); }
    }
    private void Present()
    {
        list.Clear(); actions.Clear();
        if (catalog is null) return;
        foreach (var pack in catalog.Items)
        {
            RoundedButton action;
            list.Add(new Container
            {
                RelativeSizeAxes = Axes.X, Height = 140, Masking = true, CornerRadius = 8,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = colours.Background4 },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 10), Padding = new MarginPadding { Left = 18, Top = 16, Right = 195 },
                        Children = new Drawable[]
                        {
                            new TruncatingSpriteText { Text = pack.Title, RelativeSizeAxes = Axes.X, Font = OsuFont.GetFont(size: 24, weight: FontWeight.SemiBold) },
                            new OsuSpriteText { Text = $"{string.Join(" / ", pack.Keys.Select(key => key + "K"))} · Lv. {pack.MinimumLevel}–{pack.MaximumLevel} · {pack.ChartCount} BMS · {pack.SizeBytes / 1048576d:0.0} MiB", Font = OsuFont.GetFont(size: 18), Colour = colours.Content2 },
                            new TruncatingSpriteText { Text = pack.Description, RelativeSizeAxes = Axes.X, Font = OsuFont.GetFont(size: 17) },
                        },
                    },
                    action = new RoundedButton
                    {
                        Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight, Margin = new MarginPadding { Right = 18 }, Width = 155, Height = 45,
                        Action = () => { if (client.PackInstalled(pack)) game.OpenInstalledPack(client.PackDirectory(pack)); else _ = Download(pack); },
                    },
                },
            });
            actions.Add((pack, action));
        }
        if (catalog.Items.Length == 0) list.Add(new OsuSpriteText { Text = D("No song packs found."), Font = OsuFont.GetFont(size: 22) });
        Changed();
    }
    private async Task Download(CloudSongPack pack)
    {
        downloading = pack.Id; error = ""; Changed();
        try { await client.DownloadPack(new Uri(client.PackServer, "packs/" + pack.Id).ToString(), lifetime.Token); }
        catch (Exception failure) { error = failure.Message; }
        finally { downloading = null; Changed(); }
    }
    protected override void Update()
    {
        base.Update();
        if (Interlocked.Exchange(ref dirty, 0) == 0) return;
        progress.Width = (float)(client.Progress?.Fraction ?? 0);
        status.Text = error.Length > 0 ? error : downloading is not null && client.Progress is { } transfer
            ? LocalisableString.Format("{0} · {1:0}% · {2:0.0} / {3:0.0} MiB", D(transfer.Stage), transfer.Fraction * 100, transfer.Completed / 1048576d, transfer.Total / 1048576d)
            : loading ? D("Loading song packs…") : LocalisableString.Format("{0} · {1}", catalog?.Total ?? 0, D("Song packs"));
        previous.Enabled.Value = !loading && page > 1;
        next.Enabled.Value = !loading && catalog is { } result && page * result.PageSize < result.Total;
        pagination.Text = $"{page} / {Math.Max(1, (int)Math.Ceiling((catalog?.Total ?? 0) / 20d))}";
        cancel.Enabled.Value = downloading is not null && client.Busy;
        cancel.Alpha = downloading is not null ? 1 : 0;
        foreach (var (pack, button) in actions)
        {
            button.Text = D(downloading == pack.Id ? "Downloading" : client.PackInstalled(pack) ? "Open library" : "Download");
            button.Enabled.Value = !client.Busy && !loading;
        }
    }
    public override bool OnExiting(ScreenExitEvent e)
    {
        if (base.OnExiting(e)) return true;
        lifetime.Cancel(); return false;
    }
    protected override void Dispose(bool isDisposing)
    {
        client.Changed -= Changed; delayed?.Cancel(); request?.Cancel(); request?.Dispose(); lifetime.Cancel(); lifetime.Dispose();
        base.Dispose(isDisposing);
    }
    private partial class KeysDropdown : SettingsDropdown<int>
    {
        protected override OsuDropdown<int> CreateDropdown() => new KeysControl();
        private partial class KeysControl : DropdownControl { protected override LocalisableString GenerateItemText(int item) => item == 0 ? D("All keys") : $"{item}Key"; }
    }
    private partial class SortDropdown : SettingsDropdown<string>
    {
        protected override OsuDropdown<string> CreateDropdown() => new SortControl();
        private partial class SortControl : DropdownControl { protected override LocalisableString GenerateItemText(string item) => D(item switch { "newest" => "Newest", "difficulty" => "Difficulty", _ => "Title" }); }
    }
}
