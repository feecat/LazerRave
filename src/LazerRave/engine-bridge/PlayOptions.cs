using Tomlyn.Model;

namespace LazerRave.Bridge;

internal sealed record PlayOption(string Name, string Label, string Category, int Min, int Max, int Default, string[]? Choices = null);

internal static class PlayOptionCatalog
{
    public static readonly string[] Arrangements = ["off", "mirror", "random", "s-random", "scatter", "converge"];
    public static readonly PlayOption[] All =
    [
        new("gauge", "Gauge", "Gauge", 0, 5, 0, ["NORMAL", "HARD", "DEATH", "EASY", "P-ATTACK", "G-ATTACK"]),
        new("gauge2", "2P gauge", "Gauge", 0, 5, 0, ["NORMAL", "HARD", "DEATH", "EASY", "P-ATTACK", "G-ATTACK"]),
        new("battle", "Play mode", "Gauge", 0, 4, 0, ["OFF", "BATTLE", "DOUBLE BATTLE", "SP / DP CONVERSION", "GHOST BATTLE"]),
        new("autoplay", "Autoplay", "Gauge", 0, 1, 0, ["OFF", "ON"]),
        new("auto_key", "Auto key sound", "Gauge", 0, 1, 0, ["OFF", "ON"]),
        new("assist", "Auto scratch", "Gauge", 0, 1, 0, ["OFF", "ON"]),
        new("assist2", "2P auto scratch", "Gauge", 0, 1, 0, ["OFF", "ON"]),
        new("gauge_shift", "Gauge auto shift", "Gauge", 0, 1, 0, ["OFF", "ON"]),
        new("speed2", "2P scroll speed (×100)", "Speed", 50, 1000, 200),
        new("hsfix", "Speed reference", "Speed", 0, 5, 0, ["OFF", "MAX BPM", "MIN BPM", "AVERAGE BPM", "CONSTANT", "MAIN BPM"]),
        new("base_speed", "Base speed (%)", "Speed", 1, 1000, 100),
        new("hs_min", "Minimum speed (×100)", "Speed", 10, 1000, 10),
        new("hs_max", "Maximum speed (×100)", "Speed", 10, 1000, 1000),
        new("hs_step", "In-game speed step (×100)", "Speed", 1, 100, 5),
        new("lock_speed", "Disable arrow-key speed changes", "Speed", 0, 1, 0, ["OFF", "ON"]),
        new("arrangement2", "2P arrangement", "Lanes", 0, 5, 0, ["OFF", "MIRROR", "RANDOM", "S-RANDOM", "SCATTER", "CONVERGE"]),
        new("effect", "Visibility", "Lanes", 0, 3, 0, ["OFF", "HIDDEN", "SUDDEN", "HIDDEN + SUDDEN"]),
        new("effect2", "2P visibility", "Lanes", 0, 3, 0, ["OFF", "HIDDEN", "SUDDEN", "HIDDEN + SUDDEN"]),
        new("lane_cover", "Lane cover", "Lanes", 0, 1, 0, ["OFF", "ON"]),
        new("lane_cover2", "2P lane cover", "Lanes", 0, 1, 0, ["OFF", "ON"]),
        new("cover", "Lane cover (%)", "Lanes", 0, 100, 0),
        new("cover2", "2P lane cover (%)", "Lanes", 0, 100, 0),
        new("cover_step", "Lane cover step (%)", "Lanes", 1, 100, 10),
        new("lift_enabled", "Lift", "Lanes", 0, 1, 0, ["OFF", "ON"]),
        new("lift_enabled2", "2P lift", "Lanes", 0, 1, 0, ["OFF", "ON"]),
        new("lift", "Lift (%)", "Lanes", 0, 100, 0),
        new("lift2", "2P lift (%)", "Lanes", 0, 100, 0),
        new("dp_flip", "DP flip", "Lanes", 0, 1, 0, ["OFF", "ON"]),
        new("scratch_random", "Include scratch in random", "Lanes", 0, 1, 0, ["OFF", "ON"]),
        new("scratch_random2", "2P include scratch in random", "Lanes", 0, 1, 0, ["OFF", "ON"]),
        new("fixed_lane", "Fixed lane in random", "Lanes", 0, 7, 0, ["OFF", "1", "2", "3", "4", "5", "6", "7"]),
        new("fixed_lane2", "2P fixed lane in random", "Lanes", 0, 7, 0, ["OFF", "1", "2", "3", "4", "5", "6", "7"]),
        new("new_random", "New random on restart", "Lanes", 0, 1, 0, ["OFF", "ON"]),
        new("bga", "BGA", "More", 0, 2, 1, ["OFF", "ON", "AUTOPLAY ONLY"]),
        new("bga_size", "BGA size", "More", 0, 1, 0, ["NORMAL", "EXTENDED"]),
        new("poor_bga", "Miss BGA duration (ms)", "More", 0, 5000, 500),
        new("auto_judge", "Auto judgement adjustment", "More", 0, 2, 0, ["OFF", "ON", "ON (NO BGA / GHOST)"]),
        new("ghost", "Score ghost", "More", 0, 3, 0, ["OFF", "TYPE A", "TYPE B", "TYPE C"]),
        new("score_graph", "Score graph", "More", 0, 1, 0, ["OFF", "ON"]),
        new("target", "Score target", "More", 0, 8, 0, ["NONE", "MY BEST", "AAA", "AA", "A", "PERCENTAGE", "IR TOP", "IR NEXT", "IR AVERAGE"]),
        new("target_percent", "Target (%)", "More", 50, 100, 90),
        new("replay_save", "Replay saving", "More", 0, 4, 0, ["OFF", "ALL", "BEST SCORE", "CLEAR", "FULL COMBO"]),
        new("disable_click_exit", "Disable left-click exit", "More", 0, 1, 0, ["OFF", "ON"]),
        new("extra_enabled", "Extra mode", "Modifiers", 0, 1, 0, ["OFF", "ON"]),
        new("extra", "Extra level", "Modifiers", 0, 2, 0, ["LEVEL 1", "LEVEL 2", "LEVEL 3"]),
        new("accel", "Acceleration", "Modifiers", 0, 3, 0, ["OFF", "ACCELERATION", "DECELERATION", "RANDOM"]),
        new("softlanding", "Soft landing", "Modifiers", 0, 2, 0, ["OFF", "LEVEL 1", "LEVEL 2"]),
        new("gambol", "Gambol", "Modifiers", 0, 2, 0, ["OFF", "LEVEL 1", "LEVEL 2"]),
        new("addnote", "Add notes (%)", "Modifiers", 0, 100, 0),
        new("addlong", "Add long notes (%)", "Modifiers", 0, 100, 0),
        new("addmine", "Add mines (%)", "Modifiers", 0, 100, 0),
        new("earthquake", "Earthquake (%)", "Modifiers", 0, 100, 0),
        new("tornado", "Tornado (%)", "Modifiers", 0, 100, 0),
        new("superloop", "Superloop (%)", "Modifiers", 0, 100, 0),
        new("char", "Char (%)", "Modifiers", 0, 100, 0),
        new("heartbeat", "Heartbeat (%)", "Modifiers", 0, 100, 0),
        new("loudness", "Loudness (%)", "Modifiers", 0, 100, 0),
        new("nabeatsu", "Nabeatsu (%)", "Modifiers", 0, 100, 0),
        new("sincurve", "Sine curve (%)", "Modifiers", 0, 100, 0),
        new("wave", "Wave (%)", "Modifiers", 0, 100, 0),
        new("spiral", "Spiral (%)", "Modifiers", 0, 100, 0),
        new("sidejump", "Side jump (%)", "Modifiers", 0, 100, 0),
        new("lunaris", "Lunaris", "Modifiers", 0, 1, 0, ["OFF", "ON"]),
    ];

    public static int Get(FrontendSettings settings, PlayOption option) =>
        settings.PlayOptions.TryGetValue(option.Name, out int value) ? value : option.Default;

    public static IReadOnlyDictionary<string, int> Read(TomlTable? table)
    {
        var values = new Dictionary<string, int>();
        foreach (var option in All)
        {
            int value = option.Default;
            if (table?.TryGetValue(option.Name, out var saved) == true)
            {
                if (saved is not long integer || integer < option.Min || integer > option.Max)
                    throw new InvalidDataException($"Play option {option.Name} must be an integer between {option.Min} and {option.Max}.");
                value = (int)integer;
            }
            if (value < option.Min || value > option.Max) throw new InvalidDataException($"Invalid play option: {option.Name}");
            values.Add(option.Name, value);
        }
        Validate(values);
        return values;
    }

    public static void Validate(IReadOnlyDictionary<string, int> values)
    {
        foreach (var option in All)
            if (values.TryGetValue(option.Name, out int value) && (value < option.Min || value > option.Max))
                throw new ArgumentException($"Invalid play option: {option.Name}");
        int Value(string name) => values.TryGetValue(name, out int value) ? value : All.Single(o => o.Name == name).Default;
        if (Value("hs_min") > Value("hs_max")) throw new ArgumentException("Minimum speed must not exceed maximum speed.");
    }
}
