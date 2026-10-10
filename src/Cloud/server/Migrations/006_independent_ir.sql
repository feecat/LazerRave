CREATE TABLE ir_boards (
    id uuid PRIMARY KEY,
    chart_id uuid NOT NULL REFERENCES charts(id),
    scope_key varchar(64) NOT NULL,
    owner_id uuid REFERENCES users(id),
    visibility varchar(16) NOT NULL CHECK(visibility IN ('public','unlisted','restricted','hidden')),
    title varchar(200) NOT NULL, artist varchar(200) NOT NULL,
    difficulty varchar(120) NOT NULL,
    keys integer NOT NULL CHECK(keys IN (5,7,9,10,14)),
    level integer NOT NULL CHECK(level BETWEEN 0 AND 999),
    bpm double precision, length_ms double precision,
    approved boolean NOT NULL DEFAULT false,
    moderated_hidden boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE(chart_id,scope_key)
);
INSERT INTO ir_boards(id,chart_id,scope_key,owner_id,visibility,title,artist,difficulty,keys,level,approved)
SELECT c.id,c.id,'community',
    (SELECT p.uploader_id FROM packs p JOIN pack_charts pc ON pc.pack_id=p.id WHERE pc.chart_id=c.id ORDER BY p.created_at,p.id LIMIT 1),
    CASE WHEN EXISTS(SELECT 1 FROM packs p JOIN pack_charts pc ON pc.pack_id=p.id WHERE pc.chart_id=c.id AND p.published) THEN 'public' ELSE 'hidden' END,
    c.title,c.artist,c.difficulty,c.keys,c.level,
    EXISTS(SELECT 1 FROM packs p JOIN pack_charts pc ON pc.pack_id=p.id WHERE pc.chart_id=c.id AND p.published)
FROM charts c;
CREATE INDEX ir_boards_directory ON ir_boards(visibility,keys,title,id);
CREATE TABLE ir_board_members (
    board_id uuid NOT NULL REFERENCES ir_boards(id) ON DELETE CASCADE,
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    PRIMARY KEY(board_id,user_id)
);
ALTER TABLE scores ADD COLUMN board_id uuid REFERENCES ir_boards(id);
UPDATE scores SET board_id=chart_id;
ALTER TABLE scores ALTER COLUMN board_id SET NOT NULL;
ALTER TABLE scores DROP CONSTRAINT scores_user_id_client_run_id_key;
ALTER TABLE scores ADD CONSTRAINT scores_board_run_unique UNIQUE(user_id,board_id,client_run_id);
ALTER TABLE scores ADD COLUMN withdrawn boolean NOT NULL DEFAULT false;
ALTER TABLE scores ADD COLUMN review_note varchar(400) NOT NULL DEFAULT '';
CREATE INDEX scores_board_player_best ON scores(board_id,ruleset,user_id,ex_score DESC,created_at,id) WHERE NOT withdrawn;
DROP VIEW personal_bests;
CREATE VIEW personal_bests AS
SELECT DISTINCT ON (user_id,board_id,ruleset,arrangement,gauge,verified) s.*,
    bad+poor AS misses,
    min(bad+poor) OVER records AS min_misses,
    max(CASE clear WHEN 'perfect' THEN 6 WHEN 'full-combo' THEN 5 WHEN 'hard' THEN 4
        WHEN 'normal' THEN 3 WHEN 'easy' THEN 2 WHEN 'assist' THEN 1 ELSE 0 END) OVER records AS best_clear_order
FROM scores s WHERE NOT withdrawn
WINDOW records AS (PARTITION BY user_id,board_id,ruleset,arrangement,gauge,verified)
ORDER BY user_id,board_id,ruleset,arrangement,gauge,verified,ex_score DESC,created_at,id;
