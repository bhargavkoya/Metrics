# Decision Log

Newest last. Format: context, decision, consequences.

## D-001: Non-default local ports (2026-10-08)
Dev machine already runs Postgres on 5432 and Redis on 6379, so connections hit the wrong server. Docker maps Postgres to host 5434 and Redis to 6380; `appsettings.json` matches. Anyone cloning needs those ports free.

## D-002: Planning defaults for PRD ambiguities (2026-10-08)
Approved as "go with defaults":
1. Automations are seeded only; no create/update endpoints.
2. Real JWT auth (register/login) arrives in Phase 1; Phase 0 has none.
3. `Department` is a string field on Automation; the catalog team/department filter uses it.
4. Date-range filter applies to `Automation.LastActivityAt` (latest log or document change, falling back to `CreatedAt`).
5. `Currency / Currency` and `Percentage / Percentage` yield Number.
6. Currency carries a per-metric code (default USD); no conversion; `Currency +/- Currency` requires the same code.
7. A metric definition is label + type only; values exist only in logs.
8. Every log must supply all active input metrics.
9. An input metric cannot be deleted while an active computed formula references it.

## D-003: History snapshot without definition FK (2026-10-08)
`MetricLogValue` copies label and type and has no FK to `MetricDefinition`, so deleting or soft-deleting definitions never alters or breaks history.

## D-004: Workflow (2026-10-08)
One branch per phase off `main`, PR per phase, user merges. The repo's initial `master` branch was renamed to `main`, with a baseline commit (CLAUDE.md, PRD, .gitignore) made directly on it.
