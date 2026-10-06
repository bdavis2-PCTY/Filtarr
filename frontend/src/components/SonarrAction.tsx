import { useState } from "react";
import { api, type SeriesInfo } from "../api";
import Icon, { BusyIcon } from "./Icon";

/**
 * "Open in Sonarr" for series in the library; for others an "Add to Sonarr" button that adds the series
 * (monitored, with nothing under it monitored) and then opens it.
 */
export default function SonarrAction({
  info,
  onAdded,
}: {
  info: SeriesInfo;
  onAdded: (updated: SeriesInfo) => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  if (info.sonarrLink)
    return (
      <a href={info.sonarrLink} target="_blank" rel="noreferrer" className="sonarr-link">
        Open in Sonarr ↗
      </a>
    );
  // In the library, but Sonarr gave no page address to link to.
  if (info.inSonarr) return null;

  const add = async () => {
    // Open the tab right now, inside the click, so the browser doesn't treat it as a popup; it is pointed at Sonarr once the add finishes.
    const tab = window.open("about:blank", "_blank");
    setBusy(true);
    setError("");
    try {
      const updated = await api.addToSonarr(info.imdbId);
      onAdded(updated);
      if (updated.sonarrLink && tab) tab.location.href = updated.sonarrLink;
      else tab?.close();
    } catch (e) {
      tab?.close();
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="sonarr-action">
      <button
        disabled={busy}
        title="Adds the series to Sonarr as monitored, with no seasons or episodes monitored, and nothing is downloaded"
        onClick={add}
      >
        <BusyIcon busy={busy} name="plus-circle" />
        {busy ? "Adding…" : "Add to Sonarr"}
      </button>
      {error && <div className="error">{error}</div>}
    </div>
  );
}
