# Automation Metrics Dashboard (P2 POC)

Base-level POC: a catalog of automation projects, each with typed input metrics, optional computed (formula) metrics, metric logs, and a trend chart. See `CLAUDE.md` for conventions, `docs/ARCHITECTURE.md` for design and `docs/DECISIONS.md` for the decision log.

**Presenting it:** [docs/DEMO_SCRIPT.md](docs/DEMO_SCRIPT.md) (timed 5-minute run) and [docs/INTERVIEW_NOTES.md](docs/INTERVIEW_NOTES.md) (design stories, limits, likely questions).

**Step-by-step local testing (run it, then walk through every feature): [docs/LOCAL_TESTING.md](docs/LOCAL_TESTING.md).**

## Prerequisites

.NET 8 SDK, Node 20+, Docker.

## Run

```
docker compose up -d                                   # postgres (host port 5434) + redis (host port 6380)
dotnet ef database update --project src/Metrics.Infrastructure --startup-project src/Metrics.Api
dotnet run --project src/Metrics.Api --urls http://localhost:5223
cd web && npm install && npm run dev                   # http://localhost:5173
```

- Swagger: http://localhost:5223/swagger
- Health: http://localhost:5223/api/health
- Catalog: `GET /api/automations?from=&to=&department=&q=` (log in first; see Swagger)

Host ports are non-default (5434, 6380) to avoid clashing with other local Postgres/Redis instances.

## Configuration and secrets

All settings are in `src/Metrics.Api/appsettings.json`. Values you must change are marked `CHANGE_ME`; find them with:

```
grep -rn CHANGE_ME src/Metrics.Api/appsettings.json
```

Put real values in `src/Metrics.Api/appsettings.Local.json` (gitignored, optional, loaded last), mirroring the same keys, e.g. `{ "Jwt": { "Key": "<random 32+ chars>" } }`. In Development the placeholder key works with a warning; any other environment refuses to start with it.

## Demo accounts (Development only)

On startup in Development the API applies migrations and seeds these users. The password is `Seed:DevPassword` from config (default `CHANGE_ME_Password123!`):

| Email | Team |
|---|---|
| tech.alice@demo.local, tech.bob@demo.local | Technical |
| biz.carol@demo.local, biz.dave@demo.local | Business |

You can also register your own account at http://localhost:5173/register.

## Test

```
dotnet test
dotnet test --filter "FullyQualifiedName~SomeTestClass.SomeMethod"   # single test
```

## Status

Phase 0 (scaffolding) and Phase 1 (auth and team assignment) done. Phase 2 (automation catalog with filters) and Phase 3 (related documents) and Phase 4 (formula engine and metric definitions) and Phase 5 (reporting, chart, history) and Phase 6 (Redis caching, polling, background worker) done. All PRD section 7 MVP phases are complete.

## Uploads

Related documents are stored on local disk under `Storage:UploadsPath` (default `src/Metrics.Api/uploads`, gitignored) with GUID file names; metadata lives in PostgreSQL. Allowed types: PDF, DOCX, XLSX, PNG, JPG; max 10 MB.

## Demo data

In Development the API seeds the "Trade Reconciliation Bot" automation with typed metrics (records, manual and automated time per run, manual and automated cost per run), two computed metrics (time saved, cost saved) and 12 historical reports, so the ROI tab has a chart and history on first run. Log in as a Technical user to report new values.

## Caching, polling and the worker

The ROI read is cached in Redis (the key includes the automation's data version; see `docs/ARCHITECTURE.md`). The ROI tab updates by long polling and the catalog by regular polling every 30 s; there is no SignalR or WebSocket. A background worker warms the cache and flags automations whose latest report is older than `Roi:StaleAfterDays`. Tunables are in the `Roi` section of `appsettings.json` (none are secrets).

Useful checks:

```
curl -i -H "Authorization: Bearer <token>" http://localhost:5223/api/automations/<id>/roi   # X-Cache: HIT | MISS | BYPASS
docker compose exec redis redis-cli --scan --pattern 'metrics:*'                             # cached keys
docker compose stop redis                                                                    # the API keeps working (BYPASS)
```

## Demo script (PRD section 7, end to end)

1. Log in as `tech.alice@demo.local` and open the catalog: six cards; try the department filter, search and date range.
2. Open "Trade Reconciliation Bot": client and requirement; upload a PDF under Related documents and download it again.
3. ROI tab: current figures, trend (switch metrics), history. Add a metric of your own: an input, then a computed metric with a formula; try a bad formula to see the positioned error.
4. Report a new set of values: figures, chart and history update, and the computed metrics are calculated.
5. Open the same automation in a second browser window as `biz.carol@demo.local` (read-only). Report again from Alice's window: Carol's view updates on its own within a second.
6. Delete a computed metric and recreate it with a different formula: the old history keeps the old numbers.
