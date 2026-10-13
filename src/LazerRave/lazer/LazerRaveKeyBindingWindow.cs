using LazerRave.Bridge;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Input.Bindings;
using osu.Game.Overlays;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;
using static LazerRave.Lazer.LazerRaveText;

namespace LazerRave.Lazer;

internal partial class LazerRaveKeyBindingWindow(NativeKeyBindings bindings) : OsuFocusedOverlayContainer
{
    [Cached] private readonly OverlayColourProvider colours = new(OverlayColourScheme.Blue);
    private readonly Dictionary<int, KeyBindingDraft> drafts = new();
    private readonly Bindable<string> mode = new("7Key / 14Key");
    private FillFlowContainer rows = null!;
    private OsuSpriteText status = null!;
    private RoundedButton firstPlayer = null!, secondPlayer = null!;
    private int keys = 7, player;
    private int? captureRow;
    private bool append;
    private KeyBindingDraft Current => drafts[keys];
    protected override bool DimMainContent => false;

    public void Open()
    {
        drafts.Clear(); captureRow = null; player = 0;
        keys = mode.Value.StartsWith("5") ? 5 : mode.Value.StartsWith("9") ? 9 : 7;
        TryLoad();
        Show();
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        RelativeSizeAxes = Axes.Both;
        InternalChildren = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(0, 0, 0, 190) },
            new Container
            {
                Anchor = Anchor.Centre, Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both, Size = new Vector2(.86f, .9f),
                Masking = true, CornerRadius = 12,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = colours.Background5 },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both, Padding = new MarginPadding(24),
                        Children = new Drawable[]
                        {
                            Label(D("LR2 key bindings"), 26),
                            new GridContainer
                            {
                                Y = 46, RelativeSizeAxes = Axes.X, Height = 38, Depth = -10,
                                ColumnDimensions = [new Dimension(), new Dimension(GridSizeMode.Absolute, 100), new Dimension(GridSizeMode.Absolute, 100)],
                                Content = new Drawable?[][] { [
                                    new OsuDropdown<string> { RelativeSizeAxes = Axes.X, Items = new[] { "7Key / 14Key", "5Key / 10Key", "9Key" }, Current = mode },
                                    firstPlayer = new BindingButton { Text = "1P", Action = () => SetPlayer(0) },
                                    secondPlayer = new BindingButton { Text = "2P", Action = () => SetPlayer(1) },
                                ] },
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both, Padding = new MarginPadding { Top = 100, Bottom = 96 },
                                Child = new OsuScrollContainer
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Child = new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical,
                                        Spacing = new Vector2(0, 8), Padding = new MarginPadding { Right = 12 },
                                        Children = new Drawable[]
                                        {
                                            new OsuTextFlowContainer(text => text.Font = OsuFont.GetFont(size: 16))
                                            {
                                                RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y,
                                                Text = D("Click a binding, then press a key. Use + to add another binding; Esc cancels capture."),
                                            },
                                            rows = new FillFlowContainer { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical, Spacing = new Vector2(0, 6) },
                                        },
                                    },
                                },
                            },
                            status = new TruncatingSpriteText
                            {
                                Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft, Y = -52,
                                RelativeSizeAxes = Axes.X, Font = OsuFont.GetFont(size: 16),
                            },
                            new GridContainer
                            {
                                Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft,
                                RelativeSizeAxes = Axes.X, Height = 40,
                                ColumnDimensions = [new Dimension(), new Dimension(), new Dimension(), new Dimension()],
                                Content = new Drawable?[][] { [
                                    new BindingButton { Text = D("Restore defaults"), Action = RestoreDefaults },
                                    new BindingButton { Text = D("Reload from LR2"), Action = () => { drafts.Clear(); TryLoad(); } },
                                    new BindingButton { Text = D("Save key bindings"), Action = Save },
                                    new BindingButton { Text = D("Close"), Action = Hide },
                                ] },
                            },
                        },
                    },
                },
            },
        };
        mode.BindValueChanged(change =>
        {
            keys = change.NewValue.StartsWith("5") ? 5 : change.NewValue.StartsWith("9") ? 9 : 7;
            player = 0; TryLoad();
        });
    }

    private void TryLoad()
    {
        captureRow = null;
        try
        {
            if (!drafts.ContainsKey(keys)) drafts[keys] = bindings.Read(keys);
            Status(D("Existing LR2 bindings are loaded. Changes take effect on the next game launch."));
            RefreshRows();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException or FormatException or OverflowException)
        {
            rows.Clear(); Status(D(error.Message), true);
        }
    }

    private void SetPlayer(int value) { if (keys == 9 && value != 0) return; player = value; captureRow = null; RefreshRows(); }
    private void RefreshRows()
    {
        if (!drafts.ContainsKey(keys)) return;
        rows.Clear();
        firstPlayer.BackgroundColour = player == 0 ? colours.Colour3 : colours.Background3;
        secondPlayer.BackgroundColour = player == 1 ? colours.Colour3 : colours.Background3;
        secondPlayer.Enabled.Value = keys != 9;
        int offset = player * 20;
        foreach (int row in NativeKeyBindings.Rows(keys).Where(row => player == 0 ? row < 20 : row >= 20))
        {
            int local = row - offset;
            var title = local switch
            {
                10 => D("Scratch up"), 11 => D("Scratch down"), 12 => D("START"), 13 => D("SELECT"),
                _ => LocalisableString.Interpolate($"{D("Key")} {local}"),
            };
            rows.Add(new GridContainer
            {
                RelativeSizeAxes = Axes.X, Height = 40,
                ColumnDimensions = [new Dimension(GridSizeMode.Absolute, 140), new Dimension(), new Dimension(GridSizeMode.Absolute, 44), new Dimension(GridSizeMode.Absolute, 44)],
                Content = new Drawable?[][] { [
                    new Container { RelativeSizeAxes = Axes.Both, Child = Label(title, 18, true) },
                    new BindingButton { Text = captureRow == row ? D("Press a key…") : Display(row), TooltipText = Display(row), Action = () => Capture(row, false) },
                    new BindingButton { Text = "+", TooltipText = D("Add binding"), Action = () => Capture(row, true) },
                    new BindingButton { Text = "×", TooltipText = D("Clear keyboard bindings"), Action = () => { Current.Bindings[row] = []; captureRow = null; RefreshRows(); Status(D("Unsaved changes")); } },
                ] },
            });
        }
    }

    private string Display(int row) => Current.Bindings[row].Length == 0 ? "—" : string.Join(" / ", Current.Bindings[row].Select(NativeKeyboard.Name));
    private void Capture(int row, bool add)
    {
        captureRow = row; append = add; RefreshRows(); Status(D("Press a key. Esc cancels; Delete clears keyboard bindings."));
    }
    private void RestoreDefaults() { if (!drafts.ContainsKey(keys)) return; Current.RestoreDefaults(); captureRow = null; RefreshRows(); Status(D("Unsaved changes")); }
    private void Save()
    {
        captureRow = null;
        try
        {
            bindings.Save(drafts.Values);
            foreach (int profile in drafts.Keys.ToArray()) drafts[profile] = bindings.Read(profile);
            RefreshRows(); Status(D("Key bindings saved to LR2. Changes take effect on the next game launch."));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Xml.XmlException or FormatException or OverflowException)
        { Status(D(error.Message), true); }
    }
    protected override bool OnKeyDown(KeyDownEvent e)
    {
        if (e.Repeat) return true;
        if (captureRow is null) return base.OnKeyDown(e);
        CaptureKey(e.Key);
        return true;
    }
    private void CaptureKey(Key key)
    {
        if (captureRow is not { } row) return;
        if (key == Key.Escape) { CancelCapture(); return; }
        if (key == Key.Delete) { Current.Bindings[row] = []; CancelCapture(); return; }
        if (!NativeKeyboard.TryCode(key.ToString(), out int code)) { Status(D("This key is reserved by LR2 or is not supported."), true); return; }
        try { Current.Assign(row, code, append); captureRow = null; RefreshRows(); Status(D("Unsaved changes")); }
        catch (ArgumentException error) { Status(D(error.Message), true); }
    }
    public override bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
    {
        if (e.Action == GlobalAction.Back && !e.Repeat)
        {
            if (captureRow is not null) CancelCapture(); else Hide();
            return true;
        }
        if (captureRow is not null)
        {
            if (!e.Repeat)
            {
                var pressed = e.CurrentState.Keyboard.Keys.ToArray();
                if (pressed.Length == 1) CaptureKey(pressed[0]);
                else Status(D("Press one key at a time."), true);
            }
            return true;
        }
        return base.OnPressed(e);
    }
    private void CancelCapture()
    {
        captureRow = null; RefreshRows();
        Status(D(drafts.Values.Any(draft => draft.HasChanges) ? "Unsaved changes" : "Existing LR2 bindings are loaded. Changes take effect on the next game launch."));
    }
    private void Status(LocalisableString text, bool error = false) { status.Text = text; status.Colour = error ? Color4.OrangeRed : colours.Content1; }
    private static OsuSpriteText Label(LocalisableString text, float size, bool centred = false) => new()
    {
        Text = text, Font = OsuFont.GetFont(size: size),
        Anchor = centred ? Anchor.CentreLeft : Anchor.TopLeft, Origin = centred ? Anchor.CentreLeft : Anchor.TopLeft,
    };
    protected override void PopIn() => this.FadeIn(160);
    protected override void PopOut() { captureRow = null; base.PopOut(); this.FadeOut(120); }

    private partial class BindingButton : RoundedButton
    {
        public BindingButton()
        {
            RelativeSizeAxes = Axes.Both;
            Size = Vector2.One;
            Margin = new MarginPadding { Horizontal = 2 };
        }

        protected override SpriteText CreateText() => new TruncatingSpriteText
        {
            Anchor = Anchor.Centre, Origin = Anchor.Centre,
            Font = OsuFont.GetFont(size: 18),
        };

        protected override void Update()
        {
            base.Update();
            SpriteText.MaxWidth = Math.Max(0, DrawWidth - 16);
        }
    }
}
