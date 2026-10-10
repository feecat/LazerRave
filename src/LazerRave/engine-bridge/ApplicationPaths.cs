using System.Diagnostics;
using LazerRave.Content;

namespace LazerRave.Bridge;

internal static class ApplicationPaths
{
    public static string UserData => Path.Combine(AppContext.BaseDirectory, "userdata");
    public static string Settings => Path.Combine(UserData, "settings.toml");
    public static string Logs => Path.Combine(UserData, "logs");
    public static string Cache => Path.Combine(AppContext.BaseDirectory, "cache");
    public static string Shared => LibraryFolders.Shared(AppContext.BaseDirectory);
    public static string ResolveLibraryRoot(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path, AppContext.BaseDirectory));
    public static string[] LibraryRoots(IEnumerable<string> configured) => LibraryFolders.NormalizeRoots(configured, AppContext.BaseDirectory)
        .Select(ResolveLibraryRoot).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static void Initialize(bool migrateLegacy)
    {
        Directory.CreateDirectory(UserData);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Cache);
        LibraryFolders.Ensure(AppContext.BaseDirectory);
        string marker = Path.Combine(UserData, "migration-complete.txt");
        if (!migrateLegacy || File.Exists(marker)) return;

        foreach (var process in Process.GetProcessesByName("LazerRave"))
        {
            using (process)
                if (process.Id != Environment.ProcessId)
                    throw new IOException("Close other LazerRave instances before migrating existing settings.");
        }

        string previousSettings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LazerRave", "settings.toml");
        string previousClient = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LazerRave-lazer");
        CopyMissing(previousSettings, Settings);
        foreach (string name in new[] { "framework.ini", "game.ini", "input.json" })
            CopyMissing(Path.Combine(previousClient, name), Path.Combine(UserData, name));
        if (Directory.Exists(previousClient))
        {
            foreach (string file in Directory.EnumerateFiles(previousClient, "*.realm"))
                CopyMissing(file, Path.Combine(UserData, Path.GetFileName(file)));
            foreach (string folder in new[] { "files", "exports", "screenshots" })
                CopyDirectory(Path.Combine(previousClient, folder), Path.Combine(UserData, folder));
        }
        File.WriteAllText(marker, "Legacy settings and client data copied without replacing existing portable files.\nOriginal user-profile data retained.\n");
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source) || (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) return;
        foreach (string file in Directory.EnumerateFiles(source))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
            CopyMissing(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (string directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static void CopyMissing(string source, string destination)
    {
        if (!File.Exists(source) || File.Exists(destination)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temporary);
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
