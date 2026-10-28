# PV Robotics Badge Attendance (badge-scan)

ASP.NET Core (.NET 10) site for badge check-ins; see `README.md` for pages, accounts and
the API. Tests: `dotnet test`.

## Connecting Project-Tracker

If you're here to connect attendance data to **Project-Tracker**, the work happens in that
repo (https://github.com/MeckMan2025/Project-Tracker), not this one. Read:

1. `docs/project-tracker.md`: the read-only data key, `/api/scans/since`, every field, and
   which scans count as attendance.
2. `docs/samples/`: example answers to build against.
3. Project-Tracker's `project-tracker/BADGE-ATTENDANCE.md`: what exists on that side and
   the decisions to make with a mentor.

Don't change this repo for the sync. If a field or endpoint is missing, ask a mentor.
`tests/BadgeScan.Web.Tests/ProjectTrackerContractTests.cs` guards what Project-Tracker reads.
