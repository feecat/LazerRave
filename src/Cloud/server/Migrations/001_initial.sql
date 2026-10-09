CREATE TABLE users (
    id uuid PRIMARY KEY,
    username varchar(24) NOT NULL,
    email varchar(254) NOT NULL,
    password_hash text NOT NULL,
    display_name varchar(40) NOT NULL,
    signature varchar(200) NOT NULL DEFAULT '',
    bio varchar(2000) NOT NULL DEFAULT '',
    avatar_key text,
    role varchar(16) NOT NULL DEFAULT 'player' CHECK (role IN ('player','admin')),
    disabled boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX users_username_unique ON users (lower(username));
CREATE UNIQUE INDEX users_email_unique ON users (lower(email));
CREATE TABLE sessions (
    token_hash char(64) PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    expires_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX sessions_user ON sessions(user_id);
CREATE TABLE packs (
    id uuid PRIMARY KEY,
    title varchar(120) NOT NULL,
    description varchar(2000) NOT NULL DEFAULT '',
    file_key text NOT NULL,
    sha256 char(64) NOT NULL,
    size_bytes bigint NOT NULL,
    published boolean NOT NULL DEFAULT false,
    uploader_id uuid NOT NULL REFERENCES users(id),
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE charts (
    id uuid PRIMARY KEY,
    sha256 char(64) NOT NULL UNIQUE,
    md5 char(32) NOT NULL,
    title varchar(200) NOT NULL,
    artist varchar(200) NOT NULL,
    difficulty varchar(120) NOT NULL,
    keys integer NOT NULL CHECK (keys IN (5,7,9,10,14)),
    level integer NOT NULL CHECK (level BETWEEN 0 AND 999),
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE pack_charts (
    pack_id uuid NOT NULL REFERENCES packs(id) ON DELETE CASCADE,
    chart_id uuid NOT NULL REFERENCES charts(id),
    path text NOT NULL,
    PRIMARY KEY (pack_id,chart_id,path)
);
CREATE TABLE scores (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES users(id),
    chart_id uuid NOT NULL REFERENCES charts(id),
    client_run_id uuid NOT NULL,
    ruleset varchar(40) NOT NULL,
    arrangement varchar(16) NOT NULL,
    gauge varchar(16) NOT NULL,
    perfect integer NOT NULL CHECK (perfect >= 0),
    great integer NOT NULL CHECK (great >= 0),
    good integer NOT NULL CHECK (good >= 0),
    bad integer NOT NULL CHECK (bad >= 0),
    poor integer NOT NULL CHECK (poor >= 0),
    max_combo integer NOT NULL CHECK (max_combo >= 0),
    clear varchar(24) NOT NULL,
    ex_score integer GENERATED ALWAYS AS (perfect * 2 + great) STORED,
    verified boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (user_id,client_run_id)
);
CREATE INDEX scores_chart ON scores(chart_id,ruleset,arrangement,verified,ex_score DESC);
CREATE INDEX scores_user ON scores(user_id,created_at DESC);
CREATE TABLE matches (
    id uuid PRIMARY KEY,
    room_id uuid NOT NULL,
    chart_id uuid NOT NULL REFERENCES charts(id),
    state varchar(24) NOT NULL,
    started_at timestamptz NOT NULL,
    finished_at timestamptz,
    results jsonb NOT NULL DEFAULT '[]'
);
CREATE TABLE chat_messages (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES users(id),
    channel varchar(64) NOT NULL,
    text varchar(500) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX chat_channel ON chat_messages(channel,id DESC);
CREATE TABLE audit_log (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES users(id),
    action varchar(80) NOT NULL,
    target text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
