# Badge Scan

The badge scanner attendance kiosk for Pleasant Valley FIRST Robotics, written in C# with
ASP.NET Core (.NET 10). It runs on the program's robotics server as program-wide
"team 1" and keeps its own PostgreSQL database there.

| | Address | Who can see it |
|---|---|---|
| **Preview** | `https://dev-1.firstpvrobotics.com` | Mentors (password) |
| **Live kiosk** | `https://1.firstpvrobotics.com` | Mentors, and the kiosk laptop with the `kiosk` password |

Every scan is saved with the moment in UTC and on the Central wall clock (CST or CDT),
plus the stamp the kiosk showed, like `Th10/08/2026|17:17`. The first scan of a day
checks a person in; later scans are saved too and the kiosk says they already scanned.
Scans are not sent anywhere else: another project will carry them into the attendance app.

**No student names belong in this repository.** People and badges live only in the
database, added through the Badges and Import pages. Test code uses made-up names.

## Pages

| Page | What it does |
|---|---|
| `/` | The kiosk: a clock, the box the scanner types into, the last ten scans (live) |
| `/badges` | Who holds which badge: assign, change, clear, add a person |
| `/scans` | Every scan between two dates, with a CSV download (`/scans.csv`) |
| `/import` | Load people and badges, or old scans, from a CSV |

## JSON

| | Address | Body → answer |
|---|---|---|
| `POST` | `/api/scans` | `{ "badge": "0000000027", "team": 7196 }` → the saved scan; 404 when the badge is unknown |
| `GET` | `/api/scans?date=2026-10-08&to=…&team=…&limit=…` | scans for a Central date or range, newest first |
| `GET` | `/api/scans/stream` | server-sent events, one `scan` event per new scan |
| `POST` | `/api/scans/import` | a CSV body `name,team,stamp` |
| `GET` `POST` | `/api/badges` | the people; add one `{ "team": 7196, "name": "…", "badge": "27" }` |
| `PUT` `DELETE` | `/api/badges/{id}` | `{ "badge": "27" }` or `{ "badge": null }`; remove a person |
| `POST` | `/api/badges/import` | a CSV body `name,team,badge` |

All of it sits behind the server's password, like the pages.

## Run it on your computer

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download), then:

```bash
dotnet watch --project src/BadgeScan.Web     # http://localhost:5300, reloads when you save
dotnet test                                  # runs the tests
```

Without a database the site keeps everything in memory and says so at the top of each
page. To use a real PostgreSQL on your computer, set `PGHOST`, `PGPORT`, `PGUSER`,
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
| `Data/Migrations/*.sql` | The database, as plain SQL. Add a new file to change it; never edit an old one |
| `Data/Migrator.cs` | Runs those files at start |
| `Data/PostgresStore.cs`, `Data/InMemoryStore.cs` | Where badges and scans are kept |
| `wwwroot/js/kiosk.js`, `badges.js` | The pages' JavaScript, no build step |
| `tests/BadgeScan.Web.Tests/` | Tests GitHub runs on every pull request. They never touch a real database |

## How a change gets to the server

1. Branch, change, `dotnet test`.
2. Open a pull request into `main`. GitHub builds the site, runs the tests, builds the
   container and asks it `/healthz`.
3. Merge. Within a minute the robotics server updates the **preview**.
4. Tag a version (`git tag v0.1.1 && git push origin v0.1.1`). Within a minute the live
   kiosk updates. A release that fails its health check is not kept; the previous one
   keeps running.

Server-side notes (which team, the kiosk password, where logs are) are in the mentors'
"Seamer Server" folder, not here.

## Files the server needs

- `Dockerfile`: compiles the site and runs it.
- `compose.yaml`: how the server runs the container, and which settings it is handed.
- `robotics.yml`: the address, the port, the health check. No `public: true`: it stays behind a password.
- `.github/workflows/check.yml`: the checks on every pull request.
