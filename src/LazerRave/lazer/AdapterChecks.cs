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
        foreach (var folder in library.Folders.Select(f => f.Path).Prepend(null))
        foreach (int keys in new[] { 5, 7, 0 })
        {
            store.Filter(folder, keys);
            var sets = store.GetBeatmapSets(null).ToArray();
            Require(sets.Select(set => set.ID).Distinct().Count() == sets.Length, "Duplicate song directories.");
            Require(sets.All(set => set.Beatmaps.Any(info => keys == 0 || store.ChartFor(info)?.Keys == keys)), "Key filter returned an unrelated song.");
            Require(sets.SelectMany(set => set.Beatmaps).All(info => store.ChartFor(info) is { } chart && info.DifficultyName.Contains($"Lv.{chart.Level}")), "BMS difficulty labels must preserve chart levels.");
            var expected = library.Songs.Where(song => folder is null || song.Directory.Equals(folder, StringComparison.OrdinalIgnoreCase)
                || SongLibrary.ContainsPath(folder, song.Directory)).SelectMany(song => song.Charts).Where(chart => keys == 0 || chart.Keys == keys)
                .Select(chart => BmsBeatmapStore.StableId(chart.Path)).ToHashSet();
            Require(expected.SetEquals(sets.SelectMany(set => set.Beatmaps).Where(store.IsVisible).Select(info => info.ID)), "Folder scope omitted nested charts or included a sibling folder.");
        }
        CheckNestedFolders(directory);
        CheckClientCapabilities();

        var path = Path.Combine(directory, "settings.toml");
        File.WriteAllText(path, "signature = \"preserved\"\n");
        var preferences = new DesktopSettings(settings, path, false);
        preferences.Window.Value = "2048x1152"; preferences.Presentation.Value = "standalone"; preferences.Save();
        var saved = Toml.ToModel(File.ReadAllText(path));
        Require((string)saved["signature"] == "preserved" && (long)saved["window_width"] == 2048, "Settings updates lost unknown fields or a custom window size.");
        Require(FrontendSettings.Read(path).Presentation == "standalone", "Separate-window selection must survive a restart.");
        var fixtureChart = new Chart(Path.Combine(directory, "fixture.bms"), "Fixture", "", 7, 1, 2, 120, 1, null);
        File.WriteAllText(fixtureChart.Path, "#TITLE Fixture");
        var request = EngineBridge.Request("play", preferences.Value, fixtureChart);
        Require(request.Root?.Element("chart")?.Value == fixtureChart.Path && request.Root.Element("embed-window") is null,
            "Separate-window play must pass the selected chart without an embedding target.");
        var original = File.ReadAllText(path);
        preferences.Window.Value = "invalid";
        bool rejected = false;
        try { preferences.Save(); } catch (ArgumentException) { rejected = true; }
        Require(rejected && File.ReadAllText(path) == original, "Invalid settings must not replace the saved configuration.");
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
        string[] paths = [Path.Combine(root, "Pack", "Song", "normal.bms"), Path.Combine(root, "Pack", "Song", "another.bms"),
            Path.Combine(root, "Pack-other", "Song", "normal.bms")];
        foreach (var path in paths) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "#TITLE Fixture"); }
        var document = new XDocument(new XElement("lazerrave", new XAttribute("status", "ok"), paths.Select((path, index) =>
            new XElement("chart", new XAttribute("path", path), new XAttribute("title", "Fixture"), new XAttribute("keys", index == 1 ? 5 : 7)))));
        var library = SongLibrary.Parse(document, directory, [root]);
        var store = new BmsBeatmapStore(); store.Replace(library, [root + Path.DirectorySeparatorChar]);
        void Expect(int count)
        {
            if (store.GetBeatmapSets(null).SelectMany(set => set.Beatmaps).Count(store.IsVisible) != count)
                throw new InvalidDataException("Nested library navigation returned an unexpected chart count.");
        }
        Expect(2);
        store.Filter(Path.Combine(root, "Pack") + Path.DirectorySeparatorChar, 7); Expect(1);
        store.Filter(store.Directory, 5); Expect(1);
        store.Replace(library, [root]); Expect(1);
        store.Filter(Path.Combine(root, "Pack", "Song"), 0); Expect(2);
        store.Filter(null, 0); Expect(3);
        if (store.ChildFolders.Count() != 1) throw new InvalidDataException("Virtual library root lost folder navigation.");
    }
}
