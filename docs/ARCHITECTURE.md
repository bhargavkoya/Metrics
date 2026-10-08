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

## Auth (Phase 1)

- Flow: `POST /api/auth/register|login` -> `AuthService` (Application) -> `IUserRepository` (EF), `IPasswordHasher` (ASP.NET PBKDF2), `ITokenService` (JWT HS256). Response is `{ token, expiresAt, user }`.
- The API validates the bearer token (issuer, audience, signature, lifetime). Authorization: a fallback policy requires any authenticated user; the `Technical` policy requires claim `team=Technical`. Use `[Authorize(Policy = Policies.Technical)]` on write endpoints; `[AllowAnonymous]` only on health and register/login.
- Application exceptions (`ValidationFailedException` 400, `ConflictException` 409, `UnauthorizedException` 401) are mapped to ProblemDetails by `ApiExceptionHandler`.
- Web: `AuthProvider` keeps the session in React state mirrored to `localStorage`; `api/client.ts` attaches the bearer token and logs out on 401; `ProtectedRoute` guards pages.
- Config: see DECISIONS D-005 (`Jwt:*`, `Seed:*` in `appsettings.json`, overrides in `appsettings.Local.json`).

## Catalog (Phase 2)

- Endpoints (any authenticated employee, read-only, global access): `GET /api/automations` (cards), `GET /api/automations/departments`, `GET /api/automations/{id}` (detail). No create/update/delete (automations are seeded; D-002).
- `GET /api/automations` query params, all optional and ANDed: `from`, `to` (date range on `LastActivityAt`), `department` (exact, case-insensitive), `q` (case-insensitive substring of name or description). Results are ordered by `LastActivityAt` desc, then name. Cards carry no metric aggregates (PRD: plain catalog).
- Date semantics: dates are treated as UTC. A bare `to` date includes that whole day; `from` > `to` returns 400. Normalization lives in `AutomationService.Normalize`; the repository only receives a ready query.
- `NotFoundException` maps to 404 in `ApiExceptionHandler`.
- Web: `CatalogPage` keeps filters in the URL query string (`q` is debounced 300 ms); `AutomationDetailPage` is a stub until documents (Phase 3) and ROI (Phase 5).
- Dev seed: 6 automations across Capital Allocation, Investment Operations, Risk and Compliance, with activity 2 to 57 days ago (relative to startup, only inserted if missing by name).

## Documents (Phase 3)

- Endpoints under `/api/automations/{automationId}/documents`: `GET` list and `GET {id}/download` for any employee; `POST` (multipart field `file`) and `DELETE {id}` for the Technical team only.
- Flow: `DocumentsController` -> `DocumentService` (Application: validation via the pure `FileValidator`, orchestration, orphan cleanup) -> `IFileStorage` (`LocalDiskFileStorage`) and `IDocumentRepository` (`EfDocumentRepository`, which also bumps `LastActivityAt` in the same save).
- Security properties are listed in DECISIONS D-008: GUID file names, extension allowlist plus magic bytes, size cap, path-traversal guards, per-automation lookup, attachment-only downloads.
- Web: the detail page has an Overview tab (details plus `DocumentsSection`) and an ROI tab placeholder. Technical users see the drag-and-drop uploader (multi-file, per-file status) and Delete; Business users only see the list and Download. Downloads use an authenticated `fetch` and a blob save because a plain link can't send the bearer token.
- Config: `Storage:UploadsPath` in `appsettings.json` (relative paths resolve against the API content root; the folder is gitignored).

## Formula engine and metric definitions (Phase 4)

- Layout: `Metrics.Application/Formulas` (pure, no I/O): `Tokenizer` -> `Parser` (AST: `NumberLit`, `MetricRef`, `Negate`, `Binary`) -> `TypeChecker` (uses `TypeRules`) -> `Evaluator`. `FormulaEngine` implements `IFormulaEngine.Analyze` (never throws for bad formulas; returns result type, references and positioned errors) and `Evaluate` (returns null when data makes the result undefined). `MetricType` = value kind plus currency code.
- Grammar: `expr := term (('+'|'-') term)*`, `term := unary (('*'|'/') unary)*`, `unary := '-' unary | primary`, `primary := NUMBER | '[' label ']' | '(' expr ')'`.
- Type table (N number, P percentage, C currency, D duration):

  | Op | Allowed -> result |
  |---|---|
  | `+` `-` | same type only |
  | `*` | N*N->N; N*C, N*D, N*P (either order) keep the other type; C*P, D*P (either order) -> C, D |
  | `/` | C/C, D/D, P/P, N/N -> N; C/N, D/N, P/N keep the left type |

  Currency codes must match for `+ - /`. Error codes: EmptyFormula, TooLong, TooDeep, SyntaxError, UnknownMetric, IncompatibleTypes, IncompatibleCurrency, DivisionByZeroLiteral, NoMetricReference.
- Endpoints under `/api/automations/{id}/metrics`: `GET` (any employee); `POST` create input or computed, `POST validate-formula` (dry run, always 200), `DELETE {metricId}` (Technical). No update route.
- Service flow (`MetricService`): validate label and kind, build the input type map from live input definitions, analyze the formula for computed metrics and store the inferred type. `EfMetricRepository` persists and bumps `DataVersion` in the same save; the partial unique index on live labels backs the case-insensitive pre-check.
- Web: the ROI tab hosts `MetricsPanel` (list, delete with confirm), `AddInputForm`, and `AddComputedForm` with debounced live validation and clickable input chips. Design rationale is in DECISIONS D-009.

## Reporting and the ROI tab (Phase 5)

- Endpoints under `/api/automations/{id}`: `POST logs` (Technical), `GET logs?page&pageSize` (newest first, max 100 per page), `GET roi?points=30` (current figures + chart series + `dataVersion`). Reads are open to any employee.
- Flow: `LogsController` -> `LogService` (validate against the live input metrics) -> `LogSnapshotBuilder` (pure: input rows + computed rows via `IFormulaEngine.Evaluate`, each with label/type/currency/formula snapshots) -> `EfLogRepository.AddAsync` (log, `LastActivityAt` and `DataVersion` in one save). `RoiService` assembles the read model from the live definitions and the most recent logs.
- Semantics: history rows are immutable snapshots; current figures and chart series cover live metrics only; series are keyed by definition id; null computed values mean "undefined for this data" (see DECISIONS D-010).
- Web: the ROI tab (`RoiTab`) stacks `CurrentFigures`, `TrendChart` (Recharts, one metric at a time, axis and tooltip formatted by type), `ReportForm` (Technical only; typed controls, Duration as h/m/s), `LogHistory` ("Load more"), and the metrics management panel. Any write bumps a refresh key that reloads every section.

## Planned (not yet built)

Caching and invalidation by `DataVersion`, polling (regular + long poll).
