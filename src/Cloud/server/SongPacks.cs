using Microsoft.Extensions.Primitives;

namespace Cloud;

public sealed class SongPacks(Pg db, ContentStore content)
{
    public static string DownloadName(string title, Guid id)
    {
        var name = System.Text.RegularExpressions.Regex.Replace(title, "[\\x00-\\x1f<>:\"/\\\\|?*]", "_").Trim(' ', '.');
        if (name.Length == 0 || System.Text.RegularExpressions.Regex.IsMatch(name, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\\.)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            name = "LazerRave-" + id;
        return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? name : name + ".zip";
    }

    public static void Validate(string? query, int keys, string sort, int page)
    {
        if (query?.Length > 120 || !new[] { 0, 5, 7, 9, 10, 14 }.Contains(keys) || !new[] { "title", "newest", "difficulty" }.Contains(sort) || page is < 1 or > 10000)
            throw new ApiError(400, "Invalid song pack filters.");
    }

    public async Task<object> Catalog(string? query, int keys, string sort, int page)
    {
        Validate(query, keys, sort, page);
        const int size = 20;
        const string filter = "p.published AND (@q='' OR p.title ILIKE @q OR p.description ILIKE @q) AND (@keys=0 OR EXISTS(SELECT 1 FROM pack_charts pc JOIN charts c ON c.id=pc.chart_id WHERE pc.pack_id=p.id AND c.keys=@keys))";
        var q = string.IsNullOrWhiteSpace(query) ? "" : "%" + query.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        var total = (long)(await db.Query("SELECT count(*) AS total FROM packs p WHERE " + filter, ("q", q), ("keys", keys))).Single()["total"]!;
        var order = sort switch { "newest" => "p.created_at DESC,p.id", "difficulty" => "minimum_level,p.title,p.id", _ => "p.title,p.id" };
        var items = await db.Query("""
            SELECT p.id,p.title,p.description,p.sha256,p.size_bytes,p.created_at,
              count(DISTINCT c.id)::int AS chart_count,
              COALESCE(array_agg(DISTINCT c.keys ORDER BY c.keys) FILTER(WHERE c.id IS NOT NULL),ARRAY[]::int[]) AS keys,
              COALESCE(min(c.level),0) AS minimum_level,COALESCE(max(c.level),0) AS maximum_level
            FROM packs p LEFT JOIN pack_charts pc ON pc.pack_id=p.id LEFT JOIN charts c ON c.id=pc.chart_id
            WHERE
            """ + " " + filter + " GROUP BY p.id ORDER BY " + order + " LIMIT @size OFFSET @offset", ("q", q), ("keys", keys), ("size", size), ("offset", (page - 1) * size));
        return new { items, total, page, pageSize = size };
    }

    public async Task Publish(Guid id, Guid user, bool published)
    {
        var rows = await db.Query("UPDATE packs SET published=@published WHERE id=@id RETURNING id", ("id", id), ("published", published));
        if (rows.Count == 0) throw new ApiError(404, "Pack not found.");
        if (published) await db.Query("UPDATE ir_boards b SET visibility='public',approved=true WHERE b.scope_key='community' AND (b.visibility IN ('public','unlisted') OR (b.visibility='hidden' AND NOT b.moderated_hidden)) AND EXISTS(SELECT 1 FROM pack_charts pc WHERE pc.pack_id=@id AND pc.chart_id=b.chart_id)", ("id", id));
        await db.Query("INSERT INTO audit_log(user_id,action,target) VALUES(@user,@action,@target)", ("user", user), ("action", published ? "pack.publish" : "pack.unpublish"), ("target", id.ToString()));
    }

    public async Task<Guid> Import(string username, string zip, string title, CancellationToken cancellation)
    {
        var account = (await db.Query("SELECT id FROM users WHERE lower(username)=lower(@name) AND role='admin' AND NOT disabled", ("name", username))).SingleOrDefault()
            ?? throw new InvalidOperationException("A registered administrator is required for pack import.");
        var user = (Guid)account["id"]!;
        await using var stream = File.OpenRead(zip);
        var sha = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellation)).ToLowerInvariant();
        var existing = await db.Query("SELECT id FROM packs WHERE sha256=@sha ORDER BY published DESC,created_at LIMIT 1", ("sha", sha));
        Guid id;
        if (existing.Count > 0) id = (Guid)existing[0]["id"]!;
        else
        {
            stream.Position = 0;
            var request = new DefaultHttpContext().Request;
            request.ContentType = "multipart/form-data; boundary=import";
            request.Form = new FormCollection(new Dictionary<string, StringValues> { ["title"] = title, ["description"] = "" },
                new FormFileCollection { new FormFile(stream, 0, stream.Length, "file", Path.GetFileName(zip)) { Headers = new HeaderDictionary(), ContentType = "application/zip" } });
            id = await content.UploadPack(request, user, cancellation);
        }
        await Publish(id, user, true);
        return id;
    }
}
