-- People and their badges. One row per person per team. badge_number is NULL until a badge
-- is handed out. Numbers are stored normalised (no leading zeros), so 0000000027 and 27 are
-- the same badge.
CREATE TABLE badges (
  id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  team_number   integer NOT NULL,
  person_name   text    NOT NULL CHECK (btrim(person_name) <> ''),
  badge_number  text    CHECK (badge_number ~ '^[0-9A-Za-z]+$' AND badge_number !~ '^0.'),
  created_at    timestamptz NOT NULL DEFAULT now(),
  updated_at    timestamptz NOT NULL DEFAULT now(),
  UNIQUE (team_number, person_name),
  UNIQUE (team_number, badge_number)        -- NULLs are distinct, so many people may have no badge yet
);
CREATE INDEX badges_badge_number_idx ON badges (badge_number);

-- Every scan, including repeats and badges nobody recognised. The person's name is copied in
-- at scan time, so handing a badge to someone else later never rewrites history.
-- The instant is kept twice: as UTC, and as the Central wall clock (CST or CDT) that the
-- kiosk showed, because that is the time people remember.
CREATE TABLE badge_scans (
  id               bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  badge_number     text,                        -- normalised; NULL only for imported rows with no badge on file
  team_number      integer,                     -- NULL when the badge was not recognised
  person_name      text,                        -- NULL when the badge was not recognised
  recognised       boolean NOT NULL,
  first_of_day     boolean NOT NULL,            -- the scan that checked this person in for that Central day
  scanned_utc      timestamptz NOT NULL,
  scanned_central  timestamp   NOT NULL,        -- the same instant on a Central wall clock
  central_zone     text NOT NULL CHECK (central_zone IN ('CST', 'CDT')),
  central_date     date NOT NULL,               -- scanned_central's date; the "day" of first_of_day
  stamp            text NOT NULL,               -- Th10/08/2026|17:17, exactly what the kiosk showed
  source           text NOT NULL DEFAULT 'kiosk' CHECK (source IN ('kiosk', 'import')),
  created_at       timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX badge_scans_central_date_idx ON badge_scans (central_date, id);
CREATE INDEX badge_scans_person_idx ON badge_scans (team_number, person_name, central_date);
-- One check-in per person per Central day, even if two scans arrive at the same moment.
CREATE UNIQUE INDEX badge_scans_first_of_day_uq ON badge_scans (team_number, person_name, central_date) WHERE first_of_day;
-- Running the one-time import twice must not add the same scan twice.
CREATE UNIQUE INDEX badge_scans_import_uq ON badge_scans (team_number, person_name, scanned_central) WHERE source = 'import';

-- For checking with psql that the site and PostgreSQL agree about Central time.
-- (The site writes both columns itself: PostgreSQL will not store a computed column for a
-- time zone conversion, because the rules for a zone can change.)
CREATE VIEW badge_scans_check AS
SELECT id, scanned_utc, scanned_central, central_zone,
       (scanned_utc AT TIME ZONE 'America/Chicago') AS pg_central,
       scanned_central = (scanned_utc AT TIME ZONE 'America/Chicago') AS matches
FROM badge_scans;
