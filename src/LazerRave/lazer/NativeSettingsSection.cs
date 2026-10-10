using LazerRave.Bridge;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Settings;
using static LazerRave.Lazer.LazerRaveText;

namespace LazerRave.Lazer;

internal partial class NativeSettingsSection(NativeSettings settings, Action reload, Action openFolder) : SettingsSection
{
    public override LocalisableString Header => D("Native LR2");
    public override Drawable CreateIcon() => new SpriteIcon { Icon = FontAwesome.Solid.Desktop };

    [BackgroundDependencyLoader]
    private void load()
    {
        Add(new Group("Native display", new Drawable[]
        {
            Choices("Display mode", settings.ScreenMode, ["Fullscreen", "Windowed", "Borderless"]),
            Toggle("Vertical sync", "system/vsync"),
        }));
        Add(new Group("LR2 audio", new Drawable[]
        {
            Choices("Audio backend", settings.Numbers["sound/output"], ["WASAPI (LR2 compatibility)", "WASAPI", "ASIO"]),
            new DeviceSetting(settings) { LabelText = D("Playback device"), Current = settings.Numbers["sound/driver"] },
            new SettingsTextBox { LabelText = D("Audio buffer size (samples)"), Current = settings.Text["sound/bufferlength"] },
            Slider("Audio buffer count", "sound/numbuffers"),
            Slider("LR2 master volume", "sound/volumemaster"),
            Slider("LR2 BGM volume", "sound/volumebgm"),
            Slider("LR2 key volume", "sound/volumekey"),
        }));
        Add(new Group("Native input and library", new Drawable[]
        {
            Slider("Input release delay (ms)", "system/inputinterval"),
            Toggle("Disable Windows keys", "system/disablesystemkey"),
            Choices("Library auto reload", settings.Numbers["system/autoreload"], ["OFF", "When entering a folder", "All folders on startup"]),
            Toggle("Native song previews", "select/preview"),
            Toggle("Write LR2 log", "system/outputlog"),
        }));
        Add(new Group("LR2 skins", new[]
        {
            Skin("7-key skin", "play_7"), Skin("5-key skin", "play_5"), Skin("14-key skin", "play_14"),
            Skin("10-key skin", "play_10"), Skin("9-key skin", "play_9"),
            Skin("Native song selection skin", "select"), Skin("Song loading skin", "decide"),
            Skin("Result skin", "result"), Skin("LR2 sound set", "soundset"),
            new SettingsTextBox { LabelText = D("LR2 font"), Current = settings.Text["skin/fontname"] },
            Toggle("Use system font instead of skin font", "skin/disableimagefont"),
            new SettingsButton { Text = D("Reload native configuration"), Action = reload },
            new SettingsButton { Text = D("Open LR2 configuration folder"), Action = openFolder },
        }));
    }

    private Drawable Skin(string label, string key) => new SkinSetting(settings.Text["skin/" + key], settings.Runtime,
        key == "soundset" ? ".lr2ss" : ".lr2skin") { LabelText = D(label), ShowsDefaultIndicator = false };
    private Drawable Toggle(string label, string key)
    {
        var integer = settings.Numbers[key];
        var enabled = new BindableBool(integer.Value != 0);
        integer.BindValueChanged(change => enabled.Value = change.NewValue != 0);
        enabled.BindValueChanged(change => integer.Value = change.NewValue ? 1 : 0);
        return new SettingsCheckbox { LabelText = D(label), Current = enabled };
    }
    private Drawable Slider(string label, string key)
    {
        var current = settings.Numbers[key];
        var slider = new SettingsSlider<int> { Current = current, DisplayAsPercentage = false, KeyboardStep = 1 };
        current.BindValueChanged(change => slider.LabelText = LocalisableString.Interpolate($"{D(label)} · {change.NewValue}"), true);
        return slider;
    }
    private static Drawable Choices(string label, BindableInt current, string[] items) => new NamedSetting(items.Select((item, index) => (item, index))
        .ToDictionary(pair => pair.index, pair => pair.item)) { LabelText = D(label), Current = current, Items = Enumerable.Range(0, items.Length) };

    private partial class Group(string title, Drawable[] controls) : SettingsSubsection
    {
        protected override LocalisableString Header => D(title);
        [BackgroundDependencyLoader] private void load() => Children = controls;
    }

    private partial class NamedSetting(IReadOnlyDictionary<int, string> names) : SettingsDropdown<int>
    {
        protected override OsuDropdown<int> CreateDropdown() => new NamedControl(names);
        private partial class NamedControl(IReadOnlyDictionary<int, string> names) : DropdownControl
        {
            protected override LocalisableString GenerateItemText(int item) => names.TryGetValue(item, out string? name) ? D(name) : item.ToString();
        }
    }

    private partial class SkinSetting(Bindable<string> path, string runtime, string extension) : SettingsItem<FileInfo?>
    {
        private bool updating;
        protected override Drawable CreateControl() => new FormFileSelector(extension) { PlaceholderText = D("Choose file") };
        [BackgroundDependencyLoader]
        private void load()
        {
            path.BindValueChanged(change =>
            {
                updating = true;
                try { Current.Value = string.IsNullOrWhiteSpace(change.NewValue) ? null : new FileInfo(Path.GetFullPath(change.NewValue, runtime)); }
                catch (Exception error) when (error is ArgumentException or NotSupportedException) { Current.Value = null; }
                finally { updating = false; }
            }, true);
            Current.BindValueChanged(change =>
            {
                if (!updating && change.NewValue is { } file) path.Value = Path.GetRelativePath(runtime, file.FullName);
            });
        }
    }

    private partial class DeviceSetting(NativeSettings settings) : SettingsDropdown<int>
    {
        private readonly Dictionary<int, string> deviceNames = new();
        private bool refreshingDevices;
        protected override OsuDropdown<int> CreateDropdown() => new DeviceControl(deviceNames);
        [BackgroundDependencyLoader]
        private void load()
        {
            settings.Numbers["sound/output"].BindValueChanged(change =>
            {
                if (!settings.IsReloading && change.NewValue != change.OldValue) settings.Numbers["sound/driver"].Value = 0;
                RefreshDevices();
            }, true);
            settings.Numbers["sound/driver"].BindValueChanged(change =>
            {
                if (!refreshingDevices && !deviceNames.ContainsKey(change.NewValue)) RefreshDevices();
            });
        }
        private void RefreshDevices()
        {
            int current = settings.Numbers["sound/driver"].Value;
            refreshingDevices = true;
            try
            {
                deviceNames.Clear();
                try { foreach (var device in NativeAudioDevices.Enumerate(settings.Runtime, settings.Numbers["sound/output"].Value)) deviceNames.Add(device.Key, device.Value); }
                catch (Exception error) when (error is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException) { }
                deviceNames.TryAdd(current, current == 0 ? "Default device" : "Device " + current);
                Items = Array.Empty<int>();
                Items = deviceNames.Keys.Order().ToArray();
            }
            finally
            {
                settings.Numbers["sound/driver"].Value = current;
                refreshingDevices = false;
            }
        }
        private partial class DeviceControl(IReadOnlyDictionary<int, string> names) : DropdownControl
        {
            protected override LocalisableString GenerateItemText(int item) => names.TryGetValue(item, out string? name) ? D(name) : item.ToString();
        }
    }
}
