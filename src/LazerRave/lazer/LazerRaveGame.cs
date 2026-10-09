using static LazerRave.Lazer.LazerRaveText;
using osu.Framework.Extensions;
using LazerRave.Bridge;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.IO.Stores;
using osu.Framework.Input.Handlers;
using osu.Framework.Input.Handlers.Joystick;
using osu.Framework.Input.Handlers.Midi;
using osu.Framework.Input.Handlers.Touch;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Threading;
using osu.Game;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.IO;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Overlays.Toolbar;
using osu.Game.Rulesets.Mania;
using osu.Game.Screens;
using osu.Game.Screens.Select;
using osu.Game.Screens.Menu;
using osuTK;

namespace LazerRave.Lazer;

[Cached]
internal partial class LazerRaveGame : OsuGame
{
    private readonly EngineBridge bridge;
    private readonly BmsBeatmapStore catalog;
    private readonly DesktopSettings preferences;
    private readonly CloudClient cloud;
    public PlayRecordStore Records { get; } = new(Path.Combine(ApplicationPaths.UserData, "play-records"));
    public CloudClient Cloud => cloud;
    private LazerRaveCloudPanel cloudPanel = null!;
    private LazerRaveMultiplayer? multiplayer;
    private readonly string startupMessage;
    private readonly CancellationTokenSource lifetime = new();
    private LazerRaveSettingsPanel settingsPanel = null!;
    private LazerRaveSongSelect songSelect = null!;
    private ResourceStore<byte[]> media = null!;
    private PreviewPlayer preview = null!;
    private NativeGameViewport? viewport;
    private LazerRaveFolderBar folderBar = null!;
    private OsuDropdown<string> keys = null!;
    private CloudUser? displayedCloudUser;
    private string? displayedAvatarUrl;
    private int cloudChanged = 1;
    private Container chrome = null!;
    private bool refreshing;
    private readonly BindableDouble menuTrackVolume = new(1);
    private int previewGeneration;
    private ScheduledDelegate? pendingSettingsSave;
    private Bindable<FrameSync>? frameworkFrameSync;
    private bool applyingFrameLimit;
    private Guid? launchedCloudMatch;
    private readonly PerformanceRun? benchmark;
    private readonly System.Diagnostics.Stopwatch benchmarkClock = new();
    private bool benchmarkSelected, benchmarkStarted;
    private double nextFrameSample;
    private readonly List<string> frontendFrames = ["elapsed_s,draw_fps,update_fps,draw_interval_ms,update_interval_ms"];
    private void SaveFrontendFrames() => File.WriteAllLines(Path.Combine(bridge.Runtime, "frontend-fps.csv"), frontendFrames);
    public bool IsChartVisible(BeatmapInfo info) => catalog.IsVisible(info);

    /// <summary>
    /// Folders of the level currently being browsed, for the song select carousel to list as rows.
    /// </summary>
    public IReadOnlyList<FolderDefinition> CurrentFolders => catalog.ChildFolders
        .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
        .Select(folder => new FolderDefinition(folder.Name, folder.Path))
        .ToArray();

    /// <summary>Descends into a folder chosen from the carousel.</summary>
    public void EnterFolder(string path) => Navigate(path);
    public int Keys => catalog.Keys;
    public IReadOnlyList<Chart> SelectedDifficulties => SelectedChart is { } selected
        ? catalog.Library.Songs.First(song => song.Directory.Equals(Path.GetDirectoryName(selected.Path), StringComparison.OrdinalIgnoreCase)).Charts : [];
    public int MaximumChartLevel => catalog.Library.Songs.SelectMany(song => song.Charts).Select(chart => chart.Level).DefaultIfEmpty(20).Max();
    public void SetLibraryLevelRange(double? minimum, double? maximum) => catalog.SetLevelRange(minimum, maximum);
    public void SetLibrarySearch(string query) { catalog.SetSearch(query); }
    public bool CanSelectDifficulty(Chart chart) => catalog.GetBeatmapSets(null).SelectMany(set => set.Beatmaps)
        .Any(info => catalog.ChartFor(info)?.Path == chart.Path && catalog.IsVisible(info));
    public void SelectDifficulty(Chart chart)
    {
        var info = catalog.GetBeatmapSets(null).SelectMany(set => set.Beatmaps).FirstOrDefault(info => catalog.ChartFor(info)?.Path == chart.Path && catalog.IsVisible(info));
        if (info is not null) Beatmap.Value = BeatmapManager.GetWorkingBeatmap(info);
    }

    public DesktopSettings PlaySettings => preferences;
    public Chart? SelectedChart => catalog.ChartFor(Beatmap.Value.BeatmapInfo);
    public void ToggleCloud() => cloudPanel.ToggleVisibility();
    public void OpenMultiplayer()
    {
        if (!cloud.Connected) { cloudPanel.OpenLobby(); return; }
        CloseAllOverlays();
        if (multiplayer is { ValidForResume: true }) { multiplayer.MakeCurrent(); return; }
        ScreenStack.Push(multiplayer = new LazerRaveMultiplayer());
    }
    public void OpenCloudAccount() => cloudPanel.Show();
    public void OpenCloudWebsite(string url) => Host.OpenUrlExternally(url);
    public void OpenRoomLibrary(bool chooseSong = true)
    {
        var room = cloud.Room ?? throw new InvalidOperationException("Join a room first.");
        if (chooseSong && room.HostId != cloud.User?.Id) throw new InvalidOperationException("Only the host may select a song.");
        if (ScreenStack.CurrentScreen is LazerRaveSongSelect) return;
        ScreenStack.Push(new LazerRaveSongSelect
        {
            ConfirmationText = chooseSong ? "Use selected difficulty" : "Return to room",
            ConfirmSelection = async (info, cancellation) =>
            {
                if (cloud.Room?.Id != room.Id) throw new InvalidOperationException("The room has changed.");
                if (!chooseSong) return;
                var chart = catalog.ChartFor(info) ?? throw new InvalidOperationException("Choose a BMS chart first.");
                await cloud.SelectChart(new(chart.Path, chart.Title, chart.Artist, chart.Keys, chart.Level), cancellation);
            },
        });
    }
    public void OpenSharedChart(string path)
    {
        OpenRoomLibrary(false);
        catalog.Filter(Path.GetDirectoryName(path), catalog.Library.Songs.SelectMany(s => s.Charts).FirstOrDefault(c => c.Path.Equals(path, StringComparison.OrdinalIgnoreCase))?.Keys ?? 7);
        var info = catalog.GetBeatmapSets(null).SelectMany(s => s.Beatmaps).FirstOrDefault(b => catalog.ChartFor(b)?.Path.Equals(path, StringComparison.OrdinalIgnoreCase) == true);
        if (info is not null) Beatmap.Value = BeatmapManager.GetWorkingBeatmap(info);
        UpdateFolderBar(); songSelect?.RefreshKeyFilter();
    }
    private async Task ImportSharedChart(string path)
    {
        var library = await bridge.Import(preferences.Value, path, lifetime.Token);
        Schedule(() => { catalog.Replace(library, ApplicationPaths.LibraryRoots(preferences.Value.Roots)); ResetMedia(); UpdateFolderBar(); songSelect?.RefreshKeyFilter(); });
    }
    public override bool UseDevelopmentServer => false;
    protected override bool ShowDeveloperBuildBanner => false;
    public override string Version => ClientVersion.DisplayName;
    protected override int UnhandledExceptionsBeforeCrash => 0;

    public LazerRaveGame(EngineBridge bridge, FrontendSettings initial, SongLibrary library, string message, PerformanceRun? benchmark = null)
    {
        this.bridge = bridge; startupMessage = message;
        this.benchmark = benchmark;
        preferences = new(initial, FrontendSettings.SharedPath, benchmark is not null);
        catalog = new(); catalog.Replace(library, ApplicationPaths.LibraryRoots(initial.Roots));
        cloud = new CloudClient(() => catalog.Library.Songs.Select(s => s.Directory).ToArray(), () => preferences.Value.Encoding, ImportSharedChart,
            sessions: new CloudSessionStore(Path.Combine(ApplicationPaths.UserData, "cloud-session.bin")));
        cloud.Changed += CloudChanged;
        var api = new DummyAPIAccess(); api.SetState(APIState.Offline);
        api.LocalUser.Value = CloudIdentity.Create(null, null, initial.Player);
        API = api; Name = "LazerRave";
    }

    protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
    {
        var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
        dependencies.Cache(this); dependencies.Cache(cloud); dependencies.CacheAs<BeatmapStore>(catalog);
        return dependencies;
    }
    public override EndpointConfiguration CreateEndpoints() => new()
    {
        WebsiteUrl = "http://127.0.0.1:1", APIUrl = "http://127.0.0.1:1",
        MetadataUrl = "http://127.0.0.1:1", MultiplayerUrl = "http://127.0.0.1:1", SpectatorUrl = "http://127.0.0.1:1",
    };
    protected override OnlineStore CreateOnlineStore() => new CloudAvatarStore(() => cloud.Server);
    private sealed class CloudAvatarStore(Func<Uri?> server) : OnlineStore
    {
        protected override string GetLookupUrl(string url) => CloudClient.IsAvatarUrl(server(), url) ? url : "";
    }
    private void CloudChanged() => Interlocked.Exchange(ref cloudChanged, 1);
    private void RefreshCloudIdentity(bool force = false)
    {
        var user = cloud.User;
        var avatarUrl = cloud.AvatarUri?.AbsoluteUri;
        if (!force && user == displayedCloudUser && avatarUrl == displayedAvatarUrl) return;
        displayedCloudUser = user; displayedAvatarUrl = avatarUrl;
        var localUser = CloudIdentity.Create(user, avatarUrl, preferences.Value.Player);
        CloudIdentity.Apply(((DummyAPIAccess)API).LocalUser, localUser, preferences.Value.Player);
    }

    public override void SetHost(GameHost host)
    {
        base.SetHost(host);
        foreach (var handler in host.AvailableInputHandlers)
            LazerRaveInputPolicy.Apply(handler);
        if (host.Window is { } window)
        {
            window.Title = "LazerRave";
            using var icon = typeof(LazerRaveGame).Assembly.GetManifestResourceStream("LazerRave.Branding.logo.png")
                ?? throw new InvalidDataException("The frontend logo resource is missing.");
            window.SetIconFromStream(icon);
        }
    }
    protected override IDictionary<FrameworkSetting, object> GetFrameworkConfigDefaults()
    {
        var defaults = base.GetFrameworkConfigDefaults();
        defaults[FrameworkSetting.Locale] = "en";
        defaults[FrameworkSetting.FrameSync] = FrameSync.Unlimited;
        defaults[FrameworkSetting.WindowMode] = WindowMode.Windowed;
        defaults[FrameworkSetting.WindowedSize] = new System.Drawing.Size(1920, 1080);
        if (benchmark is not null)
        {
            defaults[FrameworkSetting.WindowMode] = WindowMode.Windowed;
            defaults[FrameworkSetting.WindowedSize] = new System.Drawing.Size(1280, 800);
        }
        return defaults;
    }
    protected override BeatmapDifficultyCache CreateBeatmapDifficultyCache() => new() { AutomaticCalculationEnabled = false };
    protected override Loader CreateLoader() => new LazerRaveLoader();
    protected override Storage CreateStorage(GameHost host, Storage defaultStorage) => new OsuStorage(host, new NativeStorage(ApplicationPaths.UserData));
    protected override OsuLogo CreateLogo() => new LazerRaveLogo();
    protected override SettingsOverlay CreateSettingsOverlay() => settingsPanel = new LazerRaveSettingsPanel(preferences, ApplySettings);
    protected override LoginOverlay CreateLoginOverlay() => cloudPanel = new LazerRaveCloudPanel(this, cloud);
    protected override Toolbar CreateToolbar() => new LazerRaveToolbar();
    protected override NowPlayingOverlay CreateNowPlayingOverlay() => new LazerRaveNowPlayingOverlay();
    public override SettingsSubsection CreateSettingsSubsectionFor(InputHandler handler) => handler is TouchHandler or JoystickHandler or MidiHandler
        ? null! : base.CreateSettingsSubsectionFor(handler);
    public override Drawable CreateFrameLimiterSetting(FrameworkConfigManager config) => new SettingsDropdown<string>
    {
        LabelText = osu.Game.Localisation.GraphicsSettingsStrings.FrameLimiter,
        Current = preferences.FrontendFrameLimit,
        Items = new[] { "60", "120", "144", "165", "240", "360", "480", "1000", "Display", "2x", "4x", "8x", "Unlimited", preferences.FrontendFrameLimit.Value }.Distinct(),
    };

    private void ApplyFrontendFrameLimit()
    {
        var mode = preferences.FrontendFrameLimit.Value switch
        {
            "Display" => FrameSync.VSync, "2x" => FrameSync.Limit2x, "4x" => FrameSync.Limit4x, "8x" => FrameSync.Limit8x,
            _ => FrameSync.Unlimited,
        };
        applyingFrameLimit = true;
        try
        {
            frameworkFrameSync!.Value = mode;
            if (int.TryParse(preferences.FrontendFrameLimit.Value, out var cap)) Host.MaximumDrawHz = cap;
            else if (mode == FrameSync.Unlimited) Host.MaximumDrawHz = 0;
        }
        finally { applyingFrameLimit = false; }
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Textures.AddTextureSource(Host.CreateTextureLoaderStore(new DllResourceStore(typeof(LazerRaveGame).Assembly)));
        Ruleset.Value = new ManiaRuleset().RulesetInfo;
        BeatmapManager.ExternalBeatmapResolver = info => catalog.Resolve(info, Audio, Textures, preferences.Value.Encoding);
        media = new ResourceStore<byte[]>(); ResetMedia();
        preview = new PreviewPlayer(Audio, media);
        Audio.Tracks.AddAdjustment(AdjustableProperty.Volume, menuTrackVolume);
        Add(catalog);
    }
    protected override void LoadComplete()
    {
        base.LoadComplete();
        if (benchmark is not null) benchmarkClock.Start();
        if (Host.Window is not null) viewport = new NativeGameViewport(Host.Window);
        ScreenContainer.Add(chrome = new PopoverContainer { RelativeSizeAxes = Axes.Both, Depth = -2 });
        BuildChrome();
        cloudPanel.Hide();
        chrome.Hide();
        Beatmap.BindValueChanged(OnSelectionChanged);
        ReportLibraryError(startupMessage);
        preferences.Speed.BindValueChanged(_ => QueueSettingsSave());
        preferences.Offset.BindValueChanged(_ => QueueSettingsSave());
        foreach (var option in preferences.PlayOptions.Values) option.BindValueChanged(_ => QueueSettingsSave());
        preferences.Roots.CollectionChanged += (_, _) => QueueSettingsSave();
        frameworkFrameSync = Dependencies.Get<FrameworkConfigManager>().GetBindable<FrameSync>(FrameworkSetting.FrameSync);
        frameworkFrameSync.BindValueChanged(change =>
        {
            if (!applyingFrameLimit) preferences.FrontendFrameLimit.Value = change.NewValue switch
            {
                FrameSync.VSync => "Display", FrameSync.Limit2x => "2x", FrameSync.Limit4x => "4x", FrameSync.Limit8x => "8x", _ => "Unlimited",
            };
        });
        preferences.FrontendFrameLimit.BindValueChanged(_ => { ApplyFrontendFrameLimit(); QueueSettingsSave(); }, true);
        foreach (var setting in new[] { preferences.Arrangement, preferences.Encoding, preferences.Window,
            preferences.Player, preferences.Avatar, preferences.FrameLimit, preferences.RenderProfile, preferences.Presentation })
            setting.BindValueChanged(_ => QueueSettingsSave());
    }

    private void ResetMedia()
    {
        var cache = Path.Combine(ApplicationPaths.Cache, "frontend");
        var store = new FileMediaStore(ApplicationPaths.LibraryRoots(preferences.Value.Roots), preferences.Value.Avatar, cache);
        media.AddStore(store); Textures.AddTextureSource(Host.CreateTextureLoaderStore(store));
    }
    private void BuildChrome()
    {
        chrome.Clear();
        chrome.Add(folderBar = new LazerRaveFolderBar(catalog, Navigate) { Position = new Vector2(28, 8) });
        keys = new OsuDropdown<string> { Width = 128, Scale = new Vector2(1.25f), Position = new Vector2(464, 8), Items = new[] { "7Key", "5Key", "9Key", "10Key", "14Key", "All" } };
        keys.Current.Value = catalog.Keys == 0 ? "All" : $"{catalog.Keys}Key";
        keys.Current.BindValueChanged(change => { catalog.Filter(catalog.Directory, change.NewValue == "All" ? 0 : int.Parse(change.NewValue.Replace("Key", ""))); songSelect?.RefreshKeyFilter(); });
        chrome.Add(keys);
        UpdateFolderBar();
        RefreshCloudIdentity(true);
    }
    private void UpdateFolderBar()
    {
        folderBar?.Refresh();

        if (keys is not null) keys.X = 28 + Math.Max(420, folderBar?.OccupiedWidth ?? 0) + 16;
    }
    private void Navigate(string? path)
    {
        if (ScreenStack.CurrentScreen is not LazerRaveSongSelect) return;
        songSelect.ClearSearch(); catalog.SetSearch("");
        catalog.Filter(path, catalog.Keys); UpdateFolderBar();
        songSelect.RefreshKeyFilter();
    }
    private void ReportLibraryError(string? error)
    {
        if (!string.IsNullOrWhiteSpace(error)) SetLibraryMessage(error);
    }
    public bool ParentFolder()
    {
        if (settingsPanel.State.Value == Visibility.Visible) { settingsPanel.Hide(); return true; }
        if (ScreenStack.CurrentScreen is GamePlayScreen play) { play.RequestReturn(); return true; }
        if (catalog.Query.Length > 0) { songSelect.ClearSearch(); catalog.SetSearch(""); songSelect.RefreshKeyFilter(); return true; }
        if (catalog.Directory is null) return false;
        var parent = catalog.Library.Folders.FirstOrDefault(folder => string.Equals(folder.Path, catalog.Directory, StringComparison.OrdinalIgnoreCase))?.Parent;
        Navigate(parent); return true;
    }
    public void ToggleSettings() => settingsPanel.ToggleVisibility();
    public void SelectRandom()
    {
        var available = catalog.GetBeatmapSets(null).SelectMany(set => set.Beatmaps).Where(info => catalog.Keys == 0 || catalog.ChartFor(info)?.Keys == catalog.Keys).ToArray();
        if (available.Length > 0) Beatmap.Value = BeatmapManager.GetWorkingBeatmap(available[Random.Shared.Next(available.Length)]);
    }
    public void StartGame(BeatmapInfo info) => StartChart(info, null);
    public void WatchReplay(string path) => StartChart(Beatmap.Value.BeatmapInfo, path);
    private void StartChart(BeatmapInfo info, string? replay)
    {
        if (ScreenStack.CurrentScreen is not LazerRaveSongSelect { IsRoomSelection: false } || catalog.ChartFor(info) is not { } chart || !catalog.IsVisible(info) || viewport is null) return;
        if (preferences.PlayOptions["battle"].Value == 4)
        {
            SetLibraryMessage(D("Ghost Battle requires rival selection in the classic menu."));
            return;
        }
        if (!SaveSettings()) { settingsPanel.Show(); return; }
        ++previewGeneration; preview.Stop(); settingsPanel.Hide(); chrome.Hide();
        CloseAllOverlays();
        menuTrackVolume.Value = 0;
        ScreenStack.Push(new GamePlayScreen(bridge, preferences.Value, chart, viewport, OnGameReturned)
        { RelativeSizeAxes = Axes.Both, Records = Records, RecordPlayer = cloud.User?.Username ?? preferences.Value.Player, ReplaySource = replay });
    }
    public void SetLibraryMessage(osu.Framework.Localisation.LocalisableString text) => Notifications.Post(new osu.Game.Overlays.Notifications.SimpleNotification { Text = text });
    private void UpdateMultiplayerStart()
    {
        var room = cloud.Room;
        if (!cloud.Connected || room is not { MatchId: { } match, StartAt: { } start } || launchedCloudMatch == match
            || room.State is not ("countdown" or "playing") || cloud.ServerNow < start || viewport is null) return;
        if (ScreenStack.CurrentScreen is GamePlayScreen) return;
        var member = room.Members.FirstOrDefault(member => member.Id == cloud.User?.Id);
        if (member is not { Ready: true, ContentState: "available" } || cloud.AvailableChart is not { } path) return;
        var chart = catalog.Library.Songs.SelectMany(song => song.Charts).FirstOrDefault(chart => chart.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (chart is null) { launchedCloudMatch = match; cloud.SetGameStatus("The room chart is not in the library. Rescan before starting another round."); return; }
        if (ScreenStack.CurrentScreen is LazerRaveSongSelect selection) { selection.Exit(); return; }
        if (ScreenStack.CurrentScreen is not LazerRaveMultiplayer)
        {
            if (multiplayer is { ValidForResume: true }) multiplayer.MakeCurrent();
            return;
        }
        launchedCloudMatch = match;
        if (!SaveSettings()) { settingsPanel.Show(); cloud.SetGameStatus("Game settings could not be saved."); return; }
        ++previewGeneration; preview.Stop(); settingsPanel.Hide(); chrome.Hide(); CloseAllOverlays(); menuTrackVolume.Value = 0;
        ScreenStack.Push(new GamePlayScreen(bridge, preferences.Value, chart, viewport, error =>
        {
            menuTrackVolume.Value = 1;
            cloud.SetGameStatus(error ?? "Returned to room.");
            _ = RefreshAsync(false, error);
            ScreenStack.Push(new LazerRaveRoundResults(cloud, match, chart.Title, error) { RelativeSizeAxes = Axes.Both });
        }) { RelativeSizeAxes = Axes.Both, MultiplayerClient = cloud, MatchId = match, Records = Records, RecordPlayer = cloud.User?.Username ?? preferences.Value.Player });
    }
    private void OnGameReturned(string? error)
    {
        if (benchmark is not null)
        {
            SaveFrontendFrames();
            File.WriteAllText(Path.Combine(bridge.Runtime, "benchmark-result.txt"), error is null ? "PASS: timed session completed.\n" : "FAIL: " + error);
            Host.Exit();
            return;
        }
        menuTrackVolume.Value = 1;
        chrome.Show(); if (error is not null) SetLibraryMessage(error);
        _ = RefreshAsync(false, error);
    }
    private void ApplySettings()
    {
        if (!SaveSettings()) return;
        settingsPanel.Hide(); Rescan();
    }
    private void QueueSettingsSave()
    {
        pendingSettingsSave?.Cancel();
        settingsPanel.SetSaveStatus(D("Saving…"), false);
        pendingSettingsSave = Scheduler.AddDelayed(() => SaveSettings(), 400);
    }
    private bool SaveSettings()
    {
        pendingSettingsSave?.Cancel();
        if (!preferences.HasChanges)
        {
            settingsPanel.SetSaveStatus(D("Saved"), false);
            return true;
        }
        try
        {
            var previous = preferences.Value;
            preferences.Save();
            settingsPanel.SetSaveStatus(D("Saved"), false);
            bool rootsChanged = !previous.Roots.SequenceEqual(preferences.Value.Roots, StringComparer.OrdinalIgnoreCase);
            if (rootsChanged || previous.Avatar != preferences.Value.Avatar) ResetMedia();
            if (rootsChanged || previous.Avatar != preferences.Value.Avatar || previous.Player != preferences.Value.Player) BuildChrome();
            if (rootsChanged || previous.Encoding != preferences.Value.Encoding) Rescan();
            return true;
        }
        catch (Exception error)
        {
            settingsPanel.SetSaveStatus(error.Message, true);
            return false;
        }
    }
    public void Rescan() => _ = RefreshAsync(true);
    private async Task RefreshAsync(bool sync, string? returnError = null)
    {
        if (refreshing) return;
        refreshing = true; ++previewGeneration; preview.Stop(); SetLibraryMessage(D("Scanning…"));
        try
        {
            var library = await bridge.Catalog(preferences.Value, sync, lifetime.Token);
            Schedule(() =>
            {
                catalog.Replace(library, ApplicationPaths.LibraryRoots(preferences.Value.Roots)); UpdateFolderBar(); songSelect?.RefreshKeyFilter();
                var selected = catalog.GetBeatmapSets(null).SelectMany(set => set.Beatmaps).FirstOrDefault(info => info.ID == Beatmap.Value.BeatmapInfo.ID);
                if (selected is not null) Beatmap.Value = BeatmapManager.GetWorkingBeatmap(selected);
                ReportLibraryError(returnError);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Schedule(() => SetLibraryMessage(error.Message)); }
        finally { Schedule(() => refreshing = false); }
    }
    private async void OnSelectionChanged(osu.Framework.Bindables.ValueChangedEvent<WorkingBeatmap> change)
    {
        int generation = ++previewGeneration; preview.Stop();
        if (catalog.ChartFor(change.NewValue.BeatmapInfo) is not { } chart) return;
        if (ScreenStack.CurrentScreen is not LazerRaveSongSelect) return;
        try
        {
            var plan = await Task.Run(() => PreviewPlan.Read(chart, preferences.Value.Encoding), lifetime.Token);
            Schedule(() => { if (generation == previewGeneration && ScreenStack.CurrentScreen is LazerRaveSongSelect) preview.Start(plan, Time.Current); });
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Schedule(() => SetLibraryMessage(error.Message)); }
    }
    protected override void ScreenChanged(IOsuScreen? current, IOsuScreen? next)
    {
        base.ScreenChanged(current, next);
        preview?.Stop(); ++previewGeneration;
        if (next is LazerRaveSongSelect select)
        {
            songSelect = select;
            Ruleset.Value = new ManiaRuleset().RulesetInfo;
            chrome?.Show();
        }
        else chrome?.Hide();
    }
    protected override void Update()
    {
        base.Update(); preview?.Update(Time.Current);
        if (int.TryParse(preferences.FrontendFrameLimit.Value, out var cap) && Host.MaximumDrawHz != cap) Host.MaximumDrawHz = cap;
        if (Interlocked.Exchange(ref cloudChanged, 0) != 0) RefreshCloudIdentity();
        UpdateMultiplayerStart();

        if (benchmark is null || !benchmarkClock.IsRunning) return;
        if (benchmarkClock.Elapsed.TotalSeconds >= nextFrameSample)
        {
            nextFrameSample = benchmarkClock.Elapsed.TotalSeconds + 1;
            frontendFrames.Add(FormattableString.Invariant($"{benchmarkClock.Elapsed.TotalSeconds:F3},{Host.DrawThread.Clock.FramesPerSecond:F3},{Host.UpdateThread.Clock.FramesPerSecond:F3},{Host.DrawThread.Clock.ElapsedFrameTime:F4},{Host.UpdateThread.Clock.ElapsedFrameTime:F4}"));
        }
        if (benchmarkClock.Elapsed.TotalSeconds > (benchmark.Seconds == 0 ? 900 : benchmark.Seconds + 90))
        {
            File.WriteAllText(Path.Combine(bridge.Runtime, "benchmark-result.txt"), "FAIL: benchmark timed out.\n");
            lifetime.Cancel(); Host.Exit(); benchmarkClock.Stop(); return;
        }
        if (!benchmarkSelected && ScreenStack.CurrentScreen is LazerRaveMainMenu menu && menu.IsLoaded)
        {
            benchmarkSelected = true; CloseAllOverlays(); ScreenStack.Push(new LazerRaveSongSelect());
        }
        else if (!benchmarkStarted && ScreenStack.CurrentScreen is LazerRaveSongSelect select && select.IsLoaded)
        {
            var info = catalog.GetBeatmapSets(null).SelectMany(set => set.Beatmaps)
                .FirstOrDefault(info => string.Equals(catalog.ChartFor(info)?.Path, benchmark.Chart, StringComparison.OrdinalIgnoreCase));
            if (info is null) throw new InvalidDataException("Benchmark chart is absent from the engine catalog.");
            benchmarkStarted = true; Beatmap.Value = BeatmapManager.GetWorkingBeatmap(info); StartGame(info);
            if (benchmark.Seconds > 0) Scheduler.AddDelayed(() => { if (ScreenStack.CurrentScreen is GamePlayScreen play) play.RequestReturn(); }, benchmark.Seconds * 1000);
        }
    }
    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing && !IsDisposed)
        {
            pendingSettingsSave?.Cancel();
            frameworkFrameSync?.UnbindAll();
            try { if (preferences.HasChanges) preferences.Save(); }
            catch (Exception error)
            {
                var file = Path.Combine(ApplicationPaths.Logs, "settings-save-error.txt");
                try { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, error.ToString()); }
                catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { Console.Error.WriteLine(error); }
            }
            lifetime.Cancel(); viewport?.Dispose(); preview?.Dispose();
            cloud.Changed -= CloudChanged;
            _ = cloud.DisposeAsync();
        }
        base.Dispose(isDisposing);
    }
}
