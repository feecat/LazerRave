namespace Cloud;

public sealed record ScoreInput(Guid ChartId, Guid ClientRunId, string Ruleset, string Arrangement, string Gauge,
    int Perfect, int Great, int Good, int Bad, int Poor, int MaxCombo, string Clear, bool Autoplay = false, bool Assist = false, bool Modifiers = false);

public sealed class Ranking(Pg db)
{
    public static readonly string[] Arrangements = ["off", "mirror", "random", "s-random", "scatter", "converge"];
    public static readonly string[] Gauges = ["normal", "hard", "death", "easy", "p-attack", "g-attack"];
    public static void Validate(ScoreInput score)
    {
        int[] judgements = [score.Perfect, score.Great, score.Good, score.Bad, score.Poor];
        if (score.ChartId == Guid.Empty || score.ClientRunId == Guid.Empty || score.Ruleset != "openlr2-v1" ||
            !Arrangements.Contains(score.Arrangement) || !Gauges.Contains(score.Gauge) ||
            !new[] { "failed", "assist", "easy", "normal", "hard", "full-combo", "perfect" }.Contains(score.Clear) ||
            judgements.Any(n => n is < 0 or > 1000000) || score.MaxCombo < 0 || score.MaxCombo > judgements.Sum() ||
            score.Autoplay || score.Assist || score.Modifiers)
            throw new ApiError(400, "Invalid or ranking-ineligible score. Autoplay, assists and MANIAC modifiers are excluded.");
    }

    public async Task<Dictionary<string, object?>> Submit(Guid user, ScoreInput score)
    {
        Validate(score);
        var rows = await db.Query("""
            INSERT INTO scores(id,user_id,chart_id,client_run_id,ruleset,arrangement,gauge,perfect,great,good,bad,poor,max_combo,clear)
            SELECT @id,@user,@chart,@run,@rules,@arrangement,@gauge,@perfect,@great,@good,@bad,@poor,@combo,@clear
            WHERE EXISTS(SELECT 1 FROM pack_charts pc JOIN packs p ON p.id=pc.pack_id WHERE pc.chart_id=@chart AND p.published)
            ON CONFLICT(user_id,client_run_id) DO NOTHING RETURNING *
            """, ("id", Guid.NewGuid()), ("user", user), ("chart", score.ChartId), ("run", score.ClientRunId),
            ("rules", score.Ruleset), ("arrangement", score.Arrangement), ("gauge", score.Gauge), ("perfect", score.Perfect),
            ("great", score.Great), ("good", score.Good), ("bad", score.Bad), ("poor", score.Poor), ("combo", score.MaxCombo), ("clear", score.Clear));
        if (rows.Count > 0) return rows[0];
        rows = await db.Query("SELECT * FROM scores WHERE user_id=@user AND client_run_id=@run", ("user", user), ("run", score.ClientRunId));
        if (rows.Count == 0) throw new ApiError(404, "Chart is not in the published catalog.");
        var row = rows[0];
        if ((Guid)row["chartId"]! != score.ChartId || (string)row["ruleset"]! != score.Ruleset || (string)row["arrangement"]! != score.Arrangement ||
            (string)row["gauge"]! != score.Gauge || (int)row["perfect"]! != score.Perfect || (int)row["great"]! != score.Great ||
            (int)row["good"]! != score.Good || (int)row["bad"]! != score.Bad || (int)row["poor"]! != score.Poor ||
            (int)row["maxCombo"]! != score.MaxCombo || (string)row["clear"]! != score.Clear)
            throw new ApiError(409, "The run identifier was already used for a different score.");
        return row;
    }

    public Task<List<Dictionary<string, object?>>> Board(Guid chart, string arrangement, string gauge, bool verified, int page)
    {
        if (!Arrangements.Contains(arrangement) || !Gauges.Contains(gauge) || page is < 1 or > 10000) throw new ApiError(400, "Invalid ranking filter.");
        return db.Query("""
            WITH best AS (
                SELECT DISTINCT ON (s.user_id) s.*,s.bad+s.poor AS misses
                FROM scores s JOIN users u ON u.id=s.user_id
                WHERE s.chart_id=@chart AND s.ruleset='openlr2-v1' AND s.arrangement=@arrangement AND s.gauge=@gauge
                  AND (NOT @verified OR s.verified) AND NOT u.disabled
                ORDER BY s.user_id,s.ex_score DESC,s.bad+s.poor,s.max_combo DESC,s.created_at,s.id
            ), ranked AS (
                SELECT row_number() OVER(ORDER BY b.ex_score DESC,b.misses,b.max_combo DESC,b.created_at,b.id) AS rank,
                    b.id,b.user_id,u.username,u.display_name,b.ex_score,b.misses,b.max_combo,b.clear,b.verified,b.created_at
                FROM best b JOIN users u ON u.id=b.user_id
            ) SELECT * FROM ranked ORDER BY rank LIMIT 50 OFFSET @offset
            """, ("chart", chart), ("arrangement", arrangement), ("gauge", gauge), ("verified", verified), ("offset", (page - 1) * 50));
    }
}
