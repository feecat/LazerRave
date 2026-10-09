using static LazerRave.Lazer.LazerRaveText;
using LazerRave.Bridge;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Input.Bindings;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Screens.Footer;
using osuTK;
using osuTK.Graphics;

namespace LazerRave.Lazer;

internal partial class LazerRavePlayButton(string category) : ScreenFooterButton, IHasPopover, IHasTooltip
{
    [Resolved] private LazerRaveGame game { get; set; } = null!;
    [Resolved] private OverlayColourProvider colourProvider { get; set; } = null!;
    private readonly BindableInt gauge = new();
    private readonly BindableDouble speed = new();
    private readonly Bindable<string> arrangement = new();
    public override LocalisableString TooltipText => D(category);

    [BackgroundDependencyLoader]
    private void load()
    {
        Icon = category switch
        {
            "Gauge" => FontAwesome.Solid.Heart,
            "Speed" => FontAwesome.Solid.TachometerAlt,
            _ => FontAwesome.Solid.Random,
        };
        AccentColour = category switch
        {
            "Gauge" => new Color4(240, 130, 147, 255),
            "Speed" => new Color4(101, 193, 240, 255),
            _ => new Color4(240, 188, 87, 255),
        };
        if (category == "Gauge") Hotkey = GlobalAction.ToggleModSelection;
        gauge.BindTo(game.PlaySettings.PlayOptions["gauge"]);
        speed.BindTo(game.PlaySettings.Speed);
        arrangement.BindTo(game.PlaySettings.Arrangement);
        gauge.BindValueChanged(_ => RefreshLabel());
        speed.BindValueChanged(_ => RefreshLabel());
        arrangement.BindValueChanged(_ => RefreshLabel());
        RefreshLabel();
        Action = () =>
        {
            if (this.FindClosestParent<PopoverContainer>()?.CurrentTarget == this) this.HidePopover();
            else this.ShowPopover();
        };
    }

    private void RefreshLabel() => Text = category switch
    {
        "Gauge" => PlayOptionCatalog.All.Single(option => option.Name == "gauge").Choices![gauge.Value],
        "Speed" => speed.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "×",
        _ => arrangement.Value.ToUpperInvariant(),
    };

    public Popover GetPopover() => new LazerRavePlayPopover(game.PlaySettings, category, this) { ColourProvider = colourProvider };

    protected override void Dispose(bool isDisposing)
    {
        gauge.UnbindAll();
        speed.UnbindAll();
        arrangement.UnbindAll();
        base.Dispose(isDisposing);
    }
}

internal partial class LazerRavePlayPopover(DesktopSettings settings, string initialCategory, ScreenFooterButton button) : OsuPopover
{
    public required OverlayColourProvider ColourProvider { get; init; }
    private FillFlowContainer controls = null!;
    private OsuScrollContainer scroll = null!;
    private readonly Dictionary<string, RoundedButton> tabs = new();
    private string category = initialCategory;

    protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
    {
        var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
        dependencies.Cache(ColourProvider);
        return dependencies;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Content.Padding = new MarginPadding(14);
        var tabFlow = new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(4),
            Y = 38,
        };
        foreach (string section in new[] { "Gauge", "Speed", "Lanes", "More", "Modifiers" })
        {
            var tab = new MenuTab
            {
                Text = D(section),
                Size = new Vector2(72, 34),
                Action = () => ShowCategory(section),
            };
            tabs.Add(section, tab);
            tabFlow.Add(tab);
        }
        Child = new Container
        {
            Width = 380,
            Height = 440,
            Children = new Drawable[]
            {
                new OsuSpriteText { Text = D("Play options"), Font = OsuFont.GetFont(size: 24, weight: FontWeight.SemiBold) },
                tabFlow,
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Top = 86 },
                    Child = scroll = new OsuScrollContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Child = controls = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 12),
                            Padding = new MarginPadding { Right = 12, Bottom = 12 },
                        },
                    },
                },
            },
        };
        ShowCategory(category);
    }

    private void ShowCategory(string section)
    {
        category = section;
        controls.Clear();
        scroll.ScrollToStart();
        foreach (var tab in tabs)
            tab.Value.BackgroundColour = tab.Key == section ? ColourProvider.Colour3 : ColourProvider.Background3;
        if (section == "Speed")
        {
            var speedControl = new SettingsSlider<double>
            {
                LabelText = D("Scroll speed"), Current = settings.Speed, DisplayAsPercentage = false, KeyboardStep = .05f,
            };
            speedControl.Current.BindValueChanged(change => speedControl.LabelText = LocalisableString.Interpolate($"{D("Scroll speed")} · {change.NewValue:0.00}×"), true);
            controls.Add(speedControl);
        }
        if (section == "Lanes")
            controls.Add(new ArrangementSetting { LabelText = D("Arrangement"), Items = PlayOptionCatalog.Arrangements, Current = settings.Arrangement });
        if (section == "More")
            controls.Add(new SettingsSlider<int> { LabelText = D("Judgement offset (ms)"), Current = settings.Offset, DisplayAsPercentage = false, KeyboardStep = 1 });
        if (section == "Modifiers")
            controls.Add(new OsuSpriteText
            {
                Text = D("Some modifiers are not recorded in LR2 replays."),
                Font = OsuFont.GetFont(size: 14),
                Colour = ColourProvider.Content2,
            });
        foreach (var option in PlayOptionCatalog.All.Where(option => option.Category == section))
        {
            var current = settings.PlayOptions[option.Name];
            if (option.Choices is { } choices)
                controls.Add(new OptionSetting(option) { LabelText = D(option.Label), Items = Enumerable.Range(option.Min, choices.Length), Current = current });
            else
            {
                var slider = new SettingsSlider<int>
                {
                    Current = current, DisplayAsPercentage = false, KeyboardStep = 1,
                };
                slider.Current.BindValueChanged(change => slider.LabelText = LocalisableString.Interpolate($"{D(option.Label)} · {change.NewValue}"), true);
                controls.Add(slider);
            }
        }
    }

    protected override void UpdateState(ValueChangedEvent<Visibility> state)
    {
        base.UpdateState(state);
        button.OverlayState.Value = state.NewValue;
    }

    private partial class MenuTab : RoundedButton
    {
        protected override SpriteText CreateText() => new OsuSpriteText
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Depth = -1,
            Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
        };
    }

    private partial class OptionSetting(PlayOption option) : SettingsDropdown<int>
    {
        protected override OsuDropdown<int> CreateDropdown() => new OptionControl(option);
        private partial class OptionControl(PlayOption option) : DropdownControl
        {
            protected override LocalisableString GenerateItemText(int item) => option.Name == "battle" && item == 4
                ? D("GHOST BATTLE (CLASSIC)") : D(option.Choices![item - option.Min]);
        }
    }

    internal partial class ArrangementSetting : SettingsDropdown<string>
    {
        protected override OsuDropdown<string> CreateDropdown() => new ArrangementControl();
        private partial class ArrangementControl : DropdownControl
        {
            protected override LocalisableString GenerateItemText(string item) => D(item.ToUpperInvariant());
        }
    }
}
