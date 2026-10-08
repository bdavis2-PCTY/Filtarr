# Filtarr

Rule-based episode monitoring for Sonarr. Define filters (globally or per series); episodes that match are
automatically set to monitored in Sonarr (and optionally searched for).

- **Backend:** ASP.NET Core (.NET 10) minimal API + EF Core SQLite (`src/Filtarr.Api`, DB file `filtarr.db`)
- **Frontend:** React + TypeScript + Vite (`frontend`)

## Structure

```
Filtarr.slnx
src/Filtarr.Api/
  Controllers/   MVC API controllers (Settings, Metadata, Filters, Sync) - thin, delegate to services
  Services/      Interfaces.cs (ISonarrClient, IFilterEngine, ISettingsService, IFilterService, ISyncRunStore, ISyncService)
                 + implementations; registered in Program.cs
  Data/          EF Core DbContext (SQLite)
  Models/        Entities and DTOs
frontend/        React UI (builds into src/Filtarr.Api/wwwroot)
src/Filtarr.Api.Tests/   xUnit tests; services are tested against hand-written fakes of their interfaces
```

Run the tests with `dotnet test Filtarr.slnx`.

## Build a release

```powershell
.\scripts\Build-Release.ps1
```

Runs the tests, builds the UI, and publishes the API (which serves the UI) into `output\` (cleared first; git-ignored), then verifies it and
writes `release-info.json` (build time, git commit). Run the result with `dotnet Filtarr.Api.dll` (or `Filtarr.Api.exe`) from that folder; it
needs the ASP.NET Core 10 runtime unless built with `-SelfContained`.

Options: `-OutputDir <path>`, `-Runtime <rid>` (e.g. `win-x64`), `-SelfContained`, `-SkipTests`, `-SkipInstall`, `-CleanInstall` (force `npm ci`),
`-SmokeTest` (starts the release on a free port with a throw-away data folder and checks the API and UI answer). `appsettings.*.json` files
other than `appsettings.json` are never included, because they can hold real keys. If scripts are blocked:
`powershell -ExecutionPolicy Bypass -File .\scripts\Build-Release.ps1`.

## Windows installer

```powershell
.\scripts\Build-Installer.ps1 -Version 1.0.0      # needs Inno Setup 6: winget install JRSoftware.InnoSetup
```

Produces `installer\output\Filtarr-Setup-<version>.exe` (self-contained, so the target machine needs no .NET). The wizard asks for the install
folder, whether Filtarr runs as a Windows service (default) or is started manually from the Start menu, and the web UI port, and offers a
desktop shortcut, start at sign-in (manual mode) and a Windows Firewall rule (off by default: Filtarr has no login). Running the installer
again upgrades in place and keeps `appsettings.json`; uninstalling removes the service and firewall rule but keeps your data folder
(`%ProgramData%\Filtarr` for the service, `%AppData%\Filtarr` for manual). Unattended:
`Filtarr-Setup-1.0.0.exe /VERYSILENT /SUPPRESSMSGBOXES /DIR="C:\Filtarr" /PORT=5080 /SERVICE=1 /TASKS="firewall"`.
The script is `installer\Filtarr.iss`.

## Run as a Windows service

Filtarr hosts its own web server (Kestrel) on `Filtarr:Port` from `appsettings.json`, like Sonarr/Radarr; it does not need IIS.
Start `Filtarr.Api.exe` from a console, or register it once as a service that starts with the machine (elevated PowerShell):

```powershell
.\scripts\Install-Service.ps1 -InstallDir C:\Filtarr -DataDirectory C:\ProgramData\Filtarr -Port 5080
```

The script creates the `Filtarr` service (NetworkService by default), points it at the data directory, opens the firewall port and starts it.
Change the port in `appsettings.json` and restart the service. To update, stop the service, copy a new release over `InstallDir` (keep
your `appsettings.json`) and start it again.

## Run (development)

```bash
dotnet run --project src/Filtarr.Api          # API on http://localhost:5080
cd frontend && npm install && npm run dev      # UI on http://localhost:5173 (proxies /api)
```

## Run (single process)

```bash
cd frontend && npm install && npm run build    # outputs into src/Filtarr.Api/wwwroot
dotnet run --project src/Filtarr.Api           # UI + API on http://localhost:5080
```

Then open **Settings**, enter your Sonarr URL and API key, and test the connection.

## Configuration

Set in `src/Filtarr.Api/appsettings.json` (section `Filtarr`), or override with environment variables / command-line
(e.g. `Filtarr__Port=6000`). Restart the API after changing them. The Sonarr and OMDb connection details are **not** configured
here; they are edited on the Settings page and stored in the database (see below).

| Setting | Default | Description |
|---|---|---|
| `Port` | `5080` | HTTP port the API and UI listen on (all interfaces) |
| `LogDirectory` | *(empty)* | Where log files go. Empty = `Logs` inside the data directory; a relative path is resolved against the data directory. One file per day, named by date (e.g. `20261006.log`); the newest 31 are kept. Levels are set in the `Serilog` section |
| `ImdbSuggestionUrl` | `https://v3.sg.media-imdb.com/suggestion` | IMDb search endpoint used by the Search tab (no API key needed) |
| `DataDirectory` | `%APPDATA%/Filtarr` | Where Filtarr stores its files, including `filtarr.db`. Created if missing; env vars are expanded |

### Settings page

The **Settings** page edits the Sonarr URL, API key, root folder and quality profile, the OMDb API key and the log level; changes
apply immediately, no restart. They are stored **in the database only** (there are no appsettings entries for them), with these
defaults:

| Setting | Default |
|---|---|
| Sonarr URL | `http://localhost:8989` |
| Sonarr API key | *(empty)* |
| OMDb API key | *(empty)* |
| Root folder, Quality profile (for series added from Filtarr) | *(empty)* = the first one Sonarr lists |

The root folder and quality profile are dropdowns filled from your Sonarr.

**Upgrading:** if an older version's `Filtarr:SonarrUrl`, `SonarrApiKey`, `OmdbApiKey`, `SonarrRootFolderPath` or
`SonarrQualityProfile` entries are still in your appsettings or environment, the first start copies them into the database once
(never overwriting what is already saved there, and without logging the values) and then ignores them. Delete the old entries
afterwards.

- **Test before saving:** a changed Sonarr URL/key, or a new OMDb key, can only be saved after its **Test** button succeeds for
  exactly the values entered (editing a field afterwards invalidates the test). The server re-tests them on save too, so the rule
  can't be bypassed.
- **Log level:** Verbose, Info, Warning or Error. Until you save one, the level comes from the `Serilog` section (Warning by default).
  Noisy namespaces (ASP.NET Core, EF Core, HttpClient) stay at Warning regardless.
- The keys are stored in the database file (`filtarr.db`), so keep your data directory private and out of source control.

### Adding series to Sonarr

Search results and series pages show **Open in Sonarr** (using the Sonarr URL above, so it must be reachable from your browser)
for series in the library. For others, **Add to Sonarr** adds the series and opens it. The series is **monitored** but no seasons or
episodes are monitored, new seasons are not auto-monitored, and no search is started, so nothing downloads until a Filtarr sync
monitors episodes. It uses the root folder and quality profile chosen on the Settings page (or Sonarr's first ones). Newly added series can take a
few seconds to show their episodes in Sonarr.

## How filters work

- A filter = conditions that must **all** match (AND). Fields cover series (rating, year, genres, network, status,
  certification, language...) and episode (season, number, title, air date, days since aired, runtime, downloaded, finale, special).
- An episode is monitored if it matches **any** enabled *monitor* filter and **no** enabled *never monitor* filter.
- In the filter editor, **Generate name** fills in the name from the conditions (e.g. `Series rating ≥ 8 & Is not a season/series finale`,
  prefixed with `Never:` for filters that exclude episodes). It replaces whatever name is there, so use it before typing your own.
- Global filters apply to every series; per-series filters are applied in addition.
- **Search** opens on the list of every series that already has filters (in Sonarr or not), each with its filter count.
  Typing a search (2+ characters) shows all matching IMDb TV series, with or without filters, each marked with how many filters
  it has ("2 filters (1 disabled)" / "no filters"). Global filters are not counted. IMDb title lookups are cached for 6 hours.
- **Search** finds TV series on IMDb. Opening one shows its page, laid out like an IMDb title page: title and years, certification,
  runtime, status and network, the rating, genres, plot, cast, season/episode counts, awards and your Sonarr tags. It is merged from
  OMDb (full plot, full cast, IMDb rating; needs the OMDb key, cached 24 h), Sonarr (library entry, or Sonarr's lookup for series not
  in the library) and IMDb's search data, so any part can be missing; each source being down just leaves its parts out.
- **Going back keeps your place.** Coming back to Search (browser back, the "Back to search" link, or the tab) restores the query,
  the results and the scroll position instantly, with no request if nothing changed in the meantime. If something did change (a
  filter edited, a series added...) the old results still appear immediately and are refreshed quietly behind them. "Back to
  search" behaves like the browser's back button when you came from Search, so history doesn't pile up. List & Sync likewise
  keeps its selection, preview and scroll position, unless something changed since the preview was made.
- On that page you create, edit and delete the series' filters and **test** them: *Test all filters* (shown once the series has at
  least one filter; global + series), *Test* on one filter, or *Test draft* on an unsaved filter. A test shows the episodes that would
  be monitored and whether a search would be triggered, without changing Sonarr.
- **Run now** on a series filter executes just that filter for real: it monitors the episodes it matches in Sonarr and starts a
  search for those without a file, without evaluating any other include filter. Your enabled "never monitor" filters still hold
  episodes back, episodes Filtarr monitored before are skipped, and the run appears in History as `rule: <name>`. Disabled filters
  and "never monitor" filters can't be run on their own. After you save or enable a filter you are asked whether to run it now.
- **List & Sync** links each series to its Filtarr page and each episode title to IMDb (the episode's own page when its IMDb id is
  known, i.e. when ratings were fetched for the series; otherwise IMDb's episode list for that season).
- **Episode ratings:** the `Episode rating` field is the IMDb rating of each episode (0-10), fetched from OMDb one request per
  season and cached in the database for 24 hours. Ratings are only fetched for series whose filters use the field. Episodes
  without a rating (or series without an IMDb id in Sonarr) never match a rating condition. OMDb numbers seasons like IMDb, which
  can differ from Sonarr's numbering for some shows. In test results, episode titles link to the episode on IMDb.
- Per-series filters are tied to the series' **IMDb id**, so they can be set up before the show is in Sonarr; Sonarr series are
  matched to them through the IMDb id Sonarr stores. Series without an IMDb id in Sonarr only get the global filters.
- The IMDb search uses IMDb's public search-suggestion endpoint, which is not an officially documented API.
- Missing data never matches (e.g. `rating < 5` does not match an unrated show).
- Filtarr only ever turns monitoring **on**; it never un-monitors episodes.
- **Never re-monitors:** every episode Filtarr sets to monitored is recorded in the `MonitoredEpisodes` table (by Sonarr episode id,
  with when and which filter). Episodes in that table are never monitored again, even if they match and are unmonitored in
  Sonarr, so an episode you watched and deleted does not come back. Previews and tests mark such episodes as skipped, and
  History shows how many each sync skipped. When upgrading from an earlier version, the table is seeded from the episodes your
  past syncs monitored (each run kept up to 500). Episodes monitored in Sonarr by other means are not tracked.
- **Reset history:** a series page has a **Monitored history** card showing how many episodes Filtarr has monitored for that
  series, with a **Reset history…** button. After an explicit confirmation it forgets those episodes (only Filtarr's own record;
  nothing changes in Sonarr), so the next sync monitors them again if they match your filters and are unmonitored in Sonarr, and
  Sonarr searches for and re-downloads the ones without a file.
- **List & Sync** previews the resulting list without changing anything; Sync applies it. Optional auto-sync runs on an interval.
