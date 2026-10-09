namespace Cloud;

public sealed record RoomChart(Guid Id, string Title, string Sha256, int Keys, Guid? PackId, string? ContentSha256 = null, Guid? ShareId = null, DateTime? ExpiresAt = null);
public sealed record ProgressInput(Guid MatchId, long Sequence, int ExScore, int Combo, int Misses, double Progress, int MaxCombo = 0, int ClearType = 0, bool Aborted = false);
public sealed record MemberView(Guid Id, string Username, string DisplayName, string? AvatarUrl, bool Ready, int ExScore, int Combo, int Misses, double Progress, bool Finished, bool Disconnected = false, string ContentState = "unknown", long Uid = 0, int MaxCombo = 0, int ClearType = 0, bool Aborted = false);
public sealed record RoomView(Guid Id, string Name, Guid HostId, string State, long Version, RoomChart? Chart, Guid? MatchId, DateTime? StartAt, IReadOnlyList<MemberView> Members, Guid SelectionId = default, IReadOnlyList<MemberView>? Results = null, Guid[]? ParticipantIds = null);
public sealed record CompletedMatch(Guid Id, Guid RoomId, Guid ChartId, string State, DateTime StartedAt, IReadOnlyList<MemberView> Results);

public sealed class Rooms(CloudOptions options, TimeProvider? clock = null)
{
    private sealed class Member(PublicUser user, string connection)
    {
        public PublicUser User { get; } = user;
        public string Connection { get; } = connection;
        public bool Ready, Finished, Disconnected, Aborted;
        public string ContentState = "unknown";
        public int ExScore, Combo, Misses, MaxCombo, ClearType;
        public double Progress;
        public long Sequence = -1;
        public DateTime LastProgress;
        public MemberView View() => new(User.Id, User.Username, User.DisplayName, User.AvatarUrl, Ready, ExScore, Combo, Misses, Progress, Finished, Disconnected, ContentState, User.Uid, MaxCombo, ClearType, Aborted);
    }
    private sealed class Room(Guid id, string name, Guid host)
    {
        public Guid Id = id, Host = host;
        public string Name = name, State = "lobby";
        public long Version;
        public RoomChart? Chart;
        public Guid Selection;
        public Guid? Match;
        public DateTime? StartAt;
        public bool Persisted, Dirty = true;
        public DateTime UpdatedAt = DateTime.UtcNow;
        public Dictionary<Guid, Member> Members = [];
        public Member[] RoundMembers = [];
        public MemberView[] Results = [];
    }
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    private readonly object gate = new();
    private readonly Dictionary<Guid, Room> rooms = [];
    private readonly Dictionary<Guid, HashSet<string>> online = [];
    private int connectionCount;
    private readonly Dictionary<Guid, Guid> membership = [];
    private readonly Queue<CompletedMatch> completed = [];

    public void Connect(Guid user, string connection)
    {
        lock (gate)
        {
            if (online.TryGetValue(user, out var connections) && connections.Contains(connection)) return;
            if (connectionCount >= 80) throw new ApiError(429, "Server realtime connection limit is 80.");
            if (connections?.Count >= 4) throw new ApiError(409, "Close another website tab or client before reconnecting. An account may have four realtime connections.");
            if (connections is null) online[user] = connections = [];
            connections.Add(connection);
            connectionCount++;
        }
    }
    public void Disconnect(Guid user, string connection)
    {
        lock (gate)
        {
            if (!online.TryGetValue(user, out var connections) || !connections.Remove(connection)) return;
            connectionCount--;
            if (connections.Count == 0) online.Remove(user);
            if (membership.TryGetValue(user, out var roomId) && rooms[roomId].Members[user].Connection == connection)
                LeaveCore(user);
        }
    }
    public RoomView[] List() { lock (gate) return rooms.Values.Select(View).ToArray(); }
    public RoomView? Current(Guid user) { lock (gate) return membership.TryGetValue(user, out var id) ? View(rooms[id]) : null; }

    public RoomView Create(PublicUser user, string connection, string name)
    {
        lock (gate)
        {
            if (name?.Trim().Length is not (>= 1 and <= 80)) throw new ApiError(400, "Room name must contain 1–80 characters.");
            if (membership.ContainsKey(user.Id)) throw new ApiError(409, "Leave the current room first.");
            if (rooms.Count >= options.MaxRooms) throw new ApiError(429, "Server room limit reached.");
            var room = new Room(Guid.NewGuid(), name.Trim(), user.Id);
            rooms.Add(room.Id, room);
            JoinCore(room, user, connection);
            return View(room);
        }
    }
    public RoomView Join(Guid id, PublicUser user, string connection)
    {
        lock (gate)
        {
            if (membership.TryGetValue(user.Id, out var existing))
            {
                if (existing == id) return View(rooms[id]);
                throw new ApiError(409, "Leave the current room first.");
            }
            if (!rooms.TryGetValue(id, out var room)) throw new ApiError(404, "Room not found.");
            if (room.State is not ("lobby" or "results")) throw new ApiError(409, "Wait until this round finishes.");
            if (room.Members.Count >= 16) throw new ApiError(409, "Room is full (16 players).");
            JoinCore(room, user, connection);
            return View(room);
        }
    }
    private void JoinCore(Room room, PublicUser user, string connection)
    {
        room.Members.Add(user.Id, new Member(user, connection));
        membership[user.Id] = room.Id;
        InvalidateReady(room);
        Changed(room);
    }
    public Guid? Leave(Guid user) { lock (gate) return LeaveCore(user); }
    private Guid? LeaveCore(Guid user)
    {
        if (!membership.Remove(user, out var id)) return null;
        var room = rooms[id];
        room.Members[user].Disconnected = true;
        room.Members.Remove(user);
        if (room.Members.Count == 0)
        {
            if (room.Match is { } match && room.State is "countdown" or "playing")
                completed.Enqueue(new(match, room.Id, room.Chart!.Id, "interrupted", room.StartAt ?? Now, room.RoundMembers.Select(member => member.View()).ToArray()));
            rooms.Remove(id);
            return id;
        }
        if (room.Host == user) room.Host = room.Members.Keys.First();
        if (room.State == "countdown") End(room, "cancelled");
        else if (room.State == "playing" && room.RoundMembers.All(m => m.Finished || m.Disconnected)) End(room, "finished");
        else if (room.State == "lobby") InvalidateReady(room);
        Changed(room);
        return id;
    }

    public RoomView TransferHost(Guid user, Guid target, long version)
    {
        lock (gate)
        {
            var room = Get(user);
            Host(room, user, version);
            if (room.State is not ("lobby" or "results")) throw new ApiError(409, "Wait until the round finishes before transferring host.");
            if (target == user || !room.Members.ContainsKey(target)) throw new ApiError(400, "Choose another room participant.");
            room.Host = target;
            Changed(room);
            return View(room);
        }
    }
    public (RoomView Room, string[] Connections) Kick(Guid user, Guid target, long version)
    {
        lock (gate)
        {
            var room = Get(user);
            Host(room, user, version);
            if (room.State is not ("lobby" or "results")) throw new ApiError(409, "Wait until the round finishes before removing a player.");
            if (target == user || !room.Members.TryGetValue(target, out var member)) throw new ApiError(400, "Choose another room participant.");
            LeaveCore(target);
            var connections = online.TryGetValue(target, out var active) ? active.Append(member.Connection).Distinct().ToArray() : [member.Connection];
            return (View(room), connections);
        }
    }
    public RoomView Select(Guid user, RoomChart chart, long version)
    {
        lock (gate)
        {
            var room = Get(user);
            Host(room, user, version);
            if (room.State is not ("lobby" or "results")) throw new ApiError(409, "A round is in progress.");
            room.Chart = chart;
            room.Selection = Guid.NewGuid();
            foreach (var member in room.Members.Values) member.ContentState = "unknown";
            room.Results = [];
            room.RoundMembers = [];
            room.Match = null;
            room.StartAt = null;
            room.State = "lobby";
            InvalidateReady(room);
            Changed(room);
            return View(room);
        }
    }
    public RoomView Ready(Guid user, bool ready, string sha256, long version)
    {
        lock (gate)
        {
            var room = Get(user);
            if (room.Version != version || room.State is not ("lobby" or "results") || room.Chart is null || room.Chart.Sha256 != sha256)
                throw new ApiError(409, "Room changed, or downloaded chart identity does not match.");
            if (ready && room.Members[user].ContentState != "available")
                throw new ApiError(409, "Match the selected BMS chart before becoming ready.");
            if (room.State == "results") room.State = "lobby";
            room.Members[user].Ready = ready;
            Changed(room);
            return View(room);
        }
    }
    public RoomView RequireSelection(Guid user, Guid roomId, Guid selection, string? content = null, bool host = false)
    {
        lock (gate)
        {
            var room = Get(user);
            if (room.Id != roomId || room.Selection != selection || room.Chart is null || content is not null && room.Chart.ContentSha256 != content)
                throw new ApiError(409, "The selected song has changed.");
            if (host && room.Host != user) throw new ApiError(403, "Only the room host may upload this song.");
            if (host && room.State is not ("lobby" or "results")) throw new ApiError(409, "Wait until the round finishes.");
            return View(room);
        }
    }
    public RoomView AttachContent(Guid user, Guid roomId, Guid selection, string content, Guid share, DateTime expires)
    {
        lock (gate)
        {
            RequireSelection(user, roomId, selection, host: true);
            var room = Get(user);
            room.Chart = room.Chart! with { ContentSha256 = content, ShareId = share, ExpiresAt = expires };
            Changed(room);
            return View(room);
        }
    }
    public RoomView ContentPresence(Guid user, Guid selection, string sha256, string state)
    {
        lock (gate)
        {
            var room = Get(user);
            if (room.Selection != selection || (room.Chart is null || (room.Chart.Sha256 != sha256 && room.Chart.ContentSha256 != sha256)) || state is not ("missing" or "downloading" or "available"))
                throw new ApiError(409, "Content report does not match the selected song.");
            var member = room.Members[user];
            if (member.ContentState == state) return View(room);
            member.ContentState = state;
            if (state != "available") member.Ready = false;
            Changed(room);
            return View(room);
        }
    }
    public RoomView Start(Guid user, long version, bool force = false)
    {
        lock (gate)
        {
            if (completed.Count >= 256) throw new ApiError(503, "Match persistence is temporarily unavailable.");
            var room = Get(user);
            Host(room, user, version);
            if (room.State != "lobby" || room.Chart is null || room.Members[user].ContentState != "available") throw new ApiError(409, "The host must have the selected BMS chart.");
            if (!force && !room.Members.Values.All(m => m.Ready && m.ContentState == "available")) throw new ApiError(409, "All players must confirm the chart and be ready.");
            room.Match = Guid.NewGuid();
            room.State = "countdown";
            room.StartAt = null;
            room.Persisted = false;
            room.RoundMembers = room.Members.Values.Where(m => m.ContentState == "available").ToArray();
            room.Results = [];
            foreach (var member in room.Members.Values)
            {
                member.ExScore = member.Combo = member.Misses = member.MaxCombo = member.ClearType = 0;
                member.Aborted = false; member.LastProgress = default;
                member.Progress = 0; member.Sequence = -1; member.Finished = false;
            }
            Changed(room);
            return View(room);
        }
    }
    public RoomView ConfirmStart(Guid id, Guid match)
    {
        lock (gate)
        {
            if (!rooms.TryGetValue(id, out var room) || room.Match != match || room.State != "countdown") throw new ApiError(409, "Countdown was cancelled.");
            room.StartAt = Now.AddSeconds(3);
            room.Persisted = true;
            Changed(room);
            return View(room);
        }
    }
    public void CancelStart(Guid id, Guid match)
    {
        lock (gate)
            if (rooms.TryGetValue(id, out var room) && room.Match == match && room.State == "countdown") End(room, "cancelled");
    }
    public RoomView Progress(Guid user, ProgressInput input, bool finish)
    {
        lock (gate)
        {
            var room = Get(user);
            Tick(room);
            var member = room.Members[user];
            if (!room.RoundMembers.Contains(member)) throw new ApiError(403, "You are not participating in this round.");
            if (room.State != "playing" || input.MatchId != room.Match || member.Finished) throw new ApiError(409, "Round is not accepting scores.");
            if (input.Sequence <= member.Sequence || input.Sequence < 0 || input.ExScore < member.ExScore || input.ExScore > 3000000 ||
                input.Combo is < 0 or > 1000000 || input.Misses < member.Misses || input.Misses > 1000000 ||
                !double.IsFinite(input.Progress) || input.Progress < member.Progress || input.Progress > 1 || (finish && !input.Aborted && input.Progress != 1) || (!finish && input.Aborted) || input.MaxCombo is < 0 or > 1000000 || input.ClearType is < 0 or > 9)
                throw new ApiError(400, "Invalid or stale progress snapshot.");
            if (!finish && Now - member.LastProgress < TimeSpan.FromMilliseconds(80)) throw new ApiError(429, "Progress is limited to approximately 10 Hz.");
            member.Sequence = input.Sequence; member.ExScore = input.ExScore; member.Combo = input.Combo; member.Misses = input.Misses;
            member.MaxCombo = Math.Max(member.MaxCombo, Math.Max(input.MaxCombo, input.Combo)); member.ClearType = input.ClearType; member.Aborted = input.Aborted;
            member.Progress = input.Progress; member.Finished = finish; member.LastProgress = Now;
            Changed(room);
            if (room.RoundMembers.All(m => m.Finished || m.Disconnected)) End(room, "finished");
            return View(room);
        }
    }
    public RoomView[] PendingSnapshots()
    {
        lock (gate)
        {
            foreach (var room in rooms.Values.ToArray())
            {
                Tick(room);
                if (room.State == "playing" && Now > room.StartAt!.Value.AddMinutes(30)) End(room, "timed-out");
                if (room.State is "lobby" or "results" && Now - room.UpdatedAt > TimeSpan.FromHours(1))
                {
                    foreach (var user in room.Members.Keys) membership.Remove(user);
                    rooms.Remove(room.Id);
                }
            }
            var snapshots = rooms.Values.Where(r => r.Dirty).Select(View).ToArray();
            foreach (var room in rooms.Values) room.Dirty = false;
            return snapshots;
        }
    }
    public CompletedMatch? PeekCompleted() { lock (gate) return completed.TryPeek(out var match) ? match : null; }
    public void AckCompleted(Guid id) { lock (gate) if (completed.TryPeek(out var match) && match.Id == id) completed.Dequeue(); }
    private Room Get(Guid user) => membership.TryGetValue(user, out var id) ? rooms[id] : throw new ApiError(404, "Join a room first.");
    private static void Host(Room room, Guid user, long version)
    {
        if (room.Host != user) throw new ApiError(403, "Only the host may change this room.");
        if (room.Version != version) throw new ApiError(409, "Room version is stale.");
    }
    private static void InvalidateReady(Room room) { foreach (var member in room.Members.Values) member.Ready = false; }
    private void Changed(Room room) { room.Version++; room.Dirty = true; room.UpdatedAt = Now; }
    private void Tick(Room room)
    {
        if (room.State == "countdown" && room.Persisted && room.StartAt <= Now) { room.State = "playing"; Changed(room); }
    }
    private void End(Room room, string state)
    {
        room.Results = room.RoundMembers.Select(member => member.View()).OrderByDescending(member => member.ExScore)
            .ThenBy(member => member.Misses).ThenByDescending(member => member.MaxCombo).ThenBy(member => member.Id).ToArray();
        if (room.Match is { } match) completed.Enqueue(new(match, room.Id, room.Chart!.Id, state, room.StartAt ?? Now, room.Results));
        room.State = "results";
        InvalidateReady(room);
        Changed(room);
    }
    private static RoomView View(Room room) => new(room.Id, room.Name, room.Host, room.State, room.Version, room.Chart, room.Match, room.StartAt,
        room.Members.Values.Select(m => m.View()).OrderByDescending(m => m.ExScore).ThenBy(m => m.Misses).ThenByDescending(m => m.MaxCombo).ThenBy(m => m.Id).ToArray(), room.Selection, room.State is "countdown" or "playing" ? room.RoundMembers.Select(member => member.View()).ToArray() : room.Results,
        room.RoundMembers.Select(member => member.User.Id).ToArray());
}
