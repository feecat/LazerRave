using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;
using osu.Game.Overlays;

namespace LazerRave.Lazer;

internal partial class LazerRaveNowPlayingOverlay : NowPlayingOverlay
{
    protected override void UpdateState(ValueChangedEvent<Visibility> state)
    {
        if (state.NewValue == Visibility.Visible)
        {
            State.Value = Visibility.Hidden;
            return;
        }
        base.UpdateState(state);
    }
}
