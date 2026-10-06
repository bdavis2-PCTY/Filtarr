import { useEffect, useRef, useState } from "react";
import { api, imdbEpisodeUrl, skipReason, type Preview, type Run, type Series } from "../api";
import { currentDataVersion, listMemory, useScrollMemory } from "../pageMemory";
import { seriesPath } from "../routes";
import Icon, { BusyIcon } from "../components/Icon";

const fmtDate = (d: string | null) =>
  d ? new Date(d).toLocaleDateString() : "—";

export default function ListPage() {
  const [series, setSeries] = useState<Series[]>([]);
  // Coming back (e.g. from a series you opened out of the preview) restores the selection, preview and scroll position.
  // A preview is a snapshot, so it is only restored if nothing has changed since it was made.
  const remembered = listMemory.get();
  const stillCurrent = remembered?.version === currentDataVersion();
  const loadedVersion = useRef(remembered?.version ?? currentDataVersion());
  useScrollMemory("list");
  const [seriesId, setSeriesId] = useState<number | undefined>(remembered?.seriesId);
  const [preview, setPreview] = useState<Preview | null>(stillCurrent ? (remembered?.preview ?? null) : null);
  const [run, setRun] = useState<Run | null>(stillCurrent ? (remembered?.run ?? null) : null);
  const [busy, setBusy] = useState("");
  const [error, setError] = useState("");

  useEffect(() => {
    api
      .series()
      .then(setSeries)
      .catch(() => {});
  }, []);

  useEffect(() => {
    listMemory.set({ seriesId, preview, run, version: loadedVersion.current });
  }, [seriesId, preview, run]);

  const doPreview = async () => {
    setBusy("preview");
    setError("");
    setRun(null);
    try {
      const result = await api.preview(seriesId);
      loadedVersion.current = currentDataVersion();
      setPreview(result);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy("");
    }
  };
  const doSync = async () => {
    if (
      !confirm(
        "Monitor all matching episodes in Sonarr now? (Applies to all series.)",
      )
    )
      return;
    setBusy("sync");
    setError("");
    try {
      const result = await api.sync();
      loadedVersion.current = currentDataVersion();
      setRun(result);
      setPreview(null);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy("");
    }
  };

  const pending =
    preview?.matches.filter((m) => !skipReason(m)).length ?? 0;
  const previously =
    preview?.matches.filter((m) => !m.monitored && m.previouslyMonitoredAt).length ?? 0;

  return (
    <>
      <div className="row">
        <select
          value={seriesId ?? ""}
          onChange={(e) =>
            setSeriesId(e.target.value ? Number(e.target.value) : undefined)
          }
        >
          <option value="">All series</option>
          {series.map((s) => (
            <option key={s.id} value={s.id}>
              {s.title}
            </option>
          ))}
        </select>
        <button disabled={!!busy} onClick={doPreview}>
          <BusyIcon busy={busy === "preview"} name="eye" />
          {busy === "preview" ? "Evaluating…" : "Preview list"}
        </button>
        <span className="spacer" />
        <button className="primary" disabled={!!busy} onClick={doSync}>
          <BusyIcon busy={busy === "sync"} name="sync" />
          {busy === "sync" ? "Syncing…" : "Sync to Sonarr now"}
        </button>
      </div>
      {error && <p className="error">{error}</p>}
      {run && (
        <div className={`card ${run.error ? "err" : ""}`}>
          {run.error ? (
            <>Sync failed: {run.error}</>
          ) : (
            <>
              Sync complete: {run.episodesMonitored} episode(s) newly monitored
              ({run.episodesMatched} matched across {run.seriesScanned} series
              {run.episodesSkipped > 0 &&
                `; ${run.episodesSkipped} skipped because Filtarr monitored them before`}
              ).
            </>
          )}
        </div>
      )}
      {preview && (
        <>
          <p className="hint">
            {preview.matches.length} episode(s) match across{" "}
            {preview.seriesScanned} series; {pending} would be newly monitored
            {previously > 0 &&
              ` (${previously} more skipped because Filtarr monitored them before)`}
            . This is a preview, so nothing was changed.
          </p>
          <table>
            <thead>
              <tr>
                <th>Series</th>
                <th>Episode</th>
                <th>Title</th>
                <th>Aired</th>
                <th>Matched by</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              {preview.matches.map((m) => (
                <tr key={m.episodeId} className={skipReason(m) ? "dim" : ""}>
                  <td>
                    {m.seriesImdbId ? (
                      <a href={seriesPath(m.seriesImdbId)} title="Open in Filtarr">
                        {m.series}
                      </a>
                    ) : (
                      m.series
                    )}
                  </td>
                  <td>
                    S{String(m.season).padStart(2, "0")}E
                    {String(m.number).padStart(2, "0")}
                  </td>
                  <td>
                    {imdbEpisodeUrl(m) ? (
                      <a href={imdbEpisodeUrl(m)!} target="_blank" rel="noreferrer" title="Open on IMDb">
                        {m.title || "(untitled)"}
                      </a>
                    ) : (
                      m.title
                    )}
                  </td>
                  <td>{fmtDate(m.airDate)}</td>
                  <td>{m.filter}</td>
                  <td>
                    {skipReason(m) ??
                      (m.hasFile
                        ? "will monitor (has file)"
                        : "will monitor + search")}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </>
  );
}
