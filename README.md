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

Host ports are non-default (5434, 6380) to avoid clashing with other local Postgres/Redis instances.

## Test

```
dotnet test
dotnet test --filter "FullyQualifiedName~SomeTestClass.SomeMethod"   # single test
```

## Status

Phase 0 (scaffolding) only. No auth or features yet.
