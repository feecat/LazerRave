using osu.Framework.Localisation;

namespace LazerRave.Lazer;

internal static class LazerRaveText
{
    public static LocalisableString D(string english, params object[] args) => new TranslatableString("LazerRave.Lazer.Localisation.Messages:" + english, english, args);

    public static LocalisableString RoomState(string state) => D(state switch
    {
        "lobby" => "Waiting for players",
        "countdown" => "Starting",
        "playing" => "Playing",
        "results" => "Results",
        _ => state,
    });
}
