using System.Globalization;
using LazerRave.Bridge;
using osu.Framework.Bindables;

namespace LazerRave.Lazer;

internal sealed class NativeSettings
{
    private readonly NativeConfiguration configuration;
    private readonly bool readOnly;
    private readonly Dictionary<string, string> defaults;
    private Dictionary<string, string> saved = new();
    public string? Error { get; private set; }
    public bool IsReloading { get; private set; }
    public BindableInt ScreenMode { get; } = new(1) { MinValue = 0, MaxValue = 2 };
    public IReadOnlyDictionary<string, BindableInt> Numbers { get; } = new Dictionary<string, BindableInt>
    {
        ["system/vsync"] = Number(0, 0, 1),
        ["system/inputinterval"] = Number(16, 0, 32),
        ["system/disablesystemkey"] = Number(0, 0, 1),
        ["system/outputlog"] = Number(0, 0, 1),
        ["system/autoreload"] = Number(2, 0, 2),
        ["sound/output"] = Number(1, 0, 2),
        ["sound/driver"] = Number(0, 0, 255),
        ["sound/numbuffers"] = Number(4, 1, 16),
        ["sound/volumemaster"] = Number(100, 0, 100),
        ["sound/volumebgm"] = Number(100, 0, 100),
        ["sound/volumekey"] = Number(100, 0, 100),
        ["select/preview"] = Number(1, 0, 1),
        ["skin/disableimagefont"] = Number(0, 0, 1),
    };
    public IReadOnlyDictionary<string, Bindable<string>> Text { get; } = new Dictionary<string, Bindable<string>>
    {
        ["sound/bufferlength"] = new("384"),
        ["skin/play_7"] = new(""), ["skin/play_5"] = new(""), ["skin/play_14"] = new(""),
        ["skin/play_10"] = new(""), ["skin/play_9"] = new(""), ["skin/select"] = new(""),
        ["skin/decide"] = new(""), ["skin/result"] = new(""), ["skin/soundset"] = new(""),
        ["skin/fontname"] = new("Arial"),
    };
    public string Runtime { get; }

    public NativeSettings(string runtime, bool readOnly)
    {
        Runtime = runtime; this.readOnly = readOnly;
        configuration = new NativeConfiguration(runtime);
        defaults = Values();
        Reload();
    }

    public void Reload()
    {
        IsReloading = true;
        try
        {
            var main = NativeConfiguration.Read(configuration.ConfigPath);
            var extension = NativeConfiguration.Read(configuration.ExtensionPath);
            var values = new Dictionary<string, string>(defaults);
            foreach (var key in values.Keys.ToArray())
                values[key] = key == "screen-mode"
                    ? NativeConfiguration.Value(extension, "system/screenmode") ?? NativeConfiguration.Value(main, "system/screenmode") ?? "1"
                    : NativeConfiguration.Value(main, key) ?? values[key];
            if (!int.TryParse(values["screen-mode"], out int mode) || mode is < 0 or > 2)
                throw new InvalidDataException("Invalid native LR2 display mode.");
            foreach (var (key, number) in Numbers)
                if (!int.TryParse(values[key], out int value) || value < number.MinValue || value > number.MaxValue)
                    throw new InvalidDataException("Invalid native LR2 setting: " + key);
            ScreenMode.Value = mode;
            foreach (var (key, number) in Numbers) number.Value = int.Parse(values[key], CultureInfo.InvariantCulture);
            foreach (var (key, text) in Text) text.Value = values[key];
            saved = Values(); Error = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException)
        { Error = error.Message; }
        finally { IsReloading = false; }
    }

    public bool HasChanges => Error is null && Values().Any(pair => !saved.TryGetValue(pair.Key, out var previous) || previous != pair.Value);
    public void Save(FrontendSettings? shared = null)
    {
        if (readOnly) return;
        if (Error is not null) throw new InvalidDataException(Error);
        var values = Values();
        var changes = values.Where(pair => !saved.TryGetValue(pair.Key, out var previous) || previous != pair.Value)
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var addon = new Dictionary<string, string>();
        if (changes.Remove("screen-mode", out var mode))
        {
            changes["system/screenmode"] = mode == "1" ? "1" : "0";
            addon["system/screenmode"] = mode;
        }
        if (!int.TryParse(Text["sound/bufferlength"].Value, out int buffer) || buffer is < 16 or > 8192)
            throw new ArgumentException("Audio buffer size must be between 16 and 8192 samples.");
        if (changes.Keys.Any(key => key.StartsWith("sound/volume"))) changes["sound/volumeflag"] = "1";
        foreach (var (key, value) in changes.Where(pair => pair.Key.StartsWith("skin/") && pair.Key != "skin/fontname" && pair.Key != "skin/disableimagefont"))
        {
            string extension = key == "skin/soundset" ? ".lr2ss" : ".lr2skin";
            if (value.Length == 0 || !value.EndsWith(extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.GetFullPath(value, Runtime)))
                throw new ArgumentException("Select an existing LR2 skin or sound set: " + key);
        }
        if (shared is not null)
        {
            changes["system/windowsize_x"] = shared.Width.ToString(CultureInfo.InvariantCulture);
            changes["system/windowsize_y"] = shared.Height.ToString(CultureInfo.InvariantCulture);
            changes["play/hs"] = Math.Round(shared.Speed * 100).ToString(CultureInfo.InvariantCulture);
            changes["play/judgetime"] = shared.Offset.ToString(CultureInfo.InvariantCulture);
            changes["play/random"] = Array.IndexOf(PlayOptionCatalog.Arrangements, shared.Arrangement).ToString(CultureInfo.InvariantCulture);
            foreach (var (option, field) in SharedPlayFields)
                changes["play/" + field] = PlayOptionCatalog.Get(shared, PlayOptionCatalog.All.Single(item => item.Name == option)).ToString(CultureInfo.InvariantCulture);
            addon["system/screenmode"] = values["screen-mode"];
            foreach (var (option, field) in new[] { ("gauge_shift", "gaugeautoshift"), ("new_random", "newrandomrestart"), ("lift_enabled", "lifttype"), ("lift", "lift") })
                addon["play/" + field] = PlayOptionCatalog.Get(shared, PlayOptionCatalog.All.Single(item => item.Name == option)).ToString(CultureInfo.InvariantCulture);
            changes["system/screenmode"] = ScreenMode.Value == 1 ? "1" : "0";
        }
        configuration.Save(changes, addon, shared is null ? null : ApplicationPaths.LibraryRoots(shared.Roots));
        saved = values;
    }

    private Dictionary<string, string> Values() => Numbers.ToDictionary(pair => pair.Key, pair => pair.Value.Value.ToString(CultureInfo.InvariantCulture))
        .Concat(Text.Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value.Value.Trim())))
        .Append(new("screen-mode", ScreenMode.Value.ToString(CultureInfo.InvariantCulture)))
        .ToDictionary(pair => pair.Key, pair => pair.Value);

    private static BindableInt Number(int value, int min, int max) => new(value) { MinValue = min, MaxValue = max };
    private static readonly (string Option, string Field)[] SharedPlayFields =
    [
        ("gauge", "gauge"), ("effect", "effect"), ("assist", "autoscratch"), ("auto_key", "autokey"),
        ("auto_judge", "autojudgeadjust"), ("bga", "bga"), ("bga_size", "bgasize"), ("poor_bga", "poorbga"),
        ("hsfix", "hstype"), ("hs_min", "hsmin"), ("hs_max", "hsmax"), ("hs_step", "hsmargin"),
        ("base_speed", "basespeed"), ("lock_speed", "disablecurspeedchange"),
        ("lane_cover", "shuttertype"), ("cover", "shutter"), ("cover_step", "shuttermargin"),
        ("ghost", "ghost"), ("score_graph", "scoregraph"), ("target", "target"),
        ("target_percent", "defaulttarget"), ("replay_save", "replaysave"), ("disable_click_exit", "disableleftclickexit"),
    ];
}
