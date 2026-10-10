CREATE FUNCTION lr_song_title(value text) RETURNS text LANGUAGE sql IMMUTABLE STRICT AS $$
    SELECT COALESCE(NULLIF(btrim(regexp_replace(regexp_replace(regexp_replace(value,
        '[[:space:]]*[\[(][[:space:]]*((SP|DP|DOUBLE|SINGLE|[0-9]+[[:space:]]*(KEYS?|K))[[:space:]_/-]+)?(BEGINNER|NORMAL|HYPER|ANOTHER|INSANE|BLACK|LIGHT|EASY|HARD|EXTRA|EX|BASIC|STANDARD|MANIAC|LUNATIC|LEGENDARIA)[[:space:]+_0-9-]*[\])][[:space:]]*$',
        '', 'i'),
        '[[:space:]]*[\[(][[:space:]]*((5|7|9|10|14)[[:space:]]*(KEYS?|K)[[:space:]]*(LIGHT|BEGINNER|NORMAL|HYPER|ANOTHER|INSANE)?|[BNHAI](5|7|9|10|14)|SP|DP)[[:space:]]*[\])][[:space:]]*$', '', 'i'),
        '[[:space:]]+', ' ', 'g')), ''), btrim(value));
$$;
CREATE FUNCTION lr_chart_difficulty(title text, difficulty text) RETURNS text LANGUAGE sql IMMUTABLE STRICT AS $$
    SELECT CASE btrim(difficulty)
        WHEN '1' THEN 'BEGINNER' WHEN '2' THEN 'NORMAL' WHEN '3' THEN 'HYPER'
        WHEN '4' THEN 'ANOTHER' WHEN '5' THEN 'INSANE'
        WHEN '' THEN COALESCE(upper(substring(title from '(?i)[\[(][[:space:]]*(?:(?:SP|DP|DOUBLE|SINGLE|[0-9]+[[:space:]]*KEYS?)[[:space:]_/-]*)?(BEGINNER|NORMAL|HYPER|ANOTHER|INSANE|BLACK|LIGHT|EASY|HARD|EXTRA|EX|BASIC|STANDARD|MANIAC|LUNATIC|LEGENDARIA)[[:space:]+_0-9-]*[\])][[:space:]]*$')),
            CASE upper(substring(title from '(?i)[\[(]([BNHAI])(?:5|7|9|10|14)[\])][[:space:]]*$'))
                WHEN 'B' THEN 'BEGINNER' WHEN 'N' THEN 'NORMAL' WHEN 'H' THEN 'HYPER' WHEN 'A' THEN 'ANOTHER' WHEN 'I' THEN 'INSANE' ELSE 'UNKNOWN' END)
        ELSE upper(btrim(difficulty)) END;
$$;
ALTER TABLE ir_boards ADD COLUMN song_title text GENERATED ALWAYS AS (lr_song_title(title)) STORED;
ALTER TABLE ir_boards ADD COLUMN song_key text GENERATED ALWAYS AS
    (md5(lower(lr_song_title(title)) || chr(31) || lower(btrim(regexp_replace(artist, '[[:space:]]+', ' ', 'g'))))) STORED;
ALTER TABLE ir_boards ADD COLUMN display_difficulty text GENERATED ALWAYS AS (lr_chart_difficulty(title,difficulty)) STORED;
CREATE INDEX ir_boards_song_directory ON ir_boards(song_key,visibility,keys,level);
