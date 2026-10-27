# Pulling attendance into Project-Tracker

This is the badge-scan side of the attendance link, for whoever builds the Project-Tracker
side. Everything here is ready to use: a read-only key, the endpoints below, and sample
answers in [`samples/`](samples/) to build against before there are real scans.

**Site:** `https://attendance.firstpvrobotics.com`

## 1. The key

Project-Tracker reads with a **data key**, not with someone's password.

1. A mentor signs in, opens **Accounts → Data keys**, and makes one labelled
   `Project-Tracker`. The key (`bsk_…`) is shown **once**.
2. It goes straight into a Supabase secret, never into code, git, `.env` files that get
   committed, or chat:
   ```sh
   supabase secrets set BADGE_SCAN_KEY=bsk_…
   ```
3. Every request sends it as `Authorization: Bearer <key>`.

The key can only **read**: scans and the badge list. It can't add people, change badges or
record scans. If it leaks, a mentor revokes it on the same page and makes a new one.

**Call it from the server, not the browser.** Use a Supabase Edge Function or a scheduled
job. In browser code anyone could copy the key from the page, and the site doesn't allow
cross-site browser calls anyway.

## 2. Check the key works

```sh
curl -H "Authorization: Bearer $BADGE_SCAN_KEY" https://attendance.firstpvrobotics.com/api/me
# {"kind":"program","name":"Project-Tracker"}
```

`401` means the key is wrong or was revoked.

## 3. The endpoints

| Call | Answer |
|---|---|
| `GET /api/me` | who the key is ([sample](samples/me.json)) |
| `GET /api/scans/since?after=0&limit=500` | every scan with an id above `after`, oldest first, at most `limit` (max 1000) ([sample](samples/scans-since.json)) |
| `GET /api/badges` | everyone and the badge they hold; `?team=7196` or `?team=mentor` to filter ([sample](samples/badges.json)) |
| `GET /scans.csv?date=2026-10-01&to=2026-10-31` | the same scans as a spreadsheet, for checking by hand |

### Paging through scans

`/api/scans/since` answers `{ "after": 0, "next": 3, "scans": [ … ] }`. Save `next`, and
ask with `after=<next>` next time. An empty `scans` list means you're caught up (`next`
stays the same). Scan ids only go up and scans are never edited, so:

- keep **one number** (the last `next`) in a table on your side;
- importing the same scan twice must be harmless, so upsert by scan `id`.

```ts
// Sketch of a sync run (an Edge Function on a schedule, or behind a "Sync" button).
let after = await loadCursor();               // 0 the first time
while (true) {
  const res = await fetch(`${BASE}/api/scans/since?after=${after}&limit=500`, {
    headers: { Authorization: `Bearer ${Deno.env.get("BADGE_SCAN_KEY")}` },
  });
  if (!res.ok) throw new Error(`badge-scan answered ${res.status}`);
  const page = await res.json();
  if (page.scans.length === 0) break;
  for (const scan of page.scans) await useScan(scan);   // your part
  after = page.next;
  await saveCursor(after);                     // save as you go, so a crash resumes here
}
```

Every five minutes during meetings is plenty. Nothing here is rate-limited for reading.

## 4. What a scan means

| Field | Meaning |
|---|---|
| `id` | unique, only goes up. Your cursor and your upsert key |
| `badge` | the badge number, without leading zeros (`"27"`) |
| `team` | team number (`7196`), or `null` for a mentor, coach or advisor |
| `role` | `"mentor"`, `"coach"`, `"advisor"`, or `null` for someone on a team |
| `group` | for showing: `"Team 7196"` or `"Mentor"` |
| `name` | the person's name **as it was at the time of the scan** |
| `recognised` | `false` when nobody had that badge yet. Skip those |
| `firstOfDay` | `true` on a person's first scan of the Central day. **This is the check-in** |
| `stamp` | `Tu10/27/2026\|17:05`, Central time: the same format as Project-Tracker's `attendance_records.badge_scan` |
| `centralDate` | `2026-10-27`, the attendance day (Central) |
| `scannedUtc`, `scannedCentral` | exact times |
| `source` | `"kiosk"`, or `"import"` for old scans loaded from a spreadsheet |
| `meetingId`, `openedBy` | which kiosk session took it and who opened it (`null` for imports) |

**For attendance, use only scans with `recognised` and `firstOfDay` both `true`.** That's
one per person per day; the rest are people scanning twice.

## 5. Matching a scan to a Project-Tracker person

Project-Tracker already has `profiles.badge_id` and `attendance_records.badge_scan` (see
its `supabase/badge_scanner.sql`). The natural match is **badge number + team**:

- `scan.badge` ↔ `profiles.badge_id`
- `scan.team` ↔ `profiles.team_number`. **Check this mapping:** Project-Tracker stores
  Radical as `team_number = NULL`, while here Radical is `7196`, and mentors, coaches and
  advisors have `team: null` with a `role`.

Badges are now handed out on the attendance site (Badges page and the kiosk), so this is
where they live. Project-Tracker can copy them into `profiles.badge_id` from
`GET /api/badges`, or just match on badge number at sync time. A scan that matches no one
should be logged for a mentor to look at, not dropped silently.

## 6. Rules of the road

- Read-only, always. Changes to people and badges happen on the attendance site.
- The fields above are covered by `tests/BadgeScan.Web.Tests/ProjectTrackerContractTests.cs`.
  If this side ever has to change one, that test fails first, and the change comes with a
  note here.
- Need something that isn't here (a new field, a meetings list)? Ask a mentor to add it on
  this side rather than working around it.
- The dev copy (`dev-1.firstpvrobotics.com`) is behind the mentors' server password, so
  build against `samples/` first, then the live site with the key. Reading live is safe.
