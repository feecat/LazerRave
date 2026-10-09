using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mono.Cecil;

namespace LazerRave.ResourcePack;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[0] == "--layout-dependencies")
            {
                LayoutDependencies(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
                return 0;
            }
            if (args.Length != 3)
                throw new ArgumentException("Usage: LazerRave.ResourcePack input.dll output.dll removal-policy.tsv");

            Build(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Resource packaging failed: " + error.Message);
            return 1;
        }
    }

    private static void LayoutDependencies(string input, string output)
    {
        var document = JsonNode.Parse(File.ReadAllText(input)) ?? throw new InvalidOperationException("Empty publish dependency manifest.");
        var targets = document["targets"]?.AsObject() ?? throw new InvalidOperationException("Publish dependency targets are missing.");
        int packs = 0;
        int satellites = 0;
        foreach (var target in targets)
        foreach (var library in target.Value!.AsObject())
        {
            if (library.Value?["runtime"] is JsonObject runtime)
            {
                foreach (var asset in runtime)
                {
                    if (Path.GetFileName(asset.Key) != "osu.Game.Resources.dll") continue;
                    asset.Value!["localPath"] = "Resources/osu.Game.Resources.dll";
                    packs++;
                }
            }
            if (library.Value?["resources"] is not JsonObject resources) continue;
            foreach (var asset in resources)
            {
                string culture = asset.Value?["locale"]?.GetValue<string>() ?? throw new InvalidOperationException("Satellite resource culture is missing.");
                string filename = Path.GetFileName(asset.Key);
                if (!filename.EndsWith(".resources.dll", StringComparison.Ordinal) || culture.Length == 0
                    || culture.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
                    throw new InvalidOperationException("Invalid satellite resource path: " + asset.Key);
                asset.Value!["localPath"] = $"Localization/{culture}/{filename}";
                satellites++;
            }
        }
        if (packs == 0) throw new InvalidOperationException("The publish manifest does not contain osu.Game.Resources.");
        string content = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (!File.Exists(output) || File.ReadAllText(output) != content)
            File.WriteAllText(output, content, new UTF8Encoding(false));
        Console.WriteLine($"Resource dependency layout: {packs} main pack, {satellites} satellite assemblies.");
    }

    private static void Build(string input, string output, string policy)
    {
        if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The resource input must remain unchanged.");

        string inventory = Path.ChangeExtension(output, ".inventory.tsv");
        string summary = Path.ChangeExtension(output, ".summary.txt");
        string cache = Path.ChangeExtension(output, ".fingerprint.txt");
        string fingerprint = $"{Hash(input)}\n{Hash(policy)}\n{typeof(Program).Module.ModuleVersionId}";
        if (File.Exists(output) && File.Exists(inventory) && File.Exists(summary) && File.Exists(cache)
            && File.ReadAllText(cache) == fingerprint + "\n" + Hash(output))
        {
            Console.WriteLine("Resource pack is up to date: " + output);
            return;
        }

        var rules = ReadRules(policy);
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(input)!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
        using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { AssemblyResolver = resolver });
        if (assembly.Name.Name != "osu.Game.Resources" || assembly.Name.HasPublicKey)
            throw new InvalidOperationException("Expected the unsigned osu.Game.Resources upstream assembly.");

        long removedBytes = 0;
        int removedCount = 0;
        var matched = new HashSet<Rule>();
        var rows = new List<string> { "resource\tbytes\taction\treason" };
        foreach (var resource in assembly.MainModule.Resources.ToArray())
        {
            Rule? rule = rules.FirstOrDefault(candidate => candidate.Matches(resource.Name));
            long bytes = resource is EmbeddedResource embedded ? embedded.GetResourceData().LongLength : 0;
            if (rule is not null)
            {
                if (resource is not EmbeddedResource)
                    throw new InvalidOperationException("Only embedded assets may be removed: " + resource.Name);
                assembly.MainModule.Resources.Remove(resource);
                removedBytes += bytes;
                removedCount++;
                matched.Add(rule);
            }
            rows.Add($"{resource.Name}\t{bytes.ToString(CultureInfo.InvariantCulture)}\t{(rule is null ? "keep" : "remove")}\t{rule?.Reason ?? "retained"}");
        }

        var missing = rules.Where(rule => !matched.Contains(rule)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException("Removal policy no longer matches the pinned resource package: " + string.Join(", ", missing.Select(rule => rule.Pattern)));

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string temporary = output + ".tmp";
        try
        {
            assembly.Write(temporary, new WriterParameters { DeterministicMvid = true, WriteSymbols = false });
            File.Move(temporary, output, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        File.WriteAllLines(inventory, rows, Encoding.UTF8);
        string report = FormattableString.Invariant($"Upstream: ppy.osu.Game.Resources 2026.918.0\nIdentity: {assembly.Name.FullName}\nInput SHA256: {Hash(input)}\nPolicy SHA256: {Hash(policy)}\nInput bytes: {new FileInfo(input).Length}\nOutput bytes: {new FileInfo(output).Length}\nRemoved assets: {removedCount}\nRemoved asset bytes: {removedBytes}\nRetained resources: {assembly.MainModule.Resources.Count}\n");
        File.WriteAllText(summary, report, Encoding.UTF8);
        File.WriteAllText(cache, fingerprint + "\n" + Hash(output), Encoding.UTF8);
        Console.WriteLine(FormattableString.Invariant($"Resource pack: removed {removedCount} assets ({removedBytes / 1048576.0:F2} MiB); output {new FileInfo(output).Length / 1048576.0:F2} MiB."));
    }

    private static List<Rule> ReadRules(string path)
    {
        var rules = new List<Rule>();
        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            string[] columns = line.Split('\t');
            if (columns.Length != 3 || columns[0] is not ("prefix" or "exact") || string.IsNullOrWhiteSpace(columns[2]))
                throw new InvalidOperationException("Invalid resource removal policy line: " + line);
            if (!new[] { "Samples.", "Textures.", "Tracks.", "Skins." }
                .Any(category => columns[1].StartsWith("osu.Game.Resources." + category, StringComparison.Ordinal)))
                throw new InvalidOperationException("Removal policy cannot target code, fonts, shaders or localisation: " + columns[1]);
            rules.Add(new Rule(columns[0], columns[1], columns[2]));
        }
        if (rules.Count == 0) throw new InvalidOperationException("Resource removal policy is empty.");
        return rules;
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed record Rule(string Kind, string Pattern, string Reason)
    {
        public bool Matches(string name) => Kind == "exact" ? name == Pattern : name.StartsWith(Pattern, StringComparison.Ordinal);
    }
}
