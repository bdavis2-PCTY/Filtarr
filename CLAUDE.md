# Filtarr

Rule-based episode monitoring for Sonarr. Users define filters (globally or per series); episodes that match are set to
monitored in Sonarr (and searched for). ASP.NET Core (.NET 10) API + React/TypeScript UI, SQLite storage. See `README.md` for
the user-facing description; keep it updated when behavior changes.

## Layout

- `Filtarr.slnx` — solution (API + tests). `src/Filtarr.Api` — backend. `src/Filtarr.Api.Tests` — xUnit tests.
- `frontend/` — Vite + React + TypeScript. `npm run build` outputs into `src/Filtarr.Api/wwwroot` (git-ignored), which the API serves.
- Backend: `Controllers/` (thin) → `Services/` (interfaces in `Interfaces.cs`, implementations beside them) → `Data/` (EF Core + SQLite) with
  `Models/` for entities and DTOs. Everything is registered in `Program.cs`.

## Commands

```bash
dotnet test Filtarr.slnx                       # all backend tests
dotnet run --project src/Filtarr.Api            # API + built UI on http://localhost:5080
cd frontend && npm install && npm run dev       # UI dev server on :5173 (proxies /api to :5080)
cd frontend && npm run build                    # type-checks (tsc) and builds into wwwroot
```

A running API locks `bin/`; to build or test while one is running use `--artifacts-path <other dir>`.

## Configuration and secrets

- `appsettings.json` `Filtarr` section holds only: `Port`, `DataDirectory`, `LogDirectory`, `OmdbUrl`, `ImdbSuggestionUrl`.
- Sonarr URL / API key / root folder / quality profile, the OMDb API key and the log level live **in the database** and are edited on the
  Settings page (`AppSettings`, `ISettingsService.GetEffectiveAsync`). Do not read them from `IConfiguration`/`FiltarrOptions`.
- The environment follows the build configuration (Debug = Development, Release = Production); `appsettings.{Environment}.json` overrides
  `appsettings.json`. `appsettings.Development.json` is git-ignored and holds real keys: never commit, print or log them.

## Backend conventions

- Controllers only translate HTTP; logic lives in services behind interfaces so it can be unit tested. New services need an interface,
  a DI registration, and a hand-written fake in `src/Filtarr.Api.Tests/Fakes.cs` (no mocking library).
- DB services are scoped; `SyncGate` (singleton) serializes syncs and single-rule runs; `TimeProvider` is injected for time.
- The schema is `EnsureCreated` plus `Data/SchemaUpgrader.cs` (hand-written, idempotent raw SQL). **Any model change needs a matching
  upgrader step** and a test that upgrades an older database.
- Filter semantics: a filter's conditions are ANDed; an episode is monitored if any enabled Include filter matches and no enabled Exclude
  filter does; missing data never matches. Per-series filters are keyed by IMDb id and matched to Sonarr through Sonarr's `imdbId`.
- Filtarr only turns monitoring **on**. Every episode it monitors is recorded in `MonitoredEpisodes` and is never monitored again (until the
  history is reset). Keep that invariant in anything that monitors episodes (`SyncService.ApplyAsync` is the single place).
- Sonarr has no per-episode ratings; episode ratings come from OMDb (cached per season for 24 h) and are fetched only when a filter uses them.
- Code style: nullable enabled, primary constructors, records for DTOs; comments explain *why*, not what.

## Frontend conventions

- `src/api.ts` is the only place that calls `fetch`; add types and calls there. Hash routing lives in `routes.ts` / `App.tsx` (no router library).
- Pages remember state for back navigation through `pageMemory.ts`; any non-GET API call marks remembered pages stale.
- Every `<button>` has an icon (`components/Icon.tsx`, inline SVG, `BusyIcon` for running actions); loading states use skeletons or a spinner,
  never "Loading…" text. Avoid adding dependencies.

## CI

`.github/workflows/ci.yml` runs on every pull request into `master`/`develop`: **API build & test** (`dotnet build` + `dotnet test`, Release) and
**UI build** (`npm ci` + `npm run build`). `master` and `develop` are protected: changes only land through pull requests.
`frontend/package-lock.json` is committed and must keep `registry.npmjs.org` URLs (GitHub's runners can't reach an internal registry); if a
lockfile regenerated through an internal mirror shows other hosts in `resolved`, regenerate it against the public registry before committing.

## Testing and verification

- Add tests for backend changes (xUnit, fakes). The frontend has no test runner: verify UI changes in a browser against a running instance.
- Never test against the user's real Sonarr/OMDb: use local mock servers and a scratch data directory (`Filtarr__DataDirectory`), and override
  the Sonarr/OMDb settings so nothing reaches real services. Anything that modifies Sonarr (add series, monitor, search) needs explicit approval
  when it would touch a real instance.
- IMDb search uses an unofficial public endpoint; keep it behind `IImdbClient` so it can be swapped.
