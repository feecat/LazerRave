using osu.Framework.Bindables;
using osu.Framework.Input.Handlers;
using osu.Framework.Input.Handlers.Mouse;
using osu.Framework.Input.Handlers.Pen;
using osu.Framework.Input.Handlers.Tablet;
using osu.Framework.Input.Handlers.Touch;
using osu.Framework.Input.Handlers.Joystick;
using osu.Framework.Input.Handlers.Midi;
using osu.Framework.Input;
using osu.Framework.Input.StateChanges;

namespace LazerRave.Lazer;

internal static class LazerRaveInputPolicy
{
    public static void ReleaseGameplayInput(InputManager manager)
    {
        var state = manager.CurrentState;
        new MouseButtonInput([], state.Mouse.Buttons).Apply(state, manager);
        new KeyboardKeyInput([], state.Keyboard.Keys).Apply(state, manager);
    }

    public static void Apply(InputHandler handler)
    {
        if (handler is ITabletHandler or PenHandler or TouchHandler or JoystickHandler or MidiHandler)
            Disable(handler.Enabled);
        if (handler is MouseHandler mouse)
            Disable(mouse.UseRelativeMode);
    }

    internal static void Disable(Bindable<bool> setting)
    {
        setting.Default = false;
        setting.Value = false;
        setting.BindValueChanged(value =>
        {
            if (value.NewValue) setting.Value = false;
        });
    }
}
