import { useEffect, useRef, useState } from "react";
import {
  api,
  type Draft,
  type Filter,
  imdbEpisodeUrl,
  type RuleRunResult,
  type SeriesDetails,
  skipReason,
  type SeriesInfo,
  type SeriesTestResult,
} from "../api";
import FilterManager from "../components/FilterManager";
import { useScrollMemory } from "../pageMemory";
import { backToSearch } from "../routes";
import MonitoredHistory from "../components/MonitoredHistory";
import SeriesHero from "../components/SeriesHero";
import SeriesSkeleton from "../components/SeriesSkeleton";
import Icon, { BusyIcon } from "../components/Icon";

const fmtDate = (d: string | null) => (d ? new Date(d).toLocaleDateString() : "—");
const code = (season: number, number: number) =>
  `S${String(season).padStart(2, "0")}E${String(number).padStart(2, "0")}`;

type TestState = { label: string; result?: SeriesTestResult; error?: string };

export default function SeriesPage({ imdbId }: { imdbId: string }) {
  const [info, setInfo] = useState<SeriesInfo | null>(null);
  useScrollMemory(`series:${imdbId}`); // a new series page opens at the top; revisiting one restores where you were
  const [details, setDetails] = useState<SeriesDetails | null>(null);
  const [loadingDetails, setLoadingDetails] = useState(true);
  const [loadError, setLoadError] = useState("");
  const [test, setTest] = useState<TestState | null>(null);
  const [testing, setTesting] = useState(false);
  const latest = useRef(0);

  useEffect(() => {
    api
      .seriesInfo(imdbId)
      .then(setInfo)
      .catch((e) => setLoadError((e as Error).message));
    // The extra details are best-effort: the page works without them, so a failure just leaves them out.
    api
      .seriesDetails(imdbId)
      .then(setDetails)
      .catch(() => setDetails(null))
      .finally(() => setLoadingDetails(false));
  }, [imdbId]);

  const lastTest = useRef<{ filters: Draft[] | null; label: string } | null>(null);

  // Running a single filter: the offer shown after a filter is changed, the run in progress, and what it did.
  const [offer, setOffer] = useState<Filter | null>(null);
  const [runningId, setRunningId] = useState<number | null>(null);
  const [ran, setRan] = useState<{ result?: RuleRunResult; error?: string } | null>(null);
  const [historyKey, setHistoryKey] = useState(0);

  /** After a filter is saved or switched on, offer to run it, but only if running it could do something. */
  const onSaved = (f: Filter) => {
    setRan(null);
    setOffer(info?.inSonarr && f.enabled && f.action === "Include" ? f : null);
  };

  const runRule = async (f: Filter) => {
    setOffer(null);
    setRan(null);
    setRunningId(f.id);
    try {
      setRan({ result: await api.runFilter(f.id) });
    } catch (e) {
      setRan({ error: (e as Error).message });
    } finally {
      setRunningId(null);
      setHistoryKey((k) => k + 1); // the history card's count changed
      // A test on screen predates the run; refresh it so it shows those episodes as monitored.
      if (lastTest.current) runTest(lastTest.current.filters, lastTest.current.label);
    }
  };

  const runTest = async (filters: Draft[] | null, label: string) => {
    lastTest.current = { filters, label };
    const id = ++latest.current;
    setTesting(true);
    setTest({ label });
    try {
      const result = await api.testSeries(imdbId, filters ?? undefined);
      if (id === latest.current) setTest({ label, result });
    } catch (e) {
      if (id === latest.current) setTest({ label, error: (e as Error).message });
    } finally {
      if (id === latest.current) setTesting(false);
    }
  };

  if (loadError)
    return (
      <>
        <a href="#/search" onClick={backToSearch}>← Back to search</a>
        <p className="error">{loadError}</p>
      </>
    );
  if (!info)
    return (
      <>
        <a href="#/search" onClick={backToSearch}>← Back to search</a>
        <SeriesSkeleton />
      </>
    );

  return (
    <>
      <a href="#/search" onClick={backToSearch}>← Back to search</a>
      <SeriesHero info={info} details={details} loadingDetails={loadingDetails} onAdded={setInfo} />
      {!info.inSonarr && (
        <p className="hint">
          This series isn’t in your Sonarr library yet. You can set up filters now; they take effect once Sonarr has the series, but
          they can’t be tested until then. Adding it to Sonarr monitors the series but no seasons or episodes, so nothing downloads
          until a sync monitors episodes.
        </p>
      )}

      <h3>Filters for this series</h3>
      <p className="hint">
        These apply in addition to the global filters. Testing never changes
        anything in Sonarr.
      </p>
      {offer && (
        <div className="card offer" role="alertdialog" aria-label="Run the changed filter now?">
          <p>
            <strong>Run “{offer.name}” now?</strong>
          </p>
          <p>
            You just changed this filter. Running it monitors the episodes it matches in Sonarr right away (and
            Sonarr searches for the ones without a file) instead of waiting for the next sync.
          </p>
          <ul>
            <li>Only this filter runs; your other filters are not touched.</li>
            <li>
              Episodes Filtarr has monitored before are skipped, and your enabled “never monitor” filters still apply.
            </li>
            <li>Use “Test” on the filter first if you want to preview what it would monitor.</li>
          </ul>
          <div className="row">
            <span className="spacer" />
            <button onClick={() => setOffer(null)}><Icon name="clock" />Not now</button>
            <button className="primary" onClick={() => runRule(offer)}>
              <Icon name="play" />
              Yes, run it now
            </button>
          </div>
        </div>
      )}

      {ran && <RuleRunPanel ran={ran} seriesImdbId={info.imdbId} onClose={() => setRan(null)} />}

      <FilterManager
        imdbId={info.imdbId}
        seriesTitle={info.title}
        onTest={runTest}
        onRun={runRule}
        runningId={runningId}
        onSaved={onSaved}
      />

      <MonitoredHistory
        key={historyKey}
        imdbId={info.imdbId}
        title={info.title}
        // A test on screen showed those episodes as skipped; run it again so it reflects the reset.
        onReset={() => lastTest.current && runTest(lastTest.current.filters, lastTest.current.label)}
      />

      {test && <TestPanel state={test} busy={testing} seriesImdbId={info.imdbId} />}
    </>
  );
}

/** What running one filter just did. */
function RuleRunPanel({
  ran,
  seriesImdbId,
  onClose,
}: {
  ran: { result?: RuleRunResult; error?: string };
  seriesImdbId: string;
  onClose: () => void;
}) {
  const r = ran.result;
  const failed = !!ran.error || !!r?.error;
  const plural = (n: number) => (n === 1 ? "episode" : "episodes");

  let summary: string;
  if (ran.error) summary = ran.error;
  else if (!r) summary = "";
  else if (!r.inSonarr) summary = `“${r.seriesTitle ?? "This series"}” isn’t in Sonarr, so there was nothing to run.`;
  else if (r.skippedBecauseSeriesUnmonitored)
    summary = `${r.seriesTitle} is unmonitored in Sonarr and “Skip series that are unmonitored” is on in Settings, so the filter was not run.`;
  else
    summary =
      `Ran “${r.ruleName}”: ${r.monitored} ${plural(r.monitored)} newly monitored` +
      ` (${r.matched} matched` +
      (r.alreadyMonitored > 0 ? `, ${r.alreadyMonitored} already monitored` : "") +
      (r.skippedPreviouslyMonitored > 0 ? `, ${r.skippedPreviouslyMonitored} skipped because Filtarr monitored them before` : "") +
      `).`;

  return (
    <div className={`card ${failed ? "err" : "test-panel"}`}>
      <div className="row">
        <strong>Filter run</strong>
        <span className="spacer" />
        <button onClick={onClose}><Icon name="x" />Dismiss</button>
      </div>
      <p className={failed ? "error" : ""}>{summary}</p>
      {r?.error && <p className="error">It stopped early: {r.error}. The episodes above were done before that.</p>}
      {r?.warning && <p className="error">{r.warning}</p>}
      {r && r.monitored > 0 && (
        <>
          <p className="hint">Sonarr was asked to search for the ones without a file (if that is turned on in Settings).</p>
          <table>
            <thead>
              <tr>
                <th>Episode</th>
                <th>Title</th>
                <th>Aired</th>
              </tr>
            </thead>
            <tbody>
              {(r.newlyMonitored ?? []).map((m) => (
                <tr key={m.episodeId}>
                  <td>{code(m.season, m.number)}</td>
                  <td>
                    <a href={imdbEpisodeUrl(m, seriesImdbId) ?? undefined} target="_blank" rel="noreferrer" title="Open on IMDb">
                      {m.title || "(untitled)"}
                    </a>
                  </td>
                  <td>{fmtDate(m.airDate)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </div>
  );
}

function TestPanel({
  state,
  busy,
  seriesImdbId,
}: {
  state: TestState;
  busy: boolean;
  seriesImdbId: string;
}) {
  const r = state.result;
  const showRatings = r?.matches.some((m) => m.rating !== null) ?? false;
  const toMonitor = r?.matches.filter((m) => !skipReason(m)).length ?? 0;
  const previously =
    r?.matches.filter((m) => !m.monitored && m.previouslyMonitoredAt).length ?? 0;
  return (
    <div className="card test-panel">
      <h3>Test: {state.label}</h3>
      {busy && <p className="hint">Evaluating against Sonarr…</p>}
      {state.error && <p className="error">{state.error}</p>}
      {r && !r.inSonarr && (
        <p className="hint">
          This series isn’t in Sonarr, so there are no episodes to test against.
        </p>
      )}
      {r?.inSonarr && (
        <>
          {r.skippedBecauseUnmonitored && (
            <p className="error">
              This series is unmonitored in Sonarr and “Skip series that are
              unmonitored” is on, so a sync would skip it. The results below
              show what the filters match.
            </p>
          )}
          {r.warning && <p className="error">{r.warning}</p>}
          <p>
            {r.matches.length} of {r.totalEpisodes} episodes match;{" "}
            <strong>{toMonitor}</strong> would be newly monitored (
            {r.matches.length - toMonitor - previously} already monitored
            {previously > 0 &&
              `, ${previously} skipped because Filtarr monitored them before`}
            ).
          </p>
          {r.matches.length > 0 && (
            <table>
              <thead>
                <tr>
                  <th>Episode</th>
                  <th>Title</th>
                  <th>Aired</th>
                  {showRatings && <th>Rating</th>}
                  <th>Matched by</th>
                  <th>Would…</th>
                </tr>
              </thead>
              <tbody>
                {r.matches.map((m) => (
                  <tr key={m.episodeId} className={skipReason(m) ? "dim" : ""}>
                    <td>{code(m.season, m.number)}</td>
                    <td>
                      <a
                        href={imdbEpisodeUrl(m, seriesImdbId) ?? undefined}
                        target="_blank"
                        rel="noreferrer"
                        title="Open on IMDb"
                      >
                        {m.title || "(untitled)"}
                      </a>
                    </td>
                    <td>{fmtDate(m.airDate)}</td>
                    {showRatings && (
                      <td>{m.rating !== null ? m.rating.toFixed(1) : "—"}</td>
                    )}
                    <td className="ellipsis" title={m.filter}>
                      {m.filter}
                    </td>
                    <td>
                      {skipReason(m) ??
                        (m.hasFile
                          ? "monitor (has file)"
                          : "monitor + search")}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}
    </div>
  );
}
