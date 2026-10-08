# Architecture

Living document. Updated in the same PR as the code it describes. Last updated: Phase 0.

## Solution layout

| Project | Role | Depends on |
|---|---|---|
| `Metrics.Domain` | Entities and enums, no dependencies | none |
| `Metrics.Application` | Service interfaces (`IHealthProbe`; later file storage, cache, formula engine) and the formula engine | Domain |
| `Metrics.Infrastructure` | EF Core `MetricsDbContext` + migrations, Redis, probes, later disk storage and background workers | Application |
| `Metrics.Api` | Thin controllers, DI wiring, Swagger, CORS | Application, Infrastructure |
| `Metrics.Tests` | xUnit + Moq | Application, Domain |
| `web/` | React + Vite + TS + Tailwind + Recharts | talks to API over `/api` (Vite dev proxy) |

## Domain model (implemented in Phase 0 schema)

- `User` (Team: Business or Technical), `Automation` (name, description, client, requirement, department, `LastActivityAt`, `DataVersion`), `AutomationDocument` (file metadata; GUID `StoredFileName`).
- `MetricDefinition`: label, `ValueType` (Number, Percentage, Currency, Duration), `Kind` (Input, Computed), immutable `FormulaText`, soft delete (`IsDeleted`). Label is unique per automation among live definitions (partial unique index).
- `MetricLog` + `MetricLogValue`: each log row snapshots label and type and stores both input and computed values. `MetricLogValue.MetricDefinitionId` deliberately has no foreign key, so history survives definition deletion. Values are `numeric(28,10)`; durations are stored in seconds, percentages as plain percent figures.
- Enums are stored as strings.

## Infrastructure notes

- Postgres via Npgsql; Redis via `IDistributedCache` (StackExchange.Redis, `AbortOnConnectFail=false` so the API starts without Redis).
- `GET /api/health` runs every registered `IHealthProbe` (postgres, redis) and returns 200 or 503 with per-check detail.
- Local Docker ports are 5434 (Postgres) and 6380 (Redis); see DECISIONS #D-001.

## Planned (not yet built)

Formula engine, caching and invalidation by `DataVersion`, polling (regular + long poll), upload storage, auth. Designs are in the Phase 0 planning discussion and will be written here as each phase lands.
