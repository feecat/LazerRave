using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace LazerRave.Bridge;

internal sealed class NativeKeyBindings(string runtime)
{
    private string DirectoryPath => Path.Combine(Path.GetFullPath(runtime), "LR2files", "Config");
    public static string FileName(int keys) => keys switch
    { 7 => "keyconfig.xml", 5 => "keyconfig_5.xml", 9 => "keyconfig_p.xml", _ => throw new ArgumentOutOfRangeException(nameof(keys)) };
    public static int[] Rows(int keys) => keys == 9 ? [.. Enumerable.Range(1, 9), 12, 13]
        : [.. Enumerable.Range(1, keys), 10, 11, 12, 13, .. Enumerable.Range(21, keys), 30, 31, 32, 33];

    public KeyBindingDraft Read(int keys)
    {
        string path = Path.Combine(DirectoryPath, FileName(keys));
        var document = ReadDocument(path, keys);
        return new(keys, Rows(keys).ToDictionary(row => row, row => Keyboard(document, row)));
    }

    public void Save(IEnumerable<KeyBindingDraft> drafts)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var writes = new List<(string Path, byte[] Bytes, byte[]? Original)>();
        foreach (var draft in drafts.Where(draft => draft.HasChanges || !File.Exists(Path.Combine(DirectoryPath, FileName(draft.Keys)))))
        {
            string path = Path.Combine(DirectoryPath, FileName(draft.Keys));
            byte[]? original = File.Exists(path) ? File.ReadAllBytes(path) : null;
            var document = ReadDocument(path, draft.Keys);
            foreach (int row in draft.ChangedRows)
            {
                var current = Keyboard(document, row);
                if (!current.SequenceEqual(draft.Original[row]))
                    throw new IOException("LR2 key bindings changed outside this window. Reload before saving.");
                var node = document.Root!.Element(Tag(row));
                if (node is null) { node = new XElement(Tag(row)); document.Root.Add(node); }
                var devices = node.Elements("id").Where(id => Parse(id) >= 256).Select(id => new XElement(id)).ToArray();
                if (devices.Length + draft.Bindings[row].Length > 16)
                    throw new InvalidDataException("LR2 supports at most 16 bindings per button.");
                node.Elements("id").Remove();
                node.Add(draft.Bindings[row].Select(code => new XElement("id", code)));
                node.Add(devices);
            }
            document.Declaration = new XDeclaration("1.0", "Shift_JIS", null);
            using var buffer = new MemoryStream();
            using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings { Encoding = Encoding.GetEncoding(932), Indent = true })) document.Save(writer);
            writes.Add((path, buffer.ToArray(), original));
        }
        System.IO.Directory.CreateDirectory(DirectoryPath);
        var pending = writes.Select(write => (Write: write, Temporary: write.Path + "." + Guid.NewGuid().ToString("N") + ".tmp")).ToArray();
        int installed = 0;
        try
        {
            foreach (var item in pending)
            {
                File.WriteAllBytes(item.Temporary, item.Write.Bytes);
                if (item.Write.Original is { } original) File.WriteAllBytes(item.Write.Path + ".lazerrave.bak", original);
            }
            foreach (var item in pending) { File.Move(item.Temporary, item.Write.Path, true); installed++; }
        }
        catch
        {
            foreach (var item in pending.Take(installed).Reverse())
            {
                if (item.Write.Original is null) File.Delete(item.Write.Path);
                else { File.WriteAllBytes(item.Temporary, item.Write.Original); File.Move(item.Temporary, item.Write.Path, true); }
            }
            throw;
        }
        finally { foreach (var item in pending) if (File.Exists(item.Temporary)) File.Delete(item.Temporary); }
    }

    private static XDocument ReadDocument(string path, int keys)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (!File.Exists(path))
        {
            string defaults = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "_def.xml");
            if (File.Exists(defaults)) path = defaults;
            else return new(new XElement("keyconfig", KeyBindingDraft.Defaults(keys).Select(pair =>
                new XElement(Tag(pair.Key), pair.Value.Select(code => new XElement("id", code))))));
        }
        using var reader = XmlReader.Create(path, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
        var document = XDocument.Load(reader);
        if (document.Root?.Name != "keyconfig") throw new InvalidDataException("Invalid LR2 key configuration.");
        if (document.Root.Elements().GroupBy(node => node.Name).Any(group => group.Count() > 1))
            throw new InvalidDataException("Duplicate LR2 key configuration element.");
        return document;
    }

    private static int Parse(XElement node) => int.Parse(node.Value, CultureInfo.InvariantCulture);
    private static int[] Keyboard(XDocument document, int row) => document.Root!.Element(Tag(row))?.Elements("id")
        .Select(Parse).Where(code => code is > 0 and < 256).Distinct().ToArray() ?? [];
    private static string Tag(int row) => "key" + row.ToString("D2", CultureInfo.InvariantCulture);
}

internal sealed class KeyBindingDraft(int keys, Dictionary<int, int[]> original)
{
    public int Keys { get; } = keys;
    public Dictionary<int, int[]> Original { get; } = original;
    public Dictionary<int, int[]> Bindings { get; } = original.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    public IEnumerable<int> ChangedRows => Bindings.Keys.Where(row => !Bindings[row].SequenceEqual(Original[row]));
    public bool HasChanges => ChangedRows.Any();
    public void Assign(int row, int code, bool append)
    {
        if (code is <= 0 or >= 256) throw new ArgumentOutOfRangeException(nameof(code));
        if (Bindings.Any(pair => pair.Key != row && pair.Value.Contains(code)))
            throw new ArgumentException("That key is already assigned to another button.");
        int[] values = append ? Bindings[row].Append(code).Distinct().ToArray() : [code];
        if (values.Length > 16) throw new ArgumentException("LR2 supports at most 16 bindings per button.");
        Bindings[row] = values;
    }
    public void RestoreDefaults() { foreach (var pair in Defaults(Keys)) Bindings[pair.Key] = pair.Value; }
    public static Dictionary<int, int[]> Defaults(int keys)
    {
        int[] first = keys switch { 9 => [30, 31, 32, 33, 57, 36, 37, 38, 39], 5 => [31, 32, 33, 36, 37], _ => [31, 32, 33, 57, 36, 37, 38] };
        var result = Enumerable.Range(1, keys).ToDictionary(row => row, row => new[] { first[row - 1] });
        result[12] = [16]; result[13] = [17];
        if (keys != 9)
        {
            result[10] = [30]; result[11] = [39];
            int[] second = [44, 45, 46, 47, 48, 49, 50];
            for (int i = 0; i < keys; i++) result[21 + i] = [second[i]];
            result[30] = [42]; result[31] = [54]; result[32] = [24]; result[33] = [25];
        }
        return result;
    }
}

internal static class NativeKeyboard
{
    private static readonly Dictionary<string, int> codes = CreateCodes();
    public static bool TryCode(string keyName, out int code) => codes.TryGetValue(keyName, out code);
    public static string Name(int code) => codes.FirstOrDefault(pair => pair.Value == code).Key switch
    {
        "Semicolon" => ";", "Quote" => "'", "Comma" => ",", "Period" => ".", "Slash" => "/", "BackSlash" => "\\",
        "BracketLeft" => "[", "BracketRight" => "]", "Minus" => "-", "Plus" => "=", "Tilde" => "`",
        { } name when name.StartsWith("Number") => name[6..],
        { } name => name,
        _ => "LR2 #" + code,
    };
    private static Dictionary<string, int> CreateCodes()
    {
        var result = new Dictionary<string, int>();
        foreach (var (letters, start) in new[] { ("QWERTYUIOP", 16), ("ASDFGHJKL", 30), ("ZXCVBNM", 44) })
            for (int i = 0; i < letters.Length; i++) result[letters[i].ToString()] = start + i;
        for (int i = 1; i <= 9; i++) result["Number" + i] = i + 1;
        result["Number0"] = 11;
        foreach (var (name, code) in new (string, int)[]
        {
            ("Minus", 12), ("Plus", 13), ("BackSpace", 14), ("Tab", 15), ("BracketLeft", 26), ("BracketRight", 27),
            ("Semicolon", 39), ("Quote", 40), ("Tilde", 41), ("ShiftLeft", 42), ("BackSlash", 43),
            ("Comma", 51), ("Period", 52), ("Slash", 53), ("ShiftRight", 54), ("KeypadMultiply", 55),
            ("AltLeft", 56), ("Space", 57), ("CapsLock", 58), ("Keypad7", 71), ("Keypad8", 72), ("Keypad9", 73),
            ("KeypadSubtract", 74), ("Keypad4", 75), ("Keypad5", 76), ("Keypad6", 77), ("KeypadAdd", 78),
            ("Keypad1", 79), ("Keypad2", 80), ("Keypad3", 81), ("Keypad0", 82), ("KeypadDecimal", 83),
            ("ControlLeft", 29), ("ControlRight", 157), ("AltRight", 184), ("KeypadDivide", 181),
        }) result[name] = code;
        return result;
    }
}
