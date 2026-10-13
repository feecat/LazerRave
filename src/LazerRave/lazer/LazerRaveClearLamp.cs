using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Screens.Select;
using osuTK;
using osuTK.Graphics;
using static LazerRave.Lazer.LazerRaveText;

namespace LazerRave.Lazer;

internal partial class LazerRaveBeatmapPanel : PanelBeatmap
{
    protected override bool ShowDifficultyIcon => false;
    private readonly LazerRaveClearLamp lamp = new();

    [BackgroundDependencyLoader]
    private void load() => TopLevelContent.Add(lamp);

    protected override void PrepareForUse()
    {
        base.PrepareForUse();
        lamp.Beatmap = ((GroupedBeatmap)Item!.Model).Beatmap;
    }

    protected override void FreeAfterUse()
    {
        lamp.Beatmap = null;
        base.FreeAfterUse();
    }
}

internal partial class LazerRaveClearLamp : CompositeDrawable, IHasTooltip
{
    [Resolved] private LazerRaveGame game { get; set; } = null!;
    private readonly Sprite light, halo;
    private readonly Container visual;
    private int clear, generation, lastRevision = -1;
    private string? lastPlayer;
    private bool lastEnabled;
    private BeatmapInfo? beatmap;
    public LocalisableString TooltipText { get; private set; }

    public BeatmapInfo? Beatmap
    {
        get => beatmap;
        set
        {
            beatmap = value;
            if (IsLoaded) Refresh();
        }
    }

    public LazerRaveClearLamp()
    {
        Anchor = Anchor.CentreLeft;
        Origin = Anchor.CentreLeft;
        RelativeSizeAxes = Axes.Y;
        Size = new Vector2(22, 0.9f);
        Depth = -10;
        AlwaysPresent = true;
        InternalChild = visual = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Alpha = 0,
            Children = new Drawable[]
            {
                halo = new Sprite
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.Centre,
                    X = 4,
                    Size = new Vector2(1.4f, 1),
                    Blending = BlendingParameters.Additive,
                },
                light = new Sprite
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.Centre,
                    X = 4,
                    Size = new Vector2(1, 1),
                },
            },
        };
    }

    [BackgroundDependencyLoader]
    private void load(TextureStore textures)
    {
        light.Texture = halo.Texture = textures.Get("Branding/clear-lamp")
            ?? throw new InvalidDataException("The clear lamp texture is missing.");
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        Refresh();
    }

    private void Refresh()
    {
        int request = ++generation;
        clear = 0;
        visual.Alpha = 0;
        TooltipText = default;
        lastRevision = game.ClearLamps.Revision;
        lastPlayer = game.Cloud.User?.Username ?? game.PlaySettings.Player.Value;
        lastEnabled = game.PlaySettings.ShowClearLamps.Value;
        if (lastEnabled && Beatmap is { } info && game.ChartFor(info) is { } chart)
            _ = LoadClear(chart, lastPlayer, request);
    }

    private async Task LoadClear(LazerRave.Bridge.Chart chart, string player, int request)
    {
        try
        {
            int result = await game.ClearLamps.GetClear(chart, player);
            if (IsDisposed) return;
            Schedule(() =>
            {
                if (request != generation || IsDisposed) return;
                clear = result;
                visual.Alpha = result > 0 ? 1 : 0;
                TooltipText = result > 0 ? D(result switch
                {
                    1 => "Failed", 2 => "Easy", 3 => "Normal", 4 => "Hard", 5 => "Full combo", _ => "Finished",
                }) : default;
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // An unavailable local record must not interrupt song selection.
        }
    }

    protected override void Update()
    {
        base.Update();
        if (lastRevision != game.ClearLamps.Revision || lastEnabled != game.PlaySettings.ShowClearLamps.Value
            || lastPlayer != (game.Cloud.User?.Username ?? game.PlaySettings.Player.Value))
            Refresh();
        if (clear == 0) return;

        double time = Clock.CurrentTime;
        var colour = clear switch
        {
            1 => new Color4(255, 55, 74, 255),
            2 => new Color4(95, 255, 139, 255),
            3 => new Color4(255, 219, 79, 255),
            4 => new Color4(230, 245, 255, 255),
            _ => FullComboColour(time),
        };
        float brightness;
        if (clear == 1)
        {
            double phase = time % 1400;
            brightness = phase < 160 || phase is >= 300 and < 460 ? 1 : 0.12f;
        }
        else
        {
            double period = clear == 5 ? 650 : clear == 4 ? 1500 : 2200;
            brightness = 0.55f + 0.45f * (float)((1 - Math.Cos(time * Math.Tau / period)) / 2);
        }
        light.Colour = halo.Colour = colour;
        light.Alpha = brightness;
        halo.Alpha = brightness * brightness * 0.7f;
    }

    private static readonly Color4[] fullComboColours =
    [
        Color4.White, new(100, 225, 255, 255), new(134, 132, 255, 255),
        new(255, 124, 204, 255), new(255, 213, 124, 255),
    ];

    private static Color4 FullComboColour(double time)
    {
        double frame = time % 1000 / 1000 * fullComboColours.Length;
        int index = (int)frame;
        var first = fullComboColours[index];
        var next = fullComboColours[(index + 1) % fullComboColours.Length];
        float blend = (float)(frame - index);
        return new Color4(first.R + (next.R - first.R) * blend, first.G + (next.G - first.G) * blend,
            first.B + (next.B - first.B) * blend, 1f);
    }
}
