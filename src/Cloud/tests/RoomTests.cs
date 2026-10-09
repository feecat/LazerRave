using Cloud;
using Xunit;

namespace Cloud.Tests;

public sealed class RoomTests
{
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(double seconds) => now = now.AddSeconds(seconds);
    }
    private static PublicUser Player() => new(Guid.NewGuid(), "player", "Player", "", "", null, "player", DateTime.UtcNow);
    private static readonly RoomChart chart = new(Guid.NewGuid(), "Fixture", new string('a', 64), 7, Guid.NewGuid());

    [Fact]
    public void RoomCapacityAndHostTransfer()
    {
        var rooms = new Rooms(new()); var host = Player(); var created = rooms.Create(host, "host", "Room");
        var members = Enumerable.Range(0, 15).Select(_ => Player()).ToArray();
        foreach (var member in members) rooms.Join(created.Id, member, member.Id.ToString());
        Assert.Equal(16, rooms.Current(host.Id)!.Members.Count);
        Assert.Equal(409, Assert.Throws<ApiError>(() => rooms.Join(created.Id, Player(), "extra")).Status);
        rooms.Leave(host.Id);
        Assert.Equal(members[0].Id, rooms.Current(members[0].Id)!.HostId);
        foreach (var member in members) rooms.Leave(member.Id);
        Assert.Empty(rooms.List());
    }

    [Fact]
    public void MutationsRequireHostAndCurrentVersion()
    {
        var rooms = new Rooms(new()); var host = Player(); var guest = Player();
        var created = rooms.Create(host, "host", "Room"); var current = rooms.Join(created.Id, guest, "guest");
        Assert.Equal(403, Assert.Throws<ApiError>(() => rooms.Select(guest.Id, chart, current.Version)).Status);
        Assert.Equal(409, Assert.Throws<ApiError>(() => rooms.Select(host.Id, chart, created.Version)).Status);
        current = rooms.Select(host.Id, chart, current.Version);
        Assert.Throws<ApiError>(() => rooms.Ready(guest.Id, true, new string('b', 64), current.Version));
        current = rooms.Ready(host.Id, true, chart.Sha256, current.Version);
        Assert.Throws<ApiError>(() => rooms.Start(host.Id, current.Version));
        rooms.Select(host.Id, chart with { Id = Guid.NewGuid() }, current.Version);
        Assert.All(rooms.Current(host.Id)!.Members, member => Assert.False(member.Ready));
    }

    [Fact]
    public void CountdownAndProgressRejectStaleOrInvalidReports()
    {
        var clock = new Clock(); var rooms = new Rooms(new(), clock); var host = Player();
        var current = rooms.Create(host, "host", "Room");
        current = rooms.Select(host.Id, chart, current.Version); current = rooms.Ready(host.Id, true, chart.Sha256, current.Version);
        var pending = rooms.Start(host.Id, current.Version);
        Assert.Throws<ApiError>(() => rooms.Progress(host.Id, new(pending.MatchId!.Value, 1, 10, 3, 0, .1), false));
        current = rooms.ConfirmStart(current.Id, pending.MatchId!.Value);
        Assert.Equal("countdown", current.State); clock.Advance(3.1);
        current = rooms.Progress(host.Id, new(current.MatchId!.Value, 1, 10, 3, 0, .1), false);
        Assert.Equal("playing", current.State);
        Assert.Throws<ApiError>(() => rooms.Progress(host.Id, new(current.MatchId!.Value, 1, 10, 3, 0, .1), false));
        Assert.Throws<ApiError>(() => rooms.Progress(host.Id, new(Guid.NewGuid(), 2, 20, 4, 0, .2), false));
        clock.Advance(.1);
        Assert.Throws<ApiError>(() => rooms.Progress(host.Id, new(current.MatchId!.Value, 2, 9, 4, 0, .2), false));
        Assert.Throws<ApiError>(() => rooms.Progress(host.Id, new(current.MatchId!.Value, 2, 20, 4, 0, double.NaN), false));
        current = rooms.Progress(host.Id, new(current.MatchId!.Value, 2, 20, 4, 0, 1), true);
        Assert.Equal("results", current.State);
        Assert.Equal("finished", rooms.PeekCompleted()!.State);
        rooms.AckCompleted(current.MatchId!.Value); Assert.Null(rooms.PeekCompleted());
        Assert.Throws<ApiError>(() => rooms.Progress(host.Id, new(current.MatchId.Value, 3, 30, 4, 0, 1), true));
    }

    [Fact]
    public void DisconnectCancelsCountdownAndInvalidatesReadiness()
    {
        var rooms = new Rooms(new()); var host = Player(); var guest = Player();
        rooms.Connect(host.Id, "host"); rooms.Connect(guest.Id, "guest");
        var current = rooms.Create(host, "host", "Room"); current = rooms.Join(current.Id, guest, "guest");
        current = rooms.Select(host.Id, chart, current.Version); current = rooms.Ready(host.Id, true, chart.Sha256, current.Version);
        current = rooms.Ready(guest.Id, true, chart.Sha256, current.Version);
        rooms.Start(host.Id, current.Version);
        rooms.Disconnect(host.Id, "host");
        Assert.Equal("results", rooms.Current(guest.Id)!.State); Assert.False(rooms.Current(guest.Id)!.Members[0].Ready);
        Assert.Equal("cancelled", rooms.PeekCompleted()!.State);
    }
    [Fact]
    public void ConnectionAndRoomLimitsAreEnforced()
    {
        var rooms = new Rooms(new() { MaxRooms = 1 }); var host = Player(); rooms.Connect(host.Id, "host");
        for (int i = 0; i < 3; i++) rooms.Connect(host.Id, "other" + i);
        Assert.Equal(409, Assert.Throws<ApiError>(() => rooms.Connect(host.Id, "extra")).Status);
        rooms.Disconnect(host.Id, "unknown");
        Assert.Throws<ApiError>(() => rooms.Connect(host.Id, "extra"));
        rooms.Disconnect(host.Id, "other0"); rooms.Connect(host.Id, "extra");
        rooms.Create(host, "host", "Room");
        Assert.Throws<ApiError>(() => rooms.Create(host, "host", "Another"));
        Assert.Throws<ApiError>(() => rooms.Create(Player(), "guest", "Another"));
    }

    [Fact]
    public void WebsiteDisconnectDoesNotRemoveDesktopRoomMembership()
    {
        var rooms = new Rooms(new()); var player = Player();
        rooms.Connect(player.Id, "website"); rooms.Connect(player.Id, "desktop");
        var room = rooms.Create(player, "desktop", "Room");
        rooms.Join(room.Id, player, "website");
        Assert.Single(rooms.Current(player.Id)!.Members);
        rooms.Disconnect(player.Id, "website");
        Assert.Equal(room.Id, rooms.Current(player.Id)!.Id);
        rooms.Connect(player.Id, "website2");
        rooms.Disconnect(player.Id, "desktop");
        Assert.Null(rooms.Current(player.Id)); Assert.Empty(rooms.List());
    }

    [Fact]
    public void GlobalConnectionLimitCountsAllTabsAndFreesDisconnectedSlots()
    {
        var rooms = new Rooms(new()); var users = Enumerable.Range(0, 20).Select(_ => Player()).ToArray();
        foreach (var user in users) for (int i = 0; i < 4; i++) rooms.Connect(user.Id, i.ToString());
        var extra = Player();
        Assert.Equal(429, Assert.Throws<ApiError>(() => rooms.Connect(extra.Id, "extra")).Status);
        rooms.Disconnect(users[0].Id, "0"); rooms.Connect(extra.Id, "extra");
        Assert.Throws<ApiError>(() => rooms.Connect(extra.Id, "extra2"));
    }

    [Fact]
    public void FinishedRoundRetainsDisconnectedPlayers()
    {
        var clock = new Clock(); var rooms = new Rooms(new(), clock); var host = Player(); var guest = Player();
        var current = rooms.Create(host, "host", "Room"); current = rooms.Join(current.Id, guest, "guest");
        current = rooms.Select(host.Id, chart, current.Version); current = rooms.Ready(host.Id, true, chart.Sha256, current.Version);
        current = rooms.Ready(guest.Id, true, chart.Sha256, current.Version); current = rooms.Start(host.Id, current.Version);
        current = rooms.ConfirmStart(current.Id, current.MatchId!.Value); clock.Advance(3.1);
        rooms.Progress(guest.Id, new(current.MatchId!.Value, 1, 10, 3, 0, .1), false);
        rooms.Leave(guest.Id);
        rooms.Progress(host.Id, new(current.MatchId.Value, 1, 20, 4, 0, 1), true);
        var results = rooms.PeekCompleted()!.Results;
        Assert.Equal(2, results.Count);
        Assert.True(results.Single(member => member.Id == guest.Id).Disconnected);
        Assert.Equal(10, results.Single(member => member.Id == guest.Id).ExScore);
    }
    [Fact]
    public void LocalSongRequiresMatchingResourcePresenceAndStableSelection()
    {
        var rooms = new Rooms(new()); var host = Player(); var guest = Player();
        var current = rooms.Create(host, "host", "Room"); current = rooms.Join(current.Id, guest, "guest");
        var local = chart with { PackId = null, ContentSha256 = new string('b', 64) };
        current = rooms.Select(host.Id, local, current.Version); var selection = current.SelectionId;
        Assert.Throws<ApiError>(() => rooms.Ready(guest.Id, true, local.Sha256, current.Version));
        Assert.Throws<ApiError>(() => rooms.RequireSelection(guest.Id, current.Id, selection, local.ContentSha256, true));
        Assert.Throws<ApiError>(() => rooms.ContentPresence(guest.Id, selection, new string('c', 64), "available"));
        current = rooms.ContentPresence(guest.Id, selection, local.ContentSha256!, "available");
        Assert.Equal(selection, current.SelectionId);
        current = rooms.Ready(guest.Id, true, local.Sha256, current.Version);
        var share = Guid.NewGuid();
        current = rooms.AttachContent(host.Id, current.Id, selection, local.ContentSha256!, share, DateTime.UtcNow.AddHours(2));
        Assert.Equal(share, current.Chart!.ShareId); Assert.True(current.Members.Single(m => m.Id == guest.Id).Ready);
        current = rooms.Select(host.Id, local, current.Version);
        Assert.NotEqual(selection, current.SelectionId);
        Assert.Throws<ApiError>(() => rooms.AttachContent(host.Id, current.Id, selection, local.ContentSha256!, share, DateTime.UtcNow.AddHours(2)));
    }
}
