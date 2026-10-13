# Filtarr

Rule-based episode monitoring for Sonarr. Users define filters (globally or per series); episodes that match are set to
monitored in Sonarr (and searched for). ASP.NET Core (.NET 10) API + React/TypeScript UI, SQLite storage. See `README.md` for
the user-facing description; keep it updated when behavior changes.

## Layout

- `Filtarr.slnx` — solution. Three libraries, each with a test project: `src/Filtarr.Api` (HTTP host), `src/Filtarr.Core` (services, clients,
  filter engine, API models), `src/Filtarr.Data` (entities, EF Core + SQLite, repositories). Tests: `Filtarr.Api.Tests`, `Filtarr.Core.Tests`,
  `Filtarr.Data.Tests` (xUnit).
- `frontend/` — Vite + React + TypeScript. `npm run build` outputs into `src/Filtarr.Api/wwwroot` (git-ignored), which the API serves.
- Dependencies point one way: `Filtarr.Api` → `Filtarr.Core` → `Filtarr.Data`. The API controllers use only Core service interfaces and
  `Filtarr.Core.Models`; Core services use repository interfaces and map entities to models. `Program.cs` calls `AddFiltarrCore(...)`
  (which also registers the database) and `InitializeFiltarr()`.
- `Filtarr.Core` folders (namespace = folder), each with an `Interfaces/` subfolder for its contracts: `Services/` (business logic only),
  `Clients/` (Sonarr, OMDb, IMDb; wire types in `Clients/Models`), `Catalogs/` (series lookup), `Filtering/` (filter engine and field list),
  plus `Models/` (API-safe models), `Exceptions/`, `Mapping/` (entity ↔ model), `Synchronization/` (`SyncGate`) and `Configuration/`.

## Commands

```bash
dotnet test Filtarr.slnx                       # all backend tests
dotnet run --project src/Filtarr.Api            # API + built UI on http://localhost:5080
cd frontend && npm install && npm run dev       # UI dev server on :5173 (proxies /api to :5080)
cd frontend && npm run build                    # type-checks (tsc) and builds into wwwroot
```

```powershell
.\scripts\Build-Release.ps1 [-SmokeTest] [-SelfContained -Runtime win-x64]   # tests + UI build + publish API and UI into .\output
```

The release script empties `output/` (git-ignored) first, never ships `appsettings.*.json` other than `appsettings.json` (the csproj excludes them from publish, and
the script re-checks), and refuses to run `npm ci` while something (e.g. `npm run dev`) is using `frontend/node_modules`.
Windows PowerShell 5.1 must keep working: no `??`, ternaries or `&&`, ASCII-only source, and no `$PSScriptRoot` in `param()` defaults.

A running API locks `bin/`; to build or test while one is running use `--artifacts-path <other dir>`.

## Configuration and secrets

- `appsettings.json` `Filtarr` section holds only: `Port`, `DataDirectory`, `LogDirectory`, `OmdbUrl`, `ImdbSuggestionUrl`.
- Sonarr URL / API key / root folder / quality profile, the OMDb API key and the log level live **in the database** and are edited on the
  Settings page (`AppSettings`, `ISettingsService.GetEffectiveAsync`). Do not read them from `IConfiguration`/`FiltarrOptions`.
- The environment follows the build configuration (Debug = Development, Release = Production); `appsettings.{Environment}.json` overrides
  `appsettings.json`. `appsettings.Development.json` is git-ignored and holds real keys: never commit, print or log them.

## Backend conventions

- Controllers only translate HTTP; logic lives in `Filtarr.Core` services behind interfaces so it can be unit tested. New services need an
  interface in `Services/Interfaces`, a registration in `Filtarr.Core/ServiceCollectionExtensions.cs`, and a hand-written fake in
  `src/Filtarr.Core.Tests/Fakes.cs` (no mocking library); service tests that need a real database use `TestDb` (in-memory SQLite behind the
  real repositories). Only services belong in the `Services` namespace: external-API wrappers go in `Clients`, lookups that combine sources
  in `Catalogs`, pure rule evaluation in `Filtering`.
- **Database entities never leave `Filtarr.Core`.** Services take and return `Filtarr.Core.Models` (e.g. `FilterModel`, `SyncRunModel`,
  `SettingsModel`; same JSON shape the UI uses) and convert at the boundary with `Mapping/EntityMappings`. A new entity or column needs the
  model and mapping updated. `BoundaryTests` (Core) and `ControllerBoundaryTests` (Api) fail if a database type shows up in a public
  contract or controller action.
- **All database access lives in `Filtarr.Data`.** Core never references EF Core (`AppDb` is internal): it injects the repository
  interfaces from `Filtarr.Data.Repositories.Interfaces`, one per table (settings, filters, quick filters, sync runs, monitored episodes, season
  ratings). Entities live in `Filtarr.Data.Models`. A new table needs an entity, a repository (interface in `Repositories/Interfaces`, internal implementation in `Repositories`), its
  registration in `ServiceCollectionExtensions` and tests in `Filtarr.Data.Tests`.
- Services and repositories are scoped; `SyncGate` (singleton) serializes syncs and single-rule runs; `TimeProvider` is injected for time.
- The schema is `EnsureCreated` plus `Filtarr.Data/Migrations/SchemaUpgrader.cs` (hand-written, idempotent raw SQL), run at start-up through
  `IDatabaseInitializer`. **Any model change needs a matching upgrader step** and a test that upgrades an older database
  (in `Filtarr.Data.Tests`).
- Filter semantics: a filter's conditions are ANDed; an episode is monitored if any enabled Include filter matches and no enabled Exclude
  filter does; missing data never matches. Per-series filters are keyed by IMDb id and matched to Sonarr through Sonarr's `imdbId`.
- Filtarr only turns monitoring **on**. Every episode it monitors is recorded in `MonitoredEpisodes` and is never monitored again (until the
  history is reset). Keep that invariant in anything that monitors episodes (`SyncService.ApplyAsync` is the single place).
- Sonarr has no per-episode ratings; episode ratings come from OMDb (cached per season for 24 h) and are fetched only when a filter uses them.
- Code style: nullable enabled, primary constructors, records for DTOs; comments explain *why*, not what.

## Frontend conventions

- `src/api.ts` is the only place that calls `fetch`; add types and calls there. Hash routing lives in `routes.ts` / `App.tsx` (no router library).
- Pages remember state for back navigation through `pageMemory.ts`; any non-GET API call marks remembered pages stale.
- The UI is mobile-friendly: `mobile.css` holds the small-screen rules (nav scrolls sideways, fields stack, 40px touch targets on touch
  devices). A data table that can get wide gets `className="stack"` and a `data-label` on every `<td>` so it turns into one card per row on
  phones. Check UI changes at a phone width (375px) as well as desktop.
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
