CREATE INDEX scores_recent_public_feed ON scores(created_at DESC,id DESC)
WHERE NOT withdrawn AND ruleset='openlr2-v1';
