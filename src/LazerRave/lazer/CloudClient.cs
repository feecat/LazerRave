using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using LazerRave.Content;
using LazerRave.Bridge;
using Microsoft.AspNetCore.SignalR.Client;

namespace LazerRave.Lazer;

internal sealed record CloudUser(Guid Id, string Username, string DisplayName, long Uid = 0, string? AvatarUrl = null);
internal sealed record CloudChart(Guid Id, string Title, string Sha256, int Keys, Guid? PackId, string? ContentSha256, Guid? ShareId, DateTime? ExpiresAt);
internal sealed record CloudMember(Guid Id, string DisplayName, bool Ready, string ContentState, string Username = "", string? AvatarUrl = null, long Uid = 0, int ExScore = 0, int Combo = 0, int Misses = 0, double Progress = 0, bool Finished = false, bool Disconnected = false, int MaxCombo = 0, int ClearType = 0, bool Aborted = false);
internal sealed record CloudChat(long Id, string Channel, string Text, Guid UserId, string Username, string DisplayName, DateTime CreatedAt);
internal sealed record CloudRoom(Guid Id, string Name, Guid HostId, string State, long Version, CloudChart? Chart, Guid? MatchId, DateTime? StartAt, CloudMember[] Members, Guid SelectionId, CloudMember[]? Results = null, Guid[]? ParticipantIds = null);
internal sealed record SharedSong(Guid Id, Guid SelectionId, string State, long SizeBytes, long ReceivedBytes, string ArchiveSha256, SongManifest Manifest, DateTime ExpiresAt);
internal sealed record CloudLogin(CloudUser User, string Token);
internal sealed record CloudLocalChart(string Path, string Title, string Artist, int Keys, int Level);
internal sealed record CloudClock(long ClientTime, long ServerTime, int Protocol);

internal sealed partial class CloudClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    private HttpClient? http;
    private HubConnection? hub;
    private string? token;
    private CancellationTokenSource? transfer, inspection;
    private readonly SemaphoreSlim operation = new(1);
    private readonly object chatLock = new();
    private readonly Func<IEnumerable<string>> songDirectories;
    private readonly Func<string> encoding;
    private readonly Func<string, Task> installed;
    private readonly string applicationRoot;
    private readonly CloudSessionStore? sessions;
    public bool HasSavedSession => sessions?.Exists == true;
    private string SharedRoot => LibraryFolders.Shared(applicationRoot);
    private Guid inspectedSelection;
    private Guid? kickedRoom;
    private string? localChart;
    private double serverClockOffset;
    public DateTime ServerNow => DateTime.UtcNow.AddMilliseconds(serverClockOffset);
    public CloudUser? User { get; private set; }
    public Uri? Server => http?.BaseAddress;
    internal static bool IsAvatarUrl(Uri? server, string value)
    {
        if (server is null || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.GetLeftPart(UriPartial.Authority) != server.GetLeftPart(UriPartial.Authority) || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0) return false;
        var parts = uri.AbsolutePath.Split('/');
        return parts.Length == 5 && parts[1] == "api" && parts[2] == "users" && Guid.TryParse(parts[3], out _) && parts[4] == "avatar";
    }
    public Uri? AvatarUri => ResolveAvatar(http?.BaseAddress, User);
    internal static Uri? ResolveAvatar(Uri? server, CloudUser? user)
    {
        if (server is null || string.IsNullOrWhiteSpace(user?.AvatarUrl) || !Uri.TryCreate(server, user.AvatarUrl, out var avatar)) return null;
        return avatar.GetLeftPart(UriPartial.Authority) == server.GetLeftPart(UriPartial.Authority) &&
            avatar.UserInfo.Length == 0 && avatar.Fragment.Length == 0 && avatar.AbsolutePath == $"/api/users/{user.Id}/avatar" ? avatar : null;
    }
    public CloudRoom? Room { get; private set; }
    public CloudRoom[] Rooms { get; private set; } = [];
    public bool Connected => hub?.State == HubConnectionState.Connected;
    public bool Busy { get; private set; }
    public string Status { get; private set; } = "";
    public string? AvailableChart { get; private set; }
    public TransferProgress? Progress { get; private set; }
    public bool CanStartRound => Connected && !Busy && Room is { State: "lobby", Chart: not null } room && room.HostId == User?.Id
        && AvailableChart is not null && room.Members.Length > 0 && room.Members.All(member => member.Ready && member.ContentState == "available");
    public bool CanForceStartRound => Connected && !Busy && Room is { State: "lobby", Chart: not null } room && room.HostId == User?.Id
        && AvailableChart is not null && room.Members.Any(member => member.Id == User.Id && member.ContentState == "available");
    public CloudChat[] Messages { get; private set; } = [];
    public event Action? Changed;
    public CloudClient(Func<IEnumerable<string>> songDirectories, Func<string> encoding, Func<string, Task> installed, string? applicationRoot = null, CloudSessionStore? sessions = null)
    { this.songDirectories = songDirectories; this.encoding = encoding; this.installed = installed; this.applicationRoot = Path.GetFullPath(applicationRoot ?? AppContext.BaseDirectory); this.sessions = sessions; }
    private void Notify() => Changed?.Invoke();
    public void SetGameStatus(string status) { Status = status; Notify(); }
    private void SetProgress(TransferProgress value) { Progress = value; Status = value.Stage; Notify(); }
    public static Uri ServerUri(string value)
    {
        if (!Uri.TryCreate(value.Trim().TrimEnd('/') + '/', UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/" ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))) throw new ArgumentException("Use an HTTPS server URL, or HTTP on localhost for development.");
        return uri;
    }
    public async Task Login(string server, string username, string password, CancellationToken cancellation)
    {
        var uri = ServerUri(server);
        await Disconnect();
        CreateHttp(uri);
        var result = await Request<CloudLogin>(HttpMethod.Post, "api/auth/token", new { username, password }, cancellation);
        token = result.Token; User = result.User;
        http!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Status = "Signed in"; Notify();
        sessions?.Save(uri, token);
        await ConnectAfterSignIn(cancellation);
    }
    private void CreateHttp(Uri server)
    {
        http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = server, Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.Add("X-LazerRave", "1");
    }
    public async Task Restore(CancellationToken cancellation)
    {
        var session = sessions?.Read();
        if (session is null) return;
        Status = "Restoring sign-in…"; Notify();
        await CloseConnection();
        CreateHttp(ServerUri(session.Server));
        token = session.Token;
        http!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await http.GetAsync("api/me", timeout.Token);
            await Check(response, timeout.Token);
            var content = await response.Content.ReadAsStringAsync(timeout.Token);
            User = string.IsNullOrWhiteSpace(content) ? null : JsonSerializer.Deserialize<CloudUser>(content, json);
            if (User is null) { await ForgetSession(); return; }
        }
        catch (HttpRequestException failure) when (failure.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        { await ForgetSession(); return; }
        catch (Exception failure) when (!cancellation.IsCancellationRequested && (failure is HttpRequestException or OperationCanceledException or JsonException))
        {
            await CloseConnection();
            Status = "Automatic sign-in unavailable. Retry when the server is reachable."; Notify();
            return;
        }
        Status = "Signed in"; Notify();
        await ConnectAfterSignIn(cancellation);
    }
    private async Task ForgetSession()
    {
        sessions?.Clear(); await CloseConnection();
        Status = "Session expired or revoked. Sign in again."; Notify();
    }
    private async Task ConnectAfterSignIn(CancellationToken cancellation)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            await Connect(timeout.Token);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            Status = "Signed in. Multiplayer unavailable: " + failure.Message; Notify();
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { Status = "Signed in. Multiplayer connection timed out."; Notify(); }
    }
    public async Task Connect(CancellationToken cancellation)
    {
        if (User is null || http is null) throw new InvalidOperationException("Sign in first.");
        if (hub is not null) { await hub.DisposeAsync(); hub = null; }
        var uri = http.BaseAddress!;
        hub = new HubConnectionBuilder().WithUrl(new Uri(uri, "hubs/realtime"), options =>
        {
            options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            options.Headers["X-LazerRave"] = "1";
        }).Build();
        hub.On<CloudRoom>("RoomUpdated", UpdateRoom);
        hub.On<Guid>("RoomKicked", id =>
        {
            if (Room?.Id != id) return;
            kickedRoom = id;
            CancelTransfer(); inspection?.Cancel(); Room = null; AvailableChart = localChart = null; inspectedSelection = Guid.Empty;
            Status = "You were removed from the room by the host."; Notify();
        });
        hub.On<CloudChat>("ChatMessage", message =>
        {
            lock (chatLock) Messages = Messages.Append(message).GroupBy(value => value.Id).Select(group => group.First()).OrderBy(value => value.Id).TakeLast(200).ToArray();
            Notify();
        });
        hub.On<CloudRoom[]>("RoomsChanged", values =>
        {
            Rooms = values;
            var current = values.FirstOrDefault(r => r.Members.Any(m => m.Id == User?.Id));
            if (current is not null) UpdateRoom(current);
            else if (Room is not null) { CancelTransfer(); inspection?.Cancel(); Room = null; inspectedSelection = Guid.Empty; AvailableChart = null; }
            Notify();
        });
        hub.Closed += _ => { CancelTransfer(); inspection?.Cancel(); Room = null; Rooms = []; Status = "Multiplayer disconnected. Reconnect, or close extra website tabs or clients if the connection limit was reached."; Notify(); return Task.CompletedTask; };
        await hub.StartAsync(cancellation);
        var sent = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var clock = await hub.InvokeAsync<CloudClock>("Ping", sent, cancellation);
        serverClockOffset = clock.ServerTime - (sent + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 2d;
        Rooms = await Request<CloudRoom[]>(HttpMethod.Get, "api/rooms", null, cancellation);
        Status = "Connected"; Notify();
    }
    public async Task SendChat(string channel, string text, CancellationToken cancellation) =>
        await hub!.InvokeAsync("SendChat", channel, text, cancellation);
    public async Task LoadChat(string channel, CancellationToken cancellation)
    {
        var history = await Request<CloudChat[]>(HttpMethod.Get, "api/chat/" + Uri.EscapeDataString(channel), null, cancellation);
        lock (chatLock) Messages = Messages.Concat(history).GroupBy(message => message.Id).Select(group => group.First()).OrderBy(message => message.Id).TakeLast(200).ToArray();
        Notify();
    }
    private async Task<T> Request<T>(HttpMethod method, string path, object? body, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, options: json);
        using var response = await http!.SendAsync(request, cancellation);
        await Check(response, cancellation);
        return (await response.Content.ReadFromJsonAsync<T>(json, cancellation))!;
    }
    private static async Task Check(HttpResponseMessage response, CancellationToken cancellation)
    {
        if (response.IsSuccessStatusCode) return;
        var text = await response.Content.ReadAsStringAsync(cancellation);
        try { using var data = JsonDocument.Parse(text); text = data.RootElement.GetProperty("error").GetString() ?? text; } catch (JsonException) { }
        throw new HttpRequestException(string.IsNullOrWhiteSpace(text) ? $"Server returned {(int)response.StatusCode}." : text, null, response.StatusCode);
    }
    private void UpdateRoom(CloudRoom value)
    {
        if (value.Id == kickedRoom) return;
        if (!value.Members.Any(m => m.Id == User?.Id)) return;
        if (Room?.Id == value.Id && value.Version < Room.Version) return;
        bool changed = Room?.SelectionId != value.SelectionId || Room?.Id != value.Id;
        Room = value;
        if (changed) { CancelTransfer(); inspection?.Cancel(); AvailableChart = null; localChart = null; Progress = null; }
        Notify();
        if (value.Chart is not null && (changed || inspectedSelection != value.SelectionId))
        {
            inspectedSelection = value.SelectionId;
            inspection?.Cancel(); inspection = new();
            _ = CheckLocal(value, inspection.Token);
        }
    }
    private async Task CheckLocal(CloudRoom room, CancellationToken cancellation)
    {
        try
        {
            Status = "Checking local BMS chart…"; Notify();
            foreach (var directory in songDirectories().ToArray())
            {
                if (!Directory.Exists(directory)) continue;
                foreach (var path in Directory.EnumerateFiles(directory).Where(SongContent.IsChart))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (await SongContent.HashFile(path, cancellation) != room.Chart!.Sha256) continue;
                    if (Room?.SelectionId != room.SelectionId) return;
                    localChart = AvailableChart = path;
                    await ReportContent(room, "available", cancellation); Status = "Song available"; Notify(); return;
                }
            }
            if (Room?.SelectionId == room.SelectionId) { await ReportContent(room, "missing", cancellation); Status = "Song missing — download when the host shares it."; Notify(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Status = error.Message; Notify(); }
    }
    private async Task ReportContent(CloudRoom room, string state, CancellationToken cancellation)
    {
        var updated = await hub!.InvokeAsync<CloudRoom>("ReportContent", room.SelectionId, room.Chart!.Sha256, state, cancellation);
        UpdateRoom(updated);
    }
    public async Task CreateRoom(string name, CancellationToken cancellation) => UpdateRoom(await hub!.InvokeAsync<CloudRoom>("CreateRoom", name, cancellation));
    public async Task JoinRoom(Guid id, CancellationToken cancellation)
    {
        var room = await hub!.InvokeAsync<CloudRoom>("JoinRoom", id, cancellation);
        kickedRoom = null; UpdateRoom(room);
    }
    public async Task LeaveRoom(CancellationToken cancellation)
    {
        CancelTransfer(); inspection?.Cancel(); await hub!.InvokeAsync("LeaveRoom", cancellation);
        Room = null; AvailableChart = null; localChart = null; inspectedSelection = Guid.Empty; Notify();
    }
    public async Task SelectChart(CloudLocalChart chart, CancellationToken cancellation)
    {
        var room = Room ?? throw new InvalidOperationException("Join a room first.");
        if (room.HostId != User?.Id) throw new InvalidOperationException("Only the host may select a song.");
        if (!SongContent.IsChart(chart.Path)) throw new InvalidDataException("Select a BMS chart.");
        var sha256 = await SongContent.HashFile(chart.Path, cancellation);
        var updated = await hub!.InvokeAsync<CloudRoom>("SelectLocalChart", new { sha256, contentSha256 = (string?)null, chart.Title, chart.Artist, chart.Keys, chart.Level }, room.Version, cancellation);
        UpdateRoom(updated);
    }
    public async Task Ready(CancellationToken cancellation)
    {
        var room = Room ?? throw new InvalidOperationException("Join a room first.");
        if (AvailableChart is null || room.Members.Single(m => m.Id == User!.Id).ContentState != "available") throw new InvalidOperationException("The selected BMS chart must be available first.");
        UpdateRoom(await hub!.InvokeAsync<CloudRoom>("SetReady", !room.Members.Single(m => m.Id == User!.Id).Ready, room.Chart!.Sha256, room.Version, cancellation));
    }
    public async Task StartRound(CancellationToken cancellation)
    {
        if (!CanStartRound) throw new InvalidOperationException("Only the host can start after every player has matched the BMS chart and is ready.");
        UpdateRoom(await hub!.InvokeAsync<CloudRoom>("StartRound", Room!.Version, cancellation));
    }
    public async Task ForceStartRound(CancellationToken cancellation)
    {
        if (!CanForceStartRound) throw new InvalidOperationException("Only the host with the selected BMS chart may force start.");
        UpdateRoom(await hub!.InvokeAsync<CloudRoom>("ForceStartRound", Room!.Version, cancellation));
    }
    public async Task KickPlayer(Guid target, CancellationToken cancellation) =>
        UpdateRoom(await hub!.InvokeAsync<CloudRoom>("KickPlayer", target, Room!.Version, cancellation));
    public async Task TransferHost(Guid target, CancellationToken cancellation)
    {
        UpdateRoom(await hub!.InvokeAsync<CloudRoom>("TransferHost", target, Room!.Version, cancellation));
    }
    public async Task ReportScore(Guid match, long sequence, GameplaySnapshot score, bool finish, CancellationToken cancellation)
    {
        var input = new { matchId = match, sequence, score.ExScore, score.Combo, score.Misses, score.Progress, score.MaxCombo, score.ClearType, score.Aborted };
        UpdateRoom(await hub!.InvokeAsync<CloudRoom>(finish ? "FinishRound" : "ReportProgress", input, cancellation));
    }
    public async Task Upload(CancellationToken cancellation)
    {
        await RunTransfer(async ct =>
        {
            var room = Room ?? throw new InvalidOperationException("Join a room first.");
            var chart = localChart ?? throw new InvalidOperationException("The selected local song is unavailable.");
            var progress = new CallbackProgress<TransferProgress>(SetProgress);
            var manifest = await Task.Run(() => SongTransferFiles.Inspect(chart, ct, progress, encoding()), ct);
            if (manifest.ChartSha256 != room.Chart?.Sha256) throw new InvalidDataException("The selected BMS file has changed. Select it again.");
            var zip = await Task.Run(() => SongTransferFiles.Pack(chart, manifest, Path.Combine(applicationRoot, "cache", "shared-uploads"), ct, progress), ct);
            var size = new FileInfo(zip).Length;
            var sha = await SongContent.HashFile(zip, ct);
            var upload = await Request<SharedSong>(HttpMethod.Post, $"api/rooms/{room.Id}/content", new { room.SelectionId, manifest, sizeBytes = size, archiveSha256 = sha }, ct);
            await using var input = File.OpenRead(zip);
            var clock = Stopwatch.StartNew(); long initial = upload.ReceivedBytes;
            while (upload.State == "uploading" && upload.ReceivedBytes < size)
            {
                ct.ThrowIfCancellationRequested(); input.Position = upload.ReceivedBytes;
                var bytes = new byte[(int)Math.Min(SongContent.ChunkBytes, size - input.Position)]; await input.ReadExactlyAsync(bytes, ct);
                using var message = new HttpRequestMessage(HttpMethod.Put, $"api/room-content/{upload.Id}?offset={upload.ReceivedBytes}") { Content = new ByteArrayContent(bytes) };
                message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using var response = await http!.SendAsync(message, ct);
                if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.TooManyRequests)
                {
                    await Task.Delay(1000, ct); upload = await Request<SharedSong>(HttpMethod.Get, $"api/room-content/{upload.Id}", null, ct); continue;
                }
                await Check(response, ct); upload = (await response.Content.ReadFromJsonAsync<SharedSong>(json, ct))!;
                SetProgress(new("Uploading", upload.ReceivedBytes, size, (upload.ReceivedBytes - initial) / Math.Max(.01, clock.Elapsed.TotalSeconds)));
            }
            Status = "Server validating ZIP…"; Notify();
            await Request<SharedSong>(HttpMethod.Post, $"api/room-content/{upload.Id}/complete", null, ct);
            Status = "Shared for 2 hours"; Progress = null; Notify();
        }, cancellation);
    }
    public async Task Download(CancellationToken cancellation)
    {
        await RunTransfer(async ct =>
        {
            var room = Room ?? throw new InvalidOperationException("Join a room first.");
            var id = room.Chart?.ShareId ?? throw new InvalidOperationException("The host has not shared this song yet.");
            var song = await Request<SharedSong>(HttpMethod.Get, $"api/room-content/{id}", null, ct);
            if (song.State != "ready" || song.SizeBytes > SongContent.MaxArchiveBytes || song.Manifest.ContentSha256 != room.Chart!.ContentSha256 || song.Manifest.ChartSha256 != room.Chart.Sha256)
                throw new InvalidDataException("Shared content does not match the selected song.");
            SongContent.Validate(song.Manifest);
            await ReportContent(room, "downloading", ct);
            LibraryFolders.Ensure(applicationRoot);
            var root = Path.Combine(SharedRoot, ".incoming"); Directory.CreateDirectory(root);
            LibraryFolders.NoLink(root);
            var zip = Path.Combine(root, song.ArchiveSha256 + ".zip.part");
            if (File.Exists(zip)) LibraryFolders.NoLink(zip);
            var clock = Stopwatch.StartNew();
            await using (var output = new FileStream(zip, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                if (output.Length > song.SizeBytes) output.SetLength(0);
                output.Position = output.Length; long initial = output.Position;
                SetProgress(new("Downloading", output.Position, song.SizeBytes));
                while (output.Position < song.SizeBytes)
                {
                    ct.ThrowIfCancellationRequested(); long start = output.Position;
                    using var message = new HttpRequestMessage(HttpMethod.Get, $"api/room-content/{id}/download");
                    long end = Math.Min(start + SongContent.ChunkBytes - 1, song.SizeBytes - 1);
                    message.Headers.Range = new RangeHeaderValue(start, end);
                    using var response = await http!.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
                    await Check(response, ct);
                    if (response.StatusCode != HttpStatusCode.PartialContent || response.Content.Headers.ContentRange?.From != start || response.Content.Headers.ContentRange.To != end || response.Content.Headers.ContentRange.Length != song.SizeBytes)
                        throw new InvalidDataException("Server returned an unexpected ZIP range.");
                    await using var input = await response.Content.ReadAsStreamAsync(ct);
                    var buffer = new byte[65536]; int count;
                    while ((count = await input.ReadAsync(buffer, ct)) > 0)
                    {
                        if (output.Position + count > end + 1) throw new InvalidDataException("Downloaded range exceeds expected size.");
                        await output.WriteAsync(buffer.AsMemory(0, count), ct);
                        SetProgress(new("Downloading", output.Position, song.SizeBytes, (output.Position - initial) / Math.Max(.01, clock.Elapsed.TotalSeconds)));
                    }
                    if (output.Position != end + 1) throw new IOException("Download interrupted. Click Download song to continue.");
                    await output.FlushAsync(ct);
                }
            }
            SetProgress(new("Verifying ZIP", 0, 1));
            if (await SongContent.HashFile(zip, ct) != song.ArchiveSha256) { File.Delete(zip); throw new InvalidDataException("ZIP checksum failed. Download again."); }
            var path = await Task.Run(() => SongTransferFiles.Install(zip, song.Manifest, SharedRoot, ct, new CallbackProgress<TransferProgress>(SetProgress)), ct);
            if (Room?.SelectionId != room.SelectionId) throw new OperationCanceledException(ct);
            AvailableChart = localChart = path; File.Delete(zip);
            await installed(path);
            await ReportContent(room, "available", ct);
            Progress = null; Status = "Installed to BMS/Shared"; Notify();
        }, cancellation);
    }
    private async Task RunTransfer(Func<CancellationToken, Task> action, CancellationToken cancellation)
    {
        if (!await operation.WaitAsync(0, cancellation)) throw new InvalidOperationException("Another transfer is running.");
        Busy = true; transfer?.Dispose(); transfer = CancellationTokenSource.CreateLinkedTokenSource(cancellation); Notify();
        try { await action(transfer.Token); }
        catch (OperationCanceledException) { Status = "Cancelled — click the transfer button to continue."; }
        finally
        {
            Busy = false; Progress = null; operation.Release();
            if (Room is { Chart.ContentSha256: not null } room && AvailableChart is null && Connected)
                try { await ReportContent(room, "missing", CancellationToken.None); } catch (Exception) { }
            Notify();
        }
    }
    public void CancelTransfer() => transfer?.Cancel();
    public async Task Disconnect()
    {
        sessions?.Clear();
        if (http is not null && token is not null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { using var response = await http.PostAsync("api/auth/logout", null, timeout.Token); }
            catch (Exception failure) when (failure is HttpRequestException or OperationCanceledException) { }
        }
        await CloseConnection();
        Status = "Signed out"; Notify();
    }
    private async Task CloseConnection()
    {
        CancelTransfer(); inspection?.Cancel();
        if (hub is not null) { await hub.DisposeAsync(); hub = null; }
        http?.Dispose(); http = null; token = null; User = null; Room = null; Rooms = [];
        lock (chatLock) Messages = [];
        AvailableChart = null; inspectedSelection = Guid.Empty; Notify();
    }
    public async ValueTask DisposeAsync() { await CloseConnection(); }
    private sealed class CallbackProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
}
