namespace LazerRave.Content;

public static class LibraryFolders
{
    public const string DefaultRoot = @".\BMS";
    public static string Bms(string applicationRoot) => Path.Combine(Path.GetFullPath(applicationRoot), "BMS");
    public static string Shared(string applicationRoot) => Path.Combine(Bms(applicationRoot), "Shared");

    public static string[] NormalizeRoots(IEnumerable<string> configured, string applicationRoot)
    {
        applicationRoot = Path.GetFullPath(applicationRoot);
        string Resolve(string value) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(value.Replace('\\', Path.DirectorySeparatorChar), applicationRoot));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Bms(applicationRoot), Shared(applicationRoot), Path.Combine(applicationRoot, "Shared"),
        };
        var roots = new List<string> { DefaultRoot };
        foreach (var value in configured.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()))
            if (seen.Add(Resolve(value))) roots.Add(value);
        return roots.ToArray();
    }

    public static void Ensure(string applicationRoot)
    {
        applicationRoot = Path.GetFullPath(applicationRoot);
        var bms = Bms(applicationRoot);
        var shared = Shared(applicationRoot);
        var legacy = Path.Combine(applicationRoot, "Shared");
        Directory.CreateDirectory(applicationRoot);
        NoLink(applicationRoot);
        if (Directory.Exists(bms)) NoLink(bms);
        Directory.CreateDirectory(bms);
        if (Directory.Exists(shared)) NoLink(shared);
        if (Directory.Exists(legacy))
        {
            NoLink(legacy);
            if (!Directory.Exists(shared)) Directory.Move(legacy, shared);
            else
            {
                foreach (var source in Directory.EnumerateFileSystemEntries(legacy))
                {
                    NoLink(source);
                    var name = Path.GetFileName(source);
                    var destination = Path.Combine(shared, name);
                    if (name == ".incoming" && Directory.Exists(source))
                    {
                        if (Directory.Exists(destination)) NoLink(destination);
                        Directory.CreateDirectory(destination);
                        destination = Path.Combine(destination, "legacy-" + Guid.NewGuid().ToString("N"));
                    }
                    else if (Path.Exists(destination))
                        destination = Path.Combine(shared, name + "-legacy-" + Guid.NewGuid().ToString("N"));
                    if (Directory.Exists(source)) Directory.Move(source, destination);
                    else File.Move(source, destination);
                }
                Directory.Delete(legacy, false);
            }
        }
        Directory.CreateDirectory(shared);
    }

    public static void NoLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Library installation directories cannot be symbolic links or junctions.");
    }
}
