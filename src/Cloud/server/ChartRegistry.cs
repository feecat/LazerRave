using System.Text.RegularExpressions;
using Npgsql;

namespace Cloud;

public sealed record ChartRegistration(string Sha256, string Md5, string Title, string Artist, string Difficulty,
    int Keys, int Level, string Visibility = "public", double? Bpm = null, double? LengthMs = null);
public sealed record BoardVisibilityInput(string Visibility, bool ConfirmPublication = false);
public sealed record BoardMemberInput(long Uid);
public sealed record ScoreReviewInput(bool Withdrawn, string Reason);

public sealed class ChartRegistry(Pg db)
{
    public const string Readable = "(b.visibility IN ('public','unlisted') OR (b.visibility='restricted' AND (b.owner_id=@viewer OR EXISTS(SELECT 1 FROM ir_board_members m WHERE m.board_id=b.id AND m.user_id=@viewer))))";
    public const string Fields = "b.id,b.chart_id,c.sha256,c.md5,b.title,b.artist,b.difficulty,b.display_difficulty,b.song_key,b.song_title,b.keys,b.level,b.bpm,b.length_ms,b.visibility,b.approved,b.owner_id,b.created_at,(SELECT pc.pack_id FROM pack_charts pc JOIN packs p ON p.id=pc.pack_id WHERE pc.chart_id=c.id AND p.published ORDER BY p.created_at,p.id LIMIT 1) AS pack_id";
    public static Guid Viewer(HttpContext context) => context.User.Identity?.IsAuthenticated == true ? Auth.Id(context.User) : Guid.Empty;
    public static ChartRegistration Validate(ChartRegistration input)
    {
        if (input.Sha256 is null || !Regex.IsMatch(input.Sha256, "\\A[0-9a-fA-F]{64}\\z") || input.Md5 is null || !Regex.IsMatch(input.Md5, "\\A[0-9a-fA-F]{32}\\z") ||
            input.Title?.Trim().Length is not (>= 1 and <= 200) || input.Artist is null || input.Artist.Length > 200 || input.Difficulty is null || input.Difficulty.Length > 120 ||
            !new[] { 5, 7, 9, 10, 14 }.Contains(input.Keys) || input.Level is < 0 or > 999 ||
            input.Visibility is not ("public" or "unlisted" or "restricted") ||
            input.Bpm is { } bpm && (!double.IsFinite(bpm) || bpm is < 0 or > 100000) ||
            input.LengthMs is { } length && (!double.IsFinite(length) || length is < 0 or > 86400000))
            throw new ApiError(400, "Invalid chart registration. Use a file SHA-256, MD5 and valid chart metadata.");
        return input with { Sha256 = input.Sha256.ToLowerInvariant(), Md5 = input.Md5.ToLowerInvariant(), Title = input.Title.Trim() };
    }
    public async Task<Dictionary<string, object?>> Require(Guid board, Guid viewer)
    {
        var rows = await db.Query($"SELECT {Fields} FROM ir_boards b JOIN charts c ON c.id=b.chart_id WHERE b.id=@board AND {Readable}", ("board", board), ("viewer", viewer));
        return rows.FirstOrDefault() ?? throw new ApiError(404, "Chart not found.");
    }
    public async Task<Dictionary<string, object?>> Register(Guid owner, ChartRegistration input)
    {
        input = Validate(input);
        await using var connection = await db.Open();
        await using var transaction = await connection.BeginTransactionAsync();
        await SongClassificationStore.Lock(connection, transaction);
        await using var chartCommand = new NpgsqlCommand("""
            INSERT INTO charts(id,sha256,md5,title,artist,difficulty,keys,level) VALUES(@id,@sha,@md5,@title,@artist,@difficulty,@keys,@level)
            ON CONFLICT(sha256) DO UPDATE SET md5=CASE WHEN btrim(charts.md5)='' THEN EXCLUDED.md5 ELSE charts.md5 END RETURNING id,md5
            """, connection, transaction);
        Pg.Add(chartCommand, ("id", Guid.NewGuid()), ("sha", input.Sha256), ("md5", input.Md5), ("title", input.Title), ("artist", input.Artist), ("difficulty", input.Difficulty), ("keys", input.Keys), ("level", input.Level));
        var chart = (await Pg.Read(chartCommand)).Single();
        if (!string.Equals((string)chart["md5"]!, input.Md5, StringComparison.OrdinalIgnoreCase)) throw new ApiError(409, "The SHA-256 and MD5 do not match the registered identity.");
        Guid chartId = (Guid)chart["id"]!;
        bool restricted = input.Visibility == "restricted";
        await using var boardCommand = new NpgsqlCommand("""
            INSERT INTO ir_boards(id,chart_id,scope_key,owner_id,visibility,title,artist,difficulty,keys,level,bpm,length_ms)
            VALUES(@id,@chart,@scope,@owner,@visibility,@title,@artist,@difficulty,@keys,@level,@bpm,@length)
            ON CONFLICT(chart_id,scope_key) DO NOTHING
            """, connection, transaction);
        string scope = restricted ? "user:" + owner.ToString("N") : "community";
        Pg.Add(boardCommand, ("id", restricted ? Guid.NewGuid() : chartId), ("chart", chartId), ("scope", scope), ("owner", owner), ("visibility", input.Visibility),
            ("title", input.Title), ("artist", input.Artist), ("difficulty", input.Difficulty), ("keys", input.Keys), ("level", input.Level),
            ("bpm", input.Bpm ?? 0), ("length", input.LengthMs ?? 0));
        if (await boardCommand.ExecuteNonQueryAsync() > 0)
            await SongClassificationStore.Refresh(connection, transaction);
        await using var read = new NpgsqlCommand($"SELECT {Fields} FROM ir_boards b JOIN charts c ON c.id=b.chart_id WHERE b.chart_id=@chart AND b.scope_key=@scope AND {Readable}", connection, transaction);
        Pg.Add(read, ("chart", chartId), ("scope", scope), ("viewer", owner));
        var result = (await Pg.Read(read)).FirstOrDefault() ?? throw new ApiError(404, "Chart not found.");
        await transaction.CommitAsync();
        return result;
    }
    public async Task<Dictionary<string, object?>> Resolve(string sha256, Guid viewer)
    {
        if (!Regex.IsMatch(sha256, "\\A[0-9a-fA-F]{64}\\z")) throw new ApiError(400, "Invalid chart hash.");
        var rows = await db.Query($"SELECT {Fields} FROM ir_boards b JOIN charts c ON c.id=b.chart_id WHERE c.sha256=@sha AND b.scope_key='community' AND {Readable}", ("sha", sha256.ToLowerInvariant()), ("viewer", viewer));
        return rows.FirstOrDefault() ?? throw new ApiError(404, "Chart not found.");
    }
    public Task<List<Dictionary<string, object?>>> List(string? query, int? keys, int page, Guid viewer, bool mine, int? minimum, int? maximum, string sort = "newest", string? difficulty = null)
    {
        if (page is < 1 or > 10000 || query?.Length > 100 || keys is not null && !new[] { 5, 7, 9, 10, 14 }.Contains(keys.Value) || minimum is < 0 or > 999 || maximum is < 0 or > 999 || minimum > maximum)
            throw new ApiError(400, "Invalid chart filter.");
        string order = sort switch
        {
            "newest" => "b.created_at DESC,b.id",
            "recent" => "stats.last_played_at DESC NULLS LAST,b.id",
            "plays" => "stats.play_count DESC,b.created_at DESC,b.id",
            "level-asc" => "b.level,b.title,b.id",
            "level-desc" => "b.level DESC,b.title,b.id",
            "title" => "b.title,b.level,b.id",
            _ => throw new ApiError(400, "Invalid chart sort."),
        };
        if (difficulty?.Length > 120) throw new ApiError(400, "Invalid chart filter.");
        return db.Query($"""
            SELECT {Fields},stats.play_count,stats.player_count,stats.last_played_at
            FROM ir_boards b JOIN charts c ON c.id=b.chart_id
            LEFT JOIN LATERAL (SELECT count(*) AS play_count,count(DISTINCT s.user_id) AS player_count,max(s.played_at) AS last_played_at
                FROM scores s JOIN users u ON u.id=s.user_id WHERE s.board_id=b.id AND NOT s.withdrawn AND NOT u.disabled) stats ON true
            WHERE {Readable} AND ((NOT @mine AND b.visibility='public') OR (@mine AND (b.owner_id=@viewer OR EXISTS(SELECT 1 FROM ir_board_members m WHERE m.board_id=b.id AND m.user_id=@viewer) OR EXISTS(SELECT 1 FROM scores s WHERE s.board_id=b.id AND s.user_id=@viewer AND NOT s.withdrawn))))
                AND (@keys=0 OR b.keys=@keys) AND (b.title ILIKE @query OR b.artist ILIKE @query)
                AND b.level>=@minimum AND b.level<=@maximum
                AND (@difficulty='' OR lower(b.difficulty)=lower(@difficulty))
            ORDER BY {order} LIMIT 50 OFFSET @offset
            """, ("viewer", viewer), ("mine", mine), ("keys", keys ?? 0), ("query", "%" + (query ?? "") + "%"), ("minimum", minimum ?? 0), ("maximum", maximum ?? 999), ("difficulty", difficulty ?? ""), ("offset", (page - 1) * 50));
    }
    private async Task<Dictionary<string, object?>> Owned(Guid id, Guid viewer, bool admin)
    {
        var rows = await db.Query("SELECT * FROM ir_boards WHERE id=@id AND (owner_id=@viewer OR @admin)", ("id", id), ("viewer", viewer), ("admin", admin));
        return rows.FirstOrDefault() ?? throw new ApiError(404, "Chart not found.");
    }
    public Task<List<Dictionary<string, object?>>> Administration(string? query, int page)
    {
        if (query?.Length > 100 || page is < 1 or > 10000) throw new ApiError(400, "Invalid chart filter.");
        return db.Query($"""
            SELECT {Fields} FROM ir_boards b JOIN charts c ON c.id=b.chart_id
            WHERE b.scope_key='community' AND (b.title ILIKE @query OR b.artist ILIKE @query)
            ORDER BY b.created_at DESC,b.id LIMIT 50 OFFSET @offset
            """, ("query", "%" + (query ?? "") + "%"), ("offset", (page - 1) * 50));
    }
    public async Task Visibility(Guid id, Guid actor, bool admin, BoardVisibilityInput input)
    {
        var board = await Owned(id, actor, admin);
        string old = (string)board["visibility"]!;
        if (input.Visibility is not ("public" or "unlisted" or "hidden") || (!admin && (old != "unlisted" || input.Visibility != "public")) || old == "restricted")
            throw new ApiError(400, "Private rankings stay private. Create a separate community ranking to publish scores.");
        if (input.Visibility == "public" && !input.ConfirmPublication) throw new ApiError(400, "Confirm publication of chart metadata and community scores.");
        await db.Query("""
            UPDATE ir_boards SET visibility=@visibility,moderated_hidden=(@visibility='hidden') WHERE id=@id;
            INSERT INTO audit_log(user_id,action,target) VALUES(@actor,'ir.visibility',@target)
            """, ("id", id), ("visibility", input.Visibility), ("actor", actor), ("target", id + ":" + input.Visibility));
    }
    public async Task<List<Dictionary<string, object?>>> Members(Guid id, Guid actor, bool admin)
    {
        await Owned(id, actor, admin);
        return await db.Query("SELECT u.uid,u.display_name,u.username FROM ir_board_members m JOIN users u ON u.id=m.user_id WHERE m.board_id=@id ORDER BY u.uid", ("id", id));
    }
    public async Task Member(Guid id, Guid actor, bool admin, long uid, bool remove)
    {
        var board = await Owned(id, actor, admin);
        if ((string)board["visibility"]! != "restricted") throw new ApiError(400, "Members apply only to private rankings.");
        var user = (await db.Query("SELECT id FROM users WHERE uid=@uid AND NOT disabled", ("uid", uid))).FirstOrDefault() ?? throw new ApiError(404, "Player not found.");
        await db.Query(remove ? "DELETE FROM ir_board_members WHERE board_id=@board AND user_id=@user" : "INSERT INTO ir_board_members(board_id,user_id) VALUES(@board,@user) ON CONFLICT DO NOTHING", ("board", id), ("user", user["id"]));
        await db.Query("INSERT INTO audit_log(user_id,action,target) VALUES(@actor,@action,@target)", ("actor", actor), ("action", remove ? "ir.member.remove" : "ir.member.add"), ("target", id + ":" + uid));
    }
}
