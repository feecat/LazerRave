ALTER TABLE scores ADD COLUMN played_at timestamptz;
CREATE INDEX scores_board_played_at ON scores(board_id,played_at DESC) WHERE NOT withdrawn;
UPDATE ir_boards SET visibility='public' WHERE scope_key='community' AND visibility='unlisted' AND NOT moderated_hidden;
