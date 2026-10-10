using System.Security.Cryptography;
using Npgsql;

namespace Cloud;

public sealed class ContentStore(Pg db, CloudOptions options)
{
    internal readonly SemaphoreSlim UploadGate = new(1);
    public string Root { get; } = Path.GetFullPath(options.StoragePath);
    public string FilePath(string key) => Path.Combine(Root, Path.GetFileName(key));

    public async Task<Guid> UploadPack(HttpRequest request, Guid uploader, CancellationToken cancellation)
    {
        if (!await UploadGate.WaitAsync(0, cancellation)) throw new ApiError(429, "Another upload is being validated. Try again shortly.");
        var key = Guid.NewGuid() + ".zip";
        var temporary = FilePath(key + ".pending");
        var destination = FilePath(key);
        bool committed = false;
        try
        {
            Directory.CreateDirectory(Root);
            var reserved = (long)(await db.Query("SELECT COALESCE(sum(size_bytes-received_bytes),0)::bigint AS pending FROM room_content WHERE state='uploading' AND deleted_at IS NULL"))[0]["pending"]!;
            if (Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) + reserved + options.MaxUploadBytes > options.MaxStorageBytes)
                throw new ApiError(507, "Content storage quota is exhausted.");
            if (!request.HasFormContentType) throw new ApiError(400, "Use a multipart ZIP upload.");
            var form = await request.ReadFormAsync(cancellation);
            var title = form["title"].ToString().Trim();
            var description = form["description"].ToString().Trim();
            var file = form.Files.GetFile("file");
            if (title.Length is < 1 or > 120 || description.Length > 2000 || file is null || form.Files.Count != 1 ||
                file.Length is <= 0 || file.Length > options.MaxUploadBytes || !Path.GetExtension(file.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                throw new ApiError(400, "Provide a title and a ZIP within the upload limit.");
            await using (var output = new FileStream(temporary, FileMode.CreateNew)) await file.CopyToAsync(output, cancellation);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            var charts = await ArchiveInspector.Inspect(temporary, options, timeout.Token);
            await using var input = File.OpenRead(temporary);
            var sha = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellation)).ToLowerInvariant();
            await input.DisposeAsync();
            File.Move(temporary, destination);
            var id = Guid.NewGuid();
            await using var connection = await db.Open();
            await using var tx = await connection.BeginTransactionAsync(cancellation);
            await using (var command = new NpgsqlCommand("INSERT INTO packs(id,title,description,file_key,sha256,size_bytes,uploader_id) VALUES(@id,@title,@description,@key,@sha,@size,@user)", connection, tx))
            {
                Pg.Add(command, ("id", id), ("title", title), ("description", description), ("key", key), ("sha", sha), ("size", file.Length), ("user", uploader));
                await command.ExecuteNonQueryAsync(cancellation);
            }
            foreach (var chart in charts)
            {
                await using var command = new NpgsqlCommand("""
                    WITH c AS (
                        INSERT INTO charts(id,sha256,md5,title,artist,difficulty,keys,level) VALUES(@id,@sha,@md5,@title,@artist,@difficulty,@keys,@level)
                        ON CONFLICT(sha256) DO UPDATE SET md5=CASE WHEN btrim(charts.md5)='' THEN EXCLUDED.md5 ELSE charts.md5 END RETURNING id
                    ) INSERT INTO pack_charts(pack_id,chart_id,path) SELECT @pack,id,@path FROM c RETURNING chart_id
                    """, connection, tx);
                Pg.Add(command, ("id", Guid.NewGuid()), ("sha", chart.Sha256), ("md5", chart.Md5), ("title", chart.Title), ("artist", chart.Artist),
                    ("difficulty", chart.Difficulty), ("keys", chart.Keys), ("level", chart.Level), ("pack", id), ("path", chart.Path));
                var identity = (Guid)(await Pg.Read(command)).Single()["chartId"]!;
                await using var board = new NpgsqlCommand("""
                    INSERT INTO ir_boards(id,chart_id,scope_key,owner_id,visibility,title,artist,difficulty,keys,level)
                    VALUES(@id,@id,'community',@owner,'hidden',@title,@artist,@difficulty,@keys,@level)
                    ON CONFLICT(chart_id,scope_key) DO UPDATE SET title=EXCLUDED.title,artist=EXCLUDED.artist,difficulty=EXCLUDED.difficulty,keys=EXCLUDED.keys,level=EXCLUDED.level
                    """, connection, tx);
                Pg.Add(board, ("id", identity), ("owner", uploader), ("title", chart.Title), ("artist", chart.Artist), ("difficulty", chart.Difficulty), ("keys", chart.Keys), ("level", chart.Level));
                await board.ExecuteNonQueryAsync(cancellation);
            }
            await using (var audit = new NpgsqlCommand("INSERT INTO audit_log(user_id,action,target) VALUES(@user,'pack.upload',@target)", connection, tx))
            {
                Pg.Add(audit, ("user", uploader), ("target", id.ToString()));
                await audit.ExecuteNonQueryAsync(cancellation);
            }
            await tx.CommitAsync(cancellation);
            committed = true;
            return id;
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                if (!committed && File.Exists(destination)) File.Delete(destination);
            }
            finally { UploadGate.Release(); }
        }
    }

    public async Task SaveAvatar(HttpRequest request, Guid user, CancellationToken cancellation)
    {
        if (!request.HasFormContentType) throw new ApiError(400, "Use a multipart avatar upload.");
        var form = await request.ReadFormAsync(cancellation);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length is < 8 or > 1048576 || form.Files.Count != 1) throw new ApiError(400, "Avatar must be a PNG or JPEG no larger than 1 MiB.");
        using var memory = new MemoryStream();
        await file.CopyToAsync(memory, cancellation);
        var bytes = memory.ToArray();
        bool png = bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        bool jpeg = bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
        if (!png && !jpeg) throw new ApiError(400, "Unsupported avatar format.");
        Directory.CreateDirectory(Root);
        string key = Guid.NewGuid() + (png ? ".png" : ".jpg");
        await File.WriteAllBytesAsync(FilePath(key), bytes, cancellation);
        var previous = await db.Query("SELECT avatar_key FROM users WHERE id=@id", ("id", user));
        try { await db.Query("UPDATE users SET avatar_key=@key WHERE id=@id", ("key", key), ("id", user)); }
        catch { File.Delete(FilePath(key)); throw; }
        if (previous.Single()["avatarKey"] is string old && File.Exists(FilePath(old))) File.Delete(FilePath(old));
    }
}
