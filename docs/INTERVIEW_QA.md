# Interview Q&A (architect level)

30 questions a senior or principal interviewer is likely to ask about this project, with answers grounded in the actual code and decisions (`docs/DECISIONS.md`, D-001 to D-012). It complements `docs/INTERVIEW_NOTES.md` (pitch, design stories, bugs, limits) and does not repeat its short Q&A list. Where an answer depends on a number or a limit, check it against the code before quoting it.

Answering style that works: state the decision, the reason, the trade-off you accepted, and what would make you change it.

Sections: [Architecture](#architecture-and-boundaries) · [Formula engine](#formula-engine-and-domain-modelling) · [Data and consistency](#data-and-consistency) · [Caching and real-time](#caching-and-real-time) · [Security](#security) · [Quality and delivery](#quality-and-delivery) · [Evolution and judgement](#evolution-and-judgement)

---

## Architecture and boundaries

**1. Why four projects (Domain, Application, Infrastructure, Api) for a POC? Isn't that over-engineering?**
The split is cheap here and pays for itself in one specific way: the interesting logic (formula engine, snapshot builder, validators, long-poll coordination) lives in `Application` with no EF, Redis or HTTP references, so it is unit-tested with plain objects and mocks. The cost is a few extra project files and interfaces. I did not add anything beyond that: no MediatR, no generic repository, no CQRS framework. If the interviewer pushes, the honest answer is that the layering is the minimum needed to make the cache, file storage and formula engine swappable and testable, not an architecture for its own sake.

**2. Where do you draw the line between a controller, a service and a repository?**
Controllers translate HTTP (route, model binding, status codes) and nothing else. Application services own rules and orchestration (validate against live metrics, build the snapshot, decide when to notify). Repositories own persistence and the transaction boundary for one aggregate change, for example "save the log, update `LastActivityAt`, bump `DataVersion`" in one save. Business rules in a repository or EF-specific types in a service are the two leaks I watch for.

**3. Why interfaces for the cache, file storage and formula engine, but plain classes elsewhere?**
I introduce an interface where there is a real second implementation or a real need to fake it: Redis versus "no cache" in tests, disk versus object storage later, and the engine so services can be tested without parsing. `RoiService` has no interface of its own, but `CachedRoiService` decorates it, which is why the cache logic stays out of the read model code. Interfaces with exactly one implementation and no test seam are noise.

**4. You used a decorator for caching. Why not cache inside the service?**
Separation of concerns and testability. `RoiService` answers "what is the ROI payload"; `CachedRoiService` answers "can I skip computing it". I can test each alone, and removing the cache is a one-line DI change. The decorator also keeps the rule "staleness is applied per request after the cache" explicit, because it sits on the outside.

**5. How would you evolve this from a modular monolith into services, if you had to?**
I would not, until a concrete pressure appeared (independent scaling, separate teams, different release cadence). The natural seams, in order: the background worker (already a separate hosted service, can become its own process), file storage (already behind `IFileStorage`), and the long-poll endpoint (the only part with unusual connection behaviour). The formula engine is pure and could be a library. The database would stay shared until there is a real ownership split; splitting data first is the usual mistake.

**6. Why is access global with no per-automation ownership, and how would you add ownership later?**
It is a PRD rule, recorded in D-002, so there are deliberately no owner checks. Adding ownership means an owner or membership table, a resource-based authorization check (`IAuthorizationService` with a requirement) applied in the service layer rather than scattered across controllers, and a catalog query filtered by membership. The `Technical` policy stays as the coarse gate; ownership becomes the fine-grained one.

---

## Formula engine and domain modelling

**7. Why build a parser instead of using a library such as NCalc or Roslyn scripting?**
Three reasons. Safety: a formula is untrusted user text, and dynamic evaluation is a code-injection surface; a grammar of `+ - * /` and parentheses has no surface beyond itself. Control: I need positioned, typed errors at save time and my own type algebra (no currency / duration), which generic libraries do not give. Size: the whole pipeline is small enough to reason about and test exhaustively. A library is the right call when the language needs to grow (functions, conditionals); then I would weigh the dependency.

**8. How do you guarantee the type rules are applied consistently?**
There is one table, `TypeRules`, used by both the type checker (at save time) and the evaluator (at report time). It is an allow-list: anything not listed is rejected, so adding a new type forces a decision about every operator rather than silently permitting combinations. The tests include an exhaustive operator x type-pair matrix compared against an independent statement of the rules, and I checked it bites by breaking five rules on purpose.

**9. Why are percentages stored as 12.5 instead of 0.125?**
It matches what people type and read, and avoids a confusing conversion at every boundary. The one place that must know is the evaluator: `Currency x Percentage` divides by 100, `Number x Percentage` stays in percent points. Keeping that in one place is the trade-off; the risk would be a second code path (for example a report export) forgetting the rule, so any new consumer should go through the same value type.

**10. What is the failure model for formulas: save time versus report time?**
Two stages, deliberately different. At save time everything structural is rejected: syntax, unknown metrics, incompatible types, currency mismatch, literal division by zero (including constant-folded `/(3 - 3)`), length and nesting limits. At report time only data-dependent problems remain (a zero divisor from data, overflow), and those produce a null computed value shown as "n/a" while the report still succeeds. A reporter should never lose a submission because a derived number is undefined.

**11. Why can computed metrics reference only input metrics?**
It removes dependency graphs: no cycles, no evaluation ordering, no cascading recomputation. It is a conscious scope cut for the POC. If the business wanted computed-on-computed, I would add a topological sort at save time with cycle detection and store the evaluation order, and I would keep immutability so a deleted dependency cannot change old snapshots.

---

## Data and consistency

**12. Why immutable formulas with snapshots instead of versioned formulas?**
The requirement is "history never changes." Versioning formulas and recomputing history would make that a property of the recompute logic, which has to be right forever. Snapshots make it a property of the data: each log row copies label, type, currency code and formula text at report time, and no code path rewrites it. The cost is storage (denormalised copies) and that a new formula applies only going forward, which matches the business meaning.

**13. Why no foreign key from log values to metric definitions?**
A foreign key would couple history to the lifecycle of the definition and make deletion either blocked or cascading. History must outlive definitions, so the log value is self-describing. The price is that integrity is by convention, so series are keyed by definition id, and tests cover delete-recreate-same-label to prove two histories never mix.

**14. Walk me through how you would handle two simultaneous reports on the same automation.**
Both logs are inserted as independent rows, so no data is lost. The weak point is `DataVersion`, incremented read-modify-write: two writers can read 5 and both write 6. I could not reproduce it (90 near-simultaneous reports stayed in step), but I would fix it anyway with an atomic `UPDATE ... SET "DataVersion" = "DataVersion" + 1` in the same transaction, or an optimistic concurrency token with retry, plus a test against real Postgres because the InMemory provider cannot show it. Naming the risk yourself is stronger than waiting to be asked.

**15. Why `decimal` and `numeric(28,10)`, and what do you reject at the edge?**
Money and ratios must not drift, and binary floating point cannot represent values such as 0.1 exactly. The API rejects more than 10 decimal places and values at or above 10^17, so the stored value equals what the user reported. That is a validation decision, not a rounding one: silently rounding financial inputs is worse than refusing them.

**16. What do soft deletes cost you, and why use them here?**
They complicate every query (a live filter) and uniqueness (the label is unique only among live rows, via a partial unique index). I used them because deletion must remove a metric "going forward" while old logs still render. The seed bug where soft-deleted rows made the demo data silently skip is the typical failure mode, so any "does it exist?" check must be explicit about live versus deleted.

---

## Caching and real-time

**17. Explain the version-keyed cache and why it is safer than invalidate-on-write.**
The key embeds `DataVersion`, and every request first reads the current version from Postgres, then looks up that exact key. A write bumps the version in the same save, so the old entry is never asked for again. With delete-on-write there is a classic race: a reader that started before the write repopulates the cache with old data after the writer invalidated it. Here a reader always asks for the version it just read, so a stale payload can only live under an old key. The trade-off is one cheap indexed read per request, which I deliberately do not cache.

**18. What happens to old cache entries?**
They are never deleted explicitly; the 10 minute TTL removes them. That is acceptable at this size. In a high-write system the number of dead keys could matter, and I would shorten the TTL or move to a single key per automation holding the version inside the value and compare on read.

**19. What are the failure modes of the cache and how are they contained?**
`IMetricCache` returns Hit, Miss or Unavailable and never throws. After a failure it is skipped for 30 seconds so every request does not wait on a timeout, and Redis command timeouts are capped at 1 second. I tested by stopping Redis: reads came from Postgres with `X-Cache: BYPASS`. That test found the first read taking 5.75 s because of queued commands, which the timeout cap reduced to about 2 s. The principle: a cache is an optimisation, never a dependency.

**20. How does long polling avoid missing an update?**
Order of operations. The server takes a change signal first, then reads the current version, then waits on the signal. A write that committed earlier is seen by the version check; a write that commits between the check and the wait completes the signal that was already taken. A 5 second database re-check covers a lost signal. A mutation test (take the signal after the check) failed as expected, which is how I know the ordering is tested and not just reasoned.

**21. Why long polling instead of SignalR or server-sent events, and what are the limits?**
The PRD forbids SignalR and specifies polling. Long polling works through any proxy over plain HTTP, has no connection state to recover, and held requests cost a task rather than a thread. Limits: the notifier is in-process, so it only works on one server; scale-out means Redis pub/sub behind `IChangeNotifier`. Each held request also occupies a connection slot, so very large audiences would favour SSE or WebSockets, which I would raise only if the requirement changed.

---

## Security

**22. Walk me through the upload threat model.**
Untrusted: the filename, the content type, the size claim, the bytes. Controls: extension allow-list, extension must match the file's magic bytes, size measured on bytes actually read, a generated GUID filename (the client name is data only), a storage class that accepts only that pattern and re-checks the resolved path, `CreateNew` so nothing is overwritten, `attachment` plus `nosniff` on download, and lookup by (automation, document) so URLs cannot cross automations. Known gaps I would state: OOXML is checked only for the ZIP signature, there is no malware scanning, and uploads are buffered in memory.

**23. What is wrong with storing the JWT in `localStorage`, and what would you do instead?**
Any XSS can read it. For production I would use an httpOnly, Secure, SameSite cookie with CSRF protection, short-lived access tokens with refresh, and login rate limiting. I accepted it for the POC and recorded it in D-006 so it is an explicit decision, not an oversight.

**24. How do authorization checks work, and what is the failure mode if someone forgets one?**
A fallback policy requires an authenticated user for every endpoint, so forgetting an attribute fails closed to "signed in", not "public". Writes add the `Technical` policy via the `team` claim. Anonymous is an explicit opt-in on health and register/login only. The residual risk is a write endpoint missing the Technical attribute, which is why the tests assert that a Business user gets 403 on a real Technical-only endpoint; I would extend that to every write route in a table-driven test. A claim-based team means a team change applies at next login, which I documented.

**25. How is configuration and secret handling designed, and what is the weakness?**
Everything lives in `appsettings.json` with `CHANGE_ME` placeholders (greppable), real values in a gitignored `appsettings.Local.json`, and the app refuses to start outside Development with a placeholder or short JWT key. The weakness is that a developer who never overrides the key runs with a publicly known one locally. In a real deployment I would use a secret store (Key Vault or similar) injected as environment configuration; the code reads configuration the same way either way.

---

## Quality and delivery

**26. How do you know your tests are meaningful and not just numerous?**
Mutation by hand: I broke behaviour deliberately (allow Number / Duration, key without version, signal after the check, drop the formula snapshot, etc.), confirmed a test failed, then restored the code. Separately, I ran each phase against real Postgres and Redis because the InMemory provider evaluates queries client-side, which hid an untranslatable `OrderBy` that failed on Postgres. If I had more time I would add Testcontainers so that class of bug is caught in CI.

**27. What is deliberately not tested, and is that defensible?**
There is no automated frontend suite; the UI is covered by type-check, lint and the manual guide in `docs/LOCAL_TESTING.md`. For a time-boxed POC that is a defensible trade-off, because the risk is concentrated in the backend rules. For a product, I would add a small Playwright suite starting with the two-window live update, since that is the behaviour unit tests cannot show.

**28. Describe your delivery workflow and why.**
One branch per phase from an up-to-date `main`, a PR per phase with a summary and manual verification steps, docs (architecture, decision log, testing guide) updated in the same PR as the code, and the owner merges. The benefit is reviewable slices and a decision trail; the cost is overhead that a one-person POC would normally skip. It also made it easy to explain the project phase by phase.

---

## Evolution and judgement

**29. If this had to serve 10,000 automations and 1,000 concurrent viewers, what breaks first?**
In order: (a) long-poll connections on a single server, so I would introduce Redis pub/sub for notification and likely SSE; (b) the catalog, which has no pagination and does substring search, so I would add paging and trigram or full-text indexes; (c) the worker's per-automation warm loop, which would need batching and partitioning across instances with a lock or lease; (d) the log table growth, which needs an index on (automation, time) and eventually partitioning or an archived rollup for old data. I would measure before changing any of them.

**30. What is the single decision you would revisit, and what did you learn?**
The `DataVersion` increment. The design around it (version-keyed cache, long polling) is sound, but it depends on that counter being strictly monotonic and I implemented it as read-modify-write. I found the gap by re-reading my own code while preparing, and the lesson is that a mechanism everything else relies on deserves its own concurrency test against the real database, written when the mechanism is built, not afterwards.
