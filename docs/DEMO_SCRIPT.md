# 5-minute demo script

A timed run for screen-sharing. It follows the PRD's end-to-end flow and is ordered so the most impressive and the riskiest parts land when you are confident. Read the pre-flight list the day before and again 15 minutes before.

## Pre-flight (15 minutes before)

Do these in order. All commands are in `docs/LOCAL_TESTING.md`.

1. Docker Desktop is running. `docker compose up -d`, then `docker compose ps`: both containers healthy.
2. **Reset for a clean, predictable demo** (the seeded reports are dated from the first start, so after two weeks the "No recent report" badge would appear on its own):
   ```powershell
   # stop the API first (Ctrl+C)
   docker compose down -v
   Remove-Item -Recurse -Force src\Metrics.Api\uploads -ErrorAction SilentlyContinue
   docker compose up -d
   dotnet run --project src/Metrics.Api
   ```
3. In another terminal: `cd web; npm run dev`.
4. **Window 1** (main browser window, shared on screen): sign in as `tech.alice@demo.local`.
5. **Window 2** (a private window, *not* shared yet, placed on a second monitor or ready to switch to): sign in as `biz.carol@demo.local`, open "Trade Reconciliation Bot", ROI tab. Confirm it says **Live**.
6. Have `%TEMP%\spec.pdf` ready to upload:
   ```powershell
   "%PDF-1.4`nhello`n%%EOF" | Set-Content "$env:TEMP\spec.pdf" -Encoding ascii
   ```
7. Open this file's cheat sheet (the last section) on a second screen or on paper.
8. Check: the catalog shows 6 cards and **no** amber badges; the ROI tab shows a chart with about 12 points; browser zoom is 110-125% so the audience can read.
9. Close anything that pops notifications. Turn off a screensaver.

## The script

Speak the *italic* lines in your own words; they are cues, not a script to read.

### 0:00 - 0:30 The problem and the catalog
- Show the catalog (Alice).
- *"Teams build automations for banking back-office tasks. The old tool just dumped raw metric logs. This gives each automation a home and an ROI view, with the metrics defined per project."*
- Type `risk` in search, then clear. Pick a department. Mention the filters are in the URL.
- *"Plain card catalog by design: no cross-automation totals, that was a PRD non-goal."*

### 0:30 - 1:15 Detail view and documents
- Open **Trade Reconciliation Bot**. Point at the client and the business requirement.
- Upload `spec.pdf` (drag it in). Click **Download**.
- *"Files go on disk under a random name; the original name is only in the database. Type is checked against the file's first bytes, size is capped at 10 MB."*
- (Optional, 10 seconds) drag in a text file renamed `.pdf` and show the content-mismatch error.

### 1:15 - 2:00 ROI tab tour
- Click **ROI**. Walk top to bottom: current figures, trend, history.
- Switch the trend dropdown from "Time saved per run" to "Cost saved per run": the axis changes from hours to dollars.
- *"Each value is typed: durations, currency, numbers, percentages. The type drives validation and how it is displayed."*
- Hover a blue value in the history to show the formula tooltip.

### 2:00 - 3:00 Define a metric and show the formula engine
- Scroll to **Add computed metric**. Label: `Speedup`. Click the chips to build:
  `[Manual time per run] / [Automated time per run]`
- It says **Valid. Result type: Number.** Click **Add computed metric**.
- *"This is a hand-written parser and type checker, no eval. It validates when you save."*
- Now show a rejection: replace the formula (use a throwaway label) with
  `[Records processed] / [Automated time per run]`
  - *"Number divided by duration: rejected, with the position."* The button stays disabled.
- Then `[Manual cost per run] * [Automated cost per run]`: *"Currency times currency, rejected."* Then `[Records processed] / 0`: *"Division by a literal zero, caught before it is ever saved."*
- Note: *"Speedup shows 'No value yet': new metrics are not back-filled into old reports. We never recompute history."*

### 3:00 - 3:45 Report values
- In **Report values** enter: Records `7000`, Manual time `2` h, Automated time `15` m, Manual cost `180`, Automated cost `30`. Click **Report values**.
- Figures, chart and history update with no reload. Point out: Time saved **1h 45m**, Cost saved **$150.00**, Speedup **8**.
- *"The server computed the computed metrics and stored them with the report, along with the formula text that produced them."*

### 3:45 - 4:30 Live update (the showstopper)
- Bring **Window 2** (Carol) into the shared view next to Alice's, or switch to it.
- Carol's window already shows the report you just made *without being refreshed*. Say so.
- Report again from Alice's window. The form clears after each report, so re-enter all five values and change one (for example Records `7200`). Watch Carol's figures, chart and history change within about a second.
- *"No SignalR, per the requirements: long polling. The browser keeps a request open; the server answers the moment the data changes. Carol is a Business user: read-only, no report form."*

### 4:30 - 5:00 History is never rewritten, and wrap-up
- In Alice's window, delete **Speedup** (confirm).
- Scroll the history: the report that had Speedup still shows **Speedup: 8 (removed)**.
- *"Formulas are immutable. To change one you delete and recreate; history keeps what was true when it was reported."*
- Close: *"Behind this: Redis caches the ROI read, keyed by a data version so it can never serve stale data; a background worker warms the cache and flags automations whose reporting is overdue; 427 automated tests."*

## If you have 2 more minutes (pick one)
- **Cache, visibly:** in a terminal, `docker compose exec redis redis-cli --scan --pattern "metrics:*"` shows the cached keys including a version number; then report once more and show the new version key appear.
- **Resilience:** `docker compose stop redis`, report a value, show everything still works, then `docker compose start redis`.
- **Stale flag:** only if you pre-arranged it (see `docs/LOCAL_TESTING.md`, section J); it needs a restart, so do not attempt it live.

## If something goes wrong

| Problem | What to do |
|---|---|
| Carol's window does not update | Say: "Long polling reconnects when the tab is in the background; let me refresh to show it catches up." Refresh Window 2. If the dot says *Reconnecting*, check the API terminal. |
| A page shows an error | Refresh once. If the API is down, check its terminal and restart it (`dotnet run --project src/Metrics.Api`); the data is in Docker, so nothing is lost. |
| Upload is rejected unexpectedly | Re-create `spec.pdf` with the command above (it must start with `%PDF-`). |
| You mistype a value | Just report again; that is a normal new report. |
| You delete the wrong metric | Recreate it with the same label and formula; old history is untouched. |
| Total failure | Fall back to talking through `docs/INTERVIEW_NOTES.md` and show the tests: `dotnet test` takes about 10 seconds and prints the pass count. |

## Cheat sheet

| Item | Value |
|---|---|
| Alice (Technical) | `tech.alice@demo.local` / `CHANGE_ME_Password123!` |
| Carol (Business) | `biz.carol@demo.local` / `CHANGE_ME_Password123!` |
| Web / API / Swagger | http://localhost:5173 / http://localhost:5223 / http://localhost:5223/swagger |
| Valid formula | `[Manual time per run] / [Automated time per run]` (label `Speedup`, Number) |
| Rejected formulas | `[Records processed] / [Automated time per run]`, `[Manual cost per run] * [Automated cost per run]`, `[Records processed] / 0`, `[Nope] + 1` |
| Report values | Records `7000`; Manual time `2h 0m 0s`; Automated time `0h 15m 0s`; Manual cost `180`; Automated cost `30` |
| Expected results | Time saved `1h 45m`; Cost saved `$150.00`; Speedup `8` |
| Test file | `%TEMP%\spec.pdf` |

## Timing tips
- If you are running long, skip the document rejection and the extra rejected formulas. Never skip the live update or the history-is-never-rewritten step: they are the two things most worth seeing.
- Practise the run twice with a stopwatch. The first run usually takes 7 minutes; the second takes 5.
- Do not narrate every click. Say what the system is doing and why it was designed that way.
