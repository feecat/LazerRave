ALTER TABLE users ADD COLUMN uid bigint;
WITH numbered AS (SELECT id,row_number() OVER(ORDER BY created_at,id) AS uid FROM users)
UPDATE users u SET uid=n.uid FROM numbered n WHERE u.id=n.id;
CREATE SEQUENCE users_uid_seq;
SELECT setval('users_uid_seq',COALESCE((SELECT max(uid)+1 FROM users),1),false);
ALTER TABLE users ALTER COLUMN uid SET DEFAULT nextval('users_uid_seq');
ALTER SEQUENCE users_uid_seq OWNED BY users.uid;
ALTER TABLE users ALTER COLUMN uid SET NOT NULL;
ALTER TABLE users ADD CONSTRAINT users_uid_unique UNIQUE(uid), ADD CONSTRAINT users_uid_positive CHECK(uid>0 AND uid<=9007199254740991);

ALTER TABLE scores ADD COLUMN score_max integer CHECK(score_max BETWEEN 1 AND 20000000 AND ex_score<=score_max);
ALTER TABLE scores ADD COLUMN input_type varchar(16) NOT NULL DEFAULT 'unknown' CHECK(input_type IN ('unknown','keyboard','controller','midi'));
ALTER TABLE scores ADD COLUMN comment varchar(200) NOT NULL DEFAULT '';
CREATE INDEX charts_md5 ON charts(md5);

CREATE VIEW personal_bests AS
SELECT DISTINCT ON (user_id,chart_id,ruleset,arrangement,gauge,verified) s.*,
    bad+poor AS misses,
    min(bad+poor) OVER records AS min_misses,
    max(CASE clear WHEN 'perfect' THEN 6 WHEN 'full-combo' THEN 5 WHEN 'hard' THEN 4
        WHEN 'normal' THEN 3 WHEN 'easy' THEN 2 WHEN 'assist' THEN 1 ELSE 0 END) OVER records AS best_clear_order
FROM scores s
WINDOW records AS (PARTITION BY user_id,chart_id,ruleset,arrangement,gauge,verified)
ORDER BY user_id,chart_id,ruleset,arrangement,gauge,verified,ex_score DESC,bad+poor,max_combo DESC,created_at,id;

CREATE TABLE courses (
    id uuid PRIMARY KEY,
    title varchar(200) NOT NULL,
    category varchar(80) NOT NULL DEFAULT '',
    keys integer NOT NULL CHECK(keys IN (5,7,9,10,14)),
    sha256 char(64) NOT NULL UNIQUE,
    creator_id uuid NOT NULL REFERENCES users(id),
    published boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE course_stages (
    course_id uuid NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
    stage integer NOT NULL CHECK(stage BETWEEN 1 AND 20),
    chart_id uuid NOT NULL REFERENCES charts(id),
    PRIMARY KEY(course_id,stage)
);
CREATE TABLE course_scores (
    id uuid PRIMARY KEY,
    course_id uuid NOT NULL REFERENCES courses(id),
    user_id uuid NOT NULL REFERENCES users(id),
    client_run_id uuid NOT NULL,
    ruleset varchar(40) NOT NULL,
    arrangement varchar(16) NOT NULL,
    gauge varchar(16) NOT NULL,
    perfect integer NOT NULL CHECK(perfect>=0), great integer NOT NULL CHECK(great>=0),
    good integer NOT NULL CHECK(good>=0), bad integer NOT NULL CHECK(bad>=0), poor integer NOT NULL CHECK(poor>=0),
    ex_score integer GENERATED ALWAYS AS (perfect*2+great) STORED,
    score_max integer CHECK(score_max BETWEEN 1 AND 20000000 AND perfect*2+great<=score_max),
    max_combo integer NOT NULL CHECK(max_combo>=0),
    clear varchar(24) NOT NULL CHECK(clear IN ('failed','assist','easy','normal','hard','full-combo','perfect')),
    verified boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE(user_id,client_run_id)
);
CREATE INDEX course_scores_board ON course_scores(course_id,ruleset,arrangement,gauge,verified,ex_score DESC);

CREATE TABLE difficulty_tables (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name varchar(120) NOT NULL,
    symbol varchar(16) NOT NULL,
    description varchar(2000) NOT NULL DEFAULT '',
    source_url varchar(1000),
    owner_id uuid NOT NULL REFERENCES users(id),
    published boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE difficulty_table_entries (
    table_id bigint NOT NULL REFERENCES difficulty_tables(id) ON DELETE CASCADE,
    md5 char(32) NOT NULL CHECK(md5 ~ '^[0-9a-f]{32}$'),
    level varchar(24) NOT NULL,
    title varchar(200) NOT NULL DEFAULT '',
    artist varchar(200) NOT NULL DEFAULT '',
    url varchar(1000),
    position integer NOT NULL CHECK(position>=0),
    PRIMARY KEY(table_id,md5)
);
CREATE INDEX difficulty_table_levels ON difficulty_table_entries(table_id,level,position);
