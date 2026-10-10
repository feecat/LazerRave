using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using LazerRave.Content;

namespace Cloud;

public sealed class HubGuard(Auth auth) : IHubFilter
{
    private readonly ConcurrentDictionary<string, (long Window, int Count)> requests = new();
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocation, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (await auth.Session(Auth.Token(invocation.Context.GetHttpContext()!.Request)) is null)
        {
            invocation.Context.Abort();
            throw new HubException("Session expired. Sign in again.");
        }
        long window = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var limit = requests.AddOrUpdate(invocation.Context.ConnectionId, (window, 1), (_, current) => current.Window == window ? (window, current.Count + 1) : (window, 1));
        if (limit.Count > 30) throw new HubException("Realtime request rate exceeded.");
        try { return await next(invocation); }
        catch (ApiError error) { throw new HubException(error.Message); }
    }
    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? error, Func<HubLifetimeContext, Exception?, Task> next)
    {
        requests.TryRemove(context.Context.ConnectionId, out _);
        await next(context, error);
    }
}

[Authorize]
public sealed class RealtimeHub(Rooms rooms, Auth auth, Pg db) : Hub
{
    private Guid UserId => Auth.Id(Context.User!);
    private static readonly ConcurrentDictionary<Guid, DateTime> lastChat = new();
    private async Task<PublicUser> User() => await auth.Session(Auth.Token(Context.GetHttpContext()!.Request)) ?? throw new HubException("Session expired.");
    public override async Task OnConnectedAsync()
    {
        try { rooms.Connect(UserId, Context.ConnectionId); }
        catch (ApiError error) { Context.Abort(); throw new HubException(error.Message); }
        await Groups.AddToGroupAsync(Context.ConnectionId, "lobby");
        await base.OnConnectedAsync();
    }
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        rooms.Disconnect(UserId, Context.ConnectionId);
        lastChat.TryRemove(UserId, out _);
        await Clients.Group("lobby").SendAsync("RoomsChanged", rooms.List());
        await base.OnDisconnectedAsync(exception);
    }
    public async Task<RoomView> CreateRoom(string name)
    {
        var room = rooms.Create(await User(), Context.ConnectionId, name);
        await Groups.AddToGroupAsync(Context.ConnectionId, room.Id.ToString());
        await Broadcast(room);
        return room;
    }
    public async Task<RoomView> JoinRoom(Guid id)
    {
        var room = rooms.Join(id, await User(), Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, id.ToString());
        await Broadcast(room);
        return room;
    }
    public async Task LeaveRoom()
    {
        var id = rooms.Leave(UserId);
        if (id is not null) await Groups.RemoveFromGroupAsync(Context.ConnectionId, id.ToString()!);
        await Clients.Group("lobby").SendAsync("RoomsChanged", rooms.List());
    }
    public async Task<RoomView> SelectChart(Guid chartId, Guid packId, long version)
    {
        var rows = await db.Query("SELECT c.*,b.title,b.artist,b.difficulty,b.keys,b.level FROM charts c JOIN pack_charts pc ON pc.chart_id=c.id JOIN packs p ON p.id=pc.pack_id JOIN ir_boards b ON b.id=c.id WHERE c.id=@chart AND p.id=@pack AND p.published LIMIT 1", ("chart", chartId), ("pack", packId));
        if (rows.Count == 0) throw new ApiError(404, "Select a published chart and pack.");
        var row = rows[0];
        var room = rooms.Select(UserId, new(chartId, (string)row["title"]!, (string)row["sha256"]!, (int)row["keys"]!, packId), version);
        await Broadcast(room); return room;
    }
    public async Task<RoomView> TransferHost(Guid target, long version)
    {
        var room = rooms.TransferHost(UserId, target, version);
        await Broadcast(room); return room;
    }
    public async Task<RoomView> KickPlayer(Guid target, long version)
    {
        var kicked = rooms.Kick(UserId, target, version);
        foreach (var connection in kicked.Connections)
        {
            await Groups.RemoveFromGroupAsync(connection, kicked.Room.Id.ToString());
            await Clients.Client(connection).SendAsync("RoomKicked", kicked.Room.Id);
        }
        await Broadcast(kicked.Room);
        return kicked.Room;
    }
    public async Task<RoomView> SetReady(bool ready, string sha256, long version)
    {
        var room = rooms.Ready(UserId, ready, sha256, version);
        await Broadcast(room); return room;
    }
    public async Task<RoomView> SelectLocalChart(LocalChartInput input, long version)
    {
        if (!SongContent.IsHash(input.Sha256) || (input.ContentSha256 is not null && !SongContent.IsHash(input.ContentSha256)) ||
            input.Title?.Length is not (>= 1 and <= 200) || input.Artist?.Length > 200 || input.Keys is not (5 or 7 or 9 or 10 or 14) || input.Level is < 0 or > 999)
            throw new ApiError(400, "Invalid local chart identity.");
        var current = rooms.Current(UserId) ?? throw new ApiError(404, "Join a room first.");
        if (current.HostId != UserId) throw new ApiError(403, "Only the host may select a chart.");
        if (current.Version != version || current.State is not ("lobby" or "results")) throw new ApiError(409, "Room changed.");
        var rows = await db.Query("""
            INSERT INTO charts(id,sha256,md5,title,artist,difficulty,keys,level) VALUES(@id,@sha,@md5,@title,@artist,'',@keys,@level)
            ON CONFLICT(sha256) DO UPDATE SET sha256=EXCLUDED.sha256 RETURNING id
            """, ("id", Guid.NewGuid()), ("sha", input.Sha256), ("md5", ""), ("title", input.Title), ("artist", input.Artist ?? ""), ("keys", input.Keys), ("level", input.Level));
        var room = rooms.Select(UserId, new((Guid)rows[0]["id"]!, input.Title, input.Sha256, input.Keys, null, input.ContentSha256), version);
        await Broadcast(room); return room;
    }
    public async Task<RoomView> ReportContent(Guid selectionId, string contentSha256, string state)
    {
        var room = rooms.ContentPresence(UserId, selectionId, contentSha256, state);
        await Broadcast(room); return room;
    }
    public async Task<RoomView> StartRound(long version)
        => await Start(version, false);
    public async Task<RoomView> ForceStartRound(long version)
        => await Start(version, true);
    private async Task<RoomView> Start(long version, bool force)
    {
        var pending = rooms.Start(UserId, version, force);
        try
        {
            await db.Query("INSERT INTO matches(id,room_id,chart_id,state,started_at) VALUES(@id,@room,@chart,'countdown',now())", ("id", pending.MatchId), ("room", pending.Id), ("chart", pending.Chart!.Id));
            var room = rooms.ConfirmStart(pending.Id, pending.MatchId!.Value);
            await db.Query("UPDATE matches SET started_at=@start WHERE id=@id", ("id", room.MatchId), ("start", room.StartAt));
            await Broadcast(room); return room;
        }
        catch { rooms.CancelStart(pending.Id, pending.MatchId!.Value); throw; }
    }
    public RoomView ReportProgress(ProgressInput input) => rooms.Progress(UserId, input, false);
    public async Task<RoomView> FinishRound(ProgressInput input)
    {
        var room = rooms.Progress(UserId, input, true);
        await Broadcast(room); return room;
    }
    public object Ping(long clientTime) => new { clientTime, serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), protocol = 1 };
    public async Task SendChat(string channel, string text)
    {
        text = text?.Trim() ?? "";
        if (text.Length is < 1 or > 500 || text.Any(c => char.IsControl(c) && c != '\n')) throw new ApiError(400, "Message must contain 1–500 characters.");
        string group;
        if (channel == "lobby") group = channel;
        else
        {
            var room = rooms.Current(UserId);
            if (room is null || channel != room.Id.ToString()) throw new ApiError(403, "Join the room before sending messages.");
            group = channel;
        }
        if (lastChat.TryGetValue(UserId, out var previous) && DateTime.UtcNow - previous < TimeSpan.FromSeconds(1)) throw new ApiError(429, "Wait a second between messages.");
        lastChat[UserId] = DateTime.UtcNow;
        var rows = await db.Query("INSERT INTO chat_messages(user_id,channel,text) VALUES(@user,@channel,@text) RETURNING id,created_at", ("user", UserId), ("channel", channel), ("text", text));
        var user = await User();
        await Clients.Group(group).SendAsync("ChatMessage", new { id = rows[0]["id"], channel, text, userId = user.Id, user.Username, user.DisplayName, createdAt = rows[0]["createdAt"] });
    }
    private async Task Broadcast(RoomView room)
    {
        await Clients.Group(room.Id.ToString()).SendAsync("RoomUpdated", room);
        await Clients.Group("lobby").SendAsync("RoomsChanged", rooms.List());
    }
}

public sealed record LocalChartInput(string Sha256, string? ContentSha256, string Title, string? Artist, int Keys, int Level);

public sealed class RoomTicker(Rooms rooms, IHubContext<RealtimeHub> hub, Pg db, ILogger<RoomTicker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        int ticks = 0;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                foreach (var room in rooms.PendingSnapshots())
                    await hub.Clients.Group(room.Id.ToString()).SendAsync("RoomUpdated", room, timeout.Token);
                if (++ticks % 20 == 0)
                {
                    await hub.Clients.Group("lobby").SendAsync("RoomsChanged", rooms.List(), timeout.Token);
                    while (rooms.PeekCompleted() is { } match)
                    {
                        await db.Query("UPDATE matches SET state=@state,finished_at=now(),results=@results::jsonb WHERE id=@id", ("id", match.Id), ("state", match.State), ("results", JsonSerializer.Serialize(match.Results)));
                        rooms.AckCompleted(match.Id);
                    }
                }
                if (ticks % 36000 == 0) await db.Query("DELETE FROM chat_messages WHERE created_at < now()-interval '7 days'; DELETE FROM sessions WHERE expires_at < now()");
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested) { logger.LogWarning(error, "Realtime broadcast or result persistence failed"); }
        }
    }
}
