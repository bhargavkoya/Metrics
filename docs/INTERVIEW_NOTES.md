# Interview notes

How to talk about this project: the pitch, the design decisions in the order you would explain them, the bugs found along the way, the honest limits, and likely questions with answers. Every number here comes from the code or the decision log (`docs/DECISIONS.md`, D-001 to D-012); if something changes, update both.

## The 30-second pitch

"It replaces a tool that only dumped raw metric logs. Each automation project gets a home page (client, requirement, documents) and an ROI tab where the Technical team defines *its own typed metrics*, optionally adds a *computed metric* built from a formula, and reports values. A hand-written, typed formula engine validates formulas when they are saved. Every report stores a snapshot of its inputs and computed values, so history never changes. The ROI read is cached in Redis, the page updates by long polling, and a background worker warms the cache and flags overdue reporting."

Stack: ASP.NET Core 8 (Web API, EF Core, Npgsql, `BackgroundService`), PostgreSQL, Redis, React + Vite + TypeScript + Tailwind + Recharts. About 3,100 lines of production C#, 4,300 lines of C# tests (427 tests), 2,200 lines of TypeScript.

## How a request flows (the shape of the code)

```
React page  ->  Controller (thin)  ->  Application service  ->  interfaces  ->  Infrastructure (EF, Redis, disk)
                                            |                      ^
                                            +-- Domain entities    +-- implemented here, injected by DI
```

- `Metrics.Domain`: entities and enums, no dependencies.
- `Metrics.Application`: services, DTOs, the formula engine, and the interfaces (`IFileStorage`, `IMetricCache`, `IFormulaEngine`, repositories, `IChangeNotifier`). No EF or Redis here, so the interesting logic is unit-testable without infrastructure.
- `Metrics.Infrastructure`: EF Core `DbContext` and migrations, repositories, Redis cache, disk storage, the background worker.
- `Metrics.Api`: controllers, auth, DI wiring.

A write, for example "report values": controller -> `LogService` validates against the live input metrics -> `LogSnapshotBuilder` builds the log and evaluates the computed metrics -> repository saves the log, `LastActivityAt` and `DataVersion` in one save -> the service signals `IChangeNotifier` -> waiting browsers wake and fetch the new data.

## The design stories

Use the pattern problem -> decision -> why -> trade-off.

### 1. Per-automation metric modeling
- **Problem:** the old tool forced one fixed metric set. The PRD's core value is that each automation defines its own.
- **Decision:** `MetricDefinition` rows per automation: a label, a type (Number, Percentage, Currency, Duration), a kind (Input or Computed), and for computed ones the formula text. No code change is needed to add a metric.
- **Typed values:** the type drives validation and display. One canonical unit per type so the engine never converts: Number raw, Percentage in *percent points* (12.5 means 12.5%), Currency as an amount with a per-metric ISO code, Duration in *seconds* (the UI takes hours/minutes/seconds).
- **Storage:** `decimal` everywhere, `numeric(28,10)` in Postgres. Never floating point, because money and ratios must not drift. Reports reject more than 10 decimal places and values at or above 10^17 so the stored value equals the reported value.

### 2. The formula engine (the part to be proudest of)
- **No `eval`, no dynamic compilation.** A small pipeline: tokenizer -> recursive-descent parser (AST) -> type checker -> typed evaluator. Pure code in the Application layer behind `IFormulaEngine`.
- **Grammar:** `+ - * /`, unary minus, parentheses; references are `[Label]` because labels are free text.
- **Type rules** live in one place (`TypeRules`) and are used by both the checker and the evaluator:

  | Op | Allowed -> result |
  |---|---|
  | `+` `-` | same type only |
  | `*` | Number with anything; Currency or Duration with Percentage (either order) |
  | `/` | same-type ratios give a Number; Currency, Duration or Percentage divided by a Number keep their type |

  Everything else is rejected: Currency x Currency, Number / Duration, Currency + Duration. Currency also carries its code, so USD and EUR cannot be mixed.
- **Why those rules:** the PRD says no "currency / duration" without an explicit rate concept. A rule that is not in the table is rejected by default (allow-list, not deny-list).
- **Percentages scale correctly:** 200 USD x 25% = 50 USD. The evaluator carries each value's type for exactly this reason.
- **Errors are useful:** each has a code, a message and a character position ("Cannot divide Number by Duration." at position 11). Independent errors are all reported; an error in one operand does not cascade into the other.
- **Division by zero:** a *literal* zero divisor is rejected when saving, including `/(3 - 3)` via constant folding. A zero that comes from *data* yields a null result for that report ("n/a"), and the report itself still succeeds.
- **Hostile input:** formulas are limited to 500 characters and 50 levels of nesting, so deeply nested input cannot overflow the stack.
- **Computed metrics can only reference input metrics,** so cycles are impossible by construction.
- **Immutable:** there is no update route (a test asserts 405 for PUT and PATCH). To change a formula you delete it and create a new one.

### 3. History is a snapshot, never recomputed
- **Problem:** if formulas could be edited and history recomputed, past ROI numbers would silently change.
- **Decision:** each report stores one row per input *and* one per live computed metric, each copying the label, type, currency code and (for computed) the formula text. There is deliberately no foreign key from a log row to the definition.
- **Consequences:** deleting a metric is a soft delete; old logs still render. A metric created after older reports is *not* back-filled (it shows "No value yet"). Chart series are keyed by definition id, not label, so deleting and recreating a metric under the same label does not mix two histories. Current figures show only live metrics; the history list shows everything and marks removed metrics.
- **Proof:** an API test reports a log, deletes the formula, recreates it differently under the same label, reports again, and asserts the first log is byte-for-byte identical to what was recorded.

### 4. Caching
- **What:** the ROI read (current figures + chart series).
- **How:** key `roi:{automationId}:v{DataVersion}:p{points}`. Every request reads the automation's `DataVersion` from Postgres first (one indexed query), then looks up that exact key. Every write that affects ROI data bumps the version in the same save, so the old key is simply never asked for again and expires after 10 minutes.
- **Why this instead of delete-on-write:** it avoids the classic race where a reader repopulates the cache with stale data right after a writer invalidates it. The cost is one small uncached database read per request, which is deliberately not cached.
- **Failure behaviour:** the cache must never be a failure mode. `IMetricCache` returns Hit / Miss / Unavailable and never throws. After a Redis failure the cache is skipped for 30 seconds so requests do not each wait on timeouts, and reads are served from Postgres. I tested this by stopping Redis: everything kept working; the first request afterwards took about 2 seconds (it was 5.75 s before I capped the Redis command timeouts at 1 s), then about 40 ms.
- **Observable:** the response header `X-Cache: HIT | MISS | BYPASS`.

### 5. Refresh: regular polling and long polling (no SignalR, per the PRD)
- **Catalog:** regular polling every 30 seconds; paused while the tab is hidden; an unchanged result causes no re-render; a failed poll keeps the last data.
- **ROI tab:** long polling. The browser asks "anything newer than version N?"; the server holds the request until the data changes (or 25 seconds pass and it returns 204), then the browser asks again.
- **No missed updates:** the server takes a change *signal first*, then checks the version, then waits on the signal. A write that commits in between completes the signal; a write that committed earlier is seen by the check. A 5-second database re-check backs it up.
- **Cost:** a waiting request holds a task, not a thread.
- **Limit:** the signal is in-process, so this is a single-server design. Scaling out means swapping `InProcessChangeNotifier` for Redis pub/sub behind the same interface.

### 6. The background worker
Every 60 seconds it warms the ROI cache for each automation that has reports and records which automations are *stale*: the latest report is older than 14 days. The flags go into one short-lived Redis key (expiring after three missed cycles, so a dead worker stops flagging rather than lingering), and the catalog shows a "No recent report" badge. "Stale" is a business signal (reporting is overdue), not cache age. One failing automation is logged and does not stop the cycle.

### 7. File uploads
Allow-list of extensions (PDF, DOCX, XLSX, PNG, JPG), 10 MB cap measured on the bytes actually read (not `Content-Length`), and the extension must match the file's first bytes (a renamed `.exe` is rejected). Files are stored under a generated GUID name; the original name lives only in the database. The storage class accepts only that exact name pattern, re-checks the resolved path, and opens with `CreateNew` so it never overwrites. If the database write fails after the file is saved, the file is deleted. Downloads are `attachment` with `nosniff`, and a document is looked up by (automation, document) so it cannot be reached through another automation's URL.

### 8. Auth and authorization
Email + password; passwords hashed with ASP.NET's PBKDF2 hasher; login failures return one generic message so the response does not reveal which emails exist. JWT (8 hours, no refresh). Authorization is deny-by-default (a fallback policy requires a signed-in user) with one `Technical` policy on write endpoints. Access is *global by the PRD's decision*: any employee views any automation, any Technical employee acts on any automation, no per-automation owner checks.

### 9. How I verified it
- 427 automated tests (xUnit + Moq, plus in-process API tests).
- **Mutation checks:** at several phases I deliberately broke behaviour (for example "allow Number / Duration", "key without the version", "signal after the version check") to confirm a test fails. Every one was caught.
- **Real-database runs:** I ran each phase's flow against real Postgres and Redis, not only the in-memory test database.
- The process: plan -> approval -> one branch and PR per phase, with `ARCHITECTURE.md` and the decision log updated in the same PR.

## Bugs found along the way (good stories, and they show the process works)
1. **Storage cleanup deleted the wrong file.** When saving to a name that already existed, my cleanup `catch` deleted the *existing* file. A unit test for "never overwrite" caught it. Fix: only delete a partial file this call created.
2. **A query that passed tests but failed on Postgres.** Ordering a projected DTO is not translatable by EF; the in-memory test provider evaluated it client-side and hid the error. Only a live run found it. Rule I now follow: after changing a repository query, run it once against the real database.
3. **A seed check that would have silently skipped.** "Already set up?" counted soft-deleted rows, so on a database used for manual testing the demo data would never appear.
4. **The first read after a Redis outage took 5.75 s.** Queued commands waited the library's 5-second default. Capping the timeouts brought it to about 2 s.
5. **A test I removed.** In Phase 4 I wrote a test that claimed to prove deletes leave history untouched, but there was no log storage yet, so it proved nothing. I deleted it and wrote the real one in Phase 5.
6. **Log noise.** Expected rejections (a bad password, an invalid formula) were logged as errors with stack traces. Fixed with an MVC exception filter, and a test proves a *real* fault still logs an error and returns 500.

## Honest limits and what I would do next
- **A theoretical race in `DataVersion` (found by reading the code while preparing these notes).** The version is incremented with a read-modify-write (`DataVersion++`, which EF saves as `SET DataVersion = <absolute value>`). If two people write to the *same automation* at nearly the same instant, both can read 5 and both write 6, so the version moves by one instead of two. If someone reads in the gap, the cache could hold a payload under version 6 that lacks the second write, and long-polling clients waiting on version 6 would not be told, until the 10-minute TTL expires. **I have not observed it:** I fired 90 near-simultaneous reports at one automation (three rounds of 30) and the version stayed exactly in step with the number of logs. A shell script starts each request a few milliseconds apart, so that does not rule it out; it only shows the window is narrow. The fix is cheap, so I would still make it: an atomic `UPDATE ... SET "DataVersion" = "DataVersion" + 1` in the same transaction (or an optimistic concurrency token with a retry), with a test against real Postgres, since the in-memory provider cannot reproduce it. Be upfront about it: say you found it by reasoning, tested it, could not trigger it, and would fix it anyway.
- **Long polling is single-server.** Redis pub/sub is the scale-out path.
- **The token is in `localStorage`** (exposed to XSS). An httpOnly cookie plus CSRF protection would be the production choice. No refresh tokens, no login rate limiting.
- **Uploads are buffered in memory** (up to 10 MB) so size and type are checked before anything touches disk. At larger sizes: stream to object storage, add virus scanning.
- **No frontend automated tests** (a conscious decision); the UI is covered by manual testing (`docs/LOCAL_TESTING.md`) and by type-check and lint.
- **Seeded automations only**; the PRD defines no create/edit for automations. No pagination in the catalog (the PRD gives no volume).
- **Catalog search is a substring match.** Production would use full-text or trigram search.
- **Cache warming covers only automations with reports** and the default chart size.

## Likely questions, with answers

**Why not just use `eval` or a library for formulas?** Safety and control. A formula is untrusted text typed by users; evaluating it dynamically is code injection. A tiny parser means the allowed operations are exactly the grammar, errors carry positions, and the type rules are mine to enforce, which a generic library would not do.

**How do you stop someone mixing currency and duration?** The type checker propagates a type for every sub-expression and consults one table. Anything not allowed in the table is rejected when the formula is saved, so a bad formula can never reach the reporting path.

**What happens if the data makes a formula divide by zero?** That report's computed value is stored as null and shown as "n/a"; the report itself succeeds. A *literal* zero is caught at save time instead.

**Why not recompute history when a formula changes?** Because then past ROI would silently change. Formulas are immutable, and each report stores the values and the formula text as they were. Changing a formula means delete and recreate, and the new one applies from the next report onward.

**What if a metric is deleted?** It is a soft delete. It disappears from the live set and the current figures. Existing logs keep its value, marked as removed.

**Why `decimal` and not `double`?** Money and ratios; binary floating point cannot represent values like 0.1 exactly, and sums drift. Postgres `numeric(28,10)` matches, and the API refuses input it could not store exactly.

**Why store percentages as 12.5 rather than 0.125?** It is what people type and read. The evaluator knows a percentage multiplying a currency or duration needs to be divided by 100, and that rule lives in one place.

**How does the cache stay correct?** The version is in the key and always read from the database first; every write that changes ROI data bumps it in the same save. There is no invalidate-then-repopulate window. (The one caveat is the concurrent-write race above.)

**What if Redis goes down?** The API serves from Postgres. The cache wrapper never throws, backs off for 30 seconds after a failure so requests do not each wait on timeouts, and resumes by itself. I tested it by stopping the container.

**Why long polling and not SignalR?** The PRD says polling-based refresh and no SignalR, mirroring the real dashboard's pattern. Long polling gives near-instant updates with plain HTTP and no connection state to manage, at the cost of a held request.

**How would you scale long polling to several servers?** Replace the in-process notifier with Redis pub/sub behind the same interface, so a write on one server wakes waiters on all of them. The 5-second database re-check already bounds the delay as a fallback.

**What if two people report at the same time?** Both reports are saved as separate logs (they do not conflict). The one thing that is not fully safe is the version counter; see the limits above and the fix.

**How do you prevent path traversal in uploads?** Client filenames are never used for anything on disk. Files get a generated GUID name; the storage class only accepts that exact pattern and re-checks the resolved path; the original name is data in the database only.

**Why can any Technical user act on any automation?** It is a PRD decision (D-002 / the decisions log): global access, no per-automation ownership. The code has no owner checks by design; adding them later means adding an owner column and a policy.

**Why is the JWT in `localStorage`?** A POC trade-off, recorded in D-006. It is exposed to XSS; production would use an httpOnly cookie with CSRF protection and short-lived tokens with refresh.

**How do you know the tests are any good?** Beyond counts: I broke behaviours on purpose and checked that tests fail (17 deliberate breakages across phases 4 to 6 and the cleanup, all caught), and I ran each flow against real Postgres and Redis, which found three problems the unit tests could not: the Postgres-only query failure, the seed check, and the slow first read after a Redis outage.

**What would you do with another week?** Fix the version race with an atomic increment and a Postgres-backed test; Redis pub/sub for multi-server long polling; httpOnly cookie auth; streaming uploads to object storage; a small Playwright suite for the two-window live update.

**Walk me through the architecture.** Four projects: Domain, Application (logic and interfaces), Infrastructure (EF, Redis, disk, worker), Api (thin controllers). Controllers only translate HTTP; services hold the rules; infrastructure is swapped in through interfaces, which is why the cache, storage and formula engine are all mockable.
