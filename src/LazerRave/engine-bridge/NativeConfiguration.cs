using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LazerRave.Bridge;

internal sealed class NativeConfiguration
{
    private readonly string directory;
    public NativeConfiguration(string runtime) => directory = Path.Combine(Path.GetFullPath(runtime), "LR2files", "Config");
    public string ConfigPath => Path.Combine(directory, "config.xml");
    public string ExtensionPath => Path.Combine(directory, "openlr2-config.xml");

    public static XDocument Read(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (!File.Exists(path)) return new(new XDeclaration("1.0", "Shift_JIS", null), new XElement("config"));
        var options = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 };
        using var stream = File.OpenRead(path);
        using var reader = XmlReader.Create(stream, options);
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (document.Root?.Name != "config") throw new InvalidDataException("Invalid LR2 configuration: " + path);
        return document;
    }

    public static string? Value(XDocument document, string key)
    {
        var parts = Parts(key);
        var section = Single(document.Root!, parts[0]);
        return section is null ? null : Single(section, parts[1])?.Value;
    }

    public void Save(IReadOnlyDictionary<string, string> changes, IReadOnlyDictionary<string, string>? extensionChanges = null,
        IEnumerable<string>? libraryRoots = null)
    {
        var writes = new List<(string Path, byte[] Bytes)>();
        Prepare(ConfigPath, changes, libraryRoots, writes);
        if (extensionChanges is { Count: > 0 }) Prepare(ExtensionPath, extensionChanges, null, writes);
        if (writes.Count == 0) return;
        Directory.CreateDirectory(directory);
        var pending = new List<(string Path, string Temporary, byte[]? Original)>();
        int installed = 0;
        try
        {
            foreach (var write in writes)
            {
                var temporary = write.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                var original = File.Exists(write.Path) ? File.ReadAllBytes(write.Path) : null;
                pending.Add((write.Path, temporary, original));
                File.WriteAllBytes(temporary, write.Bytes);
                if (original is not null) File.WriteAllBytes(write.Path + ".lazerrave.bak", original);
            }
            foreach (var file in pending)
            {
                File.Move(file.Temporary, file.Path, true);
                installed++;
            }
        }
        catch
        {
            foreach (var file in pending.Take(installed).Reverse())
            {
                if (file.Original is null) File.Delete(file.Path);
                else
                {
                    File.WriteAllBytes(file.Temporary, file.Original);
                    File.Move(file.Temporary, file.Path, true);
                }
            }
            throw;
        }
        finally
        {
            foreach (var file in pending) if (File.Exists(file.Temporary)) File.Delete(file.Temporary);
        }
    }

    private static void Prepare(string path, IReadOnlyDictionary<string, string> changes, IEnumerable<string>? roots,
        List<(string Path, byte[] Bytes)> writes)
    {
        if (changes.Count == 0 && roots is null) return;
        var document = Read(path);
        bool modified = false;
        foreach (var (key, value) in changes)
        {
            var parts = Parts(key);
            if (parts[0] is not ("system" or "sound" or "skin" or "select" or "play"))
                throw new ArgumentException("Unsupported native setting: " + key);
            var section = Single(document.Root!, parts[0]);
            if (section is null) { section = new XElement(parts[0]); document.Root!.Add(section); }
            var node = Single(section, parts[1]);
            if (node?.Value == value) continue;
            if (node is null) section.Add(new XElement(parts[1], value));
            else node.Value = value;
            modified = true;
        }
        if (roots is not null)
        {
            var paths = roots.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var jukebox = Single(document.Root!, "jukebox");
            if (jukebox is null) { jukebox = new XElement("jukebox"); document.Root!.Add(jukebox); }
            if (!jukebox.Elements("path").Select(node => node.Value).SequenceEqual(paths, StringComparer.OrdinalIgnoreCase))
            {
                jukebox.Elements("path").Remove();
                jukebox.Add(paths.Select(value => new XElement("path", value)));
                modified = true;
            }
        }
        if (!modified) return;
        // OpenLR2 reads both native XML files as CP932, including files labelled UTF-8 by older tools.
        document.Declaration = new XDeclaration("1.0", "Shift_JIS", null);
        using var buffer = new MemoryStream();
        using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings
        {
            Encoding = Encoding.GetEncoding(932), Indent = false, NewLineHandling = NewLineHandling.Entitize,
        })) document.Save(writer);
        buffer.WriteByte((byte)'\n');
        writes.Add((path, buffer.ToArray()));
    }

    private static string[] Parts(string key)
    {
        var parts = key.Split('/');
        if (parts.Length != 2 || parts.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Invalid native setting: " + key);
        foreach (var part in parts) XmlConvert.VerifyNCName(part);
        return parts;
    }

    private static XElement? Single(XElement parent, string name)
    {
        var nodes = parent.Elements(name).Take(2).ToArray();
        if (nodes.Length > 1) throw new InvalidDataException("Duplicate LR2 configuration element: " + name);
        return nodes.FirstOrDefault();
    }
}
