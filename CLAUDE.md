# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Status

Greenfield: the repo currently contains only the PRD at `docs/Bhargava-Koya-P2-MetricsAnalysis-PRD.docx` (a .docx, not `docs/PRD.md`). Everything below under Architecture/Commands is the *planned* layout; verify against the actual tree before relying on it. Read the PRD before planning any phase (MVP slice is §7; decisions log §8).

This is a base-level interview POC (Automation Metrics Dashboard): a demoable skeleton to screen-share in ~5 minutes, not a production system or BI platform. Do not over-engineer, and do not add features outside the PRD §7 MVP slice unless asked.

## Working agreement

- Plan before coding on any new phase; wait for user approval.
- After each phase: build, run tests, and tell the user exactly how to verify manually.
- If the PRD is ambiguous, ask; don't silently decide. Resolved ambiguities are in `docs/DECISIONS.md` (D-002), e.g. the catalog department filter uses a `Department` string on Automation.
- Small commits, clear messages.
- **Config and secrets** live in one place: `src/Metrics.Api/appsettings.json`, with `CHANGE_ME...` placeholders for anything secret (JWT key, seed password) so the user can grep for what to update. Do not scatter secrets into user-secrets, env vars or code. Real values go in the gitignored `appsettings.Local.json` (loaded last, optional); never commit them. The app refuses to start outside Development with a placeholder JWT key.

## Git workflow

- Base branch is `main`. One feature branch per phase, created from an up-to-date `main` (e.g. `phase-0/scaffolding`, `phase-1/auth`). Never commit directly to `main`.
- When a phase is done (build + tests green), push the branch and open a PR against `main` with `gh pr create`: summary, what changed, and manual verification steps.
- The user reviews and merges every PR themselves. Never merge a PR, and do not start the next phase's branch until the user confirms the previous PR is merged (then pull `main` first).
- The repo's initial local branch is `master`; the first commit/branch setup must end up with `main` as the base. Confirm with the user before pushing or renaming anything.

## Project docs (keep current throughout the dev cycle)

Maintain these under `docs/`; update them in the same PR as the code they describe:

- `docs/ARCHITECTURE.md`: solution structure, domain model, formula engine, caching, polling, upload design, API surface. Update when any of these change.
- `docs/DECISIONS.md`: running decision log (ADR-lite: number, date, decision, context, alternatives, consequences). Add an entry for every non-trivial choice or resolved PRD ambiguity, e.g. the department filter source, date-range basis, `C/C` result type.
- `README.md`: setup and run instructions; keep the Commands section here and in this file consistent.
- Also record PRD deviations or clarifications in `DECISIONS.md` rather than silently diverging. The PRD itself (`docs/*.docx`) is not edited.

## Commands

```
docker compose up -d                       # postgres + redis
dotnet run --project src/Metrics.Api
dotnet test
dotnet test --filter "FullyQualifiedName~FormulaParserTests.MethodName"   # single test
cd web && npm run dev                      # React/Vite frontend
```

## Stack

- Backend: ASP.NET Core Web API (.NET 8), EF Core + Npgsql (migrations), `BackgroundService` workers
- DB: PostgreSQL (incl. file metadata). Cache: Redis via `IDistributedCache` / StackExchange.Redis
- Frontend (`web/`): React + Vite + TypeScript + Tailwind + Recharts
- Files: local disk `uploads/`, metadata in PostgreSQL
- Refresh: regular polling + long polling. **No SignalR / websockets.**

## Architecture

Solution layout: `src/Metrics.Api`, `.Application`, `.Domain`, `.Infrastructure`, `tests/Metrics.Tests`. Thin controllers; logic in Application services; interfaces (in Application) for file storage, cache, and the formula engine, implemented in Infrastructure. C#: nullable enabled, async everywhere with `CancellationToken`, constructor DI.

Cross-cutting design that spans files:

- **Formula engine**: hand-rolled tokenizer -> AST -> typed evaluator. Operators `+ - x /` and parentheses only. No eval / dynamic compilation. Validation at save time: referenced metrics exist, types compatible (e.g. no currency / duration), no division-by-zero literals.
- **Immutable formulas**: no edit path. Delete + recreate only. Deleting a computed metric removes it going forward; historical logs are never rewritten or recomputed.
- **Metric logs** store reporter, timestamp, input values, AND the computed values as of report time (snapshot, so history stays stable after formula deletion).
- **Typed values**: number, percentage, currency, duration. The type drives both validation and display formatting.
- **Caching and polling**: the ROI read (`GET .../roi`) is cached in Redis under a key that includes the automation's `DataVersion`. **Any new write that changes what the ROI payload shows must bump `DataVersion` in the same save and call `IChangeNotifier.Notify`**: that is what invalidates the cache and wakes long-polling clients. `IMetricCache` never throws (Hit/Miss/Unavailable); with Redis down, reads are served from Postgres. A background worker warms the cache and flags automations whose latest report is older than `Roi:StaleAfterDays`. See `docs/DECISIONS.md` D-011.
- **Uploads**: extensions PDF, DOCX, XLSX, PNG, JPG; 10 MB cap; store under a generated GUID filename, keep original name in DB; never trust client filenames; guard against path traversal.

## Domain rules (do not deviate)

- Only role is Employee, with team Business (read-only) or Technical (define metrics, report values, upload/delete docs).
- Access is GLOBAL: any employee views any automation; any Technical employee acts on any automation. No per-automation owner checks.
- One client per automation (string field). No approval workflow. No cumulative cross-automation view: the dashboard is a plain card catalog with date-range and team filters.
- Seed data: users in both teams, 4-6 automations, one with sample logs.

## Testing

xUnit + Moq. Focus: formula parser/validator/evaluator, type compatibility, immutability, and log recomputation rules.

The EF InMemory provider evaluates queries client-side and hides translation errors (for example, `OrderBy` over a constructed DTO fails on Postgres). After changing a repository query, run it once against the real database through the API, not only the tests.
