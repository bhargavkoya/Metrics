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

## D-005: Config in one file with CHANGE_ME placeholders (2026-10-08)
Overrides the original "user-secrets / env vars" convention at the user's request, so every value to update is trackable in one place. `appsettings.json` holds `Jwt:Key` and `Seed:DevPassword` as `CHANGE_ME...` placeholders; `grep CHANGE_ME` lists them. Real values go in the gitignored optional `appsettings.Local.json`. Startup fails if `Jwt:Key` is under 32 chars anywhere, or still a placeholder outside Development (Development logs a warning). Tradeoff: a dev who never overrides the key runs locally with a publicly known one.

## D-006: Auth design (2026-10-08)
- Self-service register (team chosen by the user) and login return a JWT (HS256, 8h, no refresh token). Claims: `sub`, `email`, `name`, `team`. `MapInboundClaims=false` keeps claim names as issued.
- Passwords hashed with ASP.NET `PasswordHasher` (PBKDF2). Login returns one generic 401 for unknown email and wrong password.
- Authorization: fallback policy requires an authenticated user; `Technical` policy checks the `team` claim. Health and register/login are anonymous. Team is read from the token, so a team change takes effect at next login.
- Web stores the token in `localStorage` (XSS exposure vs httpOnly cookie; accepted for a POC). Any 401 with a token clears the session.
- `GET /api/auth/technical-check` is temporary, to demo/test the policy; remove once real Technical endpoints exist.
- Development startup applies migrations and seeds 4 demo users (2 Technical, 2 Business) with `Seed:DevPassword`. Not run in other environments.
- Docker services use `restart: unless-stopped`.


## D-007: Catalog filters and seeding (2026-10-08)
- Search is a case-insensitive substring match on name and description (`ToLower().Contains`), chosen over `ILIKE` so it is portable to the InMemory test provider; `%` and `_` are literal, so no wildcard escaping is needed. Fine at POC scale; a trigram or full-text index would be the production route.
- Department is an exact case-insensitive match, fed by a distinct-values endpoint.
- Date filter works on `LastActivityAt`; a bare `to` date covers the whole day; `from` > `to` is a 400 (server) and an inline message (web).
- No pagination (PRD gives no volume); revisit if the catalog grows.
- Seed activity dates are relative to startup time, and automations are matched by name, so re-running never duplicates. Sample metric logs for one automation are deferred to Phase 5, when real definitions exist to snapshot.
