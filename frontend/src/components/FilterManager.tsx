import { useCallback, useEffect, useState } from "react";
import { api, OPERATORS, type Draft, type FieldDef, type Filter, type QuickFilter } from "../api";
import FilterEditor from "./FilterEditor";
import Icon, { BusyIcon } from "./Icon";

/**
 * Lists, creates, edits and deletes the filters of one scope: a single series (imdbId) or the global filters (null).
 * When onTest is provided, filters can be tested: null = all saved filters that apply, otherwise exactly the given ones.
 * When onRun is provided, each filter gets a "Run now" button. onSaved fires after a filter is created, edited or enabled.
 */
export default function FilterManager({
  imdbId,
  seriesTitle,
  onTest,
  onRun,
  runningId,
  onSaved,
}: {
  imdbId: string | null;
  seriesTitle?: string | null;
  onTest?: (filters: Draft[] | null, label: string) => void;
  onRun?: (filter: Filter) => void;
  /** The filter currently being run, if any (all Run buttons wait while one runs). */
  runningId?: number | null;
  onSaved?: (filter: Filter) => void;
}) {
  const [fields, setFields] = useState<FieldDef[]>([]);
  const [filters, setFilters] = useState<Filter[]>([]);
  const [editing, setEditing] = useState<Draft | null>(null);
  const [quickFilters, setQuickFilters] = useState<QuickFilter[]>([]);
  const [menuOpen, setMenuOpen] = useState(false);
  const [error, setError] = useState("");
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    api.fields().then(setFields);
  }, []);

  // Quick Filters only apply to series filters. Reloaded whenever the menu opens so new ones show up without a refresh.
  useEffect(() => {
    if (imdbId && menuOpen) api.quickFilters().then(setQuickFilters).catch((e) => setError(e.message));
  }, [imdbId, menuOpen]);

  const load = useCallback(
    () =>
      api
        .filters(imdbId ? `?imdbId=${encodeURIComponent(imdbId)}` : "?globalOnly=true")
        .then(setFilters)
        .catch((e) => setError(e.message))
        .finally(() => setLoaded(true)),
    [imdbId],
  );
  useEffect(() => {
    load();
    setEditing(null);
  }, [load]);

  const blank = (): Draft => ({
    name: "",
    enabled: true,
    action: "Include",
    conditions: [],
    imdbId,
    seriesTitle: imdbId ? (seriesTitle ?? null) : null,
  });

  /** Opens the editor pre-filled from a Quick Filter. Nothing is saved until the user saves it. */
  const startFromQuickFilter = (q: QuickFilter) => {
    setMenuOpen(false);
    setEditing({ ...blank(), name: q.name, action: q.action, conditions: q.conditions.map((c) => ({ ...c })) });
  };

  const save = async (f: Draft) => {
    const saved = f.id ? await api.updateFilter(f as Filter) : await api.createFilter(f);
    setEditing(null);
    await load();
    onSaved?.(saved);
  };

  /** Why a filter can't be run on its own, or null when it can. */
  const runBlockedReason = (f: Filter) =>
    !f.enabled
      ? "Enable this filter to run it"
      : f.action !== "Include"
        ? "A never-monitor filter doesn't monitor anything by itself"
        : null;

  const act = (p: Promise<unknown>) =>
    p.then(load).catch((e) => setError((e as Error).message));

  return (
    <>
      <div className="row">
        <div className="split-button" onBlur={(e) => e.currentTarget.contains(e.relatedTarget) || setMenuOpen(false)}>
          <button
            className="primary"
            disabled={fields.length === 0}
            onClick={() => setEditing(blank())}
          >
            <Icon name="plus" />
            New filter
          </button>
          {imdbId && (
            <button
              className="primary split-toggle icon-only"
              disabled={fields.length === 0}
              aria-haspopup="menu"
              aria-expanded={menuOpen}
              aria-label="Start from a Quick Filter"
              title="Start from a Quick Filter"
              onClick={() => setMenuOpen(!menuOpen)}
            >
              <Icon name="chevron" />
            </button>
          )}
          {menuOpen && (
            <div className="menu" role="menu">
              {quickFilters.length === 0 ? (
                <span className="menu-empty">No quick filters yet. Create them on the Quick Filters tab.</span>
              ) : (
                quickFilters.map((q) => (
                  <button key={q.id} role="menuitem" onClick={() => startFromQuickFilter(q)}>
                    <Icon name="zap" />
                    {q.name}
                  </button>
                ))
              )}
            </div>
          )}
        </div>
        {onTest && filters.length > 0 && (
          <button onClick={() => onTest(null, "all saved filters")}>
            <Icon name="flask" />
            Test all filters
          </button>
        )}
      </div>
      {error && <p className="error">{error}</p>}
      {editing && (
        <FilterEditor
          initial={editing}
          fields={fields}
          onSave={save}
          onCancel={() => setEditing(null)}
          onTest={
            onTest
              ? (d) => onTest([d], `draft “${d.name || "unnamed"}”`)
              : undefined
          }
        />
      )}
      {!loaded && (
        <div role="status" aria-busy="true" aria-label="Loading filters">
          {[0, 1].map((i) => (
            <div key={i} className="card" aria-hidden="true">
              <div className="row">
                <div className="skeleton skeleton-line" style={{ width: 180 }} />
                <span className="spacer" />
                <div className="skeleton skeleton-button" style={{ width: 70 }} />
                <div className="skeleton skeleton-button" style={{ width: 70 }} />
              </div>
              <div className="skeleton skeleton-line" style={{ width: "50%", margin: "10px 0 6px" }} />
            </div>
          ))}
        </div>
      )}
      {loaded && filters.length === 0 && !editing && (
        <p className="empty">No filters yet.</p>
      )}
      {filters.map((f) => (
        <div className={`card ${f.enabled ? "" : "disabled"}`} key={f.id}>
          <div className="row">
            <strong>{f.name}</strong>
            <span className={`badge ${f.action}`}>
              {f.action === "Include" ? "monitor" : "never monitor"}
            </span>
            {!f.enabled && <span className="badge">disabled</span>}
            <span className="spacer" />
            {onRun && (
              <button
                disabled={runningId != null || runBlockedReason(f) !== null}
                title={runBlockedReason(f) ?? "Run only this filter now: monitors the episodes it matches in Sonarr"}
                onClick={() => onRun(f)}
              >
                <BusyIcon busy={runningId === f.id} name="play" />
                {runningId === f.id ? "Running…" : "Run now"}
              </button>
            )}
            {onTest && (
              <button onClick={() => onTest([f], `filter “${f.name}”`)}>
                <Icon name="flask" />
                Test
              </button>
            )}
            <button
              onClick={() =>
                act(
                  api.updateFilter({ ...f, enabled: !f.enabled }).then((updated) => {
                    if (updated.enabled) onSaved?.(updated); // switching a filter on is a change worth offering to run
                  }),
                )
              }
            >
              <Icon name={f.enabled ? "toggle-right" : "toggle-left"} />
              {f.enabled ? "Disable" : "Enable"}
            </button>
            <button onClick={() => setEditing(f)}><Icon name="edit" />Edit</button>
            <button
              className="danger"
              onClick={() =>
                confirm(`Delete “${f.name}”?`) && act(api.deleteFilter(f.id))
              }
            >
              <Icon name="trash" />
              Delete
            </button>
          </div>
          <ul className="conds">
            {f.conditions.map((c, i) => {
              const def = fields.find((x) => x.key === c.field);
              const op =
                def &&
                OPERATORS[def.type].find((o) => o.value === c.operator)?.label;
              return (
                <li key={i}>
                  {def?.label ?? c.field} <em>{op ?? c.operator}</em>{" "}
                  <code>{c.value}</code>
                </li>
              );
            })}
          </ul>
        </div>
      ))}
    </>
  );
}
