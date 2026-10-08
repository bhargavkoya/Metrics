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

## D-008: Document upload design (2026-10-08)
- Allowlist PDF, DOCX, XLSX, PNG, JPG/JPEG; 10 MB cap; the extension must match the file's magic bytes (OOXML is only checked for the ZIP signature, no deeper inspection). The stored content type comes from the validated extension, never from the client's header. Served with `Content-Disposition: attachment` and `X-Content-Type-Options: nosniff`.
- Blobs are stored flat under `Storage:UploadsPath` as `{guid}{ext}`. `LocalDiskFileStorage` accepts only that exact pattern, re-checks the resolved path, and opens with `CreateNew` so it never overwrites. The original name lives only in the database and is stripped of path parts and control characters.
- The upload is buffered in memory (max 10 MB + 1 byte) so the size is measured from the bytes actually read rather than `Content-Length`, and the signature can be checked before anything touches disk. Acceptable at this cap; the production route is streaming to object storage.
- One file per request; the web app sends multiple files as parallel requests. The request body limit is 12 MB: files between 10 and 12 MB get a clear 400, larger bodies get 413 from Kestrel.
- Upload and delete set the automation's `LastActivityAt` (it drives the catalog date filter) but do not bump `DataVersion` (documents don't affect ROI data).
- If the metadata insert fails after the blob is written, the blob is deleted. Delete removes the row first, then the blob. A blob missing on disk makes download return 404.
- Documents are looked up by (automationId, documentId), so a document cannot be reached through another automation's URL.
- Lesson: EF's InMemory provider evaluates queries client-side, so it hid an untranslatable `OrderBy` over a constructed DTO that failed on Postgres (caught in a live run, fixed by ordering before the projection). Repository queries with joins or projections need a run against real Postgres, not only the InMemory tests.

## D-009: Formula engine and metric definitions (2026-10-08)
- Pipeline: tokenizer -> recursive-descent parser (AST) -> type checker -> typed evaluator. No eval or dynamic compilation. Grammar: `+ - * /`, unary minus, parentheses; `×` `÷` `−` are accepted as aliases. Metric references are `[Label]` because labels are free text.
- Type rules live in one place (`TypeRules`) and are used by both the checker and the evaluator. `+`/`-` need the same type; `*` allows Number with anything, plus Currency or Duration with Percentage (either order); `/` allows same-type ratios (which yield Number) and Currency, Duration or Percentage divided by Number. Everything else is rejected, including Currency x Currency and Number / Duration. `Duration x Percentage` was added to the original table.
- Currency types carry their ISO code (default USD, per-metric). `+ - /` between different codes is rejected; scaling by Number or Percentage keeps the code; a Currency / Currency ratio is a plain Number.
- Percentages are stored as percent points (25 = 25%). Multiplying a percentage with a Currency or Duration scales by 1/100 in the evaluator; Number x Percentage stays in percent points.
- Formulas can only reference input metrics (no computed-on-computed), so cycles cannot occur. Literal-only subtrees are constant-folded so `/ (3 - 3)` is caught at save time. Division by zero or overflow caused by data yields a null computed value, never a failed report.
- Limits: 500 characters, 50 levels of nesting (so hostile input can't overflow the stack). Each error carries a code, a message and a 1-based position; independent type errors are all reported, and an error in one operand does not cascade.
- Definitions: labels are trimmed, 100 characters max, no `[` `]`, unique case-insensitively among live definitions (computed and input share the namespace); a deleted label can be reused. The result type of a computed metric is inferred and any client-supplied type is ignored.
- Immutability: no PUT or PATCH route exists (a test asserts 405). Labels are immutable too, so formula references can't drift.
- Delete is a soft delete. An input metric cannot be deleted while a live computed formula references it (409 naming the dependents). Definition changes bump `DataVersion` (the Phase 6 cache key) but not `LastActivityAt`, which tracks logs and documents.
- The engine's tests include an exhaustive operator x type-pair matrix checked against an independent statement of the rules. I verified they bite by temporarily breaking five rules (each was caught) and restoring the code.

## D-010: Reporting, snapshots and the ROI payload (2026-10-08)
- A log is one transaction: a row per input value plus a row per live computed metric. Each row copies the label, type, currency code and (for computed rows) the formula as they were at report time (migration `LogSnapshots` added the last two columns). Nothing ever recomputes a stored row, so deleting or recreating a formula cannot change history. `LogSnapshotBuilder` is the single place that builds a log; real reports and seed data both use it.
- Units on the wire: Number raw, Percentage in percent points, Currency as an amount in the metric's own currency, Duration in seconds (the UI takes h/m/s). Validation: every live input exactly once; no unknown, duplicate or computed ids; durations non-negative; at most 10 decimal places (trailing zeros ignored) and under 10^17, to match `numeric(28,10)`. Errors are keyed by metric id so the form can mark the right field.
- Timestamps are the server's clock only; there is no user-chosen run date (the PRD defines just reporter and timestamp). A backdate field would be a small addition if wanted.
- A computed value that the data makes undefined (a zero divisor) is stored as null and shown as "n/a"; the report itself still succeeds.
- Current figures are the latest log's values restricted to metrics that are still live, which implements "deleting a computed metric removes it going forward". A metric created after the latest log shows "no value yet" and gets no backfilled points. Chart series are keyed by definition id (not label), so a deleted-and-recreated label never mixes two definitions. The chart shows one live metric at a time because units differ; the history list shows every recorded value, marking removed metrics.
- Reporting sets `LastActivityAt` (so the catalog date filter sees it) and bumps `DataVersion`, in the same save as the log.
- `GET .../roi` returns current figures, series and `dataVersion` in one payload; Phase 6 caches this single endpoint and long-polls on `dataVersion`.
- Seed: "Trade Reconciliation Bot" gets 5 inputs, 2 computed metrics and 12 backdated logs (fixed random seed) through the real services; it is skipped if the automation already has live definitions or any logs. An earlier version of this check counted soft-deleted definitions and would have skipped seeding on a dev database that had been used for manual testing.
- Verified by mutation: five deliberate breakages (not snapshotting the formula or label, keying series by label, allowing negative durations, not requiring all inputs) were each caught by the tests.
