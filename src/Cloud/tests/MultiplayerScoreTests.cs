using Cloud;
using LazerRave.Bridge;
using Xunit;

namespace Cloud.Tests;

public sealed class MultiplayerScoreTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static PublicUser Player() => new(Guid.NewGuid(), "player", "Player", "", "", null, "player", DateTime.UtcNow);
    [Fact]
    public void HostTransferPreservesChartAndReadyAndRejectsGuestAndActiveRound()
    {
        var rooms = new Rooms(new()); var host = Player(); var guest = Player();
        var room = rooms.Create(host, "host", "Room"); room = rooms.Join(room.Id, guest, "guest");
        var chart = new RoomChart(Guid.NewGuid(), "BMS", new string('a', 64), 7, null);
        room = rooms.Select(host.Id, chart, room.Version);
        room = rooms.ContentPresence(host.Id, room.SelectionId, chart.Sha256, "available");
        room = rooms.ContentPresence(guest.Id, room.SelectionId, chart.Sha256, "available");
        room = rooms.Ready(host.Id, true, chart.Sha256, room.Version);
        room = rooms.Ready(guest.Id, true, chart.Sha256, room.Version);
        Assert.Equal(403, Assert.Throws<ApiError>(() => rooms.TransferHost(guest.Id, host.Id, room.Version)).Status);
        Assert.Equal(400, Assert.Throws<ApiError>(() => rooms.TransferHost(host.Id, Guid.NewGuid(), room.Version)).Status);
        Assert.Equal(409, Assert.Throws<ApiError>(() => rooms.TransferHost(host.Id, guest.Id, room.Version - 1)).Status);
        var selection = room.SelectionId;
        room = rooms.TransferHost(host.Id, guest.Id, room.Version);
        Assert.Equal(guest.Id, room.HostId); Assert.Equal(selection, room.SelectionId);
        Assert.All(room.Members, member => Assert.True(member.Ready));
        room = rooms.Start(guest.Id, room.Version);
        Assert.Equal(409, Assert.Throws<ApiError>(() => rooms.TransferHost(guest.Id, host.Id, room.Version)).Status);
    }
    [Fact]
    public void AbortedPlayerRetainsActualScoreAndProgressInResults()
    {
        var clock = new Clock(); var rooms = new Rooms(new(), clock); var player = Player();
        var room = rooms.Create(player, "desktop", "Room");
        room = rooms.Select(player.Id, new(Guid.NewGuid(), "BMS", new string('a', 64), 7, Guid.NewGuid()), room.Version);
        room = rooms.Ready(player.Id, true, room.Chart!.Sha256, room.Version);
        room = rooms.Start(player.Id, room.Version); room = rooms.ConfirmStart(room.Id, room.MatchId!.Value);
        clock.Now = clock.Now.AddSeconds(4);
        room = rooms.Progress(player.Id, new(room.MatchId!.Value, 1, 125, 0, 3, .25, 45, 1, true), true);
        Assert.Equal("results", room.State);
        var result = Assert.Single(room.Results!);
        Assert.True(result.Aborted); Assert.True(result.Finished);
        Assert.Equal(125, result.ExScore); Assert.Equal(45, result.MaxCombo); Assert.Equal(.25, result.Progress);
        Assert.Equal(result, Assert.Single(rooms.PeekCompleted()!.Results));
        var previous = room.MatchId;
        room = rooms.Ready(player.Id, true, room.Chart!.Sha256, room.Version);
        room = rooms.Start(player.Id, room.Version);
        Assert.NotEqual(previous, room.MatchId); Assert.Empty(room.Results!.Where(member => member.Finished));
        Assert.All(room.Members, member => { Assert.Equal(0, member.ExScore); Assert.Equal(0, member.MaxCombo); Assert.False(member.Aborted); });
    }
    [Fact]
    public void ReadsNativeScoreProtocolAndRejectsInvalidOrForeignPayload()
    {
        var path = Path.GetTempFileName();
        try
        {
            const string snapshot = "<score protocol=\"LAZERRAVE_SCORE_STREAM_V1\" ex-score=\"888\" combo=\"10\" max-combo=\"44\" misses=\"3\" progress=\"1\" clear-type=\"4\" finished=\"1\" aborted=\"0\" />";
            File.WriteAllText(path, snapshot);
            Assert.Equal(new GameplaySnapshot(888, 10, 44, 3, 1, 4, true, false), GameplaySnapshot.Read(path));
            File.WriteAllText(path, snapshot.Replace("progress=\"1\"", "progress=\"NaN\""));
            Assert.Throws<InvalidDataException>(() => GameplaySnapshot.Read(path));
            File.WriteAllText(path, snapshot.Replace("LAZERRAVE_SCORE_STREAM_V1", "OTHER"));
            Assert.Throws<InvalidDataException>(() => GameplaySnapshot.Read(path));
        }
        finally { File.Delete(path); }
    }
}
