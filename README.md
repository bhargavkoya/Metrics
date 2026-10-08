# Automation Metrics Dashboard (P2 POC)

Base-level POC: a catalog of automation projects, each with typed input metrics, optional computed (formula) metrics, metric logs, and a trend chart. See `CLAUDE.md` for conventions, `docs/ARCHITECTURE.md` for design and `docs/DECISIONS.md` for the decision log.

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

Phase 0 (scaffolding) and Phase 1 (auth and team assignment) done. Phase 2 (automation catalog with filters) and Phase 3 (related documents) done. Metric definitions and the formula engine are next.

## Uploads

Related documents are stored on local disk under `Storage:UploadsPath` (default `src/Metrics.Api/uploads`, gitignored) with GUID file names; metadata lives in PostgreSQL. Allowed types: PDF, DOCX, XLSX, PNG, JPG; max 10 MB.
