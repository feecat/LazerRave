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
using osu.Game.Overlays.Dialog;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;
using static LazerRave.Lazer.LazerRaveText;

namespace LazerRave.Lazer;

internal partial class LazerRaveKeyBindingWindow(NativeKeyBindings bindings) : OsuFocusedOverlayContainer
{
    [Cached] private readonly OverlayColourProvider colours = new(OverlayColourScheme.Blue);
    private readonly Dictionary<int, KeyBindingDraft> drafts = new();
    private readonly Bindable<string> mode = new("7Key");
    private FillFlowContainer rows = null!;
    private OsuSpriteText status = null!;
    private int keys = 7;
    private int? captureRow;
    private bool append;
    private bool conflictPromptPending;
    [Resolved] private IDialogOverlay dialogs { get; set; } = null!;
    private KeyBindingDraft Current => drafts[keys];
    protected override bool DimMainContent => false;

    public void Open()
    {
        drafts.Clear(); captureRow = null;
        keys = mode.Value.StartsWith("5") ? 5 : 7;
        TryLoad();
        Show();
    }

    internal OsuDropdown<string> CreateModeDropdown() => new() { Y = 46, RelativeSizeAxes = Axes.X, Depth = -10, Items = new[] { "7Key", "5Key" }, Current = mode };

    [BackgroundDependencyLoader]
    private void load() => BuildLayout();

    internal void BuildLayout()
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
                            CreateModeDropdown(),
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
            keys = change.NewValue.StartsWith("5") ? 5 : 7;
            TryLoad();
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
            PromptSecondPlayerConflicts([Current], saveAfter: false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException or FormatException or OverflowException)
        {
            rows.Clear(); Status(D(error.Message), true);
        }
    }

    private void RefreshRows()
    {
        if (!drafts.ContainsKey(keys)) return;
        rows.Clear();
        foreach (int row in NativeKeyBindings.Rows(keys).Where(row => row < 20))
        {
            int local = row;
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
    private void RestoreDefaults()
    {
        if (!drafts.ContainsKey(keys)) return;
        Current.RestoreDefaults(firstPlayerOnly: true); captureRow = null; RefreshRows(); Status(D("Unsaved changes"));
        PromptSecondPlayerConflicts([Current], saveAfter: false);
    }
    private void Save()
    {
        captureRow = null;
        if (!PromptSecondPlayerConflicts(drafts.Values.ToArray(), saveAfter: true)) SaveConfirmed();
    }
    private bool PromptSecondPlayerConflicts(KeyBindingDraft[] candidates, bool saveAfter)
    {
        var conflicting = candidates.Where(draft => draft.SinglePlayConflicts.Length > 0).ToArray();
        if (conflicting.Length == 0) return false;
        if (conflictPromptPending) return true;
        conflictPromptPending = true;
        string names = string.Join(" / ", conflicting.SelectMany(draft => draft.SinglePlayConflicts).Distinct().Order().Select(NativeKeyboard.Name));
        dialogs.Push(new ConfirmDialog(D("Clear conflicting 2P keyboard bindings?"), () =>
        {
            conflictPromptPending = false;
            if (IsDisposed) return;
            foreach (var draft in conflicting) draft.ClearSecondPlayerKeyboardBindings();
            RefreshRows(); Status(D("Unsaved changes"));
            if (saveAfter) SaveConfirmed();
        }, () =>
        {
            conflictPromptPending = false;
            if (!IsDisposed) Status(D("2P bindings retained. Conflicts must be resolved before saving."), true);
        }) { BodyText = LocalisableString.Interpolate($"{D("LR2 merges 2P inputs into single play. Controller bindings are retained.")}\n{names}") });
        return true;
    }
    private void SaveConfirmed()
    {
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
        try { Current.Assign(row, code, append, firstPlayerOnly: true); captureRow = null; RefreshRows(); Status(D("Unsaved changes")); PromptSecondPlayerConflicts([Current], saveAfter: false); }
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
