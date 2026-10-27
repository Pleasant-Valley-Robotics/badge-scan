# PV Attendance (badge-scan)

Badge scanner attendance for every Pleasant Valley FIRST Robotics team, written in C#
with ASP.NET Core (.NET 10). It runs on the program's robotics server as program-wide
"team 1" and keeps its own PostgreSQL database there.

| | Address | Who can see it |
|---|---|---|
| **Live** | `https://attendance.firstpvrobotics.com` | Anyone can reach the sign-in page; everything else needs an account |
| Live, by team number | `https://1.firstpvrobotics.com` | The same site, behind the server's mentor password too |
| **Preview** | `https://dev-1.firstpvrobotics.com` | The server's mentor password, then an account |

## How a meeting works

1. A lead, coach or mentor signs in on the laptop at the door and presses **Start the
   meeting**, choosing how long it lasts (3 hours unless they pick otherwise).
2. The screen becomes the kiosk, for every team. Teammates scan in; each scan shows
   their name, team, badge number, the date and the time. Nothing else can be opened on
   that screen.
3. **End meeting** at the bottom of the kiosk asks for any account's password, ends the
   meeting and signs that person in. If nobody does, the meeting ends by itself when its
   time is up, and the screen goes back to the sign-in page. A meeting left running on
   another laptop can be ended from the Start page.

Every scan is saved with the moment in UTC and on the Central wall clock (CST or CDT),
plus the stamp the kiosk showed, like `Th10/08/2026|17:17`. The first scan of a day
checks a person in; later scans are saved too and the kiosk says they already scanned.
Each scan also records which meeting it came through and who opened that meeting.
Scans are not sent anywhere: other programs read them with a data key (below).

**No student names belong in this repository.** People and badges live only in the
database, added through the Badges and Import pages. Test code uses made-up names.

## Pages

| Page | What it does |
|---|---|
| `/login` | Signing in. The only page open without an account |
| `/` | Start: start a meeting on this screen, meetings open now, today's numbers |
| `/kiosk` | The kiosk: a clock, the box the scanner types into, the last scan, the last ten (live) |
| `/kiosk/end` | Ending the meeting, with a password |
| `/badges` | Who holds which badge: assign, change, clear, add a person |
| `/scans` | Every scan between two dates, with a CSV download (`/scans.csv`) |
| `/import` | Load people and badges, or old scans, from a CSV |
| `/accounts` | Who can sign in: add someone, give a new password, turn an account off; data keys |
| `/account/password` | Choose your own password |

## Accounts

Everyone with an account (a lead, coach or mentor) can do everything, including adding
accounts. Adding someone, or giving them a new password, shows a **temporary password**
once; they choose their own the first time they sign in, and it stops working after 7
days. Passwords are at least 10 characters and are stored only as PBKDF2 hashes.

- Five wrong passwords in a row lock an account for 15 minutes. One address gets ten
  sign-in attempts a minute.
- Turning an account off, or giving it a new password, signs it out everywhere at once.
- A sign-in lasts 12 hours from its last use. Sign-ins survive releases: the keys that
  sign them are kept in the database.

**The very first account**, or getting back in when nobody can, is made from the server's
shell, inside the live container:

```bash
sudo robotics-team docker 1 exec live-web-1 dotnet BadgeScan.Web.dll account add <username> "<Full Name>" mentor
sudo robotics-team docker 1 exec live-web-1 dotnet BadgeScan.Web.dll account reset <username>
sudo robotics-team docker 1 exec live-web-1 dotnet BadgeScan.Web.dll account list
```

(`dev-web-1` for the preview, which has its own database and its own accounts.)

## Data keys: reading the scans from another program

Make a key on the Accounts page; it is shown once. A key can read scans and nothing else,
and can be revoked there. The program sends it in a header, never in the address:

```bash
curl -H "Authorization: Bearer bsk_…" "https://attendance.firstpvrobotics.com/api/scans/since?after=0"
```

The answer is `{ "after": 0, "next": 812, "scans": [ … ] }`, oldest first, at most 500
(`&limit=` up to 1000). Keep `next` and ask again with `after=<next>` to get only what is
new. Every scan is included: check `recognised` (a known badge) and `firstOfDay` (the
scan that checked someone in). `GET /api/scans?date=…` and `/scans.csv?date=…` work with
a key too.

Keep the key on a server (a Supabase edge function secret, say), never in a web page's
code: anyone who can read the page could read the key.

## JSON

| | Address | Body → answer |
|---|---|---|
| `POST` | `/api/scans` | `{ "badge": "0000000027" }` → the saved scan; 404 when the badge is unknown. Kiosk or staff |
| `GET` | `/api/scans?date=2026-10-08&to=…&team=…&limit=…` | scans for a Central date or range, newest first. A kiosk gets today's last ten only |
| `GET` | `/api/scans/since?after=0&limit=500` | every scan after an id, oldest first. Staff or data key |
| `GET` | `/api/scans/stream` | server-sent events, one `scan` event per new scan. Kiosk or staff |
| `POST` | `/api/scans/import` | a CSV body `name,team,stamp` |
| `GET` `POST` | `/api/badges` | the people; add one `{ "team": 7196, "name": "…", "badge": "27" }` |
| `PUT` `DELETE` | `/api/badges/{id}` | `{ "badge": "27" }` or `{ "badge": null }`; remove a person |
| `POST` | `/api/badges/import` | a CSV body `name,team,badge` |

Everything else is for a signed-in lead, coach or mentor. Who may do what is in
`Services/SignIns.cs`. A scan carries `name`, `team`, `badge`, `stamp`, `dateText`
("Tuesday, October 27, 2026"), `timeText` ("5:17 PM CDT"), `scannedUtc`,
`scannedCentral`, `firstOfDay`, `recognised`, `meetingId` and `openedBy`.

## Run it on your computer

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download), then:

```bash
dotnet watch --project src/BadgeScan.Web     # http://localhost:5300, reloads when you save
dotnet test                                  # runs the tests
```

Without a database the site keeps everything in memory and says so at the top of each
page, and `dotnet watch` makes one account to sign in with: `dev`, password
`development only`. To use a real PostgreSQL on your computer, set `PGHOST`, `PGPORT`, `PGUSER`,
`PGPASSWORD` and `PGDATABASE` in `src/BadgeScan.Web/Properties/launchSettings.json`
(never commit a password). The tables are created on first start.

## How it's put together

| File or folder | What it does |
|---|---|
| `src/BadgeScan.Web/Pages/` | One `.cshtml` per page; `Shared/_Layout.cshtml` is the frame around them |
| `Controllers/Api/` | The JSON: `ScansApiController` (scans, the live stream, CSV), `BadgesApiController` |
| `Services/ScanRecorder.cs` | What a scan means: who, when (UTC and Central), first of the day or not |
| `Services/CentralTime.cs` | The team's wall clock and the stamp format |
| `Services/BadgeNumber.cs` | `0000000027` and `27` are the same badge |
| `Services/ScanBroadcaster.cs` | Hands each new scan to every open kiosk |
| `Services/Importer.cs` | The CSV imports |
| `Services/SignIns.cs` | Who is asking (staff, kiosk, or a program with a data key) and what each may do |
| `Services/AccountService.cs` | Passwords, lockouts, temporary passwords, data keys |
| `Services/AccountCommand.cs` | `dotnet BadgeScan.Web.dll account …` from the server's shell |
| `Data/Migrations/*.sql` | The database, as plain SQL. Add a new file to change it; never edit an old one |
| `Data/Migrator.cs` | Runs those files at start |
| `Data/PostgresStore.cs`, `Data/InMemoryStore.cs` | Where badges and scans are kept |
| `Data/PostgresAccountStore.cs`, `Data/InMemoryAccountStore.cs` | Where accounts, meetings and data keys are kept |
| `Data/PostgresXmlRepository.cs` | Keeps the sign-in cookie keys in the database |
| `wwwroot/js/kiosk.js`, `badges.js` | The pages' JavaScript, no build step |
| `tests/BadgeScan.Web.Tests/` | Tests GitHub runs on every pull request. They never touch a real database |

## How a change gets to the server

1. Branch, change, `dotnet test`.
2. Open a pull request into `main`. GitHub builds the site, runs the tests, builds the
   container and asks it `/healthz`.
3. Merge. Within a minute the robotics server updates the **preview**.
4. Tag a version (`git tag v0.2.1 && git push origin v0.2.1`). Within a minute the live
   site updates. A release that fails its health check is not kept; the previous one
   keeps running.

Server-side notes (which team, the kiosk password, where logs are) are in the mentors'
"Seamer Server" folder, not here.

## Files the server needs

- `Dockerfile`: compiles the site and runs it.
- `compose.yaml`: how the server runs the container, and which settings it is handed.
- `robotics.yml`: the address, the port, the health check. No `public: true`, so
  `1.firstpvrobotics.com` keeps the server's password as well. The public name
  `attendance.firstpvrobotics.com` is a mentors' route on the server
  (`/etc/caddy/robotics/routes/attendance.caddy`) to the same live container.
- `.github/workflows/check.yml`: the checks on every pull request.
