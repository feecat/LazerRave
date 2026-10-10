using static LazerRave.Lazer.LazerRaveText;
using osu.Framework.Localisation;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Screens;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Rooms;
using osu.Game.Screens.OnlinePlay;
using osu.Game.Screens.OnlinePlay.Lounge;
using osu.Game.Screens.OnlinePlay.Lounge.Components;
using osu.Game.Screens.OnlinePlay.Match.Components;
using osuTK;
using MatchType = osu.Game.Online.Rooms.MatchType;

namespace LazerRave.Lazer;

internal partial class LazerRaveMultiplayer : OnlinePlayScreen
{
    [Cached] private readonly CloudRoomMap rooms = new();
    protected override bool RequiresOnlineAPI => false;
    [Resolved] private LocalisationManager localisation { get; set; } = null!;
    protected override string ScreenTitle => localisation?.GetLocalisedString(D("Multiplayer")) ?? "Multiplayer";
    protected override LoungeSubScreen CreateLounge() => new LazerRaveLounge();
}

internal sealed class CloudRoomMap
{
    private readonly Dictionary<Guid, long> ids = [];
    private readonly Dictionary<long, Guid> cloudIds = [];
    private readonly Dictionary<Guid, int> fallbackUsers = [];
    public Guid CloudId(Room room) => cloudIds[room.RoomID!.Value];
    public Room Convert(CloudRoom room, CloudClient client)
    {
        if (!ids.TryGetValue(room.Id, out long id))
        {
            ids[room.Id] = id = ids.Count + 1;
            cloudIds[id] = room.Id;
        }
        var users = room.Members.Select(member => User(member, client)).ToArray();
        return new Room
        {
            RoomID = id, Name = room.Name, MaxParticipants = 16, ParticipantCount = users.Length,
            Host = users.FirstOrDefault(user => user.Id == User(room.Members.First(member => member.Id == room.HostId), client).Id),
            RecentParticipants = users, Type = MatchType.HeadToHead,
            Status = room.State is "lobby" or "results" ? RoomStatus.Idle : RoomStatus.Playing,
            Description = room.Chart is { } chart ? $"{chart.Keys}Key · {chart.Title}" : "Choosing a song",
        };
    }
    public APIUser User(CloudMember member, CloudClient client)
    {
        if (!fallbackUsers.TryGetValue(member.Id, out int fallback)) fallbackUsers[member.Id] = fallback = int.MaxValue - fallbackUsers.Count;
        var user = new CloudUser(member.Id, member.Username, member.DisplayName, member.Uid > 0 ? member.Uid : fallback, member.AvatarUrl);
        return CloudIdentity.Create(user, CloudClient.ResolveAvatar(client.Server, user)?.AbsoluteUri, "Player");
    }
}

internal partial class LazerRaveLounge : LoungeSubScreen
{
    public override string ShortTitle => string.Empty;
    [Resolved] private LocalisationManager localisation { get; set; } = null!;
    [Resolved] private CloudClient client { get; set; } = null!;
    [Resolved] private CloudRoomMap rooms { get; set; } = null!;
    private FormCheckBox showPlaying = null!, showFull = null!;
    private OsuSpriteText status = null!;
    private string error = "";
    protected override LoungeListingPoller CreateListingPoller(Action<Room[]> received, IBindable<LoungeFilterCriteria?> criteria) =>
        new CloudListingPoller(Dependencies.Get<CloudClient>(), Dependencies.Get<CloudRoomMap>()) { RoomsReceived = received, Filter = { BindTarget = criteria } };
    protected override RoomListing CreateRoomListing() => new CloudRoomListing();
    protected override IEnumerable<Drawable> CreateFilterControls()
    {
        foreach (var control in base.CreateFilterControls()) yield return control;
        StatusDropdown.Items = [RoomModeFilter.Open, RoomModeFilter.Owned, RoomModeFilter.Participated];
        yield return new Container { Width = 200, AutoSizeAxes = Axes.Y, Child = showPlaying = new FormCheckBox { Caption = D("In progress"), ExtendedHeight = true } };
        yield return new Container { Width = 180, AutoSizeAxes = Axes.Y, Child = showFull = new FormCheckBox { Caption = D("Full rooms"), ExtendedHeight = true } };
        showPlaying.Current.BindValueChanged(_ => UpdateFilter()); showFull.Current.BindValueChanged(_ => UpdateFilter());
    }
    protected override LoungeFilterCriteria CreateFilterCriteria()
    {
        var criteria = base.CreateFilterCriteria();
        criteria.Full = showFull.Current.Value;
        criteria.Status = showPlaying.Current.Value ? null : RoomStatusFilter.Idle;
        return criteria;
    }
    [BackgroundDependencyLoader]
    private void load() => AddInternal(status = new OsuSpriteText
    {
        Anchor = Anchor.BottomCentre, Origin = Anchor.BottomCentre, Y = -14, Font = osu.Game.Graphics.OsuFont.GetFont(size: 16),
    });
    protected override void Update()
    {
        base.Update();
        status.Text = error.Length > 0 ? D(error) : !client.Connected ? D(client.Status) : client.Rooms.Length == 0 ? D("No rooms available. Create a room to begin.") : D("{0} rooms · {1} players", client.Rooms.Length, client.Rooms.Sum(room => room.Members.Length));
    }
    protected override OsuButton CreateNewRoomButton() => new CloudCreateRoomButton();
    protected override Room CreateNewRoom() => new() { Name = localisation.GetLocalisedString(D("{0}'s room", client.User!.DisplayName)), Description = "Choosing a song", MaxParticipants = 16, Type = MatchType.HeadToHead };
    protected override OnlinePlaySubScreen CreateRoomSubScreen(Room room) => new LazerRaveRoomScreen(room);
    protected override void JoinInternal(Room room, string? password, Action<Room> onSuccess, Action<string, Exception?> onFailure) => _ = JoinCloudRoom(room, onSuccess, onFailure);
    private async Task JoinCloudRoom(Room room, Action<Room> success, Action<string, Exception?> failure)
    {
        try { await client.JoinRoom(rooms.CloudId(room), default); Schedule(() => { error = ""; success(room); }); }
        catch (Exception exception) { Schedule(() => { error = exception.Message; failure(exception.Message, exception); }); }
    }
    public override void Close(Room room) { }
}

internal partial class CloudCreateRoomButton : CreateRoomButton
{
    [Resolved] private CloudClient client { get; set; } = null!;
    public CloudCreateRoomButton() { Text = D("Create room"); }
    protected override void Update() { base.Update(); Enabled.Value = client.Connected && client.Room is null; }
}

internal partial class CloudListingPoller(CloudClient client, CloudRoomMap rooms) : LoungeListingPoller
{
    private string signature = "";
    protected override Task Poll()
    {
        var filter = Filter.Value;
        var values = client.Rooms.Where(room =>
            (filter?.Full != false || room.Members.Length < 16) &&
            (filter?.Status != RoomStatusFilter.Idle || room.State is "lobby" or "results") &&
            (filter?.Mode != RoomModeFilter.Owned || room.HostId == client.User?.Id) &&
            (filter?.Mode != RoomModeFilter.Participated || room.Members.Any(member => member.Id == client.User?.Id)))
            .Select(room => rooms.Convert(room, client)).ToArray();
        Schedule(() => RoomsReceived(values));
        return Task.CompletedTask;
    }
    protected override void Update()
    {
        base.Update();
        var next = client.Connected + "/" + string.Join(';', client.Rooms.Select(room => $"{room.Id}:{room.Version}"));
        if (next == signature) return;
        signature = next; PollImmediately();
    }
}

internal partial class CloudRoomListing : RoomListing
{
    protected override LoungeRoomPanel CreateRoomPanel(Room room, Bindable<Room?> selection) =>
        new CloudLoungeRoomPanel(room) { SelectedRoom = selection, ShowDescription = true };
}

internal partial class CloudLoungeRoomPanel : LoungeRoomPanel
{
    protected override LocalisableString Description => D(Room.Description ?? "");
    public CloudLoungeRoomPanel(Room room) : base(room) { ShowUserProfiles = false; ShowBeatmapStatus = false; }
    public override MenuItem[] ContextMenuItems => [];
}
