using Tomlyn.Model;

namespace LazerRave.Bridge;

internal static class SupportedKeyModes
{
    public static bool Contains(int keys) => keys is 5 or 7;
    public static void Require(int keys)
    {
        if (!Contains(keys)) throw new ArgumentException("The modern client supports 5K and 7K charts only.");
    }
}

internal sealed record ReadingSpeedProfile(bool Automatic, double Manual, int Target)
{
    public bool NormalizeBase { get; init; }
    public int BaseBpm { get; init; } = 150;
    public void Validate()
    {
        if (!double.IsFinite(Manual) || Manual is < .5 or > 10 || Target is < 100 or > 1500 || BaseBpm is < 30 or > 1000)
            throw new InvalidDataException("Invalid reading speed preferences.");
    }
}

internal sealed record ReadingSpeedProfiles(ReadingSpeedProfile Five, ReadingSpeedProfile Seven)
{
    public static ReadingSpeedProfiles Legacy(double speed) => new(new(false, speed, 600), new(false, speed, 600));
    public static ReadingSpeedProfiles Defaults(double speed = 3) => new(new(false, speed, 600) { NormalizeBase = true }, new(false, speed, 600) { NormalizeBase = true });
    public ReadingSpeedProfile ForKeys(int keys) { SupportedKeyModes.Require(keys); return keys == 5 ? Five : Seven; }
    public static ReadingSpeedProfiles Read(object? value, double legacySpeed, bool fresh = false)
    {
        if (value is null && fresh) return Defaults(legacySpeed);
        if (value is not null && value is not TomlTable) throw new InvalidDataException("Reading speed settings must be a table.");
        var table = value as TomlTable;
        ReadingSpeedProfile ReadProfile(string key)
        {
            if (table?.TryGetValue(key, out var row) != true) return new(false, legacySpeed, 600);
            if (row is not TomlTable profile) throw new InvalidDataException("Reading speed profiles must be tables.");
            bool automatic = false; double manual = legacySpeed; int target = 600;
            if (profile.TryGetValue("automatic", out var a)) automatic = a is bool enabled ? enabled : throw new InvalidDataException("Invalid automatic speed mode.");
            if (profile.TryGetValue("manual", out var m)) manual = m switch { double d => d, long n => n, _ => throw new InvalidDataException("Invalid manual speed.") };
            if (profile.TryGetValue("target", out var t)) target = t is long n && n is >= 100 and <= 1500 ? (int)n : throw new InvalidDataException("Invalid target reading speed.");
            bool normalize = false; int baseBpm = 150;
            if (profile.TryGetValue("normalize_base", out var normal)) normalize = normal is bool enabled ? enabled : throw new InvalidDataException("Invalid base BPM normalization mode.");
            if (profile.TryGetValue("base_bpm", out var b)) baseBpm = b is long n && n is >= 30 and <= 1000 ? (int)n : throw new InvalidDataException("Invalid normalized base BPM.");
            var result = new ReadingSpeedProfile(automatic, manual, target) { NormalizeBase = normalize, BaseBpm = baseBpm }; result.Validate(); return result;
        }
        return new(ReadProfile("five"), ReadProfile("seven"));
    }
    public void Write(TomlTable table)
    {
        Five.Validate(); Seven.Validate();
        var container = table.TryGetValue("reading_speed", out var previous) && previous is TomlTable saved ? saved : new TomlTable();
        foreach (var (key, profile) in new[] { ("five", Five), ("seven", Seven) })
        {
            var row = container.TryGetValue(key, out var old) && old is TomlTable fields ? fields : new TomlTable();
            row["automatic"] = profile.Automatic; row["manual"] = profile.Manual; row["target"] = (long)profile.Target;
            row["normalize_base"] = profile.NormalizeBase; row["base_bpm"] = (long)profile.BaseBpm; container[key] = row;
        }
        table["reading_speed"] = container;
    }
}

internal static class ReadingSpeed
{
    public static double VisibleFraction(IReadOnlyDictionary<string, int> options)
    {
        double cover = options.GetValueOrDefault("lane_cover") == 1 ? options.GetValueOrDefault("cover") / 100.0 : 0;
        double lift = options.GetValueOrDefault("lift_enabled") == 1 ? options.GetValueOrDefault("lift") / 100.0 : 0;
        return Math.Clamp(1 - cover - lift, .05, 1);
    }
    public static double AutomaticMultiplier(ReadingSpeedProfile profile, IReadOnlyDictionary<string, int> options) =>
        Math.Round(Math.Clamp(profile.Target / 150.0 * VisibleFraction(options), .5, 10), 2);
    private static (double Multiplier, int BasePercent) RepresentMultiplier(double desired)
    {
        if (!double.IsFinite(desired) || desired is < .005 or > 100)
            throw new InvalidDataException("The normalized speed exceeds LR2's range. Adjust the base BPM or scroll multiplier.");
        if (desired is >= .5 and <= 10) return (Math.Round(desired * 100) / 100, 100);
        double speed = desired < .5 ? .5 : 10;
        return (speed, (int)Math.Round(desired / speed * 100));
    }
    public static FrontendSettings Apply(FrontendSettings settings, Chart chart)
    {
        SupportedKeyModes.Require(chart.Keys);
        var profile = settings.SpeedProfiles.ForKeys(chart.Keys); profile.Validate();
        var options = new Dictionary<string, int>(settings.PlayOptions);
        foreach (var option in PlayOptionCatalog.All.Where(option => !PlayOptionCatalog.IsSinglePlay(option) && option.Name != "battle")) options[option.Name] = option.Default;
        if (profile.NormalizeBase)
        {
            double dominant = BmsTimeline.Read(chart, settings.Encoding).NoteBaseBpm;
            var normalized = RepresentMultiplier(profile.Manual * profile.BaseBpm / dominant);
            options["hsfix"] = 0; options["base_speed"] = normalized.BasePercent;
            return settings with { Speed = normalized.Multiplier, PlayOptions = options };
        }
        if (!profile.Automatic) return settings with { Speed = profile.Manual, PlayOptions = options };
        options["hsfix"] = 5; options["base_speed"] = 100;
        return settings with { Speed = AutomaticMultiplier(profile, options), PlayOptions = options };
    }
}
