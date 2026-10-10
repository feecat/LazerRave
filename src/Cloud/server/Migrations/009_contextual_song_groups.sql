ALTER TABLE ir_boards ALTER COLUMN song_title DROP EXPRESSION;
ALTER TABLE ir_boards ALTER COLUMN song_key DROP EXPRESSION;
ALTER TABLE ir_boards ALTER COLUMN display_difficulty DROP EXPRESSION;
ALTER TABLE ir_boards ALTER COLUMN song_title SET DEFAULT '';
ALTER TABLE ir_boards ALTER COLUMN song_key SET DEFAULT '';
ALTER TABLE ir_boards ALTER COLUMN display_difficulty SET DEFAULT 'UNKNOWN';
CREATE TABLE song_group_aliases (
    old_key text NOT NULL,
    board_id uuid NOT NULL REFERENCES ir_boards(id) ON DELETE CASCADE,
    PRIMARY KEY(old_key,board_id)
);
