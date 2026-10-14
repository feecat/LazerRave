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

internal partial class LazerRaveFavoriteButton : ScreenFooterButton
{
    [Resolved] private LazerRaveGame game { get; set; } = null!;
    [BackgroundDependencyLoader]
    private void load()
    {
        Icon = FontAwesome.Solid.Star;
        Action = game.ToggleSelectedFavorite;
        game.SongSelectionChanged += Changed;
        game.LibraryPreferences.Changed += Changed;
        RefreshLabel();
    }
    private void Changed() { if (!IsDisposed) Schedule(RefreshLabel); }
    private void RefreshLabel()
    {
        bool favorite = game.IsSelectedSongFavorite;
        Text = D(favorite ? "Favorited" : "Favorite");
        AccentColour = favorite ? new Color4(240, 188, 87, 255) : new Color4(101, 193, 240, 255);
    }
    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing) { game.SongSelectionChanged -= Changed; game.LibraryPreferences.Changed -= Changed; }
        base.Dispose(isDisposing);
    }
}

internal partial class LazerRavePlayButton(string category) : ScreenFooterButton, IHasPopover, IHasTooltip
{
    [Resolved] private LazerRaveGame game { get; set; } = null!;
    [Resolved] private OverlayColourProvider colourProvider { get; set; } = null!;
    private readonly BindableInt gauge = new();
    private readonly BindableDouble speed = new();
    private readonly BindableBool automaticSpeed = new();
    private readonly BindableBool normalizedBase = new();
    private readonly BindableInt baseBpm = new();
    private readonly BindableInt targetSpeed = new();
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
        automaticSpeed.BindTo(game.PlaySettings.AutomaticSpeed);
        normalizedBase.BindTo(game.PlaySettings.NormalizeBaseSpeed);
        baseBpm.BindTo(game.PlaySettings.BaseBpm);
        normalizedBase.BindValueChanged(_ => RefreshLabel());
        baseBpm.BindValueChanged(_ => RefreshLabel());
        targetSpeed.BindTo(game.PlaySettings.TargetSpeed);
        automaticSpeed.BindValueChanged(_ => RefreshLabel());
        targetSpeed.BindValueChanged(_ => RefreshLabel());
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
        "Speed" => normalizedBase.Value ? $"{baseBpm.Value} BPM · {speed.Value:0.00}×" : automaticSpeed.Value ? $"AUTO · {targetSpeed.Value}" : speed.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "×",
        _ => arrangement.Value.ToUpperInvariant(),
    };

    public Popover GetPopover() => new LazerRavePlayPopover(game.PlaySettings, category, this) { ColourProvider = colourProvider };

    protected override void Dispose(bool isDisposing)
    {
        gauge.UnbindAll();
        speed.UnbindAll();
        automaticSpeed.UnbindAll();
        normalizedBase.UnbindAll(); baseBpm.UnbindAll();
        targetSpeed.UnbindAll();
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
    private string category = initialCategory == "Gauge" ? "Common" : initialCategory;
    private readonly BindableBool advanced = new();
    private readonly BindableBool automatic = new();
    private readonly BindableBool normalized = new();
    private readonly BindableInt speedKeys = new();
    private readonly BindableDouble speedInput = new();
    private readonly BindableInt baseInput = new(), targetInput = new();
    private OsuSpriteText? estimatedSpeedLabel;
    private int estimateVersion;
    [Resolved] private LazerRaveGame game { get; set; } = null!;

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
        foreach (string section in new[] { "Common", "Speed", "Lanes", "Practice", "Advanced" })
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
            Width = 440,
            Height = 500,
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
        automatic.BindTo(settings.AutomaticSpeed);
        normalized.BindTo(settings.NormalizeBaseSpeed);
        normalized.BindValueChanged(_ => { if (category == "Speed") ShowCategory(category); });
        speedKeys.BindTo(settings.SpeedKeys);
        automatic.BindValueChanged(_ => { if (category == "Speed") ShowCategory(category); });
        speedKeys.BindValueChanged(_ => { if (category == "Speed") ShowCategory(category); });
        advanced.BindValueChanged(_ => ShowCategory(category));
        speedInput.BindTo(settings.Speed); speedInput.BindValueChanged(_ => RefreshEstimate());
        baseInput.BindTo(settings.BaseBpm); baseInput.BindValueChanged(_ => RefreshEstimate());
        targetInput.BindTo(settings.TargetSpeed); targetInput.BindValueChanged(_ => RefreshEstimate());
        ShowCategory(category);
    }

    protected override void Dispose(bool isDisposing)
    {
        estimateVersion++;
        speedInput.UnbindAll(); baseInput.UnbindAll(); targetInput.UnbindAll();
        automatic.UnbindAll(); normalized.UnbindAll(); speedKeys.UnbindAll(); advanced.UnbindAll();
        base.Dispose(isDisposing);
    }

    private void RefreshEstimate()
    {
        if (category == "Speed" && estimatedSpeedLabel is { } label && controls.Children.Contains(label)) _ = EstimateSpeed(label);
    }

    private async Task EstimateSpeed(OsuSpriteText label)
    {
        int version = ++estimateVersion;
        if (game.SelectedChart is not { } chart) return;
        var snapshot = settings.Value with
        {
            SpeedProfiles = settings.CurrentSpeedProfiles, Encoding = settings.Encoding.Value,
            PlayOptions = settings.PlayOptions.ToDictionary(pair => pair.Key, pair => pair.Value.Value),
        };
        try
        {
            double speed = await Task.Run(() => snapshot.ForChart(chart).Speed);
            if (!IsDisposed) Schedule(() => { if (!IsDisposed && version == estimateVersion && controls.Children.Contains(label)) label.Text = $"SPEED ≈ {Math.Round(speed * 100):0}"; });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or FormatException) { }
    }

    private void ShowCategory(string section)
    {
        estimateVersion++; estimatedSpeedLabel = null;
        category = section;
        controls.Clear();
        scroll.ScrollToStart();
        foreach (var tab in tabs)
            tab.Value.BackgroundColour = tab.Key == section ? ColourProvider.Colour3 : ColourProvider.Background3;
        if (section is "Speed" or "Lanes" or "Practice")
            controls.Add(new SettingsCheckbox { LabelText = D("Show advanced options"), Current = advanced });
        if (section == "Speed")
        {
            controls.Add(new OsuSpriteText { Text = $"{settings.SpeedKeys.Value}K", Font = OsuFont.GetFont(size: 20) });
            estimatedSpeedLabel = new OsuSpriteText { Text = "SPEED —", Font = OsuFont.GetFont(size: 18) };
            controls.Add(estimatedSpeedLabel);
            RefreshEstimate();
            controls.Add(new SettingsCheckbox { LabelText = D("Normalize base BPM in frontend"), Current = settings.NormalizeBaseSpeed });
            if (settings.NormalizeBaseSpeed.Value)
            {
                controls.Add(new SettingsSlider<int> { LabelText = D("Unified base BPM"), Current = settings.BaseBpm, DisplayAsPercentage = false, KeyboardStep = 1 });
                controls.Add(new SettingsSlider<double> { LabelText = D("Scroll speed"), Current = settings.Speed, DisplayAsPercentage = false, KeyboardStep = .05f });
                controls.Add(new OsuTextFlowContainer(text => text.Font = OsuFont.GetFont(size: 15)) { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Text = D("Final multiplier = base BPM / note-dominant BPM × scroll speed. Calculated by the frontend, without LR2 speed fixing.") });
            }
            else if (settings.AutomaticSpeed.Value)
            {
                controls.Add(new SettingsCheckbox { LabelText = D("Automatic reading speed"), Current = settings.AutomaticSpeed });
                controls.Add(new SettingsSlider<int> { LabelText = D("Target reading speed (BPM × multiplier)"), Current = settings.TargetSpeed, DisplayAsPercentage = false, KeyboardStep = 10 });
                controls.Add(new OsuTextFlowContainer(text => text.Font = OsuFont.GetFont(size: 15)) { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Text = D("Uses the main BPM. Tempo changes are preserved; cover and lift adjust the initial speed. This is not a skin-calibrated green number.") });
            }
            else
            {
                controls.Add(new SettingsCheckbox { LabelText = D("Automatic reading speed"), Current = settings.AutomaticSpeed });
                controls.Add(new SettingsSlider<double> { LabelText = D("Scroll speed"), Current = settings.Speed, DisplayAsPercentage = false, KeyboardStep = .05f });
            }
        }
        if (section is "Common" or "Lanes")
            controls.Add(new ArrangementSetting { LabelText = D("Arrangement"), Items = PlayOptionCatalog.Arrangements, Current = settings.Arrangement });
        if (section == "Practice")
            controls.Add(new OsuTextFlowContainer(text => text.Font = OsuFont.GetFont(size: 15)) { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Text = D("Assisted and modified plays may not count toward personal bests or IR. Some modifiers are not recorded in LR2 replays.") });
        if (section is "Common" or "Practice" && PlayOptionCatalog.All.Where(PlayOptionCatalog.IsSinglePlay).Where(PlayOptionCatalog.IsPractice).Any(option => settings.PlayOptions[option.Name].Value != option.Default))
            controls.Add(new RoundedButton { Text = D("Reset practice options"), RelativeSizeAxes = Axes.X, Height = 40, Action = () => { foreach (var option in PlayOptionCatalog.All.Where(PlayOptionCatalog.IsSinglePlay).Where(PlayOptionCatalog.IsPractice)) settings.PlayOptions[option.Name].Value = option.Default; ShowCategory(category); } });
        if (settings.PlayOptions["battle"].Value != 0)
            controls.Add(new RoundedButton { Text = D("Use single play (disable classic battle)"), RelativeSizeAxes = Axes.X, Height = 40, Action = () => { settings.PlayOptions["battle"].Value = 0; ShowCategory(category); } });
        foreach (var option in PlayOptionCatalog.Visible(section, advanced.Value).Where(option => !(settings.AutomaticSpeed.Value || settings.NormalizeBaseSpeed.Value) || section != "Speed" || option.Name is "hs_step" or "lock_speed"))
        {
            var current = settings.PlayOptions[option.Name].GetBoundCopy();
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
