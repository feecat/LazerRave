namespace Cloud;

public sealed record ScoreInput(Guid ChartId, Guid ClientRunId, string Ruleset, string Arrangement, string Gauge,
    int Perfect, int Great, int Good, int Bad, int Poor, int MaxCombo, string Clear, bool Autoplay = false, bool Assist = false, bool Modifiers = false,
    int? ScoreMax = null, string InputType = "unknown", string Comment = "", int? NormalScore = null);

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
            score.Autoplay || score.Assist || score.Modifiers ||
            score.ScoreMax is not null && (score.ScoreMax is < 1 or > 20000000 || score.Perfect * 2 + score.Great > score.ScoreMax) ||
            score.NormalScore is < 0 ||
            !new[] { "unknown", "keyboard", "controller", "midi" }.Contains(score.InputType) || score.Comment is null || score.Comment.Length > 200)
            throw new ApiError(400, "Invalid or ranking-ineligible score. Autoplay, assists and MANIAC modifiers are excluded.");
    }

    public async Task<Dictionary<string, object?>> Submit(Guid user, ScoreInput score)
    {
        Validate(score);
        var rows = await db.Query("""
            INSERT INTO scores(id,user_id,chart_id,client_run_id,ruleset,arrangement,gauge,perfect,great,good,bad,poor,max_combo,clear,score_max,input_type,comment,normal_score)
            SELECT @id,@user,@chart,@run,@rules,@arrangement,@gauge,@perfect,@great,@good,@bad,@poor,@combo,@clear,@maximum,@input,@comment,@normal
            WHERE EXISTS(SELECT 1 FROM pack_charts pc JOIN packs p ON p.id=pc.pack_id WHERE pc.chart_id=@chart AND p.published)
            ON CONFLICT(user_id,client_run_id) DO NOTHING RETURNING *
            """, ("id", Guid.NewGuid()), ("user", user), ("chart", score.ChartId), ("run", score.ClientRunId),
            ("rules", score.Ruleset), ("arrangement", score.Arrangement), ("gauge", score.Gauge), ("perfect", score.Perfect),
            ("great", score.Great), ("good", score.Good), ("bad", score.Bad), ("poor", score.Poor), ("combo", score.MaxCombo), ("clear", score.Clear),
            ("maximum", score.ScoreMax), ("input", score.InputType), ("comment", score.Comment), ("normal", score.NormalScore));
        if (rows.Count > 0) return rows[0];
        rows = await db.Query("SELECT * FROM scores WHERE user_id=@user AND client_run_id=@run", ("user", user), ("run", score.ClientRunId));
        if (rows.Count == 0) throw new ApiError(404, "Chart is not in the published catalog.");
        var row = rows[0];
        if ((Guid)row["chartId"]! != score.ChartId || (string)row["ruleset"]! != score.Ruleset || (string)row["arrangement"]! != score.Arrangement ||
            (string)row["gauge"]! != score.Gauge || (int)row["perfect"]! != score.Perfect || (int)row["great"]! != score.Great ||
            (int)row["good"]! != score.Good || (int)row["bad"]! != score.Bad || (int)row["poor"]! != score.Poor ||
            (int)row["maxCombo"]! != score.MaxCombo || (string)row["clear"]! != score.Clear ||
            (row["scoreMax"] as int?) != score.ScoreMax || (row["normalScore"] as int?) != score.NormalScore ||
            (string)row["inputType"]! != score.InputType || (string)row["comment"]! != score.Comment)
            throw new ApiError(409, "The run identifier was already used for a different score.");
        return row;
    }

    public Task<List<Dictionary<string, object?>>> Board(Guid chart, string arrangement, string gauge, bool verified, int page)
    {
        if (arrangement != "all" && !Arrangements.Contains(arrangement) || gauge != "all" && !Gauges.Contains(gauge) || page is < 1 or > 10000) throw new ApiError(400, "Invalid ranking filter.");
        return db.Query("""
            WITH best AS (
                SELECT DISTINCT ON (s.user_id) s.*,
                    s.bad+s.poor AS misses,
                    min(s.bad+s.poor) OVER(PARTITION BY s.user_id) AS overall_min_misses,
                    max(CASE s.clear WHEN 'perfect' THEN 6 WHEN 'full-combo' THEN 5 WHEN 'hard' THEN 4
                        WHEN 'normal' THEN 3 WHEN 'easy' THEN 2 WHEN 'assist' THEN 1 ELSE 0 END) OVER(PARTITION BY s.user_id) AS overall_clear_order
                FROM scores s JOIN users u ON u.id=s.user_id
                WHERE s.chart_id=@chart AND s.ruleset='openlr2-v1' AND (@arrangement='all' OR s.arrangement=@arrangement) AND (@gauge='all' OR s.gauge=@gauge)
                  AND (NOT @verified OR s.verified) AND NOT u.disabled
                ORDER BY s.user_id,s.ex_score DESC,s.created_at,s.id
            ), ranked AS (
                SELECT rank() OVER(ORDER BY b.ex_score DESC) AS rank,
                    b.id,b.client_run_id,b.user_id,u.uid,u.username,u.display_name,b.ex_score,b.score_max,b.misses,b.overall_min_misses AS min_misses,b.max_combo,b.clear,
                    b.perfect,b.great,b.good,b.bad,b.poor,b.normal_score,b.arrangement,b.gauge,b.input_type,b.comment,b.verified,b.created_at,
                    CASE b.overall_clear_order WHEN 6 THEN 'perfect' WHEN 5 THEN 'full-combo' WHEN 4 THEN 'hard'
                        WHEN 3 THEN 'normal' WHEN 2 THEN 'easy' WHEN 1 THEN 'assist' ELSE 'failed' END AS best_clear,
                    CASE WHEN b.score_max IS NULL THEN NULL WHEN b.ex_score*9>=b.score_max*8 THEN 'AAA'
                        WHEN b.ex_score*9>=b.score_max*7 THEN 'AA' WHEN b.ex_score*9>=b.score_max*6 THEN 'A'
                        WHEN b.ex_score*9>=b.score_max*5 THEN 'B' WHEN b.ex_score*9>=b.score_max*4 THEN 'C'
                        WHEN b.ex_score*9>=b.score_max*3 THEN 'D' WHEN b.ex_score*9>=b.score_max*2 THEN 'E' ELSE 'F' END AS letter_rank
                FROM best b JOIN users u ON u.id=b.user_id
            ) SELECT * FROM ranked ORDER BY rank,created_at,id LIMIT 50 OFFSET @offset
            """, ("chart", chart), ("arrangement", arrangement), ("gauge", gauge), ("verified", verified), ("offset", (page - 1) * 50));
    }
}
