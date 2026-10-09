using System.Security.Cryptography;
using System.Text.Json;
using LazerRave.Content;
using Microsoft.AspNetCore.SignalR;

namespace Cloud;

public sealed record UploadSongInput(Guid SelectionId, SongManifest Manifest, long SizeBytes, string ArchiveSha256);
public sealed record RoomContentView(Guid Id, Guid SelectionId, string State, long SizeBytes, long ReceivedBytes, string ArchiveSha256, SongManifest Manifest, DateTime ExpiresAt);

public sealed class RoomContentStore(Pg db, Rooms rooms, ContentStore content, CloudOptions options, IHubContext<RealtimeHub> hub, ILogger<RoomContentStore> logger)
{
    private SemaphoreSlim Gate => content.UploadGate;
    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    public string Root => Path.Combine(content.Root, "rooms");
    private string PathFor(Guid id, bool ready) => Path.Combine(Root, id.ToString("N") + (ready ? ".zip" : ".part"));
    private static RoomContentView View(Dictionary<string, object?> row) => new((Guid)row["id"]!, (Guid)row["selectionId"]!, (string)row["state"]!,
        (long)row["sizeBytes"]!, (long)row["receivedBytes"]!, (string)row["archiveSha256"]!,
        JsonSerializer.Deserialize<SongManifest>((string)row["manifest"]!, json)!, (DateTime)row["expiresAt"]!);
    private async Task<Dictionary<string, object?>> Row(Guid id) => (await db.Query("SELECT * FROM room_content WHERE id=@id", ("id", id))).FirstOrDefault() ?? throw new ApiError(404, "Shared song not found.");
    private void Authorize(Dictionary<string, object?> row, Guid user, bool upload)
    {
        if (upload && (Guid)row["uploaderId"]! != user) throw new ApiError(403, "This upload belongs to another player.");
        rooms.RequireSelection(user, (Guid)row["roomId"]!, (Guid)row["selectionId"]!, (string)row["contentSha256"]!, upload);
        if ((DateTime)row["expiresAt"]! <= DateTime.UtcNow || (string)row["state"]! == "expired") throw new ApiError(410, "The shared song has expired. Ask the host to upload it again.");
    }
    private async Task Broadcast(RoomView room)
    {
        await hub.Clients.Group(room.Id.ToString()).SendAsync("RoomUpdated", room);
        await hub.Clients.Group("lobby").SendAsync("RoomsChanged", rooms.List());
    }

    public async Task<RoomContentView> Begin(Guid user, Guid roomId, UploadSongInput input, CancellationToken cancellation)
    {
        SongContent.Validate(input.Manifest, options.MaxExpandedBytes);
        if (input.SizeBytes is <= 0 || input.SizeBytes > options.MaxUploadBytes || !SongContent.IsHash(input.ArchiveSha256))
            throw new ApiError(400, "Invalid ZIP size or checksum.");
        rooms.RequireSelection(user, roomId, input.SelectionId, input.Manifest.ContentSha256, true);
        var selected = rooms.Current(user)!.Chart!;
        if (selected.Sha256 != input.Manifest.ChartSha256) throw new ApiError(409, "The selected difficulty has changed.");
        if (!await Gate.WaitAsync(0, cancellation)) throw new ApiError(429, "An upload is being validated. Try again shortly.");
        try
        {
            await CleanupCore();
            rooms.RequireSelection(user, roomId, input.SelectionId, input.Manifest.ContentSha256, true);
            var existing = (await db.Query("SELECT * FROM room_content WHERE room_id=@room AND selection_id=@selection AND uploader_id=@user AND state IN ('uploading','ready')", ("room", roomId), ("selection", input.SelectionId), ("user", user))).FirstOrDefault();
            if (existing is not null)
            {
                if ((string)existing["archiveSha256"]! != input.ArchiveSha256 || (long)existing["sizeBytes"]! != input.SizeBytes)
                    throw new ApiError(409, "Resume the original ZIP or select the song again.");
                return View(existing);
            }
            var reusable = (await db.Query("SELECT * FROM room_content WHERE room_id=@room AND uploader_id=@user AND content_sha256=@content AND archive_sha256=@archive AND state='ready' AND expires_at>now() AND deleted_at IS NULL ORDER BY expires_at DESC LIMIT 1",
                ("room", roomId), ("user", user), ("content", input.Manifest.ContentSha256), ("archive", input.ArchiveSha256))).FirstOrDefault();
            if (reusable is not null && File.Exists(PathFor((Guid)reusable["id"]!, true)))
            {
                await db.Query("UPDATE room_content SET selection_id=@selection,chart_id=@chart,manifest=@manifest::jsonb WHERE id=@id",
                    ("selection", input.SelectionId), ("chart", selected.Id), ("manifest", JsonSerializer.Serialize(input.Manifest, json)), ("id", (Guid)reusable["id"]!));
                return View(await Row((Guid)reusable["id"]!));
            }
            var reserved = (await db.Query("SELECT COALESCE(sum(size_bytes),0)::bigint AS total,COALESCE(sum(size_bytes-received_bytes) FILTER(WHERE state='uploading'),0)::bigint AS pending FROM room_content WHERE deleted_at IS NULL"))[0];
            long used = Directory.Exists(content.Root) ? Directory.EnumerateFiles(content.Root, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;
            if ((long)reserved["total"]! + input.SizeBytes > options.MaxTemporaryBytes || used + (long)reserved["pending"]! + input.SizeBytes > options.MaxStorageBytes)
                throw new ApiError(507, "Temporary song storage is full. Try again after older shares expire.");
            var id = Guid.NewGuid();
            Directory.CreateDirectory(Root);
            await using (var output = new FileStream(PathFor(id, false), FileMode.CreateNew)) { }
            try
            {
                await db.Query("""
                    INSERT INTO room_content(id,room_id,selection_id,uploader_id,chart_id,content_sha256,archive_sha256,manifest,size_bytes,state,expires_at)
                    VALUES(@id,@room,@selection,@user,@chart,@content,@archive,@manifest::jsonb,@size,'uploading',@expiry)
                    """, ("id", id), ("room", roomId), ("selection", input.SelectionId), ("user", user), ("chart", selected.Id),
                    ("content", input.Manifest.ContentSha256), ("archive", input.ArchiveSha256), ("manifest", JsonSerializer.Serialize(input.Manifest, json)), ("size", input.SizeBytes), ("expiry", DateTime.UtcNow.AddHours(1)));
            }
            catch { File.Delete(PathFor(id, false)); throw; }
            return View(await Row(id));
        }
        finally { Gate.Release(); }
    }
    public async Task<RoomContentView> Status(Guid user, Guid id, bool upload = false)
    {
        var row = await Row(id); Authorize(row, user, upload); return View(row);
    }
    public async Task<RoomContentView> Append(Guid user, Guid id, long offset, HttpRequest request, CancellationToken cancellation)
    {
        if (request.ContentLength is not (> 0 and <= SongContent.ChunkBytes)) throw new ApiError(400, "Send a ZIP chunk of at most 8 MiB with Content-Length.");
        if (!await Gate.WaitAsync(0, cancellation)) throw new ApiError(429, "Another content operation is in progress. Retry this chunk.");
        try
        {
            var row = await Row(id); Authorize(row, user, true);
            var view = View(row);
            if (view.State != "uploading" || offset != view.ReceivedBytes || offset + request.ContentLength.Value > view.SizeBytes)
                throw new ApiError(409, "Upload offset changed. Read the upload status before retrying.");
            await using (var output = new FileStream(PathFor(id, false), FileMode.Open, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                output.SetLength(offset); output.Position = offset;
                long remaining = request.ContentLength.Value;
                var buffer = new byte[65536];
                try
                {
                    while (remaining > 0)
                    {
                        int count = await request.Body.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellation);
                        if (count == 0) throw new ApiError(400, "ZIP chunk was truncated.");
                        await output.WriteAsync(buffer.AsMemory(0, count), cancellation); remaining -= count;
                    }
                    await output.FlushAsync(cancellation);
                    Authorize(row, user, true);
                    await db.Query("UPDATE room_content SET received_bytes=@offset,expires_at=@expiry WHERE id=@id AND state='uploading'", ("offset", offset + request.ContentLength.Value), ("id", id), ("expiry", DateTime.UtcNow.AddHours(1)));
                }
                catch { output.SetLength(offset); throw; }
            }
            return View(await Row(id));
        }
        finally { Gate.Release(); }
    }
    public async Task<RoomContentView> Complete(Guid user, Guid id, CancellationToken cancellation)
    {
        if (!await Gate.WaitAsync(0, cancellation)) throw new ApiError(429, "Another ZIP is being validated. Try again shortly.");
        try
        {
            var row = await Row(id); Authorize(row, user, true);
            var view = View(row);
            if (view.State == "ready")
            {
                await Broadcast(rooms.AttachContent(user, (Guid)row["roomId"]!, view.SelectionId, view.Manifest.ContentSha256, id, view.ExpiresAt));
                return view;
            }
            if (view.ReceivedBytes != view.SizeBytes) throw new ApiError(409, "Upload is incomplete.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromMinutes(2));
            var temporary = PathFor(id, false);
            if (await SongContent.HashFile(temporary, deadline.Token) != view.ArchiveSha256) throw new ApiError(400, "Uploaded ZIP checksum is invalid.");
            var charts = await ArchiveInspector.Inspect(temporary, options, deadline.Token, view.Manifest);
            var chart = charts.Single(c => c.Path == view.Manifest.ChartPath);
            Authorize(row, user, true);
            var expires = DateTime.UtcNow.Add(SongContent.Lifetime);
            File.Move(temporary, PathFor(id, true), true);
            try
            {
                await db.Query("""
                    UPDATE charts SET md5=@md5,title=@title,artist=@artist,difficulty=@difficulty,keys=@keys,level=@level WHERE id=@chart;
                    UPDATE room_content SET state='ready',expires_at=@expiry WHERE id=@id;
                    """, ("md5", chart.Md5), ("title", chart.Title), ("artist", chart.Artist), ("difficulty", chart.Difficulty), ("keys", chart.Keys), ("level", chart.Level),
                    ("chart", (Guid)row["chartId"]!), ("expiry", expires), ("id", id));
            }
            catch { File.Move(PathFor(id, true), temporary, true); throw; }
            var completed = View(await Row(id));
            await Broadcast(rooms.AttachContent(user, (Guid)row["roomId"]!, view.SelectionId, view.Manifest.ContentSha256, id, completed.ExpiresAt));
            return completed;
        }
        finally { Gate.Release(); }
    }
    public async Task<IResult> Download(Guid user, Guid id)
    {
        var row = await Row(id); Authorize(row, user, false);
        var view = View(row);
        if (view.State != "ready" || !File.Exists(PathFor(id, true))) throw new ApiError(409, "The host has not finished uploading this song.");
        return new ExpiringFile(PathFor(id, true), view);
    }
    private sealed class ExpiringFile(string path, RoomContentView view) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            var remaining = view.ExpiresAt - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) throw new ApiError(410, "The shared song has expired.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            deadline.CancelAfter(remaining);
            context.RequestAborted = deadline.Token;
            context.Response.Headers.CacheControl = "private, no-store";
            var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await Results.File(file, "application/zip", $"LazerRave-Shared-{view.Id}.zip", enableRangeProcessing: true,
                entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue('"' + view.ArchiveSha256 + '"')).ExecuteAsync(context);
        }
    }
    public async Task Cleanup(CancellationToken cancellation)
    {
        if (!await Gate.WaitAsync(0, cancellation)) return;
        try { await CleanupCore(); }
        finally { Gate.Release(); }
    }
    private async Task CleanupCore()
    {
        var expired = await db.Query("UPDATE room_content SET state='expired' WHERE expires_at<=now() AND deleted_at IS NULL RETURNING id");
        foreach (var row in expired)
        {
            var id = (Guid)row["id"]!;
            try
            {
                foreach (var ready in new[] { false, true }) if (File.Exists(PathFor(id, ready))) File.Delete(PathFor(id, ready));
                await db.Query("UPDATE room_content SET deleted_at=now(),manifest='{}'::jsonb WHERE id=@id", ("id", id));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { logger.LogWarning(error, "Expired shared song {ContentId} could not be deleted", id); }
        }
        if (Directory.Exists(Root))
            foreach (var file in Directory.EnumerateFiles(Root))
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddHours(-1) && Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out var id) &&
                    (await db.Query("SELECT id FROM room_content WHERE id=@id AND deleted_at IS NULL", ("id", id))).Count == 0)
                    try { File.Delete(file); } catch (IOException error) { logger.LogWarning(error, "Orphaned shared song could not be deleted"); }
    }
}

public sealed class RoomContentCleanup(RoomContentStore content, ILogger<RoomContentCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try { await content.Cleanup(stoppingToken); }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested) { logger.LogWarning(error, "Shared song cleanup failed; retrying next minute"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
