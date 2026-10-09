using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using LazerRave.Content;
using Microsoft.AspNetCore.SignalR.Client;

namespace LazerRave.Lazer;

internal sealed record CloudUser(Guid Id, string Username, string DisplayName, long Uid = 0, string? AvatarUrl = null);
internal sealed record CloudChart(Guid Id, string Title, string Sha256, int Keys, Guid? PackId, string? ContentSha256, Guid? ShareId, DateTime? ExpiresAt);
internal sealed record CloudMember(Guid Id, string DisplayName, bool Ready, string ContentState);
internal sealed record CloudRoom(Guid Id, string Name, Guid HostId, string State, long Version, CloudChart? Chart, Guid? MatchId, DateTime? StartAt, CloudMember[] Members, Guid SelectionId);
internal sealed record SharedSong(Guid Id, Guid SelectionId, string State, long SizeBytes, long ReceivedBytes, string ArchiveSha256, SongManifest Manifest, DateTime ExpiresAt);
internal sealed record CloudLogin(CloudUser User, string Token);
internal sealed record CloudLocalChart(string Path, string Title, string Artist, int Keys, int Level);

internal sealed class CloudClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    private HttpClient? http;
    private HubConnection? hub;
    private string? token;
    private CancellationTokenSource? transfer, inspection;
    private readonly SemaphoreSlim operation = new(1);
    private readonly Func<IEnumerable<string>> songDirectories;
    private readonly Func<string> encoding;
    private readonly Func<string, Task> installed;
    private readonly string applicationRoot;
    private string SharedRoot => Path.Combine(applicationRoot, "Shared");
    private Guid inspectedSelection;
    private SongManifest? localManifest;
    private string? localChart;
    public CloudUser? User { get; private set; }
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
    public event Action? Changed;
    public CloudClient(Func<IEnumerable<string>> songDirectories, Func<string> encoding, Func<string, Task> installed, string? applicationRoot = null)
    { this.songDirectories = songDirectories; this.encoding = encoding; this.installed = installed; this.applicationRoot = Path.GetFullPath(applicationRoot ?? AppContext.BaseDirectory); }
    private void Notify() => Changed?.Invoke();
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
        http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = uri, Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.Add("X-LazerRave", "1");
        var result = await Request<CloudLogin>(HttpMethod.Post, "api/auth/token", new { username, password }, cancellation);
        token = result.Token; User = result.User;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Status = "Signed in"; Notify();
        try { await Connect(cancellation); }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            Status = "Signed in. Multiplayer unavailable: " + failure.Message; Notify();
        }
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
        await hub.InvokeAsync<object>("Ping", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), cancellation);
        Rooms = await Request<CloudRoom[]>(HttpMethod.Get, "api/rooms", null, cancellation);
        Status = "Connected"; Notify();
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
        if (!value.Members.Any(m => m.Id == User?.Id)) return;
        if (Room?.Id == value.Id && value.Version < Room.Version) return;
        bool changed = Room?.SelectionId != value.SelectionId || Room?.Id != value.Id;
        Room = value;
        if (changed) { CancelTransfer(); inspection?.Cancel(); AvailableChart = null; localManifest = null; localChart = null; Progress = null; }
        Notify();
        if (value.Chart?.ContentSha256 is not null && (changed || inspectedSelection != value.SelectionId))
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
            Status = "Checking local song resources…"; Notify();
            foreach (var directory in songDirectories().ToArray())
            {
                if (!Directory.Exists(directory)) continue;
                foreach (var path in Directory.EnumerateFiles(directory).Where(SongContent.IsChart))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (await SongContent.HashFile(path, cancellation) != room.Chart!.Sha256) continue;
                    SongManifest manifest;
                    try { manifest = await Task.Run(() => SongTransferFiles.Inspect(path, cancellation, encoding: encoding()), cancellation); }
                    catch (Exception error) when (error is IOException or InvalidDataException) { continue; }
                    if (manifest.ContentSha256 != room.Chart.ContentSha256) continue;
                    if (Room?.SelectionId != room.SelectionId) return;
                    localManifest = manifest; localChart = AvailableChart = path;
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
        var updated = await hub!.InvokeAsync<CloudRoom>("ReportContent", room.SelectionId, room.Chart!.ContentSha256, state, cancellation);
        UpdateRoom(updated);
    }
    public async Task CreateRoom(string name, CancellationToken cancellation) => UpdateRoom(await hub!.InvokeAsync<CloudRoom>("CreateRoom", name, cancellation));
    public async Task JoinRoom(Guid id, CancellationToken cancellation) => UpdateRoom(await hub!.InvokeAsync<CloudRoom>("JoinRoom", id, cancellation));
    public async Task LeaveRoom(CancellationToken cancellation)
    {
        CancelTransfer(); inspection?.Cancel(); await hub!.InvokeAsync("LeaveRoom", cancellation);
        Room = null; AvailableChart = null; localManifest = null; localChart = null; inspectedSelection = Guid.Empty; Notify();
    }
    public async Task SelectChart(CloudLocalChart chart, CancellationToken cancellation)
    {
        var room = Room ?? throw new InvalidOperationException("Join a room first.");
        if (room.HostId != User?.Id) throw new InvalidOperationException("Only the host may select a song.");
        var progress = new CallbackProgress<TransferProgress>(SetProgress);
        var manifest = await Task.Run(() => SongTransferFiles.Inspect(chart.Path, cancellation, progress, encoding()), cancellation);
        var updated = await hub!.InvokeAsync<CloudRoom>("SelectLocalChart", new { sha256 = manifest.ChartSha256, contentSha256 = manifest.ContentSha256, chart.Title, chart.Artist, chart.Keys, chart.Level }, room.Version, cancellation);
        UpdateRoom(updated);
    }
    public async Task Ready(CancellationToken cancellation)
    {
        var room = Room ?? throw new InvalidOperationException("Join a room first.");
        if (AvailableChart is null) throw new InvalidOperationException("Download and verify the song first.");
        UpdateRoom(await hub!.InvokeAsync<CloudRoom>("SetReady", !room.Members.Single(m => m.Id == User!.Id).Ready, room.Chart!.Sha256, room.Version, cancellation));
    }
    public async Task Upload(CancellationToken cancellation)
    {
        await RunTransfer(async ct =>
        {
            var room = Room ?? throw new InvalidOperationException("Join a room first.");
            var manifest = localManifest ?? throw new InvalidOperationException("Wait for local resource verification before uploading.");
            var chart = localChart ?? throw new InvalidOperationException("The selected local song is unavailable.");
            var progress = new CallbackProgress<TransferProgress>(SetProgress);
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
            var root = Path.Combine(SharedRoot, ".incoming"); Directory.CreateDirectory(root);
            var zip = Path.Combine(root, song.ArchiveSha256 + ".zip.part");
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
            AvailableChart = localChart = path; localManifest = song.Manifest; File.Delete(zip);
            await installed(path);
            await ReportContent(room, "available", ct);
            Progress = null; Status = "Installed to Shared"; Notify();
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
        CancelTransfer(); inspection?.Cancel();
        if (hub is not null) { await hub.DisposeAsync(); hub = null; }
        if (http is not null && token is not null)
            try { using var response = await http.PostAsync("api/auth/logout", null); } catch (HttpRequestException) { }
        http?.Dispose(); http = null; token = null; User = null; Room = null; Rooms = []; AvailableChart = null; inspectedSelection = Guid.Empty; Notify();
    }
    public async ValueTask DisposeAsync() { await Disconnect(); }
    private sealed class CallbackProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
}
