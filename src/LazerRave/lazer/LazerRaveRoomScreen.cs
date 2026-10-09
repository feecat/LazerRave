// Layout adapted from osu.Game.Screens.OnlinePlay.Multiplayer.MultiplayerMatchSubScreen.
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Screens;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Online.Rooms;
using osu.Game.Overlays;
using osu.Game.Screens;
using osu.Game.Screens.Backgrounds;
using osu.Game.Screens.OnlinePlay;
using osu.Game.Screens.OnlinePlay.Lounge.Components;
using osu.Game.Screens.OnlinePlay.Match.Components;
using osu.Game.Users.Drawables;
using osuTK;
using osuTK.Graphics;

namespace LazerRave.Lazer;

internal partial class LazerRaveRoomScreen(Room model) : OnlinePlaySubScreen
{
    [Resolved] private CloudClient client { get; set; } = null!;
    [Resolved] private CloudRoomMap rooms { get; set; } = null!;
    [Resolved] private LazerRaveGame game { get; set; } = null!;
    public override string Title => "Room";
    protected override BackgroundScreen CreateBackground() => new BackgroundScreenBlack();
    private readonly Bindable<string> name = new(model.Name), message = new("");
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<Guid, CloudParticipantRow> rows = [];
    private FillFlowContainer participants = null!, messages = null!;
    private SectionHeader participantHeader = null!;
    private FormTextBox roomName = null!;
    private TruncatingSpriteText chartTitle = null!, chartDetails = null!;
    private OsuTextFlowContainer transferStatus = null!;
    private OsuSpriteText status = null!;
    private Box progress = null!;
    private OsuScrollContainer chatScroll = null!;
    private PurpleRoundedButton create = null!, choose = null!, upload = null!, download = null!, open = null!, cancel = null!, ready = null!, start = null!, send = null!;
    private int dirty = 1;
    private volatile bool working;
    private bool sending;
    private string error = "", channel = "", messageSignature = "";
    private bool leaving;
    private Guid? roomId;

    [BackgroundDependencyLoader]
    private void load()
    {
        roomId = model.RoomID is null ? null : rooms.CloudId(model);
        Padding = new MarginPadding { Top = Header.HEIGHT };
        InternalChild = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Padding = new MarginPadding { Horizontal = WaveOverlayContainer.WIDTH_PADDING, Bottom = 70 },
            Children = new Drawable[]
            {
                new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    RowDimensions = [new Dimension(GridSizeMode.Absolute, 96), new Dimension(GridSizeMode.Absolute, 10), new Dimension()],
                    Content = new Drawable?[][]
                    {
                        [new CloudRoomHeader(model) { ShowDescription = true }],
                        [null],
                        [new Container
                        {
                            RelativeSizeAxes = Axes.Both, Masking = true, CornerRadius = 10,
                            Children = new Drawable[]
                            {
                                new Box { RelativeSizeAxes = Axes.Both, Colour = Color4Extensions.FromHex("3e3a44") },
                                new GridContainer
                                {
                                    RelativeSizeAxes = Axes.Both, Padding = new MarginPadding(20) { Top = 10 },
                                    ColumnDimensions = [new Dimension(), new Dimension(GridSizeMode.Absolute, 10), new Dimension(), new Dimension(GridSizeMode.Absolute, 10), new Dimension()],
                                    Content = new Drawable?[][]
                                    {
                                        [new GridContainer
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            RowDimensions = [new Dimension(GridSizeMode.AutoSize), new Dimension()],
                                            Content = new Drawable?[][]
                                            {
                                                [participantHeader = new SectionHeader("Participants")],
                                                [new OsuScrollContainer { RelativeSizeAxes = Axes.Both, Child = participants = new FillFlowContainer
                                                { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical, Spacing = new Vector2(0, 1) } }],
                                            },
                                        }, null,
                                        new OsuScrollContainer
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Child = new FillFlowContainer
                                            {
                                                RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical, Spacing = new Vector2(0, 10), Padding = new MarginPadding { Right = 8, Bottom = 16 },
                                                Children = new Drawable[]
                                                {
                                                    new SectionHeader("Song"),
                                                    roomName = new FormTextBox { Caption = "Room name", Current = name, RelativeSizeAxes = Axes.X },
                                                    create = Button("Create room", async () => { await client.CreateRoom(name.Value, lifetime.Token); roomId = client.Room!.Id; }),
                                                    chartTitle = new TruncatingSpriteText { RelativeSizeAxes = Axes.X, Font = OsuFont.GetFont(size: 23) },
                                                    chartDetails = new TruncatingSpriteText { RelativeSizeAxes = Axes.X, Font = OsuFont.GetFont(size: 16) },
                                                    choose = Button("Choose song", () => { game.OpenRoomLibrary(); return Task.CompletedTask; }),
                                                    new SectionHeader("Song files"),
                                                    transferStatus = new OsuTextFlowContainer(text => text.Font = OsuFont.GetFont(size: 15)) { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y },
                                                    new Container { RelativeSizeAxes = Axes.X, Height = 5, Children = new Drawable[]
                                                    {
                                                        new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black.Opacity(.3f) },
                                                        progress = new Box { RelativeSizeAxes = Axes.Both, Width = 0, Colour = Color4.Cyan },
                                                    } },
                                                    upload = Button("Upload and share song", () => client.Upload(lifetime.Token)),
                                                    download = Button("Download song / continue", () => client.Download(lifetime.Token)),
                                                    open = Button("Open selected song", () => { game.OpenSharedChart(client.AvailableChart!); return Task.CompletedTask; }),
                                                    cancel = new PurpleRoundedButton { Text = "Cancel transfer", RelativeSizeAxes = Axes.X, Height = 40, Action = client.CancelTransfer },
                                                },
                                            },
                                        }, null,
                                        new GridContainer
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            RowDimensions = [new Dimension(GridSizeMode.AutoSize), new Dimension(), new Dimension(GridSizeMode.AutoSize), new Dimension(GridSizeMode.Absolute, 40)],
                                            Content = new Drawable?[][]
                                            {
                                                [new SectionHeader("Chat")],
                                                [chatScroll = new OsuScrollContainer { RelativeSizeAxes = Axes.Both, Child = messages = new FillFlowContainer
                                                { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical, Spacing = new Vector2(0, 8), Padding = new MarginPadding { Right = 8, Bottom = 10 } } }],
                                                [CreateChatInput()],
                                                [send = new PurpleRoundedButton { Text = "Send", RelativeSizeAxes = Axes.X, Height = 40, Action = () => _ = Send() }],
                                            },
                                        }],
                                    },
                                },
                            },
                        }],
                    },
                },
                new Container
                {
                    Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft, Y = 60, RelativeSizeAxes = Axes.X, Height = 50,
                    Children = new Drawable[]
                    {
                        new PurpleRoundedButton { Text = "Leave room", Size = new Vector2(150, 50), Action = RequestLeave },
                        status = new TruncatingSpriteText { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, X = 170, RelativeSizeAxes = Axes.X, Width = .6f, Font = OsuFont.GetFont(size: 15) },
                        ready = new PurpleRoundedButton { Text = "Ready", Anchor = Anchor.TopRight, Origin = Anchor.TopRight, Size = new Vector2(150, 50), Action = () => _ = Run(() => client.Ready(lifetime.Token)) },
                        start = new PurpleRoundedButton { Text = "Start game", Anchor = Anchor.TopRight, Origin = Anchor.TopRight, X = -166, Size = new Vector2(170, 50), Action = () => _ = Run(() => client.StartRound(lifetime.Token)) },
                    },
                },
            },
        };
    }
    private PurpleRoundedButton Button(string text, Func<Task> action) => new() { Text = text, RelativeSizeAxes = Axes.X, Height = 40, Action = () => _ = Run(action) };
    private void Changed() => Interlocked.Exchange(ref dirty, 1);
    private FormTextBox CreateChatInput()
    {
        var input = new FormTextBox { Caption = "Message", Current = message, RelativeSizeAxes = Axes.X };
        input.OnCommit += (textBox, enter) => { if (enter) _ = Send(); };
        return input;
    }
    private async Task LoadHistory(string value)
    {
        try { await client.LoadChat(value, lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { error = exception.Message; Changed(); }
    }
    private async Task Run(Func<Task> action)
    {
        if (working) return;
        working = true; error = ""; Changed();
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { error = exception.Message; }
        finally { working = false; Changed(); }
    }
    private async Task Send()
    {
        var text = message.Value.Trim();
        if (sending || text.Length is 0 or > 500) return;
        sending = true; Changed();
        try { await client.SendChat(channel, text, lifetime.Token); Schedule(() => message.Value = ""); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { error = exception.Message; }
        finally { sending = false; Changed(); }
    }
    protected override void LoadComplete()
    {
        base.LoadComplete(); client.Changed += Changed;
        message.BindValueChanged(_ => Changed());
    }
    protected override void Update()
    {
        base.Update();
        if (Interlocked.Exchange(ref dirty, 0) == 0)
        {
            if (error.Length == 0 && client.Room is { State: "countdown", StartAt: { } startAt }) status.Text = $"Starting in {Math.Max(0, Math.Ceiling((startAt - client.ServerNow).TotalSeconds))}…";
            return;
        }
        var room = client.Room;
        if (roomId is not null && (room is null || room.Id != roomId))
        {
            if (this.IsCurrentScreen()) this.Exit();
            return;
        }
        if (room is not null) model.CopyFrom(rooms.Convert(room, client));
        var nextChannel = room?.Id.ToString() ?? "lobby";
        if (channel != nextChannel) { channel = nextChannel; _ = LoadHistory(channel); }
        bool host = room?.HostId == client.User?.Id, available = client.AvailableChart is not null;
        roomName.Alpha = create.Alpha = room is null ? 1 : 0;
        create.Enabled.Value = client.Connected && !working;
        choose.Enabled.Value = host && !working && room?.State is "lobby" or "results";
        chartTitle.Text = room?.Chart?.Title ?? "Choose a song";
        chartDetails.Text = room?.Chart is { } chart ? $"{chart.Keys}Key · {(available ? "Available" : "Missing BMS chart")}" : "";
        var p = client.Progress;
        transferStatus.Text = p is null ? room?.Chart?.ExpiresAt is { } expiry ? "Share expires at " + expiry.ToLocalTime().ToString("HH:mm") : "" :
            $"{p.Stage} · {p.Fraction:P0}\n{p.Completed / 1048576d:F1} / {p.Total / 1048576d:F1} MiB" + (p.BytesPerSecond > 0 ? $" · {p.BytesPerSecond / 1048576d:F1} MiB/s" : "");
        progress.Width = (float)(p?.Fraction ?? 0);
        upload.Enabled.Value = host && available && !working && !client.Busy && room?.Chart is { } selected &&
            room.Members.Any(member => member.ContentState != "available") && (selected.ExpiresAt is null || selected.ExpiresAt <= DateTime.UtcNow);
        download.Enabled.Value = !available && !working && !client.Busy && room?.Chart is { ShareId: not null } downloadable && downloadable.ExpiresAt > DateTime.UtcNow;
        open.Enabled.Value = available && !working;
        cancel.Alpha = client.Busy ? 1 : 0; cancel.Enabled.Value = client.Busy;
        var me = room?.Members.FirstOrDefault(member => member.Id == client.User?.Id);
        ready.Text = me?.Ready == true ? "Not ready" : "Ready";
        ready.Enabled.Value = available && me?.ContentState == "available" && !working && !client.Busy && room?.State is "lobby" or "results";
        start.Alpha = host ? 1 : 0;
        start.Enabled.Value = !working && client.CanStartRound;
        send.Enabled.Value = client.Connected && !sending && message.Value.Trim().Length is > 0 and <= 500;
        status.Text = error.Length > 0 ? error : !client.Connected ? client.Status : room is null ? "New room" : room.State == "lobby" && room.Chart is not null
            ? $"{room.Members.Count(member => member.Ready)} / {room.Members.Length} ready · {client.Status}" : $"{room.State} · {client.Status}";
        status.Colour = error.Length > 0 ? Color4.OrangeRed : Color4.White;
        participantHeader.DetailsText.Value = $"{room?.Members.Length ?? 0} / 16";
        var members = room?.Members ?? [];
        foreach (var removed in rows.Keys.Except(members.Select(member => member.Id)).ToArray()) { participants.Remove(rows[removed], true); rows.Remove(removed); }
        foreach (var member in members.OrderByDescending(member => member.Id == room!.HostId))
        {
            if (!rows.TryGetValue(member.Id, out var row)) { rows[member.Id] = row = new CloudParticipantRow(); participants.Add(row); }
            row.Set(member, room!.HostId, rooms.User(member, client), host && member.Id != client.User?.Id && room.State is "lobby" or "results", () => _ = Run(() => client.TransferHost(member.Id, lifetime.Token)));
            participants.SetLayoutPosition(row, member.Id == room.HostId ? -1 : Array.FindIndex(members, value => value.Id == member.Id));
        }
        var chat = client.Messages.Where(value => value.Channel == channel).ToArray();
        var signature = string.Join(',', chat.Select(value => value.Id));
        if (signature == messageSignature) return;
        messageSignature = signature; messages.Clear();
        foreach (var entry in chat)
            messages.Add(new OsuTextFlowContainer(text => text.Font = OsuFont.GetFont(size: 15))
            { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Text = $"{entry.DisplayName} · {entry.CreatedAt.ToLocalTime():HH:mm}\n{entry.Text}" });
        Schedule(() => chatScroll.ScrollToEnd());
    }
    public override void OnResuming(ScreenTransitionEvent e) { base.OnResuming(e); Changed(); }
    private void RequestLeave()
    {
        if (working || client.Busy) { error = "Cancel the current transfer before leaving."; Changed(); return; }
        _ = Run(async () =>
        {
            if (client.Room is not null) await client.LeaveRoom(lifetime.Token);
            leaving = true;
            Schedule(() => { if (this.IsCurrentScreen()) this.Exit(); });
        });
    }
    public override bool OnExiting(ScreenExitEvent e)
    {
        if (!leaving && roomId is not null && client.Room?.Id == roomId) { RequestLeave(); return true; }
        return base.OnExiting(e);
    }
    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing) { client.Changed -= Changed; lifetime.Cancel(); }
        base.Dispose(isDisposing);
    }
}

internal partial class CloudRoomHeader : RoomPanel
{
    public CloudRoomHeader(Room room) : base(room) { ShowExternalLink = false; ShowUserProfiles = false; ShowBeatmapStatus = false; }
    public override MenuItem[] ContextMenuItems => [];
}

// Participant row keeps the crown, avatar, name and state columns from ParticipantPanel.
internal partial class CloudParticipantRow : CompositeDrawable
{
    private SpriteIcon crown = null!;
    private UpdateableAvatar avatar = null!;
    private TruncatingSpriteText name = null!;
    private OsuSpriteText state = null!;
    private CloudMember? previous;
    private IconButton transferHost = null!;
    public CloudParticipantRow() { RelativeSizeAxes = Axes.X; Height = 40; }
    [BackgroundDependencyLoader]
    private void load() => InternalChildren = new Drawable[]
    {
        new Box { RelativeSizeAxes = Axes.Both, Colour = Color4Extensions.FromHex("33413c") },
        new GridContainer
        {
            RelativeSizeAxes = Axes.Both, Padding = new MarginPadding { Horizontal = 5 },
            ColumnDimensions = [new Dimension(GridSizeMode.Absolute, 18), new Dimension(GridSizeMode.Absolute, 34), new Dimension(), new Dimension(GridSizeMode.AutoSize), new Dimension(GridSizeMode.Absolute, 30)],
            Content = new Drawable?[][] { [
                crown = new SpriteIcon { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Icon = FontAwesome.Solid.Crown, Size = new Vector2(14), Colour = Color4Extensions.FromHex("f7e65d") },
                avatar = new UpdateableAvatar(isInteractive: false) { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, RelativeSizeAxes = Axes.None, Size = new Vector2(30) },
                name = new TruncatingSpriteText { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, RelativeSizeAxes = Axes.X, Font = OsuFont.GetFont(size: 16), Padding = new MarginPadding { Left = 5 } },
                state = new OsuSpriteText { Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight, Font = OsuFont.GetFont(size: 12), Padding = new MarginPadding { Horizontal = 5 } },
                transferHost = new IconButton { Anchor = Anchor.Centre, Origin = Anchor.Centre, Icon = FontAwesome.Solid.Crown, TooltipText = "Transfer host", IconColour = Color4.LightYellow, Size = new Vector2(28) },
            ] },
        },
    };
    public void Set(CloudMember member, Guid host, osu.Game.Online.API.Requests.Responses.APIUser user, bool canTransfer, Action transfer)
    {
        if (!IsLoaded) { Schedule(() => Set(member, host, user, canTransfer, transfer)); return; }
        crown.Alpha = member.Id == host ? 1 : 0;
        transferHost.Alpha = canTransfer ? 1 : 0; transferHost.Enabled.Value = canTransfer; transferHost.Action = transfer;
        name.Text = member.DisplayName;
        state.Text = member.Ready ? "Ready" : member.ContentState switch { "available" => "Idle", "missing" => "Missing", "downloading" => "Downloading", _ => "Idle" };
        state.Colour = member.Ready ? Color4.LightGreen : member.ContentState == "missing" ? Color4.Orange : Color4.White;
        if (previous?.AvatarUrl != member.AvatarUrl || previous?.Uid != member.Uid || previous?.DisplayName != member.DisplayName)
        { avatar.User = null; avatar.User = user; }
        previous = member;
    }
}
