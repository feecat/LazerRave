using LazerRave.Bridge;
using LazerRave.Content;
using osu.Framework.Bindables;
using Tomlyn;
using Tomlyn.Model;

namespace LazerRave.Lazer;

internal sealed class DesktopSettings(FrontendSettings initial, string path, bool readOnly)
{
    private bool frontendFrameLimitSaved = File.Exists(path) && Toml.ToModel(File.ReadAllText(path)).ContainsKey("frontend_frame_limit");
    private bool defaultLibrarySaved = File.Exists(path) && Toml.ToModel(File.ReadAllText(path)).TryGetValue("directories", out var savedRoots)
        && savedRoots is TomlArray directories && directories.OfType<string>().Contains(LibraryFolders.DefaultRoot);
    public FrontendSettings Value { get; private set; } = initial with { Roots = LibraryFolders.NormalizeRoots(initial.Roots, AppContext.BaseDirectory) };
    public BindableDouble Speed { get; } = new(initial.Speed) { MinValue = .5, MaxValue = 10, Precision = .05 };
    public BindableInt Offset { get; } = new(initial.Offset) { MinValue = -1000, MaxValue = 1000 };
    public Bindable<string> Arrangement { get; } = new(initial.Arrangement);
    public IReadOnlyDictionary<string, BindableInt> PlayOptions { get; } = PlayOptionCatalog.All.ToDictionary(
        option => option.Name, option => new BindableInt(PlayOptionCatalog.Get(initial, option)) { MinValue = option.Min, MaxValue = option.Max });
    public Bindable<string> Encoding { get; } = new(initial.Encoding);
    public BindableList<string> Roots { get; } = new(LibraryFolders.NormalizeRoots(initial.Roots, AppContext.BaseDirectory));
    public Bindable<string> FrontendFrameLimit { get; } = new(initial.FrontendFrameLimit);
    public Bindable<string> Window { get; } = new($"{initial.Width}x{initial.Height}");
    public Bindable<string> Player { get; } = new(initial.Player);
    public Bindable<string> Avatar { get; } = new(initial.Avatar ?? "");
    public Bindable<string> FrameLimit { get; } = new(initial.FrameLimit switch { 0 => "Display", -1 => "Unlimited", _ => initial.FrameLimit.ToString() });

    public Bindable<string> IrUpload { get; } = new(initial.IrUpload);

    public Bindable<string> Presentation { get; } = new(initial.Presentation);

    public Bindable<string> RenderProfile { get; } = new(RenderProfiles.Labels[initial.RenderProfile]);

    public bool HasChanges => !frontendFrameLimitSaved || !defaultLibrarySaved || PlayOptionCatalog.All.Any(option => PlayOptions[option.Name].Value != PlayOptionCatalog.Get(Value, option))
        || Speed.Value != Value.Speed || Offset.Value != Value.Offset
        || Arrangement.Value != Value.Arrangement || Encoding.Value != Value.Encoding
        || !LibraryDirectories.SequenceEqual(Value.Roots, StringComparer.OrdinalIgnoreCase) || Window.Value != $"{Value.Width}x{Value.Height}"
        || FrontendFrameLimit.Value != Value.FrontendFrameLimit
        || Player.Value != Value.Player || Avatar.Value != (Value.Avatar ?? "")
        || IrUpload.Value != Value.IrUpload
        || Presentation.Value != Value.Presentation
        || RenderProfile.Value != RenderProfiles.Labels[Value.RenderProfile]
        || FrameLimit.Value != (Value.FrameLimit switch { 0 => "Display", -1 => "Unlimited", _ => Value.FrameLimit.ToString() });

    public void Save()
    {
        LibraryFolders.Ensure(AppContext.BaseDirectory);
        var roots = LibraryDirectories;
        foreach (var root in roots) if (!Directory.Exists(ApplicationPaths.ResolveLibraryRoot(root))) throw new DirectoryNotFoundException(root);
        if (!FrontendSettings.IsValidFrontendFrameLimit(FrontendFrameLimit.Value)) throw new ArgumentException("Invalid frontend frame limit.");
        var size = Window.Value.ToLowerInvariant().Replace('×', 'x').Split('x', StringSplitOptions.TrimEntries);
        if (size.Length != 2 || !int.TryParse(size[0], out int width) || !int.TryParse(size[1], out int height)
            || width is < 320 or > 7680 || height is < 240 or > 4320) throw new ArgumentException("Enter a window size such as 1920x1080.");
        if (!PlayOptionCatalog.Arrangements.Contains(Arrangement.Value) || !new[] { "auto", "utf-8", "cp932", "gb18030" }.Contains(Encoding.Value))
            throw new ArgumentException("Invalid play settings.");
        var playOptions = PlayOptions.ToDictionary(pair => pair.Key, pair => pair.Value.Value);
        PlayOptionCatalog.Validate(playOptions);
        var avatar = Avatar.Value.Trim();
        int frameLimit = FrameLimit.Value switch { "Display" => 0, "Unlimited" => -1, _ => int.Parse(FrameLimit.Value) };
        if (frameLimit is < -1 or > 1000 || frameLimit is > 0 and < 30) throw new ArgumentException("Invalid game frame limit.");
        if (avatar.Length > 0 && (!File.Exists(avatar) || !new[] { ".bmp", ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(avatar).ToLowerInvariant())))
            throw new ArgumentException("Select an existing BMP, PNG or JPEG avatar.");
        if (!new[] { "embedded", "standalone" }.Contains(Presentation.Value)) throw new ArgumentException("Invalid game presentation mode.");
        if (!new[] { "public", "private", "off" }.Contains(IrUpload.Value)) throw new ArgumentException("Invalid IR upload mode.");
        var profile = RenderProfiles.Labels.Single(pair => pair.Value == RenderProfile.Value).Key;
        var next = Value with { Roots = roots, FrontendFrameLimit = FrontendFrameLimit.Value, Speed = Speed.Value, Offset = Offset.Value, Arrangement = Arrangement.Value, PlayOptions = playOptions,
            Encoding = Encoding.Value, Width = width, Height = height, Player = Player.Value.Trim(), Avatar = avatar.Length > 0 ? Path.GetFullPath(avatar) : null, FrameLimit = frameLimit, RenderProfile = profile, Presentation = Presentation.Value, IrUpload = IrUpload.Value };
        if (!readOnly)
        {
            var table = File.Exists(path) ? Toml.ToModel(File.ReadAllText(path)) : new TomlTable();
            var directories = new TomlArray(); foreach (var root in roots) directories.Add(root);
            table["directories"] = directories; table["speed"] = next.Speed; table["offset"] = (long)next.Offset;
            table["arrangement"] = next.Arrangement; table["chart_encoding"] = next.Encoding;
            table["window_width"] = (long)width; table["window_height"] = (long)height; table["display_name"] = next.Player;
            table["avatar_path"] = next.Avatar ?? "";
            table["game_frame_limit"] = (long)next.FrameLimit;
            table["frontend_frame_limit"] = next.FrontendFrameLimit;
            table["game_render_profile"] = next.RenderProfile;
            table["game_presentation"] = next.Presentation;
            table["ir_upload"] = next.IrUpload;
            var playTable = table.TryGetValue("play", out var savedPlay) && savedPlay is TomlTable previousPlay ? previousPlay : new TomlTable();
            foreach (var option in playOptions) playTable[option.Key] = (long)option.Value;
            table["play"] = playTable;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, Toml.FromModel(table)); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        Value = next;
        frontendFrameLimitSaved = true;
        defaultLibrarySaved = true;
    }
    private string[] LibraryDirectories => LibraryFolders.NormalizeRoots(Roots, AppContext.BaseDirectory);
}
