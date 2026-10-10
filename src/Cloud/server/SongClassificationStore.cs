using Npgsql;
using LazerRave.Content;

namespace Cloud;

public static class SongClassificationStore
{
    public static async Task Lock(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellation = default)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(7341209)", connection, transaction);
        await command.ExecuteNonQueryAsync(cancellation);
    }

    public static async Task Refresh(Pg db)
    {
        await using var connection = await db.Open();
        await using var transaction = await connection.BeginTransactionAsync();
        await Lock(connection, transaction);
        await Refresh(connection, transaction);
        await transaction.CommitAsync();
    }

    public static async Task Refresh(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellation = default)
    {
        await using var read = new NpgsqlCommand("""
            SELECT b.id,b.title,b.artist,b.difficulty,b.keys,b.scope_key,
                ARRAY(SELECT pc.pack_id FROM pack_charts pc WHERE pc.chart_id=b.chart_id) AS packs
            FROM ir_boards b ORDER BY b.id
            """, connection, transaction);
        var charts = (await Pg.Read(read)).Select(row => new SongChart((Guid)row["id"]!, (string)row["title"]!,
            (string)row["artist"]!, (string)row["difficulty"]!, (int)row["keys"]!, (string)row["scopeKey"]!, (Guid[])row["packs"]!)).ToArray();
        var classified = SongTitleParser.Classify(charts);
        if (classified.Count == 0) return;
        await using var write = new NpgsqlCommand("""
            WITH resolved AS (
                SELECT id,title,difficulty,md5(lower(title)||chr(31)||lower(artist)) AS key
                FROM unnest(@ids::uuid[],@titles::text[],@artists::text[],@difficulties::text[]) AS r(id,title,artist,difficulty)
            ), aliases AS (
                INSERT INTO song_group_aliases(old_key,board_id)
                SELECT b.song_key,b.id FROM ir_boards b JOIN resolved r ON r.id=b.id
                WHERE b.song_key<>r.key AND b.song_key~'^[0-9a-f]{32}$'
                ON CONFLICT DO NOTHING
            )
            UPDATE ir_boards b SET song_title=r.title,song_key=r.key,display_difficulty=r.difficulty
            FROM resolved r WHERE b.id=r.id
                AND (b.song_title,b.song_key,b.display_difficulty) IS DISTINCT FROM (r.title,r.key,r.difficulty)
            """, connection, transaction);
        Pg.Add(write, ("ids", classified.Select(c => c.Id).ToArray()), ("titles", classified.Select(c => c.Title).ToArray()),
            ("artists", classified.Select(c => c.Artist).ToArray()), ("difficulties", classified.Select(c => c.Difficulty).ToArray()));
        await write.ExecuteNonQueryAsync(cancellation);
    }
}
