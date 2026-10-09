using LazerRave.Bridge;
using osu.Framework;
using osu.Framework.Platform;

namespace LazerRave.Lazer;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        string? report = args.Length == 2 && args[0] == "--check" ? Path.GetFullPath(args[1]) : null;
        string? temporary = null;
        bool benchmarking = args.FirstOrDefault() == "--benchmark";
        try
        {
            var benchmark = benchmarking ? PerformanceRun.Parse(args) : null;
            if (benchmark is not null && !File.Exists(Path.Combine(AppContext.BaseDirectory, "lazerrave-performance-runtime.txt")))
                throw new InvalidOperationException("Run benchmarks through scripts/performance/run_comparison.py using an isolated runtime.");
            if (args.Length > 0 && report is null && benchmark is null) throw new ArgumentException("Usage: LazerRave.exe [--check report.txt]");
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            ApplicationPaths.Initialize(migrateLegacy: !benchmarking && report is null);
            var settings = FrontendSettings.Read(FrontendSettings.SharedPath);
            if (benchmark is not null)
            {
                settings = settings with { Roots = [Path.GetDirectoryName(benchmark.Chart)!], RenderProfile = benchmark.Profile, FrameLimit = benchmark.FrameLimit, Presentation = benchmark.Presentation };
                Environment.SetEnvironmentVariable("LAZERRAVE_FRAME_TRACE", "1");
                Environment.SetEnvironmentVariable("LAZERRAVE_DIAGNOSTIC_AUTOPLAY", "1");
                if (benchmark.Seconds == 0) Environment.SetEnvironmentVariable("LAZERRAVE_DIAGNOSTIC_FULL_SONG", "1");
            }
            var bridge = new EngineBridge(AppContext.BaseDirectory);
            var message = ""; SongLibrary library;
            try { library = bridge.Catalog(settings, report is null).GetAwaiter().GetResult(); }
            catch (Exception error) { if (report is not null) throw; library = new(); message = error.Message; }
            if (report is not null)
            {
                temporary = Path.Combine(ApplicationPaths.Cache, "checks", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporary);
                AdapterChecks.Run(settings, library, temporary);
                File.WriteAllText(report, "PASS: BMS adapter, gameplay option persistence and engine mappings, six lane arrangements, ruleset discovery, retired client modules, unsupported osu! beatmap decoders, removed input policies, cloud account identity and login overlay draw state.\nFull client UI and gameplay were not launched.\n");
                return 0;
            }
            using GameHost host = Host.GetSuitableDesktopHost(benchmarking ? "LazerRave-benchmark" : "LazerRave-lazer",
                new HostOptions { FriendlyGameName = "LazerRave", PortableInstallation = true });
            using var game = new LazerRaveGame(bridge, settings, library, message, benchmark);
            host.Run(game);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            var log = report ?? (benchmarking ? Path.Combine(AppContext.BaseDirectory, "benchmark-error.txt")
                : Path.Combine(ApplicationPaths.Logs, "lazer-startup-error.txt"));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                File.WriteAllText(log, "FAIL: " + error);
            }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine("Could not write startup error report: " + logError.Message);
                try
                {
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "lazer-startup-error.txt"), "FAIL: " + error);
                }
                catch (Exception fallbackError) when (fallbackError is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine("Could not write fallback error report: " + fallbackError.Message);
                }
            }
            return 2;
        }
        finally
        {
            if (temporary is not null && Directory.Exists(temporary))
                try { Directory.Delete(temporary, true); } catch (IOException) { }
        }
    }
}
