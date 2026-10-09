// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Input.Handlers.Mouse;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Input;
using osu.Game.Localisation;

namespace osu.Game.Overlays.Settings.Sections.Input
{
    public partial class MouseSettings : InputSubsection
    {
        protected override LocalisableString Header => MouseSettingsStrings.Mouse;

        private Bindable<WindowMode> windowMode = null!;
        private Bindable<bool> minimiseOnFocusLoss = null!;
        private FormEnumDropdown<OsuConfineMouseMode> confineMouseModeSetting = null!;
        protected override bool IsToggleable => false;

        public MouseSettings(MouseHandler mouseHandler)
            : base(mouseHandler)
        {
        }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager osuConfig, FrameworkConfigManager config)
        {
            windowMode = config.GetBindable<WindowMode>(FrameworkSetting.WindowMode);
            minimiseOnFocusLoss = config.GetBindable<bool>(FrameworkSetting.MinimiseOnFocusLossInFullscreen);

            AddRange(new Drawable[]
            {
                new SettingsItemV2(confineMouseModeSetting = new FormEnumDropdown<OsuConfineMouseMode>
                {
                    Caption = MouseSettingsStrings.ConfineMouseMode,
                    Current = osuConfig.GetBindable<OsuConfineMouseMode>(OsuSetting.ConfineMouseMode)
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = MouseSettingsStrings.DisableMouseWheelVolumeAdjust,
                    HintText = MouseSettingsStrings.DisableMouseWheelVolumeAdjustTooltip,
                    Current = osuConfig.GetBindable<bool>(OsuSetting.MouseDisableWheel)
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = MouseSettingsStrings.DisableClicksDuringGameplay,
                    Current = osuConfig.GetBindable<bool>(OsuSetting.MouseDisableButtons)
                }),
            });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            windowMode.BindValueChanged(_ => updateConfineMouseModeSettingVisibility());
            minimiseOnFocusLoss.BindValueChanged(_ => updateConfineMouseModeSettingVisibility(), true);
        }

        /// <summary>
        /// Updates disabled state and tooltip of <see cref="confineMouseModeSetting"/> to match when <see cref="ConfineMouseTracker"/> is overriding the confine mode.
        /// </summary>
        private void updateConfineMouseModeSettingVisibility()
        {
            bool confineModeOverriden = windowMode.Value == WindowMode.Fullscreen && minimiseOnFocusLoss.Value;

            if (confineModeOverriden)
            {
                confineMouseModeSetting.Current.Disabled = true;
                confineMouseModeSetting.HintText = MouseSettingsStrings.NotApplicableFullscreen;
            }
            else
            {
                confineMouseModeSetting.Current.Disabled = false;
                confineMouseModeSetting.HintText = default;
            }
        }
    }
}
