import { useEffect, useState } from "react";
import { OPERATORS, type Condition, type FieldDef, type Filter } from "../api";
import { generateFilterName } from "../filterName";
import Icon, { BusyIcon } from "./Icon";

type Draft = Omit<Filter, "id"> & { id?: number };

export default function FilterEditor({
  initial,
  fields,
  onSave,
  onCancel,
  onTest,
  template = false,
}: {
  initial: Draft;
  fields: FieldDef[];
  onSave: (f: Draft) => Promise<void>;
  onCancel: () => void;
  /** When provided, shows a button that tests the unsaved draft on its own. */
  onTest?: (f: Draft) => void;
  /** Editing a Quick Filter template: no "Enabled" switch, since a template is never evaluated. */
  template?: boolean;
}) {
  const [f, setF] = useState<Draft>(initial);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => setF(initial), [initial]);

  const generated = generateFilterName(f.conditions, f.action, fields);
  const fieldOf = (key: string) => fields.find((x) => x.key === key);
  const setCond = (i: number, patch: Partial<Condition>) =>
    setF({
      ...f,
      conditions: f.conditions.map((c, j) =>
        j === i ? { ...c, ...patch } : c,
      ),
    });

  const changeField = (i: number, key: string) => {
    const def = fieldOf(key)!;
    setCond(i, {
      field: key,
      operator: OPERATORS[def.type][0].value,
      value: def.type === "bool" ? "true" : "",
    });
  };
  const addCond = () => {
    const def = fields[0];
    setF({
      ...f,
      conditions: [
        ...f.conditions,
        { field: def.key, operator: OPERATORS[def.type][0].value, value: "" },
      ],
    });
  };

  const save = async () => {
    if (!f.name.trim()) return setError("Give the filter a name.");
    if (f.conditions.length === 0)
      return setError("Add at least one condition.");
    setBusy(true);
    setError("");
    try {
      await onSave(f);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="card editor">
      <div className="row">
        <input
          placeholder="Filter name"
          value={f.name}
          onChange={(e) => setF({ ...f, name: e.target.value })}
        />
        <button
          disabled={generated === ""}
          title="Replace the name with a description of the conditions"
          onClick={() => setF({ ...f, name: generated })}
        >
          <Icon name="wand" />
          Generate name
        </button>
        <select
          value={f.action}
          onChange={(e) =>
            setF({ ...f, action: e.target.value as Draft["action"] })
          }
        >
          <option value="Include">Monitor matching episodes</option>
          <option value="Exclude">Never monitor matching episodes</option>
        </select>
        {!template && (
          <label className="inline">
            <input
              type="checkbox"
              checked={f.enabled}
              onChange={(e) => setF({ ...f, enabled: e.target.checked })}
            />{" "}
            Enabled
          </label>
        )}
      </div>
      <p className="hint">
        All conditions must match (AND). Episodes matching any enabled “monitor”
        filter are monitored, unless an “never monitor” filter also matches.
      </p>
      {f.conditions.map((c, i) => {
        const def = fieldOf(c.field)!;
        return (
          <div className="row" key={i}>
            <select
              value={c.field}
              onChange={(e) => changeField(i, e.target.value)}
            >
              {["Series", "Episode"].map((g) => (
                <optgroup key={g} label={g}>
                  {fields
                    .filter((x) => x.group === g)
                    .map((x) => (
                      <option key={x.key} value={x.key}>
                        {x.label}
                      </option>
                    ))}
                </optgroup>
              ))}
            </select>
            <select
              value={c.operator}
              onChange={(e) => setCond(i, { operator: e.target.value })}
            >
              {OPERATORS[def.type].map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
            {def.type === "bool" ? (
              <select
                value={c.value}
                onChange={(e) => setCond(i, { value: e.target.value })}
              >
                <option value="true">Yes</option>
                <option value="false">No</option>
              </select>
            ) : (
              <input
                type={
                  def.type === "date"
                    ? "date"
                    : def.type === "number"
                      ? "number"
                      : "text"
                }
                step="any"
                placeholder="Value"
                value={c.value}
                onChange={(e) => setCond(i, { value: e.target.value })}
              />
            )}
            <button
              className="danger icon-only"
              onClick={() =>
                setF({
                  ...f,
                  conditions: f.conditions.filter((_, j) => j !== i),
                })
              }
              aria-label="Remove condition"
              title="Remove condition"
            >
              <Icon name="x" />
            </button>
          </div>
        );
      })}
      <div className="row">
        <button onClick={addCond}><Icon name="plus" />Add condition</button>
        <span className="spacer" />
        {error && <span className="error">{error}</span>}
        {onTest && (
          <button
            disabled={f.conditions.length === 0}
            title="Show which episodes this filter alone would monitor, without saving"
            onClick={() => onTest(f)}
          >
            <Icon name="flask" />
            Test draft
          </button>
        )}
        <button onClick={onCancel}><Icon name="x" />Cancel</button>
        <button className="primary" disabled={busy} onClick={save}>
          <BusyIcon busy={busy} name="save" />
          {template ? "Save quick filter" : "Save filter"}
        </button>
      </div>
    </div>
  );
}
