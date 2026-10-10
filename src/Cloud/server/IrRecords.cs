namespace Cloud;

public sealed class IrRecords(Pg db)
{
    private const string scoreFields = "s.id,s.user_id,s.chart_id,s.board_id,s.client_run_id,s.ruleset,s.arrangement,s.gauge,s.perfect,s.great,s.good,s.bad,s.poor,s.max_combo,s.clear,s.score_max,s.normal_score,s.input_type,s.comment,s.ex_score,s.verified,s.created_at,s.played_at";
    public async Task<List<Dictionary<string, object?>>> Compare(long uid, long against, int page)
    {
        if (uid <= 0 || against <= 0 || uid == against || page is < 1 or > 10000)
            throw new ApiError(400, "Choose two different player UIDs and a valid page.");
        var players = await db.Query("SELECT uid FROM users WHERE uid IN (@uid,@against) AND NOT disabled", ("uid", uid), ("against", against));
        if (players.Count != 2) throw new ApiError(404, "Player not found.");
        return await db.Query("""
            WITH best AS (
                SELECT DISTINCT ON (s.board_id,u.uid) s.board_id,u.uid,s.id,s.ex_score,s.score_max,
                    min(s.bad+s.poor) OVER(PARTITION BY s.board_id,u.uid) AS min_misses,
                    max(CASE s.clear WHEN 'perfect' THEN 6 WHEN 'full-combo' THEN 5 WHEN 'hard' THEN 4
                        WHEN 'normal' THEN 3 WHEN 'easy' THEN 2 WHEN 'assist' THEN 1 ELSE 0 END) OVER(PARTITION BY s.board_id,u.uid) AS lamp
                FROM scores s JOIN users u ON u.id=s.user_id JOIN ir_boards b ON b.id=s.board_id
                WHERE u.uid IN (@uid,@against) AND NOT u.disabled AND NOT s.withdrawn AND s.ruleset='openlr2-v1' AND b.visibility='public'
                ORDER BY s.board_id,u.uid,s.ex_score DESC,s.created_at,s.id
            )
            SELECT b.id AS chart_id,b.title,b.keys,b.level,a.id AS score_id,r.id AS rival_score_id,
                a.ex_score,r.ex_score AS rival_ex_score,a.ex_score-r.ex_score AS difference,
                a.score_max,a.min_misses,r.min_misses AS rival_min_misses,a.lamp,r.lamp AS rival_lamp
            FROM best a JOIN best r ON r.board_id=a.board_id AND r.uid=@against JOIN ir_boards b ON b.id=a.board_id
            WHERE a.uid=@uid ORDER BY b.title,b.id LIMIT 50 OFFSET @offset
            """, ("uid", uid), ("against", against), ("offset", (page - 1) * 50));
    }
    public async Task<Dictionary<string, object?>> Detail(Guid id, Guid viewer)
    {
        var rows = await db.Query($"""
            SELECT {scoreFields},b.id AS chart_id,b.title,b.artist,b.difficulty,b.keys,b.level,b.visibility,c.sha256,c.md5,
                u.uid,u.username,u.display_name,
                CASE WHEN u.avatar_key IS NULL THEN NULL ELSE '/api/users/'||u.id::text||'/avatar?v='||u.avatar_key END AS avatar_url,
                false AS replay_available
            FROM scores s JOIN ir_boards b ON b.id=s.board_id JOIN charts c ON c.id=b.chart_id JOIN users u ON u.id=s.user_id
            WHERE s.id=@id AND NOT s.withdrawn AND NOT u.disabled AND {ChartRegistry.Readable}
            """, ("id", id), ("viewer", viewer));
        return rows.FirstOrDefault() ?? throw new ApiError(404, "Score not found.");
    }
    public Task<List<Dictionary<string, object?>>> Player(long uid, Guid viewer, int page, bool recent)
    {
        if (page is < 1 or > 10000) throw new ApiError(400, "Invalid records page.");
        string query = $"""
            WITH visible AS (
                SELECT {scoreFields},b.title,b.keys,b.level,b.visibility,b.id AS ranking_id,
                    s.bad+s.poor AS misses,
                    min(s.bad+s.poor) OVER(PARTITION BY s.board_id) AS min_misses,
                    max(CASE s.clear WHEN 'perfect' THEN 6 WHEN 'full-combo' THEN 5 WHEN 'hard' THEN 4
                        WHEN 'normal' THEN 3 WHEN 'easy' THEN 2 WHEN 'assist' THEN 1 ELSE 0 END) OVER(PARTITION BY s.board_id) AS lamp,
                    row_number() OVER(PARTITION BY s.board_id ORDER BY s.ex_score DESC,s.created_at,s.id) AS best
                FROM scores s JOIN ir_boards b ON b.id=s.board_id JOIN users u ON u.id=s.user_id
                WHERE u.uid=@uid AND NOT u.disabled AND NOT s.withdrawn AND s.ruleset='openlr2-v1'
                    AND (b.visibility='public' OR (u.id=@viewer AND {ChartRegistry.Readable}))
            )
            SELECT *,ranking_id AS chart_id,
                CASE lamp WHEN 6 THEN 'perfect' WHEN 5 THEN 'full-combo' WHEN 4 THEN 'hard' WHEN 3 THEN 'normal' WHEN 2 THEN 'easy' WHEN 1 THEN 'assist' ELSE 'failed' END AS best_clear
            FROM visible WHERE (@recent OR best=1) ORDER BY played_at DESC NULLS LAST,created_at DESC,id LIMIT 50 OFFSET @offset
            """;
        return db.Query(query, ("uid", uid), ("viewer", viewer), ("recent", recent), ("offset", (page - 1) * 50));
    }
    public async Task<List<Dictionary<string, object?>>> History(Guid board, Guid viewer)
    {
        await new ChartRegistry(db).Require(board, viewer);
        return await db.Query($"""
            SELECT s.id,s.ex_score,s.clear,s.bad+s.poor AS misses,s.created_at,s.played_at,s.verified,s.arrangement,s.gauge
            FROM scores s JOIN ir_boards b ON b.id=s.board_id
            WHERE s.board_id=@board AND s.user_id=@viewer AND NOT s.withdrawn AND {ChartRegistry.Readable}
            ORDER BY s.played_at DESC NULLS LAST,s.created_at DESC,s.id LIMIT 100
            """, ("board", board), ("viewer", viewer));
    }
    public async Task Review(Guid id, Guid actor, ScoreReviewInput input)
    {
        if (input.Reason?.Trim().Length is not (>= 1 and <= 400)) throw new ApiError(400, "Provide a moderation reason.");
        var rows = await db.Query("UPDATE scores SET withdrawn=@withdrawn,review_note=@reason WHERE id=@id RETURNING id", ("id", id), ("withdrawn", input.Withdrawn), ("reason", input.Reason.Trim()));
        if (rows.Count == 0) throw new ApiError(404, "Score not found.");
        await db.Query("INSERT INTO audit_log(user_id,action,target) VALUES(@actor,@action,@target)", ("actor", actor), ("action", input.Withdrawn ? "ir.score.withdraw" : "ir.score.restore"), ("target", id + ":" + input.Reason.Trim()));
    }
}
