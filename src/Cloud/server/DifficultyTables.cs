using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace Cloud;

public sealed record TableEntryInput(string Md5, string Level, string Title = "", string Artist = "", string? Url = null);
public sealed record TableInput(string Name, string Symbol, string Description, string? SourceUrl, TableEntryInput[] Entries);

public sealed class DifficultyTables(Pg db)
{
    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    public static bool ValidUrl(string? value) => value is null || Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && value.Length <= 1000;
    public static TableInput Validate(TableInput input)
    {
        if (input.Name?.Trim().Length is not (>= 1 and <= 120) || input.Symbol?.Trim().Length is not (>= 1 and <= 16) ||
            input.Description is null || input.Description.Length > 2000 || !ValidUrl(input.SourceUrl) || input.Entries?.Length is not (>= 1 and <= 10000))
            throw new ApiError(400, "Provide a table name, symbol and 1–10,000 charts; source links must use HTTP or HTTPS.");
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = input.Entries.Select(entry =>
        {
            if (entry is null || entry.Md5 is null || !Regex.IsMatch(entry.Md5, "\\A[0-9a-fA-F]{32}\\z") || !hashes.Add(entry.Md5) ||
                entry.Level?.Trim().Length is not (>= 1 and <= 24) || entry.Title is null || entry.Title.Length > 200 ||
                entry.Artist is null || entry.Artist.Length > 200 || !ValidUrl(entry.Url))
                throw new ApiError(400, "Each chart needs a unique MD5 and a level; check title, artist and link lengths.");
            return entry with { Md5 = entry.Md5.ToLowerInvariant(), Level = entry.Level.Trim() };
        }).ToArray();
        return input with { Name = input.Name.Trim(), Symbol = input.Symbol.Trim(), Entries = entries };
    }
    public Task<List<Dictionary<string, object?>>> List(bool admin) => db.Query("""
        SELECT t.*,count(e.md5) AS chart_count,count(DISTINCT e.level) AS level_count
        FROM difficulty_tables t LEFT JOIN difficulty_table_entries e ON e.table_id=t.id
        WHERE t.published OR @admin GROUP BY t.id ORDER BY t.name,t.id
        """, ("admin", admin));
    public async Task<Dictionary<string, object?>> Require(long id, bool admin)
    {
        var rows = await db.Query("SELECT * FROM difficulty_tables WHERE id=@id AND (published OR @admin)", ("id", id), ("admin", admin));
        return rows.FirstOrDefault() ?? throw new ApiError(404, "Difficulty table not found.");
    }
    public async Task<long> Save(Guid owner, long? id, TableInput input)
    {
        input = Validate(input);
        await using var connection = await db.Open();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var save = new NpgsqlCommand(id is null ? """
            INSERT INTO difficulty_tables(name,symbol,description,source_url,owner_id)
            VALUES(@name,@symbol,@description,@source,@owner) RETURNING id
            """ : """
            UPDATE difficulty_tables SET name=@name,symbol=@symbol,description=@description,source_url=@source,updated_at=now()
            WHERE id=@id RETURNING id
            """, connection, transaction);
        Pg.Add(save, ("name", input.Name), ("symbol", input.Symbol), ("description", input.Description),
            ("source", input.SourceUrl), ("owner", owner), ("id", id ?? 0));
        var result = await save.ExecuteScalarAsync();
        if (result is not long tableId) throw new ApiError(404, "Difficulty table not found.");
        await using var entries = new NpgsqlCommand("""
            DELETE FROM difficulty_table_entries WHERE table_id=@id;
            INSERT INTO difficulty_table_entries(table_id,md5,level,title,artist,url,position)
            SELECT @id,item->>'md5',item->>'level',item->>'title',item->>'artist',item->>'url',position-1
            FROM jsonb_array_elements(@entries::jsonb) WITH ORDINALITY AS imported(item,position);
            INSERT INTO audit_log(user_id,action,target) VALUES(@owner,'table.save',@target)
            """, connection, transaction);
        Pg.Add(entries, ("id", tableId), ("entries", JsonSerializer.Serialize(input.Entries, json)),
            ("owner", owner), ("target", tableId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        await entries.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return tableId;
    }
    public async Task<object> Detail(long id, bool admin)
    {
        var table = await Require(id, admin);
        var levels = await db.Query("""
            SELECT level,count(*) AS chart_count,min(position) AS position FROM difficulty_table_entries
            WHERE table_id=@id GROUP BY level ORDER BY min(position),level
            """, ("id", id));
        return new { table, levels };
    }
    public async Task<object> Entries(long id, string? level, int page, bool admin)
    {
        if (page is < 1 or > 10000 || level?.Length > 24) throw new ApiError(400, "Invalid table level or page.");
        await Require(id, admin);
        return await db.Query("""
            SELECT e.md5,e.level,COALESCE(NULLIF(c.title,''),e.title) AS title,COALESCE(NULLIF(c.artist,''),e.artist) AS artist,
                e.url,c.id AS chart_id,c.keys,c.level AS chart_level
            FROM difficulty_table_entries e
            LEFT JOIN LATERAL (SELECT c.* FROM charts c WHERE c.md5=e.md5 AND
                EXISTS(SELECT 1 FROM pack_charts pc JOIN packs p ON p.id=pc.pack_id WHERE pc.chart_id=c.id AND p.published)
                ORDER BY c.created_at,c.id LIMIT 1) c ON true
            WHERE e.table_id=@id AND (@level='' OR e.level=@level)
            ORDER BY e.position,e.md5 LIMIT 50 OFFSET @offset
            """, ("id", id), ("level", level ?? ""), ("offset", (page - 1) * 50));
    }
}

public static class DifficultyTableEndpoints
{
    public static void MapDifficultyTables(this WebApplication app)
    {
        app.MapGet("/api/tables", (DifficultyTables tables) => tables.List(false));
        app.MapGet("/api/tables/{id:long}", (long id, HttpContext context, DifficultyTables tables) => tables.Detail(id, context.User.IsInRole("admin")));
        app.MapGet("/api/tables/{id:long}/entries", (long id, string? level, int? page, HttpContext context, DifficultyTables tables) => tables.Entries(id, level, page ?? 1, context.User.IsInRole("admin")));
        app.MapGet("/api/tables/{id:long}/header.json", async (long id, DifficultyTables tables, CloudOptions options) =>
        {
            var table = await tables.Require(id, false);
            var levels = await app.Services.GetRequiredService<Pg>().Query("SELECT level FROM difficulty_table_entries WHERE table_id=@id GROUP BY level ORDER BY min(position)", ("id", id));
            return Results.Ok(new { name = table["name"], symbol = table["symbol"], level_order = levels.Select(row => row["level"]),
                data_url = $"{options.PublicOrigin.TrimEnd('/')}/api/tables/{id}/data.json" });
        });
        app.MapGet("/api/tables/{id:long}/data.json", async (long id, DifficultyTables tables, Pg db) =>
        {
            await tables.Require(id, false);
            return Results.Ok(await db.Query("SELECT md5,level,title,artist,url FROM difficulty_table_entries WHERE table_id=@id ORDER BY position", ("id", id)));
        });
        var admin = app.MapGroup("/api/admin/tables").RequireAuthorization("admin");
        admin.MapGet("", (DifficultyTables tables) => tables.List(true));
        admin.MapPost("", async (TableInput input, HttpContext context, DifficultyTables tables) => Results.Ok(new { id = await tables.Save(Auth.Id(context.User), null, input) }));
        admin.MapPut("/{id:long}", async (long id, TableInput input, HttpContext context, DifficultyTables tables) => Results.Ok(new { id = await tables.Save(Auth.Id(context.User), id, input) }));
        admin.MapPut("/{id:long}/publication", async (long id, PublicationInput input, HttpContext context, Pg db) =>
        {
            var rows = await db.Query("""
                WITH changed AS (UPDATE difficulty_tables SET published=@published,updated_at=now() WHERE id=@id RETURNING id)
                INSERT INTO audit_log(user_id,action,target) SELECT @user,'table.publication',id::text FROM changed RETURNING id
                """, ("id", id), ("published", input.Published), ("user", Auth.Id(context.User)));
            if (rows.Count == 0) throw new ApiError(404, "Difficulty table not found.");
            return Results.NoContent();
        });
    }
}
