using LazerRave.Bridge;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osuTK;
using osuTK.Graphics;
using Tomlyn;
using Tomlyn.Model;

namespace LazerRave.Lazer;

internal partial class LazerRaveCloudPanel(LazerRaveGame game, CloudClient client) : LoginOverlay
{
    [Cached] private readonly OverlayColourProvider colours = new(OverlayColourScheme.Blue);
    private readonly Bindable<string> endpoint = new("https://lazerrave.com");
    private readonly Bindable<string> username = new("");
    private readonly Bindable<string> password = new("");
    private FillFlowContainer body = null!;
    private OsuSpriteText status = null!;
    private Box progress = null!;
    private int dirty = 1;
    private string signature = "", error = "";
    private bool working;
    private readonly CancellationTokenSource lifetime = new();
    public void OpenLobby()
    {
        enterMultiplayer = true; Show();
        if (client.User is not null && !client.Connected) _ = Run(ConnectAndEnter);
    }
    protected override void LoadContent()
    {
        if (File.Exists(ApplicationPaths.Settings))
        {
            var saved = Toml.ToModel(File.ReadAllText(ApplicationPaths.Settings));
            if (saved.TryGetValue("cloud", out var value) && value is TomlTable cloud)
            {
                if (cloud.TryGetValue("server", out var server) && server is string text) endpoint.Value = text;
                if (cloud.TryGetValue("username", out var name) && name is string player) username.Value = player;
            }
        }
        AutoSizeAxes = Axes.None;
        RelativeSizeAxes = Axes.Both;
        Anchor = Origin = Anchor.TopLeft;
        EdgeEffect = default;
        Masking = false;
        Children = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(0, 0, 0, 190) },
            new Container
            {
                Anchor = Anchor.Centre, Origin = Anchor.Centre, Width = 660, RelativeSizeAxes = Axes.Y, Height = .84f,
                Masking = true, CornerRadius = 14,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = colours.Background5 },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both, Padding = new MarginPadding(24),
                        Children = new Drawable[]
                        {
                            Label("LazerRave", 28),
                            new RoundedButton { Text = "Back", Anchor = Anchor.TopRight, Origin = Anchor.TopRight, Size = new Vector2(86, 34), Action = Hide },
                            status = new OsuSpriteText { Y = 48, RelativeSizeAxes = Axes.X, Font = OsuFont.GetFont(size: 15), Text = "" },
                            new Container { Y = 82, RelativeSizeAxes = Axes.X, Height = 5, Masking = true, CornerRadius = 2,
                                Children = new Drawable[] { new Box { RelativeSizeAxes = Axes.Both, Colour = colours.Background3 }, progress = new Box { RelativeSizeAxes = Axes.Both, Width = 0, Colour = Color4.Cyan } } },
                            new Container { RelativeSizeAxes = Axes.Both, Padding = new MarginPadding { Top = 108 }, Child = new OsuScrollContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                Child = body = new FillFlowContainer { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical, Spacing = new Vector2(0, 12), Padding = new MarginPadding { Right = 12, Bottom = 16 } },
                            } },
                        },
                    },
                },
            },
        };
        client.Changed += MarkDirty;
    }
    private void MarkDirty() => Interlocked.Exchange(ref dirty, 1);
    private static OsuSpriteText Label(string text, float size = 18) => new() { Text = text, Font = OsuFont.GetFont(size: size) };
    private void Button(string text, Func<Task> action, bool enabled = true)
    {
        var button = new RoundedButton { Text = text, RelativeSizeAxes = Axes.X, Height = 40, Action = () => _ = Run(action) };
        button.Enabled.Value = enabled && !working;
        body.Add(button);
    }
    private async Task Run(Func<Task> action)
    {
        if (working) return;
        working = true; error = ""; MarkDirty();
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception failure) { error = failure.Message; }
        finally { working = false; MarkDirty(); }
    }
    private void SaveConnection()
    {
        var path = ApplicationPaths.Settings;
        var table = File.Exists(path) ? Toml.ToModel(File.ReadAllText(path)) : new TomlTable();
        table["cloud"] = new TomlTable { ["server"] = endpoint.Value.Trim(), ["username"] = username.Value.Trim() };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".cloud.tmp";
        try { File.WriteAllText(temporary, Toml.FromModel(table)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private bool enterMultiplayer;
    private async Task ConnectAndEnter()
    {
        await client.Connect(lifetime.Token);
        EnterAfterLogin();
    }
    private async Task RestoreAndEnter()
    {
        await client.Restore(lifetime.Token);
        if (client.User is { } user && client.Server is { } server)
            Schedule(() => { endpoint.Value = server.AbsoluteUri; username.Value = user.Username; });
        EnterAfterLogin();
    }
    private void EnterAfterLogin() => Schedule(() =>
    {
        if (!enterMultiplayer || !client.Connected) return;
        enterMultiplayer = false; Hide(); game.OpenMultiplayer();
    });
    private void BuildBody()
    {
        body.Clear();
        if (client.User is null)
        {
            if (client.HasSavedSession) Button("Retry saved sign-in", RestoreAndEnter);
            body.Add(new FormTextBox { Caption = "Server", Current = endpoint, RelativeSizeAxes = Axes.X });
            body.Add(new FormTextBox { Caption = "Username or email", Current = username, RelativeSizeAxes = Axes.X });
            body.Add(new FormPasswordTextBox { Caption = "Password", Current = password, RelativeSizeAxes = Axes.X });
            Button("Sign in", async () =>
            {
                await client.Login(endpoint.Value, username.Value, password.Value, lifetime.Token);
                password.Value = ""; SaveConnection(); EnterAfterLogin();
            });
            Button("Create an account on the website", () => { game.OpenCloudWebsite(new Uri(CloudClient.ServerUri(endpoint.Value), "register").ToString()); return Task.CompletedTask; });
            return;
        }
        body.Add(Label(client.User.DisplayName, 23));
        body.Add(Label("UID " + client.User.Uid, 16));
        Button("Profile on the website", () => { game.OpenCloudWebsite(new Uri(CloudClient.ServerUri(endpoint.Value), "players/" + client.User.Uid).ToString()); return Task.CompletedTask; });
        Button("Change password on the website", () => { game.OpenCloudWebsite(new Uri(CloudClient.ServerUri(endpoint.Value), "account/password").ToString()); return Task.CompletedTask; });
        Button(client.Connected ? "Enter multiplayer" : "Connect multiplayer", async () =>
        {
            enterMultiplayer = true;
            if (!client.Connected) await ConnectAndEnter(); else EnterAfterLogin();
        });
        Button("Sign out", client.Disconnect);
    }
    protected override void LoadComplete()
    {
        base.LoadComplete();
        username.BindValueChanged(_ => MarkDirty()); password.BindValueChanged(_ => MarkDirty());
        _ = Run(RestoreAndEnter);
    }
    protected override void Update()
    {
        base.Update();
        if (Interlocked.Exchange(ref dirty, 0) == 0) return;
        var p = client.Progress;
        status.Text = error.Length > 0 ? error : p is null ? client.Status : $"{p.Stage} · {p.Fraction:P0} · {p.Completed / 1048576d:F1}/{p.Total / 1048576d:F1} MiB" +
            (p.BytesPerSecond > 0 ? $" · {p.BytesPerSecond / 1048576d:F1} MiB/s · {TimeSpan.FromSeconds(Math.Max(0, (p.Total - p.Completed) / p.BytesPerSecond)):mm\\:ss} remaining" : "");
        status.Colour = error.Length > 0 ? Color4.OrangeRed : Color4.White;
        progress.Width = (float)(p?.Fraction ?? 0);
        var next = $"{client.Connected}/{client.User}/{working}";
        if (next != signature) { signature = next; BuildBody(); }
    }
    protected override void PopIn() { MarkDirty(); this.FadeIn(180); }
    protected override void PopOut() { enterMultiplayer = false; this.FadeOut(180); }
    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing) { client.Changed -= MarkDirty; lifetime.Cancel(); }
        base.Dispose(isDisposing);
    }
}
