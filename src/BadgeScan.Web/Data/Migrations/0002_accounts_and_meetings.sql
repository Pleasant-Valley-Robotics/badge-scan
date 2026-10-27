-- Sign-ins for the leads, coaches and mentors, the meetings they open, and the keys other
-- programs use to read the scans. Everyone with an account may do everything; "title" says
-- who they are (mentor, coach, lead) and is there so that can change later.
CREATE TABLE accounts (
  id                   bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  username             text NOT NULL UNIQUE CHECK (username ~ '^[a-z0-9][a-z0-9._-]{1,31}$'),
  display_name         text NOT NULL CHECK (btrim(display_name) <> ''),
  title                text NOT NULL CHECK (title IN ('mentor', 'coach', 'lead')),
  password_hash        text NOT NULL,
  -- A password someone else chose (a new account, a reset): it must be changed at the next
  -- sign-in, and stops working when it expires.
  must_change_password boolean NOT NULL DEFAULT true,
  password_expires_at  timestamptz,
  -- Changes whenever the password changes or the account is turned off, which ends every
  -- sign-in made before.
  security_stamp       text NOT NULL,
  disabled             boolean NOT NULL DEFAULT false,
  failed_sign_ins      integer NOT NULL DEFAULT 0,
  locked_until         timestamptz,
  last_sign_in_at      timestamptz,
  created_at           timestamptz NOT NULL DEFAULT now(),
  updated_at           timestamptz NOT NULL DEFAULT now()
);

-- A meeting turns one browser into the kiosk until someone ends it or its time runs out.
-- The opener's name is copied in, like a scan's person name, so history never changes.
CREATE TABLE meetings (
  id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  opened_by       bigint NOT NULL REFERENCES accounts (id),
  opened_by_name  text NOT NULL,
  started_utc     timestamptz NOT NULL,
  ends_utc        timestamptz NOT NULL CHECK (ends_utc > started_utc),
  closed_utc      timestamptz,
  closed_by       bigint REFERENCES accounts (id),
  central_date    date NOT NULL
);
CREATE INDEX meetings_open_idx ON meetings (ends_utc) WHERE closed_utc IS NULL;

-- Which meeting each scan came through, and whose sign-in opened it. Older scans have neither.
ALTER TABLE badge_scans
  ADD COLUMN meeting_id bigint REFERENCES meetings (id),
  ADD COLUMN opened_by  text;

-- Keys for programs (Project-Tracker) that read the scans. Only a hash is kept: the key is
-- shown once, when it is made. The prefix is the first few characters, to tell keys apart.
CREATE TABLE api_keys (
  id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  label         text NOT NULL CHECK (btrim(label) <> ''),
  key_hash      text NOT NULL UNIQUE,
  key_prefix    text NOT NULL,
  created_by    bigint NOT NULL REFERENCES accounts (id),
  created_at    timestamptz NOT NULL,
  last_used_at  timestamptz,
  revoked_at    timestamptz
);

-- The keys ASP.NET Core signs sign-in cookies and forms with. Kept here so a new release
-- or a restart doesn't sign everyone out.
CREATE TABLE data_protection_keys (
  id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  friendly_name text NOT NULL,
  xml           text NOT NULL,
  created_at    timestamptz NOT NULL DEFAULT now()
);
