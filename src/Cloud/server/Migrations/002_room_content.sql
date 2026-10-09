CREATE TABLE room_content (
    id uuid PRIMARY KEY,
    room_id uuid NOT NULL,
    selection_id uuid NOT NULL,
    uploader_id uuid NOT NULL REFERENCES users(id),
    chart_id uuid NOT NULL REFERENCES charts(id),
    content_sha256 char(64) NOT NULL,
    archive_sha256 char(64) NOT NULL,
    manifest jsonb NOT NULL,
    size_bytes bigint NOT NULL CHECK(size_bytes > 0),
    received_bytes bigint NOT NULL DEFAULT 0,
    state varchar(16) NOT NULL CHECK(state IN ('uploading','ready','expired')),
    created_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL,
    deleted_at timestamptz
);
CREATE INDEX room_content_expiry ON room_content(expires_at) WHERE deleted_at IS NULL;
CREATE UNIQUE INDEX room_content_upload ON room_content(room_id,selection_id,uploader_id) WHERE state IN ('uploading','ready');
