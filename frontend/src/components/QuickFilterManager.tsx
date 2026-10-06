import { useEffect, useState } from "react";
import { api, OPERATORS, type Draft, type FieldDef, type QuickFilter } from "../api";
import FilterEditor from "./FilterEditor";
import Icon, { BusyIcon } from "./Icon";



/** Lists, creates, edits and deletes Quick Filters: reusable templates for series filters. */
export default function QuickFilterManager() {
  const [fields, setFields] = useState<FieldDef[]>([]);
  const [items, setItems] = useState<QuickFilter[]>([]);
  const [editing, setEditing] = useState<Draft | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    api.fields().then(setFields);
  }, []);

  const load = () => api.quickFilters().then(setItems).catch((e) => setError(e.message));
  useEffect(() => {
    load();
  }, []);

  const save = async (d: Draft) => {
    const q = { name: d.name, action: d.action, conditions: d.conditions };
    if (editing?.id) await api.updateQuickFilter({ ...q, id: editing.id });
    else await api.createQuickFilter(q);
    setEditing(null);
    await load();
  };

  // The editor works on series-filter drafts; the fields a template doesn't have are fixed.
  const asDraft = (q: Omit<QuickFilter, "id"> & { id?: number }): Draft => ({ ...q, enabled: true, imdbId: null, seriesTitle: null });

  return (
    <>
      <div className="row">
        <button
          className="primary"
          disabled={fields.length === 0}
          onClick={() => setEditing(asDraft({ name: "", action: "Include", conditions: [] }))}
        >
          <Icon name="plus" />
          New quick filter
        </button>
      </div>
      {error && <p className="error">{error}</p>}
      {editing && (
        <FilterEditor
          template
          initial={editing}
          fields={fields}
          onSave={save}
          onCancel={() => setEditing(null)}
        />
      )}
      {items.length === 0 && !editing && <p className="empty">No quick filters yet.</p>}
      {items.map((q) => (
        <div className="card" key={q.id}>
          <div className="row">
            <strong>{q.name}</strong>
            <span className={`badge ${q.action}`}>{q.action === "Include" ? "monitor" : "never monitor"}</span>
            <span className="spacer" />
            <button onClick={() => setEditing(asDraft(q))}><Icon name="edit" />Edit</button>
            <button
              className="danger"
              onClick={() =>
                confirm(`Delete “${q.name}”?`) &&
                api.deleteQuickFilter(q.id).then(load).catch((e) => setError((e as Error).message))
              }
            >
              <Icon name="trash" />
              Delete
            </button>
          </div>
          <ul className="conds">
            {q.conditions.map((c, i) => {
              const def = fields.find((x) => x.key === c.field);
              const op = def && OPERATORS[def.type].find((o) => o.value === c.operator)?.label;
              return (
                <li key={i}>
                  {def?.label ?? c.field} <em>{op ?? c.operator}</em> <code>{c.value}</code>
                </li>
              );
            })}
          </ul>
        </div>
      ))}
    </>
  );
}

