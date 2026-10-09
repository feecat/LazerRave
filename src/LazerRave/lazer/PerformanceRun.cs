using LazerRave.Bridge;

namespace LazerRave.Lazer;

internal sealed record PerformanceRun(string Chart, int Seconds, string Profile, int FrameLimit, string Presentation)
{
    public static PerformanceRun Parse(string[] args)
    {
        if (args.Length is not (5 or 6) || !Path.IsPathFullyQualified(args[1]) || !File.Exists(args[1])
            || !int.TryParse(args[2], out int seconds) || (seconds != 0 && seconds is < 15 or > 120)
            || !RenderProfiles.Labels.ContainsKey(args[3]) || !int.TryParse(args[4], out int limit)
            || (args.Length == 6 && args[5] is not ("embedded" or "standalone"))
            || limit is < -1 or > 1000 || limit is > 0 and < 30)
            throw new ArgumentException("Usage: LazerRave.exe --benchmark <absolute chart> <0=full song|15-120 seconds> <render profile> <-1|0|30-1000> [embedded|standalone]");
        return new(Path.GetFullPath(args[1]), seconds, args[3], limit, args.Length == 6 ? args[5] : "embedded");
    }
}
