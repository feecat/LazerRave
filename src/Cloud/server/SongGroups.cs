using System.Text.RegularExpressions;

namespace Cloud;

public sealed class SongGroups(Pg db)
{
    public async Task<string[]> Difficulties() => (await db.Query("SELECT DISTINCT display_difficulty FROM ir_boards WHERE visibility='public' ORDER BY display_difficulty"))
        .Select(row => (string)row["displayDifficulty"]!).ToArray();

    public async Task<object> List(string? query, int? keys, int page, int? minimum, int? maximum, string sort, string? difficulty)
    {
        if (page is < 1 or > 10000 || query?.Length > 100 || keys is not null && !new[] { 5, 7, 9, 10, 14 }.Contains(keys.Value) || minimum is < 0 or > 999 || maximum is < 0 or > 999 || minimum > maximum || difficulty?.Length > 120)
            throw new ApiError(400, "Invalid chart filter.");
        var order = sort switch
        {
            "newest" => "created_at DESC,song_key", "recent" => "last_played_at DESC NULLS LAST,song_key",
            "plays" => "play_count DESC,created_at DESC,song_key", "level-asc" => "minimum_level,title,song_key",
            "level-desc" => "maximum_level DESC,title,song_key", "title" => "title,song_key",
            _ => throw new ApiError(400, "Invalid chart sort."),
        };
        const string filtered = """
            WITH filtered AS (
                SELECT b.* FROM ir_boards b WHERE b.visibility='public'
                AND (@keys=0 OR b.keys=@keys) AND (b.title ILIKE @query OR b.artist ILIKE @query OR b.song_title ILIKE @query)
                AND b.level>=@minimum AND b.level<=@maximum AND (@difficulty='' OR b.display_difficulty=upper(@difficulty))
            )
            """;
        (string, object?)[] parameters = [("keys", keys ?? 0), ("query", "%" + (query ?? "").Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%"), ("minimum", minimum ?? 0), ("maximum", maximum ?? 999), ("difficulty", difficulty ?? ""), ("offset", (page - 1) * 50)];
        var total = (long)(await db.Query(filtered + " SELECT count(DISTINCT song_key) AS total FROM filtered", parameters)).Single()["total"]!;
        var items = await db.Query(filtered + """
            , grouped AS (
                SELECT f.song_key,min(f.song_title) AS title,min(btrim(f.artist)) AS artist,count(*)::int AS chart_count,
                    array_agg(DISTINCT f.keys ORDER BY f.keys) AS keys,min(f.level) AS minimum_level,max(f.level) AS maximum_level,
                    max(f.created_at) AS created_at,bool_and(f.approved) AS approved
                FROM filtered f GROUP BY f.song_key
            )
            SELECT g.*,stats.play_count,stats.player_count,stats.last_played_at FROM grouped g
            LEFT JOIN LATERAL (
                SELECT count(*) AS play_count,count(DISTINCT s.user_id) AS player_count,max(s.played_at) AS last_played_at
                FROM scores s JOIN filtered f ON f.id=s.board_id JOIN users u ON u.id=s.user_id
                WHERE f.song_key=g.song_key AND NOT s.withdrawn AND NOT u.disabled
            ) stats ON true
            ORDER BY
            """ + " " + order + " LIMIT 50 OFFSET @offset", parameters);
        return new { items, total, page, pageSize = 50 };
    }

    public async Task<object> Require(string key)
    {
        if (!Regex.IsMatch(key, "\\A[0-9a-f]{32}\\z")) throw new ApiError(404, "Song not found.");
        var canonical = await db.Query("""
            SELECT b.song_key FROM song_group_aliases a JOIN ir_boards b ON b.id=a.board_id
            WHERE a.old_key=@key AND b.visibility='public'
                AND NOT EXISTS(SELECT 1 FROM ir_boards current WHERE current.song_key=@key AND current.visibility='public')
            GROUP BY b.song_key ORDER BY count(*) DESC,b.song_key LIMIT 1
            """, ("key", key));
        if (canonical.Count > 0) key = (string)canonical[0]["songKey"]!;
        var charts = await db.Query($"SELECT {ChartRegistry.Fields} FROM ir_boards b JOIN charts c ON c.id=b.chart_id WHERE b.song_key=@key AND b.visibility='public' ORDER BY b.keys,b.level,b.display_difficulty,b.title,b.id", ("key", key));
        if (charts.Count == 0) throw new ApiError(404, "Song not found.");
        return new { key, title = charts[0]["songTitle"], artist = charts.Select(chart => (string)chart["artist"]!).OrderBy(artist => artist.Length).First(), charts };
    }
}
