ALTER TABLE scores ADD COLUMN normal_score integer CHECK(normal_score >= 0);
CREATE INDEX scores_chart_player_best ON scores(chart_id,ruleset,user_id,ex_score DESC,created_at,id);
