using osu.Framework.Bindables;
using osu.Framework.Input.Handlers;
using osu.Framework.Input.Handlers.Mouse;
using osu.Framework.Input.Handlers.Pen;
using osu.Framework.Input.Handlers.Tablet;

namespace LazerRave.Lazer;

internal static class LazerRaveInputPolicy
{
    public static void Apply(InputHandler handler)
    {
        if (handler is ITabletHandler or PenHandler)
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
