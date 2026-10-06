import { useEffect, useState } from "react";
import { api, type Run } from "../api";

export default function HistoryPage() {
  const [runs, setRuns] = useState<Run[]>([]);
  const [open, setOpen] = useState<number | null>(null);
  useEffect(() => {
    api.runs().then(setRuns);
  }, []);
  if (runs.length === 0) return <p className="empty">No sync runs yet.</p>;
  return (
    <table>
      <thead>
        <tr>
          <th>Started</th>
          <th>Trigger</th>
          <th>Series</th>
          <th>Matched</th>
          <th>Newly monitored</th>
          <th>Skipped (monitored before)</th>
          <th>Result</th>
        </tr>
      </thead>
      <tbody>
        {runs.map((r) => (
          <>
            <tr
              key={r.id}
              className="clickable"
              onClick={() => setOpen(open === r.id ? null : r.id)}
            >
              <td>{new Date(r.startedAt + "Z").toLocaleString()}</td>
              <td>{r.trigger}</td>
              <td>{r.seriesScanned}</td>
              <td>{r.episodesMatched}</td>
              <td>{r.episodesMonitored}</td>
              <td>{r.episodesSkipped}</td>
              <td className={r.error ? "error" : ""}>{r.error ?? "OK"}</td>
            </tr>
            {open === r.id && r.items.length > 0 && (
              <tr key={`${r.id}d`}>
                <td colSpan={7}>
                  <ul className="conds">
                    {r.items.map((i) => (
                      <li key={i.episodeId}>
                        {i.series} — {i.episode} <em>({i.filter})</em>
                      </li>
                    ))}
                  </ul>
                </td>
              </tr>
            )}
          </>
        ))}
      </tbody>
    </table>
  );
}
