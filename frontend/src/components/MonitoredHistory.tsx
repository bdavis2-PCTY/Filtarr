import { useEffect, useState } from "react";
import { api } from "../api";
import Icon, { BusyIcon } from "./Icon";

/**
 * Shows how many episodes of this series Filtarr has monitored, and lets the user forget them. The history is what stops Filtarr
 * from re-monitoring (and re-downloading) an episode that was watched and deleted, so resetting needs an explicit confirmation.
 */
export default function MonitoredHistory({
  imdbId,
  title,
  onReset,
}: {
  imdbId: string;
  title: string;
  /** Called after a successful reset, e.g. to refresh a test result that showed those episodes as skipped. */
  onReset: () => void;
}) {
  const [count, setCount] = useState<number | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);

  useEffect(() => {
    api
      .monitoredHistory(imdbId)
      .then((r) => setCount(r.count))
      .catch((e) => setMessage({ ok: false, text: (e as Error).message }));
  }, [imdbId]);

  const reset = async () => {
    setBusy(true);
    setMessage(null);
    try {
      const { removed } = await api.resetMonitoredHistory(imdbId);
      setCount(0);
      setConfirming(false);
      setMessage({
        ok: true,
        text: `History reset: ${removed} episode${removed === 1 ? "" : "s"} forgotten. They can be monitored again by the next sync.`,
      });
      onReset();
    } catch (e) {
      setMessage({ ok: false, text: (e as Error).message });
    } finally {
      setBusy(false);
    }
  };

  const noun = count === 1 ? "episode" : "episodes";

  return (
    <div className="card">
      <div className="row">
        <strong>Monitored history</strong>
        <span className="hint">
          {count === null
            ? <span className="skeleton skeleton-line" role="status" aria-busy="true" aria-label="Loading history" style={{ display: "inline-block", width: 260, verticalAlign: "middle" }} />
            : count === 0
              ? "Filtarr hasn’t monitored any episodes of this series."
              : `Filtarr has monitored ${count} ${noun} of this series and won’t monitor ${count === 1 ? "it" : "them"} again.`}
        </span>
        <span className="spacer" />
        <button
          className="danger"
          disabled={!count || confirming || busy}
          title="Let Filtarr monitor this series' episodes again"
          onClick={() => {
            setMessage(null);
            setConfirming(true);
          }}
        >
          <Icon name="reset" />
          Reset history…
        </button>
      </div>

      {confirming && (
        <div className="confirm" role="alertdialog" aria-label="Confirm resetting the monitored history">
          <p>
            <strong>
              Reset the monitored history of “{title}”?
            </strong>
          </p>
          <p>
            Filtarr remembers the {count} {noun} it has monitored so that an episode you watched and deleted isn’t
            monitored and downloaded again. Resetting forgets that, so Filtarr treats those episodes as new:
          </p>
          <ul>
            <li>
              The next sync (automatic or manual) will <strong>monitor them again</strong> if they match your filters and
              are currently unmonitored in Sonarr, including episodes you have already watched and deleted.
            </li>
            <li>
              Sonarr will then <strong>search for and re-download</strong> the ones that have no file (unless “search for
              newly monitored episodes” is turned off in Settings).
            </li>
            <li>Nothing changes in Sonarr right now, and episodes that are still monitored are not affected.</li>
            <li>This can’t be undone, though Filtarr starts recording again from the next sync.</li>
          </ul>
          <div className="row">
            <span className="spacer" />
            <button disabled={busy} onClick={() => setConfirming(false)}>
              <Icon name="x" />
              Cancel
            </button>
            <button className="danger" disabled={busy} onClick={reset}>
              <BusyIcon busy={busy} name="reset" />
              {busy ? "Resetting…" : "Yes, reset history"}
            </button>
          </div>
        </div>
      )}

      {message && <p className={message.ok ? "ok" : "error"}>{message.text}</p>}
    </div>
  );
}
