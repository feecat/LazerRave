-- A row update participates in the registration transaction, unlike nextval.
-- The row lock serializes concurrent registrations until commit or rollback.
CREATE TABLE user_uid_counter (
    singleton boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    last_uid bigint NOT NULL CHECK (last_uid BETWEEN 0 AND 9007199254740991)
);
INSERT INTO user_uid_counter(singleton,last_uid)
SELECT true,COALESCE(max(uid),0) FROM users;

CREATE FUNCTION next_user_uid() RETURNS bigint
LANGUAGE sql VOLATILE
AS $$
    UPDATE user_uid_counter SET last_uid=last_uid+1
    WHERE singleton RETURNING last_uid;
$$;

ALTER TABLE users ALTER COLUMN uid SET DEFAULT next_user_uid();
DROP SEQUENCE users_uid_seq;
