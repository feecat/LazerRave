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
    public void DuplicateConnectionsAndMultipleRoomsAreRejected()
    {
        var rooms = new Rooms(new() { MaxRooms = 1 }); var host = Player(); rooms.Connect(host.Id, "host");
        Assert.Throws<ApiError>(() => rooms.Connect(host.Id, "other"));
        rooms.Create(host, "host", "Room");
        Assert.Throws<ApiError>(() => rooms.Create(host, "host", "Another"));
        Assert.Throws<ApiError>(() => rooms.Create(Player(), "guest", "Another"));
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
}
