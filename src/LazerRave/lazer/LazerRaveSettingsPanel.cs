using static LazerRave.Lazer.LazerRaveText;
using LazerRave.Bridge;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;

namespace LazerRave.Lazer;

internal partial class LazerRaveSettingsPanel(DesktopSettings settings, Action apply) : SettingsOverlay()
{
    private readonly OsuSpriteText saveStatus = new() { Font = osu.Game.Graphics.OsuFont.GetFont(size: 15), Margin = new MarginPadding { Horizontal = 20, Bottom = 10 } };
    public void SetSaveStatus(LocalisableString text, bool error)
    {
        saveStatus.Text = text;
        saveStatus.Colour = error ? osuTK.Graphics.Color4.OrangeRed : osuTK.Graphics.Color4.LightGreen;
    }
    protected override Drawable CreateHeader() => new FillFlowContainer
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Direction = FillDirection.Vertical,
        Children = new Drawable[] { base.CreateHeader(), saveStatus },
    };
    protected override Drawable CreateFooter() => new FillFlowContainer
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Direction = FillDirection.Vertical,
        Children = new[]
        {
            new RoundedButton { Text = D("Apply & rescan"), RelativeSizeAxes = Axes.X, Height = 40, Action = apply },
            base.CreateFooter(),
        },
    };
    [BackgroundDependencyLoader]
    private void load(LazerRaveGame game)
    {
        AddSection(new Section("OpenLR2", FontAwesome.Solid.Keyboard, new Subsection("OpenLR2", new Drawable[]
        {
            new SettingsSlider<double> { LabelText = D("Scroll speed"), Current = settings.Speed, DisplayAsPercentage = false, KeyboardStep = .05f },
            new SettingsSlider<int> { LabelText = D("Judgement offset (ms)"), Current = settings.Offset, DisplayAsPercentage = false, KeyboardStep = 1 },
            new SettingsDropdown<string> { LabelText = D("Arrangement"), Items = new[] { "off", "mirror", "random" }, Current = settings.Arrangement },
            new SettingsDropdown<string> { LabelText = D("Chart encoding"), Items = new[] { "auto", "utf-8", "cp932", "gb18030" }, Current = settings.Encoding },
        })));
        AddSection(new Section("Game viewport", FontAwesome.Solid.Desktop, new Subsection("Game viewport", new Drawable[]
        {
            new PresentationDropdown { LabelText = D("Game presentation"), Items = new[] { "embedded", "standalone" }, Current = settings.Presentation },
            new SettingsDropdown<string> { LabelText = D("Embedded rendering preset"), Items = RenderProfiles.Labels.Values, Current = settings.RenderProfile },
            new SettingsDropdown<string> { LabelText = D("Embedded frame limit"), Items = new[] { "Display", "60", "120", "144", "165", "240", "360", "480", "1000", "Unlimited", settings.FrameLimit.Value }.Distinct(), Current = settings.FrameLimit },
            new SettingsDropdown<string> { LabelText = D("Preset"), Items = new[] { "640x480", "1024x768", "1280x720", "1920x1080", "2560x1440", settings.Window.Value }.Distinct(), Current = settings.Window },
            new SettingsTextBox { LabelText = D("Custom size (width x height)"), Current = settings.Window },
        })));
        AddSection(new Section("Library", FontAwesome.Solid.FolderOpen, new Subsection("Directories", new Drawable[]
        {
            new SettingsTextBox { LabelText = D("BMS folders (separate with ;)"), Current = settings.Roots },
        })));
        AddSection(new Section("Player", FontAwesome.Solid.User, new Subsection("Profile", new Drawable[]
        {
            new SettingsTextBox { LabelText = D("Display name"), Current = settings.Player },
            new SettingsTextBox { LabelText = D("Avatar file"), Current = settings.Avatar },
        })));
    }

    private partial class PresentationDropdown : SettingsDropdown<string>
    {
        protected override osu.Game.Graphics.UserInterface.OsuDropdown<string> CreateDropdown() => new PresentationControl();
        private partial class PresentationControl : DropdownControl
        {
            protected override LocalisableString GenerateItemText(string item) => D(item == "standalone" ? "Separate window" : "Embedded");
        }
    }

    private partial class Section(string title, IconUsage icon, SettingsSubsection subsection) : SettingsSection
    {
        public override LocalisableString Header => D(title);
        public override Drawable CreateIcon() => new SpriteIcon { Icon = icon };
        [BackgroundDependencyLoader] private void load() => Add(subsection);
    }

    private partial class Subsection(string title, Drawable[] controls) : SettingsSubsection
    {
        protected override LocalisableString Header => D(title);
        [BackgroundDependencyLoader] private void load() => Children = controls;
    }
}
