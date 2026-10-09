using LazerRave.Bridge;
using System.Xml.Linq;
using Tomlyn;
using Tomlyn.Model;

namespace LazerRave.Lazer;

internal static class AdapterChecks
{
    public static void Run(FrontendSettings settings, SongLibrary library, string directory)
    {
        static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
        var store = new BmsBeatmapStore(); store.Replace(library, settings.Roots);
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
                .Where(chart => keys == 0 || chart.Keys == keys)
                .Select(chart => BmsBeatmapStore.StableId(chart.Path)).ToHashSet();
            Require(expected.SetEquals(sets.SelectMany(set => set.Beatmaps).Where(store.IsVisible).Select(info => info.ID)),
                "Folder scope included a nested chart or a sibling folder.");
        }
        CheckNestedFolders(directory);
        CheckClientCapabilities();
        CheckCloudPanelDrawing();
        CheckCloudIdentity();
        CheckPlaySettings(directory);

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
                new XAttribute("keys", index == 1 ? 5 : 7), new XAttribute("level", index + 1)))));
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
        var folders = store.ChildFolders;
        store.Replace(library, [root]);
        Expect(0, 0, 1);
        store.Replace(new SongLibrary(), [root]);
        if (folders.Single().Path != root)
            throw new InvalidDataException("A folder snapshot changed when the library was replaced.");
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
