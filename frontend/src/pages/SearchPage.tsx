import { useEffect, useRef, useState } from "react";
import { api, type SeriesInfo } from "../api";
import SonarrAction from "../components/SonarrAction";
import { currentDataVersion, searchMemory, useScrollMemory } from "../pageMemory";
import { seriesPath } from "../routes";

const MIN_QUERY = 2;

/** "3 filters" / "1 filter (1 disabled)" / "no filters". */
function FilterCount({ s }: { s: SeriesInfo }) {
  if (s.filterCount === 0) return <span className="badge">no filters</span>;
  return (
    <span className="badge Include">
      {s.filterCount} {s.filterCount === 1 ? "filter" : "filters"}
      {s.disabledFilterCount > 0 && ` (${s.disabledFilterCount} disabled)`}
    </span>
  );
}

/** Placeholder cards with the same shape as a result, shown while the list is loading. */
function SkeletonCards({ count }: { count: number }) {
  return (
    <div className="results" role="status" aria-busy="true">
      <span className="sr-only">Loading…</span>
      {Array.from({ length: count }, (_, i) => (
        <div key={i} className="result card" aria-hidden="true">
          <div className="result-main">
            <div className="poster skeleton" />
            <div className="info skeleton-info">
              <div className="skeleton skeleton-line" style={{ width: "45%" }} />
              <div className="skeleton skeleton-line" style={{ width: "70%" }} />
              <div className="row">
                <div className="skeleton skeleton-pill" />
                <div className="skeleton skeleton-pill" />
              </div>
            </div>
          </div>
          <div className="result-actions">
            <div className="skeleton skeleton-button" />
          </div>
        </div>
      ))}
    </div>
  );
}

export default function SearchPage() {
  // Coming back (browser back, "Back to search", the tab) restores the query, results and scroll position as they were.
  // If nothing changed since they loaded there is no request at all; if something did, the old results are shown right away
  // and refreshed quietly behind them.
  const remembered = searchMemory.get();
  const restore = useRef<"fresh" | "stale" | null>(
    remembered ? (remembered.version === currentDataVersion() ? "fresh" : "stale") : null,
  );
  const loadedVersion = useRef(remembered?.version ?? currentDataVersion());
  const userEdited = useRef(false);
  useScrollMemory("search");

  const [query, setQuery] = useState(remembered?.query ?? "");
  const [results, setResults] = useState<SeriesInfo[] | null>(remembered?.results ?? null);
  const [withFilters, setWithFilters] = useState<SeriesInfo[] | null>(remembered?.withFilters ?? null);
  // Busy (spinner + skeletons) only when there is a visible load. A restored page starts idle; the first thing a fresh page
  // does is load the series that have filters, so it starts busy to avoid flashing an empty page.
  const [busy, setBusy] = useState(restore.current === null);
  const [error, setError] = useState("");
  const latest = useRef(0);

  const q = query.trim();
  const searching = q.length >= MIN_QUERY;

  // The latest counter discards responses that arrive after a newer request (typing on, or clearing the box) was started.
  useEffect(() => {
    // A restore only applies until the user changes the query. (Not cleared after the first run, so React's dev-mode double
    // effect run doesn't turn it into a refetch.)
    const mode = userEdited.current ? null : restore.current;
    if (mode === "fresh") return;

    const id = ++latest.current;
    const startVersion = currentDataVersion();
    const quiet = mode === "stale"; // refresh behind the remembered list: no spinner, no skeletons, no clearing
    setError("");

    if (!searching) {
      // No search: show every series that already has filters.
      if (!quiet) {
        setResults(null);
        setBusy(true);
      }
      api
        .seriesWithFilters()
        .then((list) => {
          if (id !== latest.current) return;
          loadedVersion.current = startVersion;
          setWithFilters(list);
        })
        .catch((e) => id === latest.current && setError((e as Error).message))
        .finally(() => id === latest.current && setBusy(false));
      return;
    }

    // Busy from the first keystroke (not only once the debounced request starts) so stale results never linger.
    if (!quiet) setBusy(true);
    const timer = setTimeout(async () => {
      try {
        const found = await api.searchSeries(q);
        if (id !== latest.current) return;
        loadedVersion.current = startVersion;
        setResults(found);
      } catch (e) {
        if (id === latest.current) setError((e as Error).message);
      } finally {
        if (id === latest.current) setBusy(false);
      }
    }, quiet ? 0 : 350);
    return () => clearTimeout(timer);
  }, [q, searching]);

  // Remember what is on screen for the next visit.
  useEffect(() => {
    searchMemory.set({ query, results, withFilters, version: loadedVersion.current });
  }, [query, results, withFilters]);

  const shown = searching ? results : withFilters;
  // Results of an earlier query would be misleading, so a search always shows skeletons while it loads. The filtered-series
  // list is only replaced by skeletons when there is nothing to show yet; otherwise it stays put while it refreshes.
  const showSkeletons = busy && (searching || shown === null);

  // After a series is added to Sonarr, refresh its card wherever it appears.
  const replaceSeries = (updated: SeriesInfo) => {
    const swap = (list: SeriesInfo[] | null) =>
      list && list.map((s) => (s.imdbId === updated.imdbId ? updated : s));
    setResults(swap);
    setWithFilters(swap);
    loadedVersion.current = currentDataVersion(); // the lists were updated in place, so they are current again
  };

  return (
    <>
      <div className="row">
        <div className="search-box">
          <input
            autoFocus
            placeholder="Search IMDb for a TV series…"
            value={query}
            onChange={(e) => {
              userEdited.current = true;
              setQuery(e.target.value);
            }}
          />
          {/* At the end of the input: a spinner while loading or searching, a search icon otherwise. */}
          {busy ? (
            <span className="search-spinner" role="status" aria-label={searching ? "Searching" : "Loading"} />
          ) : (
            <svg className="search-icon" viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
              <circle cx="10.5" cy="10.5" r="6.5" fill="none" stroke="currentColor" strokeWidth="2" />
              <line x1="15.5" y1="15.5" x2="21" y2="21" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
            </svg>
          )}
        </div>
      </div>
      {error && <p className="error">{error}</p>}

      {!searching && (
        <h3>
          Series with filters
          {withFilters ? ` (${withFilters.length})` : ""}
        </h3>
      )}
      {!searching && withFilters?.length === 0 && !busy && (
        <p className="empty">
          No series have filters yet. Search for a series above, open it, and
          add a filter.
        </p>
      )}
      {searching && results?.length === 0 && !busy && (
        <p className="empty">No TV series found for “{q}”.</p>
      )}

      {showSkeletons && <SkeletonCards count={searching ? 4 : 3} />}

      <div className="results">
        {!showSkeletons && shown?.map((s) => (
          <div key={s.imdbId} className="result card">
            <a className="result-main" href={seriesPath(s.imdbId)}>
              {s.imageUrl ? (
                <img className="poster" src={s.imageUrl} alt="" loading="lazy" />
              ) : (
                <div className="poster placeholder" />
              )}
              <div className="info">
                <div>
                  <strong>{s.title}</strong>{" "}
                  <span className="hint">{s.yearRange ?? s.year}</span>
                </div>
                {s.cast && <div className="hint">{s.cast}</div>}
                <div className="row">
                  <FilterCount s={s} />
                  <span className={`badge ${s.inSonarr ? "Include" : ""}`}>
                    {s.inSonarr ? "in Sonarr" : "not in Sonarr"}
                  </span>
                </div>
              </div>
            </a>
            <div className="result-actions">
              <SonarrAction info={s} onAdded={replaceSeries} />
            </div>
          </div>
        ))}
      </div>
    </>
  );
}
