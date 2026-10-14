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
using osu.Game.Overlays.Settings.Sections;
using osu.Game.Overlays.Settings.Sections.Audio;
using osu.Game.Overlays.Settings.Sections.General;
using osu.Game.Overlays.Settings.Sections.Input;
using osu.Game.Rulesets;

namespace LazerRave.Lazer;

internal partial class LazerRaveSettingsPanel(DesktopSettings settings, Action apply) : SettingsOverlay()
{
    protected override SettingsSection CreateGeneralSection() => new ClientGeneralSection();
    protected override KeyBindingPanel CreateKeyBindingPanel() => new GameplayKeyBindingPanel();
    protected override bool IncludeRulesetSettings => false;
    protected override bool IncludeSection(SettingsSection section) => section is ClientGeneralSection or GraphicsSection or UserInterfaceSection;
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
            new RoundedButton { Text = D("Apply settings"), RelativeSizeAxes = Axes.X, Height = 40, Action = apply },
            base.CreateFooter(),
        },
    };
    [BackgroundDependencyLoader]
    private void load(LazerRaveGame game)
    {
        var native = new NativeSettingsSection(game.NativeSettings, game.ReloadNativeSettings, game.OpenClassicConfiguration, new Subsection("Rendering and chart compatibility", new Drawable[]
        {
            new SettingsDropdown<string> { LabelText = D("Embedded rendering preset"), Items = RenderProfiles.Labels.Values, Current = settings.RenderProfile },
            new SettingsDropdown<string> { LabelText = D("Chart encoding"), Items = new[] { "auto", "utf-8", "cp932", "gb18030" }, Current = settings.Encoding },
        }));
        AddSection(new FrontendAudioSection(native));
        AddSection(new Section("Internet Ranking", FontAwesome.Solid.ChartBar, new Subsection("Internet Ranking", new Drawable[]
        {
            new IrUploadDropdown { LabelText = D("Score upload"), Items = new[] { "public", "private", "off" }, Current = settings.IrUpload },
        })));
        AddSection(new Section("Gameplay and controls", FontAwesome.Solid.Keyboard, new Subsection("Gameplay and controls", new Drawable[]
        {
            new SettingsButton { Text = D("Configure gameplay key bindings"), Action = game.OpenKeyBindings },
            new SettingsSlider<int> { LabelText = D("Judgement offset (ms)"), Current = settings.Offset, DisplayAsPercentage = false, KeyboardStep = 1 },
            new LazerRavePlayPopover.ArrangementSetting { LabelText = D("Arrangement"), Items = PlayOptionCatalog.Arrangements, Current = settings.Arrangement },
        })));
        AddSection(new Section("Display and skins", FontAwesome.Solid.Desktop, new Subsection("Game viewport", new Drawable[]
        {
            new PresentationDropdown { LabelText = D("Game presentation"), Items = new[] { "embedded", "standalone" }, Current = settings.Presentation },
            new SettingsDropdown<string> { LabelText = D("Embedded frame limit"), Items = new[] { "Display", "60", "120", "144", "165", "240", "360", "480", "1000", "Unlimited", settings.FrameLimit.Value }.Distinct(), Current = settings.FrameLimit },
            new SettingsDropdown<string> { LabelText = D("Preset"), Items = new[] { "640x480", "1024x768", "1280x720", "1920x1080", "2560x1440", settings.Window.Value }.Distinct(), Current = settings.Window },
            new SettingsTextBox { LabelText = D("Custom size (width x height)"), Current = settings.Window },
        }), native.DisplayGroup(), native.SkinGroup()));
        AddSection(new Section("Library", FontAwesome.Solid.FolderOpen, new LibraryDirectorySettings(settings), new Subsection("Song select", new Drawable[]
        {
            new SettingsCheckbox { LabelText = D("Show clear lamps"), Current = settings.ShowClearLamps },
        })));
        AddSection(native);
    }

    private partial class GameplayKeyBindingPanel : KeyBindingPanel
    {
        protected override IEnumerable<SettingsSection> CreateRulesetBindingSections(RulesetStore rulesets)
            => [new Section("LazerRave", FontAwesome.Solid.Keyboard, new GameplayKeyBindings())];
    }

    private partial class GameplayKeyBindings : SettingsSubsection
    {
        protected override LocalisableString Header => GameplaySettingsStrings.GameplaySectionHeader;

        [BackgroundDependencyLoader]
        private void load(LazerRaveGame game)
        {
            Children = new Drawable[]
            {
                new SettingsButtonV2
                {
                    Text = D("Configure gameplay key bindings"),
                    TooltipText = D("Configure LR2 key bindings"),
                    Height = 60,
                    Action = game.OpenKeyBindings,
                },
            };
        }
    }

    private partial class ClientGeneralSection : SettingsSection
    {
        public override LocalisableString Header => CommonStrings.General;
        public override Drawable CreateIcon() => new SpriteIcon { Icon = FontAwesome.Solid.Cog };
        [BackgroundDependencyLoader]
        private void load(LazerRaveGame game)
        {
            Add(new LanguageSettings());
            Add(new Subsection("Application files", new Drawable[]
            {
                new SettingsButton { Text = D("Open application folder"), Action = game.OpenApplicationFolder },
                new SettingsButton { Text = D("Open player data folder"), Action = game.OpenPlayerDataFolder },
            }));
        }
    }

    private partial class FrontendAudioSection(NativeSettingsSection native) : SettingsSection
    {
        public override LocalisableString Header => D("Audio");
        public override Drawable CreateIcon() => new SpriteIcon { Icon = FontAwesome.Solid.VolumeUp };
        [BackgroundDependencyLoader]
        private void load()
        {
            Add(new Subsection("Menu and preview audio", new Drawable[] { new OsuSpriteText { Text = D("Only affects menu music and song previews."), Font = osu.Game.Graphics.OsuFont.GetFont(size: 15) } }));
            Add(new AudioDevicesSettings());
            Add(new VolumeSettings());
            Add(native.AudioGroup());
        }
    }

    private partial class IrUploadDropdown : SettingsDropdown<string>
    {
        protected override osu.Game.Graphics.UserInterface.OsuDropdown<string> CreateDropdown() => new IrUploadControl();
        private partial class IrUploadControl : DropdownControl
        {
            protected override LocalisableString GenerateItemText(string item) => D(item switch
            { "private" => "Automatic (private)", "off" => "Do not upload", _ => "Automatic (public)" });
        }
    }

    private partial class PresentationDropdown : SettingsDropdown<string>
    {
        protected override osu.Game.Graphics.UserInterface.OsuDropdown<string> CreateDropdown() => new PresentationControl();
        private partial class PresentationControl : DropdownControl
        {
            protected override LocalisableString GenerateItemText(string item) => D(item == "standalone" ? "Separate window" : "Embedded");
        }
    }

    private partial class Section(string title, IconUsage icon, params SettingsSubsection[] subsections) : SettingsSection
    {
        public override LocalisableString Header => D(title);
        public override Drawable CreateIcon() => new SpriteIcon { Icon = icon };
        [BackgroundDependencyLoader] private void load() { foreach (var subsection in subsections) Add(subsection); }
    }

    private partial class Subsection(string title, Drawable[] controls) : SettingsSubsection
    {
        protected override LocalisableString Header => D(title);
        [BackgroundDependencyLoader] private void load() => Children = controls;
    }
}
