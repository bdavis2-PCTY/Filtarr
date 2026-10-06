import { invalidatePages } from "./pageMemory";

export type FieldDef = {
  key: string;
  label: string;
  type: "number" | "string" | "date" | "bool" | "list";
  group: string;
};
export type Condition = { field: string; operator: string; value: string };
export type Filter = {
  id: number;
  name: string;
  enabled: boolean;
  action: "Include" | "Exclude";
  /** IMDb id of the series this filter is scoped to; null = global. */
  imdbId: string | null;
  seriesTitle: string | null;
  conditions: Condition[];
};
/** A reusable filter template: not tied to a series and never evaluated; used to pre-fill new series filters. */
export type QuickFilter = {
  id: number;
  name: string;
  action: "Include" | "Exclude";
  conditions: Condition[];
};
export type Draft = Omit<Filter, "id"> & { id?: number };
export type SeriesInfo = {
  imdbId: string;
  title: string;
  year: number | null;
  yearRange: string | null;
  cast: string | null;
  imageUrl: string | null;
  inSonarr: boolean;
  sonarrId: number | null;
  sonarrMonitored: boolean | null;
  /** Number of filters configured for this series (global filters not included). */
  filterCount: number;
  disabledFilterCount: number;
  /** Address of the series in Sonarr; null when it isn't in the library. */
  sonarrLink: string | null;
};
/** The rich details shown at the top of a series page, merged from IMDb, OMDb and Sonarr; any part can be missing. */
export type SeriesDetails = {
  imdbId: string;
  title: string;
  year: number | null;
  yearRange: string | null;
  posterUrl: string | null;
  backdropUrl: string | null;
  plot: string | null;
  cast: string[];
  genres: string[];
  certification: string | null;
  runtimeMinutes: number | null;
  network: string | null;
  status: string | null;
  firstAired: string | null;
  language: string | null;
  country: string | null;
  awards: string | null;
  rating: number | null;
  votes: number | null;
  /** "IMDb" (via OMDb) or "Sonarr". */
  ratingSource: string | null;
  seasons: number | null;
  episodes: number | null;
  episodesDownloaded: number | null;
  tags: string[];
  inSonarr: boolean;
  /** True when OMDb contributed (full plot, cast and IMDb rating). */
  hasFullDetails: boolean;
};
export type SeriesTestResult = {
  inSonarr: boolean;
  sonarrTitle: string | null;
  seriesMonitored: boolean;
  skippedBecauseUnmonitored: boolean;
  totalEpisodes: number;
  matches: Match[];
  warning: string | null;
};
export type Series = {
  id: number;
  title: string;
  year: number;
  monitored: boolean;
};
export type Settings = {
  sonarrUrl: string;
  dataDirectory: string;
  port: number;
  hasApiKey: boolean;
  omdbConfigured: boolean;
  /** Used when adding series to Sonarr from the UI; empty = the first one Sonarr has. */
  sonarrRootFolderPath: string;
  sonarrQualityProfile: string;
  /** Current log level, and the levels the dropdown offers. */
  logLevel: string;
  logLevels: string[];
  autoSyncEnabled: boolean;
  syncIntervalMinutes: number;
  searchOnMonitor: boolean;
  skipUnmonitoredSeries: boolean;
};
/** What running a single filter did. When `error` is set the counts show what happened before it failed. */
export type RuleRunResult = {
  ruleName: string;
  inSonarr: boolean;
  seriesTitle: string | null;
  skippedBecauseSeriesUnmonitored: boolean;
  matched: number;
  monitored: number;
  alreadyMonitored: number;
  skippedPreviouslyMonitored: number;
  newlyMonitored: Match[] | null;
  warning: string | null;
  error: string | null;
};
export type SettingsUpdate = {
  sonarrUrl?: string;
  apiKey?: string;
  omdbApiKey?: string;
  clearOmdbApiKey?: boolean;
  /** Unlike the keys, blank is a real value here: "use the first one Sonarr has". */
  sonarrRootFolderPath?: string;
  sonarrQualityProfile?: string;
  logLevel: string;
  autoSyncEnabled: boolean;
  syncIntervalMinutes: number;
  searchOnMonitor: boolean;
  skipUnmonitoredSeries: boolean;
};
export type TestResult = { ok: boolean; message: string };
export type Match = {
  episodeId: number;
  seriesId: number;
  series: string;
  season: number;
  number: number;
  title: string | null;
  airDate: string | null;
  monitored: boolean;
  hasFile: boolean;
  filter: string;
  /** IMDb id of the episode itself; only known when ratings were fetched via OMDb. */
  episodeImdbId: string | null;
  rating: number | null;
  seriesImdbId: string | null;
  /** When Filtarr last set this episode to monitored; such episodes are never monitored again. */
  previouslyMonitoredAt: string | null;
};

/** The API returns UTC timestamps without a zone suffix; without one the browser would read them as local time. */
const utcDate = (s: string) => new Date(/[zZ]|[+-]\d\d:\d\d$/.test(s) ? s : s + "Z");

/**
 * Where to read about an episode on IMDb: its own page when its IMDb id is known (it is when ratings were fetched), otherwise
 * IMDb's episode list for that season. Null when the series has no IMDb id either.
 */
export const imdbEpisodeUrl = (m: Match, seriesImdbId: string | null = m.seriesImdbId): string | null =>
  m.episodeImdbId
    ? `https://www.imdb.com/title/${m.episodeImdbId}/`
    : seriesImdbId
      ? `https://www.imdb.com/title/${seriesImdbId}/episodes/?season=${m.season}`
      : null;

/** Whether a sync would leave this match alone: already monitored in Sonarr, or monitored by Filtarr before. */
export const skipReason = (m: Match) =>
  m.monitored
    ? "already monitored"
    : m.previouslyMonitoredAt
      ? `skipped: Filtarr already monitored it on ${utcDate(m.previouslyMonitoredAt).toLocaleDateString()}`
      : null;
export type Preview = { seriesScanned: number; matches: Match[] };
export type Run = {
  id: number;
  startedAt: string;
  finishedAt: string | null;
  trigger: string;
  seriesScanned: number;
  episodesMatched: number;
  episodesMonitored: number;
  episodesSkipped: number;
  error: string | null;
  items: {
    episodeId: number;
    series: string;
    episode: string;
    filter: string;
  }[];
};

async function req<T>(method: string, url: string, body?: unknown): Promise<T> {
  const res = await fetch(url, {
    method,
    headers: body ? { "Content-Type": "application/json" } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  // Anything that may have changed data (even if it then failed part-way) makes remembered pages out of date.
  if (method !== "GET") invalidatePages();
  if (!res.ok) {
    let msg = res.statusText;
    try {
      const j = await res.json();
      msg = j.detail ?? j.error ?? msg;
    } catch {
      /* not json */
    }
    throw new Error(msg);
  }
  return res.status === 204 ? (undefined as T) : res.json();
}

export const api = {
  fields: () => req<FieldDef[]>("GET", "/api/fields"),
  series: () => req<Series[]>("GET", "/api/series"),
  /** Every series that has at least one filter. */
  seriesWithFilters: () => req<SeriesInfo[]>("GET", "/api/series/with-filters"),
  searchSeries: (q: string) =>
    req<SeriesInfo[]>("GET", `/api/series/search?q=${encodeURIComponent(q)}`),
  /** Adds the series to Sonarr (monitored, nothing under it monitored) and returns its refreshed details. */
  seriesDetails: (imdbId: string) =>
    req<SeriesDetails>("GET", `/api/series/${encodeURIComponent(imdbId)}/details`),
  /** How many of the series' episodes Filtarr has monitored (and so will never monitor again). */
  monitoredHistory: (imdbId: string) =>
    req<{ count: number }>("GET", `/api/series/${encodeURIComponent(imdbId)}/monitored-history`),
  /** Forgets that history so a later sync may monitor those episodes again. Changes nothing in Sonarr. */
  resetMonitoredHistory: (imdbId: string) =>
    req<{ removed: number }>("DELETE", `/api/series/${encodeURIComponent(imdbId)}/monitored-history`),
  addToSonarr: (imdbId: string) =>
    req<SeriesInfo>("POST", `/api/series/${encodeURIComponent(imdbId)}/add-to-sonarr`),
  seriesInfo: (imdbId: string) =>
    req<SeriesInfo>("GET", `/api/series/${encodeURIComponent(imdbId)}`),
  /** filters omitted = the saved global + series filters; otherwise exactly these filters. */
  testSeries: (imdbId: string, filters?: Draft[]) =>
    req<SeriesTestResult>(
      "POST",
      `/api/series/${encodeURIComponent(imdbId)}/test`,
      { filters: filters ?? null },
    ),
  settings: () => req<Settings>("GET", "/api/settings"),
  /** Blank sonarrUrl / apiKey / omdbApiKey mean "unchanged". The server re-tests changed connection settings and rejects failures. */
  saveSettings: (s: SettingsUpdate) => req<void>("PUT", "/api/settings", s),
  sonarrOptions: () =>
    req<{ rootFolders: string[]; qualityProfiles: string[] }>("GET", "/api/settings/sonarr-options"),
  testSonarr: (s: { sonarrUrl: string; apiKey: string }) =>
    req<TestResult>("POST", "/api/settings/test-sonarr", s),
  testOmdb: (s: { omdbApiKey: string }) =>
    req<TestResult>("POST", "/api/settings/test-omdb", s),
  filters: (q: string) => req<Filter[]>("GET", "/api/filters" + q),
  createFilter: (f: Draft) => req<Filter>("POST", "/api/filters", f),
  updateFilter: (f: Filter) => req<Filter>("PUT", `/api/filters/${f.id}`, f),
  /** Runs just this filter now for real: monitors what it matches in Sonarr. No other include filter is evaluated. */
  runFilter: (id: number) => req<RuleRunResult>("POST", `/api/filters/${id}/run`),
  deleteFilter: (id: number) => req<void>("DELETE", `/api/filters/${id}`),
  quickFilters: () => req<QuickFilter[]>("GET", "/api/quick-filters"),
  createQuickFilter: (f: Omit<QuickFilter, "id">) => req<QuickFilter>("POST", "/api/quick-filters", f),
  updateQuickFilter: (f: QuickFilter) => req<QuickFilter>("PUT", `/api/quick-filters/${f.id}`, f),
  deleteQuickFilter: (id: number) => req<void>("DELETE", `/api/quick-filters/${id}`),
  preview: (seriesId?: number) =>
    req<Preview>(
      "GET",
      "/api/preview" + (seriesId ? `?seriesId=${seriesId}` : ""),
    ),
  sync: () => req<Run>("POST", "/api/sync"),
  runs: () => req<Run[]>("GET", "/api/runs"),
};

export const OPERATORS: Record<
  FieldDef["type"],
  { value: string; label: string }[]
> = {
  number: [
    { value: "gte", label: "≥" },
    { value: "gt", label: ">" },
    { value: "lte", label: "≤" },
    { value: "lt", label: "<" },
    { value: "eq", label: "=" },
    { value: "neq", label: "≠" },
  ],
  date: [
    { value: "gte", label: "on or after" },
    { value: "gt", label: "after" },
    { value: "lte", label: "on or before" },
    { value: "lt", label: "before" },
    { value: "eq", label: "on" },
  ],
  string: [
    { value: "eq", label: "is" },
    { value: "neq", label: "is not" },
    { value: "in", label: "is one of (comma-separated)" },
    { value: "contains", label: "contains" },
    { value: "notContains", label: "does not contain" },
    { value: "startsWith", label: "starts with" },
    { value: "endsWith", label: "ends with" },
    { value: "regex", label: "matches regex" },
  ],
  list: [
    { value: "eq", label: "includes" },
    { value: "neq", label: "does not include" },
    { value: "in", label: "includes any of" },
    { value: "contains", label: "has one containing" },
    { value: "notContains", label: "has none containing" },
  ],
  bool: [{ value: "eq", label: "is" }],
};
