# Testing locally, step by step

Everything below was written for Windows (PowerShell) and checked against this repository. Run the commands from the repository root unless a step says otherwise.

## 0. What you need

| Tool | Check with | Notes |
|---|---|---|
| Docker Desktop | `docker version` | Must be **running** (the whale icon is steady). |
| .NET 8 SDK | `dotnet --list-sdks` | Needs 8.0.404 or a newer 8.0.x (`global.json` pins this); other SDKs can be installed alongside. |
| Node.js 20+ | `node -v` | Comes with npm. |
| Git | `git --version` | |

Ports this project uses, all of which must be free:

| Port | Used by |
|---|---|
| 5223 | API (and Swagger at `/swagger`) |
| 5173 | Web app (Vite) |
| 5434 | Postgres in Docker (deliberately not 5432, so it cannot clash with a Postgres you already run) |
| 6380 | Redis in Docker (deliberately not 6379) |

Check a port: `netstat -ano | findstr :5223` (no output means it is free).

## 1. Get the latest code

```powershell
git checkout main
git pull
```

## 2. Start Postgres and Redis

```powershell
docker compose up -d
docker compose ps
```

Expected: two containers, `postgres` and `redis`, both `Up ... (healthy)`. If a container is not healthy yet, wait ten seconds and run `docker compose ps` again.

## 3. Start the API (terminal 1)

```powershell
dotnet run --project src/Metrics.Api
```

Expected in the output, after a few seconds (the first start also applies the database migrations and seeds the demo data):

```
Now listening on: http://localhost:5223
Application started. Press Ctrl+C to shut down.
Hosting environment: Development
```

You will also see a warning that `Jwt:Key` is a `CHANGE_ME` placeholder. That is expected in Development. A browser tab may open on Swagger; you can close it or use it later.

Quick check in a second PowerShell window:

```powershell
Invoke-RestMethod http://localhost:5223/api/health
```

Expected: `status` is `Healthy`, with `postgres` and `redis` both healthy.

## 4. Start the web app (terminal 2)

```powershell
cd web
npm install      # first time only
npm run dev
```

Open **http://localhost:5173**. You should be redirected to the sign-in page.

## 5. Automated checks

```powershell
dotnet test                       # from the repository root: expect all tests passing (427 at the time of writing)
cd web
npm run lint                      # expect no output after the "eslint ." line
npm run build                     # expect "built in ...s"
```

## 6. Demo accounts

Seeded when the API starts in Development. The password for all of them is the value of `Seed:DevPassword` in `src/Metrics.Api/appsettings.json`, which is `CHANGE_ME_Password123!` unless you changed it.

| Email | Team | Can |
|---|---|---|
| `tech.alice@demo.local` | Technical | everything |
| `tech.bob@demo.local` | Technical | everything |
| `biz.carol@demo.local` | Business | view and download only |
| `biz.dave@demo.local` | Business | view and download only |

## 7. Manual walkthrough

Work through these in order. Each step says what you should see.

### A. Sign-in and roles
1. Sign in as `tech.alice@demo.local`. The header shows "Alice (Technical)" with a blue Technical badge.
2. Click **Log out**, then try a wrong password: you see "Invalid email or password." (the same message whether the email exists or not).
3. Click **Register**, create your own account with a short password (for example `abc`): the password field shows an error. Use 8 or more characters and pick a team; you are signed in straight away.

### B. Catalog and filters
1. Sign in as Alice. The catalog shows **6 cards**, newest activity first.
2. **Department** dropdown: pick one, the list narrows. Pick "All" to reset.
3. **Search**: type `risk`. One card remains (Risk Limit Breach Notifier). The address bar now contains `?q=risk`; refresh the page and the filter is still applied.
4. **Active from / Active to**: set "from" to about 15 days ago and leave "to" empty. Three cards remain (activity 2, 5 and 11 days ago).
5. Set "to" earlier than "from": a red message appears and no request is made.
6. Click **Clear filters**.

### C. Detail view and documents
1. Click **Details** on "Trade Reconciliation Bot". You see the client, the business requirement and a "Related documents" section.
2. Create a small valid PDF to upload (PowerShell):
   ```powershell
   "%PDF-1.4`nhello`n%%EOF" | Set-Content "$env:TEMP\spec.pdf" -Encoding ascii
   ```
   Drag it into the upload box (or use "choose files") and upload. It appears in the list with its size, your name and the date. Click **Download** and open the file.
3. Try things that must be rejected:
   - A text file renamed to `.exe` or `.txt`: "Unsupported type".
   - A fake PDF: `"this is not a pdf" | Set-Content "$env:TEMP\fake.pdf"`. Upload it: "The file content does not match its extension."
   - A file over 10 MB (no admin rights needed):
     ```powershell
     $f = [IO.File]::Create("$env:TEMP\big.pdf"); $f.SetLength(11000000); $f.Close()
     ```
     Upload it: "Exceeds the 10 MB limit".
4. Click **Delete** on your PDF and confirm. It disappears.
5. Open `src\Metrics.Api\uploads` in File Explorer while you upload: the stored file has a random name (a GUID), never the original name.
6. The catalog card's "Active ..." time for this automation updates (it counts document changes).

### D. ROI tab: what you see
Open the **ROI** tab of "Trade Reconciliation Bot".
- **Current figures**: cards for the 7 metrics (time saved and cost saved first). Values are formatted by type: durations like `1h 45m`, currency like `$153.00`, plain numbers with separators. "As of ..." shows the latest report and who made it.
- **Trend**: a line chart. Use the **Metric** dropdown to switch metrics; the axis and tooltip follow the type (durations in h/m, currency in $).
- **History**: the newest reports first, 10 at a time; **Load more** shows older ones. Hover a blue (computed) value to see the formula that produced it.
- The green dot at the top right says **Live**.

### E. Report values
1. In **Report values**, enter: Records processed `7000`, Manual time `2` h `0` m `0` s, Automated time `0` h `15` m `0` s, Manual cost `180`, Automated cost `30`.
2. Click **Report values**. The form clears, and figures, chart and history update without a page reload.
3. Check the computed values: Time saved per run `1h 45m`, Cost saved per run `$150.00`.
4. Submit with a field empty: that field shows "Enter a number." Nothing is sent.

### F. Define your own metrics and formulas
Still on the ROI tab, scroll to **Metrics**.
1. **Add input metric**: label `Runs`, type `Number`. Add it. The report form now has a Runs field.
2. **Add computed metric**: label `Records per run`, formula `[Records processed] / [Runs]`. While typing, the panel says "Valid. Result type: Number". Click an input chip to insert a reference. Add it.
3. Try these formulas; each must be rejected with a message and a position, and the Add button stays disabled:

   | Formula | Expected message |
   |---|---|
   | `[Records processed] / [Automated time per run]` | Cannot divide Number by Duration. |
   | `[Manual cost per run] * [Automated cost per run]` | Cannot multiply Currency (USD) by Currency (USD). |
   | `[Manual cost per run] + [Manual time per run]` | Cannot add Currency (USD) and Duration. |
   | `[Records processed] / 0` | Division by zero. |
   | `[Nope] + 1` | Unknown metric [Nope]. |
   | `[Records processed] +` | The formula ends unexpectedly... |

4. Until the next report, "Records per run" and "Runs" show **No value yet**: a new metric is never back-filled into earlier reports.
5. Report with `Runs` = `0`. The report succeeds and "Records per run" shows **n/a** (division by zero in the data is not an error).
6. Report again with `Runs` = `5`. "Records per run" now shows a value (records divided by 5).

### G. Formulas are immutable and history is never rewritten
1. Note the "Time saved per run" value in the most recent history entry.
2. Delete the computed metric "Time saved per run" (confirm). It leaves the current figures and the chart's metric list.
3. Scroll the history: older entries still show their "Time saved per run" values, marked **(removed)**.
4. Add a new computed metric with the **same label** and a different formula, for example `[Manual time per run] + [Automated time per run]`. Report once more.
5. The new entry uses the new formula; hover shows it. All older entries are unchanged.
6. There is no edit button anywhere: to change a formula you delete and recreate it.
7. Try deleting "Manual time per run" while a formula uses it: you get a message naming the formula. Delete the formula first.

### H. Business users are read-only
1. Open a private/incognito window and sign in as `biz.carol@demo.local`.
2. Catalog and detail work; documents can be downloaded.
3. There is no upload box, no Delete button, no "Report values" form, and no add/delete controls on the ROI tab.

### I. Live updates between two windows
1. Keep Alice's window on the ROI tab and Carol's window on the same automation's ROI tab. Both show **Live**.
2. In Alice's window, report new values.
3. In Carol's window the figures, chart and history change **by themselves within about a second**, with no refresh.
4. In Alice's window, add an input metric: Carol's metrics list updates as well.
5. Switch Carol's browser tab away for a minute and back: it catches up immediately.

### J. Stale reporting
Reporting is "stale" when the latest report is older than `Roi:StaleAfterDays` (14 by default).
1. Stop the API (Ctrl+C in terminal 1).
2. Restart it with a threshold of 0 days so any report counts as overdue:
   ```powershell
   $env:Roi__StaleAfterDays = "0"
   $env:Roi__WarmIntervalSeconds = "10"
   dotnet run --project src/Metrics.Api
   ```
3. Wait about 20 seconds. In the catalog (it refreshes every 30 s, or reload) the "Trade Reconciliation Bot" card shows a **No recent report** badge. Automations with no reports are not flagged. The ROI tab shows an amber banner.
4. Stop the API, clear the variables, and start it normally again:
   ```powershell
   Remove-Item Env:Roi__StaleAfterDays, Env:Roi__WarmIntervalSeconds
   dotnet run --project src/Metrics.Api
   ```

Note for demo day: the seeded reports are dated relative to the **first** startup. If you come back more than 14 days later the badge and banner will appear on their own. Report a new value before demoing, or use the reset in section 9.

## 8. Checking the cache and polling from PowerShell

```powershell
$login = Invoke-RestMethod -Method Post -Uri http://localhost:5223/api/auth/login -ContentType 'application/json' `
  -Body '{"email":"tech.alice@demo.local","password":"CHANGE_ME_Password123!"}'
$h  = @{ Authorization = "Bearer $($login.token)" }
$id = @(Invoke-RestMethod -Uri "http://localhost:5223/api/automations?q=Trade" -Headers $h)[0].id
```

**Cache hit or miss.** Empty the cache, then read twice:
```powershell
docker compose exec redis redis-cli flushall
(Invoke-WebRequest -Uri "http://localhost:5223/api/automations/$id/roi" -Headers $h -UseBasicParsing).Headers['X-Cache']   # MISS
(Invoke-WebRequest -Uri "http://localhost:5223/api/automations/$id/roi" -Headers $h -UseBasicParsing).Headers['X-Cache']   # HIT
```
(If a browser tab is open on the ROI tab, or the background worker just ran, the first read may already be a HIT; that is correct.)

**What is in Redis:**
```powershell
docker compose exec redis redis-cli --scan --pattern "metrics:*"
```
Expect keys like `metrics:roi:<automation id>:v<version>:p30` (the version number changes whenever a report or metric change happens) and `metrics:roi:stale`.

**A write makes the next read a MISS:** report values in the UI, then run the `Invoke-WebRequest` line again (right after, with the browser tab closed): the version in the key has moved on.

**Long polling.** This call is held open and returns `204` after 2 seconds if nothing changes:
```powershell
$v = (Invoke-RestMethod -Uri "http://localhost:5223/api/automations/$id/roi" -Headers $h).dataVersion
Measure-Command { Invoke-WebRequest -Uri "http://localhost:5223/api/automations/$id/roi/changes?sinceVersion=$v&timeoutSeconds=2" -Headers $h -UseBasicParsing } | Select-Object TotalSeconds
```
Run it again, and while it waits report values from the UI: it returns immediately with the new data.

## 9. Failure drills and resetting

**Redis outage.** The app must keep working without the cache.
```powershell
docker compose stop redis
```
Reload pages and report values: everything still works. The first request after stopping takes about 2 seconds, then requests are fast. The ROI response header `X-Cache` says `BYPASS`, and the health check reports redis as unhealthy (HTTP 503, which is why this uses `curl.exe`: `Invoke-RestMethod` would throw on the 503):
```powershell
curl.exe -s http://localhost:5223/api/health
```
```powershell
docker compose start redis
```
Caching resumes by itself within about 30 seconds.

**Reset all data** (database, cache, uploaded files), then start again:
```powershell
# stop the API first (Ctrl+C)
docker compose down -v
Remove-Item -Recurse -Force src\Metrics.Api\uploads -ErrorAction SilentlyContinue
docker compose up -d
dotnet run --project src/Metrics.Api
```
The API recreates the schema and the demo data on start.

## 10. If something goes wrong

| Symptom | Likely cause and fix |
|---|---|
| `password authentication failed for user "metrics"` | The API reached a different Postgres. Check `docker compose ps` shows Postgres on port 5434 and that `appsettings.json` uses port 5434. |
| API will not start: "Jwt:Key is still a CHANGE_ME placeholder" | You are not in the Development environment. Use `dotnet run --project src/Metrics.Api` from the repository root, or put a real key in `src/Metrics.Api/appsettings.Local.json`. |
| Web page shows "API unreachable" or login fails | The API is not running on 5223, or it is still starting. Check terminal 1. |
| `address already in use` | Another process holds 5223 / 5173 / 5434 / 6380. Find it with `netstat -ano \| findstr :<port>` and stop it. |
| Docker commands fail | Docker Desktop is not running. Start it and retry `docker compose up -d`. |
| No demo data / empty catalog | The seed only runs in Development when the data is missing. Use the reset in section 9. |
| Charts empty on a fresh automation | Expected: an automation needs at least two reports to draw a trend. |
| Login works but pages show 401 after a long time | The token lasts 8 hours. Sign in again. |
| `npm install` errors | Check `node -v` is 20 or newer; delete `web\node_modules` and retry. |
