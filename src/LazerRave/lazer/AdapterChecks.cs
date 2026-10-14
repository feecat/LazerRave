using LazerRave.Bridge;
using System.Xml.Linq;
using Tomlyn;
using Tomlyn.Model;

namespace LazerRave.Lazer;

internal static class AdapterChecks
{
    private static void CheckNativeKeyBindings(string directory)
    {
        string runtime = Path.Combine(directory, "key-bindings");
        string folder = Path.Combine(runtime, "LR2files", "Config");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "keyconfig.xml");
        File.WriteAllText(file, "<keyconfig><key01><id>31</id><id>263</id></key01><key02><id>32</id></key02><key10><id>30</id><id>768</id></key10><future>retained</future></keyconfig>");
        var store = new NativeKeyBindings(runtime);
        var draft = store.Read(7);
        draft.Assign(1, 33, false);
        store.Save([draft]);
        var saved = XDocument.Load(file);
        if (!saved.Root!.Element("key01")!.Elements("id").Select(node => (int)node).SequenceEqual(new[] { 33, 263 })
            || saved.Root.Element("future")?.Value != "retained" || !File.Exists(file + ".lazerrave.bak"))
            throw new InvalidDataException("Editing LR2 keyboard bindings lost controller assignments, unknown fields or the backup.");
        draft = store.Read(7);
        draft.Assign(2, 36, false);
        saved.Root.Element("key02")!.Element("id")!.Value = "37";
        saved.Save(file);
        try { store.Save([draft]); throw new InvalidDataException("An external LR2 binding edit was silently overwritten."); }
        catch (IOException error) when (error.Message.Contains("changed outside")) { }
        draft = store.Read(7); draft.RestoreDefaults(); store.Save([draft]);
        var defaults = store.Read(7);
        if (!Enumerable.Range(1, 7).Select(row => defaults.Bindings[row].Single()).SequenceEqual(new[] { 31, 32, 33, 57, 36, 37, 38 })
            || defaults.Bindings[10].Single() != 30 || defaults.Bindings[11].Single() != 39)
            throw new InvalidDataException("The LR2 default layout must use A / semicolon and S D F Space J K L.");
        if (NativeKeyBindings.FileName(5) == NativeKeyBindings.FileName(7) || NativeKeyBindings.FileName(9) == NativeKeyBindings.FileName(7))
            throw new InvalidDataException("Native key modes must retain their separate LR2 files.");
    }

    private static void CheckNativeSettings(string directory)
    {
        string runtime = Path.Combine(directory, "native-settings");
        var config = new NativeConfiguration(runtime);
        config.Save(new Dictionary<string, string> { ["sound/output"] = "2", ["sound/driver"] = "3", ["sound/volumemaster"] = "60" },
            new Dictionary<string, string>());
        var settings = new NativeSettings(runtime, false);
        bool outsideReload = false;
        settings.Numbers["sound/output"].BindValueChanged(_ => outsideReload |= !settings.IsReloading);
        config.Save(new Dictionary<string, string> { ["sound/output"] = "1", ["sound/driver"] = "5" }, new Dictionary<string, string>());
        settings.Reload();
        if (outsideReload || settings.HasChanges || settings.Numbers["sound/driver"].Value != 5)
            throw new InvalidDataException("Reloading native configuration must preserve its device without queuing a save.");
        var document = NativeConfiguration.Read(config.ConfigPath);
        document.Root!.Element("sound")!.Element("volumemaster")!.Remove();
        document.Save(config.ConfigPath);
        settings.Reload();
        if (settings.Numbers["sound/volumemaster"].Value != 100 || settings.HasChanges || settings.Error is not null)
            throw new InvalidDataException("Missing native settings must use installation defaults, not stale values.");
        settings.Numbers["sound/volumemaster"].Value = 80;
        settings.Save();
        var restored = new NativeSettings(runtime, false);
        if (restored.Numbers["sound/volumemaster"].Value != 80 || restored.Numbers["sound/driver"].Value != 5)
            throw new InvalidDataException("Native audio settings did not survive a restart.");
    }

    private static void CheckFrontendBaseSpeed(string directory)
    {
        static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
        string file = Path.Combine(directory, "note-base.bms");
        var chart = new Chart(file, "Note base", "", 7, 10, 4, 999, 100, null);
        File.WriteAllText(file, "#BPM 120\n#BPM01 200\n#STOP01 192000\n#LNOBJ ZZ\n#00101:01010101\n#00108:00010000\n#00109:01000000\n#00111:010001ZZ\n#00112:00000100\n#00116:00000100\n#00151:00000101\n#00152:01000001\n#001D1:01010101\n");
        var timeline = BmsTimeline.Read(chart, "auto");
        Require(timeline.NoteBaseBpm == 200 && timeline.NotesPerBpm[120] == 2 && timeline.NotesPerBpm[200] == 4, "Base BPM must count playable note heads, not duration, BGM, mines or long-note tails.");
        var profile = new ReadingSpeedProfile(true, 2, 1500) { NormalizeBase = true, BaseBpm = 150 };
        var settings = new FrontendSettings([]) { SpeedProfiles = new(profile, profile), PlayOptions = new Dictionary<string, int> { ["hsfix"] = 4, ["base_speed"] = 200, ["lane_cover"] = 1, ["cover"] = 95 } };
        var applied = settings.ForChart(chart);
        Require(Math.Abs(applied.Speed * applied.PlayOptions["base_speed"] / 100 - 1.5) < .0001 && applied.PlayOptions["hsfix"] == 0, "Frontend normalization must apply base BPM / note-dominant BPM times the manual multiplier with LR2 speed fixing off.");
        Require(settings.SpeedProfiles.Seven.Target == 1500 && settings.PlayOptions["hsfix"] == 4, "Frontend normalization changed saved automatic reading or classic options.");
        var request = EngineBridge.Request("validate", applied, chart);
        var sent = request.Root!.Element("play-options")!.Elements("option").ToDictionary(option => (string)option.Attribute("name")!, option => (int)option.Attribute("value")!);
        Require(request.Root.Element("speed")!.Value == "150" && sent["hsfix"] == 0 && sent["base_speed"] == 100, "Bridge did not send frontend-computed speed values.");
        File.WriteAllText(file, "#BPM 120\n#BPM01 200\n#BPM02 120\n#00011:01\n#00108:01\n#00111:0101\n#00208:02\n#00211:010101\n");
        Require(BmsTimeline.Read(chart, "auto").NoteBaseBpm == 120, "Repeated BPM segments must share one note count.");
        File.WriteAllText(file, "#BPM 120\n#BPM01 200\n#00011:01\n#00108:01\n#00111:01\n");
        Require(BmsTimeline.Read(chart, "auto").NoteBaseBpm == 120, "Equal note counts must choose the first playable BPM deterministically.");
        File.WriteAllText(file, "#BPM 1000\n#00011:0101\n"); applied = settings.ForChart(chart);
        Require(Math.Abs(applied.Speed * applied.PlayOptions["base_speed"] / 100 - .3) < .0001, "Low normalized multipliers must not be silently clipped to 0.5.");
        string preferences = Path.Combine(directory, "base-speed.toml");
        var desktop = new DesktopSettings(new FrontendSettings([]), preferences, false);
        desktop.NormalizeBaseSpeed.Value = true; desktop.BaseBpm.Value = 180; desktop.Speed.Value = 2.5;
        desktop.SelectSpeedProfile(5); desktop.NormalizeBaseSpeed.Value = true; desktop.BaseBpm.Value = 150; desktop.Speed.Value = 2; desktop.Save();
        var saved = FrontendSettings.Read(preferences);
        Require(saved.SpeedProfiles.Seven.NormalizeBase && saved.SpeedProfiles.Seven.BaseBpm == 180 && saved.SpeedProfiles.Five.BaseBpm == 150, "Independent frontend base BPM settings did not survive profile switches and reload.");
    }

    public static void RunExperience(string directory)
    {
        static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
        Directory.CreateDirectory(directory);
        using (var keyWindow = new LazerRaveKeyBindingWindow(new NativeKeyBindings(directory)))
        {
            keyWindow.BuildLayout();
            using var dropdown = keyWindow.CreateModeDropdown();
            Require(dropdown.AutoSizeAxes.HasFlag(osu.Framework.Graphics.Axes.Y) && !dropdown.RelativeSizeAxes.HasFlag(osu.Framework.Graphics.Axes.Y), "Key-mode dropdown height must remain automatic.");
            Require(dropdown.Items.SequenceEqual(new[] { "7Key", "5Key" }), "Key-mode dropdown must expose only 7K and 5K.");
        }
        CheckFrontendBaseSpeed(directory);
        string root = Path.Combine(directory, "experience-library"); Directory.CreateDirectory(root);
        var document = new XDocument(new XElement("lazerrave", new XAttribute("status", "ok")));
        Chart Create(string name, int keys)
        {
            string folder = Path.Combine(root, name); Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, name + ".bms"); File.WriteAllText(path, "#TITLE " + name);
            string md5 = Convert.ToHexStringLower(System.Security.Cryptography.MD5.HashData(File.ReadAllBytes(path)));
            document.Root!.Add(new XElement("chart", new XAttribute("path", path), new XAttribute("title", name), new XAttribute("keys", keys), new XAttribute("level", 10), new XAttribute("difficulty", 4), new XAttribute("bpm", 150), new XAttribute("md5", md5)));
            return new(path, name, "", keys, 10, 4, 150, 100, null) { Md5 = md5 };
        }
        var five = Create("Five", 5); var seven = Create("Seven", 7); var other = Create("DP", 14);
        var library = SongLibrary.Parse(document, directory, [root]);
        var annotations = new UserLibraryStore(Path.Combine(directory, "library-preferences.json"));
        var catalog = new BmsBeatmapStore { UserLibrary = annotations }; catalog.Replace(library, [root]); catalog.Filter(root, 0);
        Require(catalog.GetBeatmapSets(null).SelectMany(set => set.Beatmaps).All(info => catalog.ChartFor(info)?.Keys is 5 or 7), "Unsupported key modes leaked into modern selection.");
        Require(library.Songs.SelectMany(song => song.Charts).Any(chart => chart.Keys == 14), "Unsupported charts must remain in the library metadata.");
        annotations.ToggleFavorite([five]); catalog.SetView(LibraryView.Favorites);
        Require(catalog.ChildFolders.Count() == 0 && catalog.GetBeatmapSets(null).SelectMany(set => set.Beatmaps).Single().Difficulty.CircleSize == 5, "Favorites must flatten supported songs without folder rows.");
        catalog.SetSearch("no-match"); Require(catalog.GetBeatmapSets(null).Count == 0, "Favorite search did not filter."); catalog.SetSearch("");
        catalog.SetLevelRange(11, null); Require(catalog.GetBeatmapSets(null).Count == 0, "Favorite difficulty filter did not apply."); catalog.SetLevelRange(null, null);
        var time = DateTimeOffset.UtcNow; annotations.RecordPlay(five, time); annotations.RecordPlay(seven, time.AddMinutes(1)); annotations.RecordPlay(five, time.AddMinutes(2));
        catalog.SetView(LibraryView.Recent);
        Require(catalog.GetBeatmapSets(null).Count == 2 && catalog.GetBeatmapSets(null)[0].Beatmaps[0].LastPlayed == time.AddMinutes(2), "Recent plays must be unique and most recent first.");
        Require(new UserLibraryStore(Path.Combine(directory, "library-preferences.json")).IsFavorite(five), "Favorite did not survive reload.");
        string moved = Path.Combine(root, "moved.bms"); File.Move(five.Path, moved); var renamed = five with { Path = moved };
        Require(annotations.IsFavorite(renamed), "Favorite identity must survive moving a chart.");
        annotations.ToggleFavorite([seven with { Md5 = null }]);
        Require(annotations.IsFavorite(seven), "Favorite identity changed when catalog hashes became available.");
        var history = new UserLibraryStore(Path.Combine(directory, "recent-bound.json"));
        for (int i = 0; i < 110; i++) history.RecordPlay(seven with { Md5 = i.ToString("x32") }, time.AddMinutes(i));
        Require(new UserLibraryStore(Path.Combine(directory, "recent-bound.json")).Recent.Count == 100, "Recent history must remain bounded after reload.");
        var lockedPath = Path.Combine(directory, "locked.json"); var lockedStore = new UserLibraryStore(lockedPath); lockedStore.RecordPlay(seven, time);
        var before = File.ReadAllBytes(lockedPath);
        if (OperatingSystem.IsWindows())
        {
            using var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            bool writeRejected = false; try { lockedStore.ToggleFavorite([seven]); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { writeRejected = true; }
            Require(writeRejected && !lockedStore.IsFavorite(seven) && before.SequenceEqual(File.ReadAllBytes(lockedPath)), "Failed preference write changed saved data or memory.");
        }
        string corrupt = Path.Combine(directory, "corrupt.json"); File.WriteAllText(corrupt, "{broken"); var damaged = new UserLibraryStore(corrupt);
        bool rejected = false; try { damaged.ToggleFavorite([renamed]); } catch (InvalidDataException) { rejected = true; }
        Require(rejected && File.ReadAllText(corrupt) == "{broken", "Corrupt preferences were overwritten.");
        var settings = new FrontendSettings([root]) { SpeedProfiles = new(new(true, 3, 600), new(true, 2, 750)) };
        Require(settings.ForChart(renamed).Speed == 4 && settings.ForChart(seven).Speed == 5 && settings.ForChart(seven).PlayOptions["hsfix"] == 5, "Automatic reading speed must use main BPM, not constant speed.");
        var covered = settings with { PlayOptions = new Dictionary<string, int> { ["lane_cover"] = 1, ["cover"] = 25, ["lift_enabled"] = 1, ["lift"] = 25 } };
        Require(covered.ForChart(renamed).Speed == 2 && settings.SpeedProfiles.Five.Target == 600, "Cover or lift changed the saved target or did not adjust initial speed.");
        var legacyOptions = new FrontendSettings([root]) { PlayOptions = new Dictionary<string, int> { ["assist2"] = 1, ["dp_flip"] = 1 } };
        Require(legacyOptions.ForChart(seven).PlayOptions["assist2"] == 0 && legacyOptions.ForChart(seven).PlayOptions["dp_flip"] == 0 && legacyOptions.PlayOptions["assist2"] == 1, "Hidden 2P options must not affect single play or overwrite legacy preferences.");
        var effective = covered.ForChart(seven);
        var records = new PlayRecordStore(Path.Combine(directory, "experience-records"));
        string chartHash = PlayRecordStore.HashChart(seven.Path).GetAwaiter().GetResult();
        var result = records.Save(Guid.NewGuid(), chartHash, seven, "Player", effective, new GameplaySnapshot(150, 5000, 100, 0, 1, 5, true, false));
        var playback = records.Read(chartHash).Single().PlaybackSettings(settings);
        Require(result is not null && playback.Speed == effective.Speed && playback.PlayOptions["hsfix"] == 5, "Replay snapshot lost the effective automatic speed.");
        rejected = false; try { settings.ForChart(other); } catch (ArgumentException) { rejected = true; } Require(rejected, "Unsupported play launch was accepted.");
        string file = Path.Combine(directory, "experience-settings.toml"); File.WriteAllText(file, "future = 7\n[reading_speed.five]\nfuture_profile = 9\n");
        var desktop = new DesktopSettings(new FrontendSettings([root]), file, false);
        desktop.Speed.Value = 3.5; desktop.AutomaticSpeed.Value = true; desktop.TargetSpeed.Value = 750;
        desktop.SelectSpeedProfile(5); desktop.Speed.Value = 2.5; desktop.TargetSpeed.Value = 600; desktop.Save();
        var restored = FrontendSettings.Read(file);
        Require(restored.SpeedProfiles.Seven.Automatic && restored.SpeedProfiles.Seven.Manual == 3.5 && restored.SpeedProfiles.Five.Manual == 2.5, "Per-key speed settings did not survive a restart.");
        var saved = Toml.ToModel(File.ReadAllText(file)); Require((long)saved["future"] == 7 && (long)((TomlTable)((TomlTable)saved["reading_speed"])["five"])["future_profile"] == 9, "Speed persistence lost unknown fields.");
        Require(PlayOptionCatalog.Visible("Common", false).Count() == 3 && PlayOptionCatalog.Visible("Practice", false).All(PlayOptionCatalog.IsPractice), "Common and practice options are not separated.");
        Require(new[] { "Common", "Speed", "Lanes", "Practice", "Advanced" }.SelectMany(category => PlayOptionCatalog.Visible(category, true)).All(PlayOptionCatalog.IsSinglePlay), "2P options leaked into modern settings.");
        CheckNativeKeyBindings(directory);
        var draft = new NativeKeyBindings(Path.Combine(directory, "key-bindings")).Read(7);
        var second = draft.Bindings.Where(pair => pair.Key >= 20).ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        draft.RestoreDefaults(firstPlayerOnly: true);
        Require(second.All(pair => pair.Value.SequenceEqual(draft.Bindings[pair.Key])), "Restoring single-play bindings changed legacy 2P bindings.");
    }

    public static void Run(FrontendSettings settings, SongLibrary library, string directory)
    {
        RunExperience(Path.Combine(directory, "experience"));
        static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
        var store = new BmsBeatmapStore(); store.Replace(library, ApplicationPaths.LibraryRoots(settings.Roots));
        Require(library.Songs.SelectMany(song => song.Charts).All(chart => BmsBeatmapStore.StableId(chart.Path) == BmsBeatmapStore.StableId(chart.Path.ToUpperInvariant())), "Chart identities must ignore Windows path case.");
        var levels = library.Folders.Where(folder => folder.Parent is null).Select(folder => folder.Path)
            .Concat(library.Folders.Where(folder => folder.Parent is not null).Select(folder => folder.Path))
            .ToArray();
        foreach (var folder in levels)
        foreach (int keys in new[] { 5, 7, 0 })
        {
            store.Filter(folder, keys);
            var sets = store.GetBeatmapSets(null).ToArray();
            Require(sets.Select(set => set.ID).Distinct().Count() == sets.Length, "Duplicate song directories.");
            Require(sets.All(set => set.Beatmaps.Any(info => keys == 0 || store.ChartFor(info)?.Keys == keys)), "Key filter returned an unrelated song.");
            Require(sets.SelectMany(set => set.Beatmaps).All(info => store.ChartFor(info) is { } chart && info.DifficultyName.Contains($"Lv.{chart.Level}")), "BMS difficulty labels must preserve chart levels.");
            var expected = library.Browse(folder, "", keys == 0 ? null : keys, 0)
                .Where(entry => entry.Song is not null).SelectMany(entry => entry.Song!.Charts)
                .Where(chart => SupportedKeyModes.Contains(chart.Keys) && (keys == 0 || chart.Keys == keys))
                .Select(chart => BmsBeatmapStore.StableId(chart.Path)).ToHashSet();
            Require(expected.SetEquals(sets.SelectMany(set => set.Beatmaps).Where(store.IsVisible).Select(info => info.ID)),
                "Folder scope included a nested chart or a sibling folder.");
        }
        CheckNestedFolders(directory);
        CheckPlayRecords(directory);
        CheckClientCapabilities();
        CheckCloudPanelDrawing();
        CheckCloudIdentity();
        CheckCloudRoomMapping();
        CheckPlaySettings(directory);
        CheckLibraryDefaults(directory);
        CheckNativeSettings(directory);
        CheckNativeKeyBindings(directory);

        var path = Path.Combine(directory, "settings.toml");
        File.WriteAllText(path, "signature = \"preserved\"\n[play]\nfuture_option = 7\n");
        var preferences = new DesktopSettings(settings, path, false);
        preferences.Window.Value = "2048x1152"; preferences.Presentation.Value = "standalone"; preferences.Save();
        var saved = Toml.ToModel(File.ReadAllText(path));
        Require((string)saved["signature"] == "preserved" && (long)saved["window_width"] == 2048, "Settings updates lost unknown fields or a custom window size.");
        Require(FrontendSettings.Read(path).Presentation == "standalone", "Separate-window selection must survive a restart.");
        var fixtureChart = new Chart(Path.Combine(directory, "fixture.bms"), "Fixture", "", 7, 1, 2, 120, 1, null);
        File.WriteAllText(fixtureChart.Path, "#TITLE Fixture");
        foreach (var option in PlayOptionCatalog.All) preferences.PlayOptions[option.Name].Value = option.Max;
        preferences.PlayOptions["battle"].Value = 0;
        preferences.PlayOptions["hs_min"].Value = 10;
        preferences.PlayOptions["hs_max"].Value = 1000;
        preferences.Arrangement.Value = "s-random";
        preferences.Save();
        var playSaved = FrontendSettings.Read(path);
        Require(PlayOptionCatalog.All.All(option => PlayOptionCatalog.Get(playSaved, option) == preferences.PlayOptions[option.Name].Value),
            "Gameplay options must survive a restart.");
        Require((long)((TomlTable)Toml.ToModel(File.ReadAllText(path))["play"])["future_option"] == 7,
            "Saving gameplay options must preserve unknown configuration fields.");
        var bridge = new EngineBridge(AppContext.BaseDirectory);
        foreach (string arrangement in PlayOptionCatalog.Arrangements)
        {
            var playSettings = preferences.Value with { Arrangement = arrangement };
            var reply = bridge.Exchange("validate", playSettings, fixtureChart).GetAwaiter().GetResult();
            var effective = reply.Root!.Element("play-options")!.Elements("option").ToDictionary(
                option => (string)option.Attribute("name")!, option => (int)option.Attribute("value")!);
            Require(PlayOptionCatalog.All.All(option => effective[option.Name] == PlayOptionCatalog.Get(playSettings, option)),
                "The engine did not apply every requested gameplay option.");
            Require(reply.Root.Element("message")!.Value.Contains($"arrangement={Array.IndexOf(PlayOptionCatalog.Arrangements, arrangement)};"),
                "The engine did not apply the selected lane arrangement.");
        }
        var request = EngineBridge.Request("play", preferences.Value, fixtureChart);
        Require(request.Root?.Element("chart")?.Value == fixtureChart.Path && request.Root.Element("embed-window") is null,
            "Separate-window play must pass the selected chart without an embedding target.");
        var original = File.ReadAllText(path);
        preferences.Window.Value = "invalid";
        bool rejected = false;
        try { preferences.Save(); } catch (ArgumentException) { rejected = true; }
        Require(rejected && File.ReadAllText(path) == original, "Invalid settings must not replace the saved configuration.");
    }

    private static void CheckLibraryDefaults(string directory)
    {
        var fixtures = Path.Combine(directory, "directory-settings");
        Directory.CreateDirectory(fixtures);
        var file = Path.Combine(fixtures, "settings.toml");
        var defaults = FrontendSettings.Read(file);
        if (!defaults.Roots.SequenceEqual(new[] { @".\BMS" }) || defaults.Width != 1024 || defaults.Height != 768
            || defaults.FrameLimit != 240 || defaults.FrontendFrameLimit != "240" || defaults.RenderProfile != "directshow" || defaults.Presentation != "embedded")
            throw new InvalidDataException("Fresh installations must use BMS, a 1024x768 game viewport, 240 FPS and embedded D mode.");
        var second = Path.Combine(fixtures, "Another library");
        Directory.CreateDirectory(second);
        var relative = Path.GetRelativePath(AppContext.BaseDirectory, second);
        var settings = new DesktopSettings(defaults, file, false);
        if (!settings.ShowClearLamps.Value) throw new InvalidDataException("Clear lamps should be enabled by default.");
        settings.ShowClearLamps.Value = false;
        settings.Roots.Add(relative);
        settings.Roots.Add(second);
        settings.Roots.Add("");
        settings.Save();
        var current = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(fixtures);
            var saved = FrontendSettings.Read(file);
            if (saved.ShowClearLamps) throw new InvalidDataException("The clear lamp display setting must survive restart.");
            if (!saved.Roots.SequenceEqual(new[] { @".\BMS", relative }) || settings.HasChanges)
                throw new InvalidDataException("Library settings must retain relative paths and deduplicate resolved directories.");
            var request = EngineBridge.Request("catalog", saved);
            var roots = request.Root!.Elements("root").Select(root => root.Value).ToArray();
            if (!roots.Contains(ApplicationPaths.ResolveLibraryRoot("BMS")) || !roots.Contains(second) || roots.Any(root => !Path.IsPathFullyQualified(root)))
                throw new InvalidDataException("Relative library paths must resolve from the application folder for both clients.");
        }
        finally { Directory.SetCurrentDirectory(current); }
        settings.Roots.RemoveAt(1);
        settings.Roots.RemoveAt(1);
        settings.Roots.RemoveAt(1);
        settings.FrontendFrameLimit.Value = "120";
        settings.Save();
        if (FrontendSettings.Read(file).FrontendFrameLimit != "120" || FrontendSettings.Read(file).Roots.Length != 1)
            throw new InvalidDataException("Removing library entries and changing the frontend cap must survive restart.");
        settings.Roots.RemoveAt(0);
        settings.Save();
        if (!FrontendSettings.Read(file).Roots.SequenceEqual(new[] { @".\BMS" }))
            throw new InvalidDataException("The required BMS library must be restored even when removed outside the settings UI.");
        File.WriteAllText(file, "directories = []\nwindow_width = 1280\nwindow_height = 720\ngame_frame_limit = -1\ngame_render_profile = \"baseline\"\ngame_presentation = \"standalone\"\n");
        File.WriteAllText(Path.Combine(fixtures, "framework.ini"), "FrameSync = Limit4x\n");
        var legacy = FrontendSettings.Read(file);
        if (legacy.Roots.Length != 1 || legacy.Width != 1280 || legacy.Height != 720 || legacy.FrameLimit != -1
            || legacy.RenderProfile != "baseline" || legacy.Presentation != "standalone" || legacy.FrontendFrameLimit != "4x")
            throw new InvalidDataException("Existing rendering and window settings must be preserved while restoring the required library.");
    }

    private static void CheckCloudPanelDrawing()
    {
        var client = new CloudClient(() => [], () => "auto", _ => Task.CompletedTask);
        try
        {
            using var panel = new CloudPanelDrawingCheck(client);
            panel.CheckDrawing();
        }
        finally { client.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private static void CheckCloudIdentity()
    {
        var server = new Uri("https://lazerrave.com");
        var id = Guid.NewGuid();
        var user = new CloudUser(id, "account", "Display name", 1, $"/api/users/{id}/avatar?v=hash");
        var avatar = CloudClient.ResolveAvatar(server, user)?.AbsoluteUri;
        var identity = CloudIdentity.Create(user, avatar, "Player");
        if (identity.Id != 1 || identity.Username != "Display name" || identity.AvatarUrl != $"https://lazerrave.com/api/users/{id}/avatar?v=hash")
            throw new InvalidDataException("Cloud UID 1, display name or avatar did not reach the client identity.");
        if (CloudClient.ResolveAvatar(server, user with { AvatarUrl = "https://a.ppy.sh/1" }) is not null ||
            CloudClient.ResolveAvatar(server, user with { AvatarUrl = $"/api/users/{Guid.NewGuid()}/avatar" }) is not null)
            throw new InvalidDataException("Cloud avatar resolution accepted an unrelated server or user.");
        var local = CloudIdentity.Create(null, null, "Local player");
        if (local.Username != "Local player" || local.AvatarUrl is not null)
            throw new InvalidDataException("Signing out did not restore the local profile.");
        var bound = new osu.Framework.Bindables.Bindable<osu.Game.Online.API.Requests.Responses.APIUser>(local);
        var toolbar = bound.GetBoundCopy();
        CloudIdentity.Apply(bound, identity, "Local player");
        if (toolbar.Value.Username != user.DisplayName || toolbar.Value.AvatarUrl != avatar)
            throw new InvalidDataException("Logging in did not notify the bound toolbar profile.");
        var changed = CloudIdentity.Create(user with { DisplayName = "New display name" }, avatar + "2", "Local player");
        CloudIdentity.Apply(bound, changed, "Local player");
        if (toolbar.Value.Username != "New display name" || toolbar.Value.AvatarUrl != avatar + "2")
            throw new InvalidDataException("Same-UID profile changes were discarded by user equality.");
        CloudIdentity.Apply(bound, local, "Local player");
        if (toolbar.Value.Username != "Local player" || toolbar.Value.AvatarUrl is not null)
            throw new InvalidDataException("Signing out did not update the bound toolbar profile.");
    }

    private static void CheckCloudRoomMapping()
    {
        var server = new Uri("https://lazerrave.com");
        var host = Guid.NewGuid(); var guest = Guid.NewGuid();
        if (!CloudClient.IsAvatarUrl(server, $"https://lazerrave.com/api/users/{guest}/avatar?v=1") ||
            CloudClient.IsAvatarUrl(server, $"https://other.example/api/users/{guest}/avatar") ||
            CloudClient.IsAvatarUrl(server, "https://lazerrave.com/api/auth/token"))
            throw new InvalidDataException("Participant avatar requests must stay within the cloud avatar endpoint.");
        var client = new CloudClient(() => [], () => "auto", _ => Task.CompletedTask);
        try
        {
            var room = new CloudRoom(Guid.NewGuid(), "Fixture", host, "lobby", 1, null, null, null,
                [new(host, "Host", false, "available", "host", null, 1), new(guest, "Guest", true, "missing", "guest", null, 2)], Guid.NewGuid());
            var map = new CloudRoomMap(); var original = map.Convert(room, client);
            var changed = map.Convert(room with { State = "playing", Version = 2 }, client);
            if (original.RoomID != changed.RoomID || map.CloudId(changed) != room.Id || original.Host?.Id != 1 ||
                original.RecentParticipants.Select(user => user.Id).Distinct().Count() != 2 ||
                changed.Status != osu.Game.Online.Rooms.RoomStatus.Playing)
                throw new InvalidDataException("Cloud room mapping lost identity, participants or room state.");
        }
        finally { client.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private partial class CloudPanelDrawingCheck(CloudClient client) : LazerRaveCloudPanel(null!, client)
    {
        public void CheckDrawing()
        {
            LoadContent();
            using var node = CreateDrawNode();
            node.ApplyState();
        }
    }

    private static void CheckPlaySettings(string directory)
    {
        var path = Path.Combine(directory, "invalid-settings.toml");
        foreach (string content in new[] { "play = 1", "[play]\ngauge = true", "[play]\ngauge = 1.5",
            "[play]\ngauge = \"1\"", "[play]\ngauge = 6", "[play]\nhs_min = 1000\nhs_max = 10" })
        {
            File.WriteAllText(path, content);
            try { FrontendSettings.Read(path); }
            catch (Exception error) when (error is InvalidDataException or ArgumentException) { continue; }
            throw new InvalidDataException("Invalid gameplay configuration was accepted: " + content);
        }
        File.WriteAllText(path, "speed = 2.0");
        if (PlayOptionCatalog.All.Any(option => PlayOptionCatalog.Get(FrontendSettings.Read(path), option) != option.Default))
            throw new InvalidDataException("Legacy settings did not receive default gameplay options.");
    }

    private static void CheckClientCapabilities()
    {
        var mouseOption = new osu.Framework.Bindables.Bindable<bool>(true);
        LazerRaveInputPolicy.Disable(mouseOption);
        mouseOption.Value = true;
        mouseOption.SetDefault();
        if (mouseOption.Value || mouseOption.Default)
            throw new InvalidDataException("Removed input features can be re-enabled by old settings or a reset.");

        using var rulesets = new osu.Game.Rulesets.AssemblyRulesetStore();
        if (rulesets.GetRuleset(0) is not null || rulesets.GetRuleset(1) is not null || rulesets.GetRuleset(2) is not null || rulesets.GetRuleset(3) is null)
            throw new InvalidDataException("Ruleset discovery must retain Mania and exclude osu!, taiko and catch.");

        foreach (string type in new[]
        {
            "osu.Game.Screens.Edit.Submission.BeatmapSubmissionScreen",
            "osu.Game.Overlays.FirstRunSetupOverlay",
            "osu.Game.Overlays.SkinEditor.SkinEditorOverlay",
            "osu.Game.Screens.OnlinePlay.Matchmaking.QueueController",
            "osu.Game.Screens.OnlinePlay.DailyChallenge.DailyChallenge",
            "osu.Game.Screens.OnlinePlay.Playlists.Playlists"
        })
        {
            if (typeof(osu.Game.OsuGame).Assembly.GetType(type) is not null)
                throw new InvalidDataException($"A retired client module is still compiled: {type}");
        }

        if (typeof(osu.Game.OsuGame).Assembly.GetType("osu.Game.Rulesets.Edit.HitObjectComposer") is null)
            throw new InvalidDataException("The editor foundation is missing.");

        foreach (string content in new[] { "osu file format v14\n[HitObjects]\n", "{\"BeatmapInfo\":{}}" })
        {
            using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
            using var reader = new osu.Game.IO.LineBufferedReader(input);
            try
            {
                osu.Game.Beatmaps.Formats.Decoder.GetDecoder<osu.Game.Beatmaps.Beatmap>(reader);
            }
            catch (IOException) { continue; }
            throw new InvalidDataException("An osu! beatmap decoder is still registered.");
        }
    }

    private static void CheckNestedFolders(string directory)
    {
        var root = Path.Combine(directory, "BMS");
        var pack = Path.Combine(root, "Pack");
        var song = Path.Combine(pack, "Song");
        var subpack = Path.Combine(pack, "Subpack");
        string[] paths = [Path.Combine(song, "normal.bms"), Path.Combine(song, "another.bms"),
            Path.Combine(root, "Pack-other", "Song", "normal.bms"),
            Path.Combine(subpack, "Other", "normal.bms"), Path.Combine(pack, "direct.bms")];
        foreach (var path in paths) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "#TITLE Fixture"); }
        var document = new XDocument(new XElement("lazerrave", new XAttribute("status", "ok"), paths.Select((path, index) =>
            new XElement("chart", new XAttribute("path", path), new XAttribute("title", "Fixture " + index),
                new XAttribute("keys", index == 1 ? 5 : 7), new XAttribute("difficulty", index == 1 ? 4 : 2), new XAttribute("level", index + 1)))));
        var library = SongLibrary.Parse(document, directory, [root]);
        var store = new BmsBeatmapStore(); store.Replace(library, [root + Path.DirectorySeparatorChar]);
        void Expect(int songs, int charts, int folders)
        {
            if (store.GetBeatmapSets(null).Count != songs || store.SongsHere.Count() != songs ||
                store.GetBeatmapSets(null).SelectMany(set => set.Beatmaps).Count(store.IsVisible) != charts ||
                store.ChildFolders.Count() != folders)
                throw new InvalidDataException("Folder browsing lost song grouping, difficulties or collection boundaries.");
        }
        Expect(0, 0, 2);
        store.Filter(pack, 0);
        Expect(2, 3, 1);
        if (store.ChildFolders.Single().Path != subpack)
            throw new InvalidDataException("A song directory was exposed as a navigable folder.");
        var set = store.GetBeatmapSets(null).Single(set => set.ID == BmsBeatmapStore.StableId(song));
        if (set.Beatmaps.Count != 2)
            throw new InvalidDataException("Difficulties were split into separate songs.");
        CheckDifficultyGrouping(set);
        store.Filter(pack, 7);
        Expect(2, 2, 1);
        store.Filter(pack, 5);
        Expect(1, 1, 0);
        store.Filter(subpack, 0);
        Expect(1, 1, 0);
        if (!store.Ancestry.Select(folder => folder.Path).SequenceEqual(new[] { root, pack, subpack }))
            throw new InvalidDataException("Breadcrumbs do not preserve the collection hierarchy.");
        store.Filter(store.Library.Folders.Single(folder => folder.Path == store.Directory).Parent, 0);
        Expect(2, 3, 1);
        store.Filter(Path.Combine(root, "Pack-other"), 0);
        Expect(1, 1, 0);
        store.Filter(null, 0);
        Expect(0, 0, 1);
        if (store.ChildFolders.Single().Path != root || store.Ancestry.Length != 0 || store.HasParent)
            throw new InvalidDataException("The library overview cannot navigate back to its roots.");
        store.SetSearch("Fixture");
        if (store.GetBeatmapSets(null).Count != 4 || store.ChildFolders.Any())
            throw new InvalidDataException("Search did not span nested collections without duplicate sets.");
        store.SetDifficulty(4, 2);
        if (store.GetBeatmapSets(null).SelectMany(set => set.Beatmaps).Count() != 1 || store.GetBeatmapSets(null).Single().Beatmaps.Single().DifficultyName.Contains("NORMAL"))
            throw new InvalidDataException("Difficulty category and BMS level filters did not remove other charts.");
        store.SetDifficulty(0, null);
        store.SetLevelRange(2, 4);
        var rangeCharts = store.GetBeatmapSets(null).SelectMany(set => set.Beatmaps).ToArray();
        if (rangeCharts.Length != 3 || rangeCharts.Any(info => store.ChartFor(info)!.Level is < 2 or > 4))
            throw new InvalidDataException("BMS level ranges must include both endpoints and filter individual difficulties.");
        store.SetLevelRange(3, 3);
        if (store.GetBeatmapSets(null).SelectMany(set => set.Beatmaps).Count() != 1)
            throw new InvalidDataException("Equal level bounds must select an exact BMS level.");
        store.SetLevelRange(4, null);
        if (store.GetBeatmapSets(null).SelectMany(set => set.Beatmaps).Count() != 2)
            throw new InvalidDataException("Unlimited upper levels must retain higher difficulties.");
        store.SetLevelRange(null, 2);
        if (store.GetBeatmapSets(null).Count != 1 || store.GetBeatmapSets(null).Single().Beatmaps.Count != 2)
            throw new InvalidDataException("Level filtering must preserve song grouping.");
        store.SetLevelRange(null, null);
        store.SetSearch(""); store.SetDifficulty(0, null);
        Expect(0, 0, 1);
        var folders = store.ChildFolders;
        store.Replace(library, [root]);
        Expect(0, 0, 1);
        store.Replace(new SongLibrary(), [root]);
        if (folders.Single().Path != root)
            throw new InvalidDataException("A folder snapshot changed when the library was replaced.");
    }

    private static void CheckPlayRecords(string directory)
    {
        var store = new PlayRecordStore(Path.Combine(directory, "records"));
        var chartPath = Path.Combine(directory, "record.bms"); File.WriteAllText(chartPath, "#TITLE Record fixture");
        var chart = new Chart(chartPath, "Record fixture", "Test", 7, 12, 4, 150, 100, null);
        string hash = PlayRecordStore.HashChart(chartPath).GetAwaiter().GetResult();
        var movedChart = Path.Combine(directory, "moved.bms"); File.Copy(chartPath, movedChart);
        if (hash != PlayRecordStore.HashChart(movedChart).GetAwaiter().GetResult()) throw new InvalidDataException("Moving a chart changed its record identity.");
        var settings = new FrontendSettings([directory]);
        var score = new GameplaySnapshot(160, 0, 80, 2, 1, 4, true, false)
            { NormalScore = 170000, Perfect = 70, Great = 20, Good = 8, Bad = 1, Poor = 1, TotalNotes = 100, Eligible = true };
        var run = Guid.NewGuid(); string replay = store.ReplayPath(run); Directory.CreateDirectory(Path.GetDirectoryName(replay)!);
        File.WriteAllBytes(replay, new byte[12]); store.Save(run, hash, chart, "Fixture", settings, score);
        store.Save(Guid.NewGuid(), hash, chart, "Fixture", settings, score with { ExScore = 180, NormalScore = 190000 });
        store.Save(Guid.NewGuid(), hash, chart, "Fixture", settings, score with { ExScore = 200, Eligible = false });
        store.Save(Guid.NewGuid(), hash, chart, "Other", settings, score with { ExScore = 195 });
        if (store.Save(Guid.NewGuid(), hash, chart, "Fixture", settings, score with { Aborted = true }) is not null)
            throw new InvalidDataException("Aborted play was saved as a complete score.");
        File.WriteAllText(Path.Combine(directory, "records", "scores", hash, "broken.xml"), "<broken>");
        var records = store.Read(hash);
        if (records.Length != 4 || records.Single(value => value.Id == run).Score != score ||
            records.Single(value => value.Id == run).ReplayPath != replay || PlayRecordStore.PersonalBest(records, "Fixture")?.Score.ExScore != 180)
            throw new InvalidDataException("Play records lost judgements, replay linkage, personal best eligibility or player separation.");
        using var lamps = new LocalClearLampStore(store);
        if (lamps.GetClear(chart, "Fixture").GetAwaiter().GetResult() != 4 || lamps.GetClear(chart with { Path = movedChart }, "Fixture").GetAwaiter().GetResult() != 4
            || lamps.GetClear(chart, "Unplayed").GetAwaiter().GetResult() != 0)
            throw new InvalidDataException("Clear lamps must follow chart content and the current player.");
        store.Save(Guid.NewGuid(), hash, chart, "Fixture", settings, score with { ExScore = 190, ClearType = 1 });
        store.Save(Guid.NewGuid(), hash, chart, "Fixture", settings, score with { ClearType = 5, Eligible = false });
        store.Save(Guid.NewGuid(), hash, chart, "Other", settings, score with { ClearType = 5 });
        if (lamps.GetClear(chart, "Fixture").GetAwaiter().GetResult() != 4 || lamps.GetClear(chart, "Other").GetAwaiter().GetResult() != 5)
            throw new InvalidDataException("A high score, failed attempt, autoplay or another player must not overwrite the best clear lamp.");
        store.Save(Guid.NewGuid(), hash, chart, "Fixture", settings, score with { ClearType = 5 });
        if (lamps.GetClear(chart, "Fixture").GetAwaiter().GetResult() != 5)
            throw new InvalidDataException("A new best clear must refresh an already cached lamp.");
        File.Delete(replay);
        if (store.Read(hash).Single(value => value.Id == run).ReplayPath is not null) throw new InvalidDataException("A deleted replay is still available for playback.");
    }

    private static void CheckDifficultyGrouping(osu.Game.Beatmaps.BeatmapSetInfo set)
    {
        foreach (var sort in new[] { osu.Game.Screens.Select.Filter.SortMode.Title, osu.Game.Screens.Select.Filter.SortMode.Difficulty })
        {
            var criteria = new osu.Game.Screens.Select.FilterCriteria
            {
                Sort = sort, Group = osu.Game.Screens.Select.Filter.GroupMode.None, ForceBeatmapSetsGroupedTogether = true,
            };
            var items = set.Beatmaps.Select(info => new osu.Game.Graphics.Carousel.CarouselItem(info)).ToList();
            var sorting = new osu.Game.Screens.Select.BeatmapCarouselFilterSorting(() => criteria);
            var grouping = new osu.Game.Screens.Select.BeatmapCarouselFilterGrouping
            {
                GetCriteria = () => criteria, GetCollections = () => [], GetLocalUserTopRanks = _ => new Dictionary<Guid, osu.Game.Scoring.ScoreRank>(),
                GetFavouriteBeatmapSets = () => [],
            };
            var sorted = sorting.Run(items, CancellationToken.None).GetAwaiter().GetResult();
            var grouped = grouping.Run(sorted, CancellationToken.None).GetAwaiter().GetResult();
            if (!grouping.BeatmapSetsGroupedTogether || grouped.Count(item => item.Model is osu.Game.Screens.Select.GroupedBeatmapSet) != 1 ||
                grouped.Count(item => item.Model is osu.Game.Screens.Select.GroupedBeatmap) != set.Beatmaps.Count)
                throw new InvalidDataException("Sorting split a song's difficulties into separate song rows.");
        }
    }
}
