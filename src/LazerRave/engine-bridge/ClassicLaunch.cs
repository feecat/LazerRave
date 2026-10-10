using System.Diagnostics;

namespace LazerRave.Bridge;

internal static class ClassicLaunch
{
    public static ProcessStartInfo Create(string runtime)
    {
        var directory = Path.GetFullPath(runtime);
        var executable = Path.Combine(directory, "OpenLR2_x64.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException("OpenLR2_x64.exe is missing from the application folder.", executable);
        return new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
    }
}
