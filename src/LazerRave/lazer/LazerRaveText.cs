using osu.Framework.Localisation;

namespace LazerRave.Lazer;

internal static class LazerRaveText
{
    public static LocalisableString D(string english) => new TranslatableString("LazerRave.Lazer.Localisation.Messages:" + english, english);
}
