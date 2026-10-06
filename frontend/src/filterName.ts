import { OPERATORS, type Condition, type FieldDef } from "./api";

const MAX_LENGTH = 80;

/** Drops explanatory text in trailing parentheses: "Days since aired (negative = future)" -> "Days since aired". */
const short = (s: string) => s.replace(/\s*\([^)]*\)\s*$/, "").trim();

function describe(c: Condition, fields: FieldDef[]): string | null {
  const def = fields.find((f) => f.key === c.field);
  if (!def) return null;
  const label = short(def.label);

  if (def.type === "bool") {
    // "Already downloaded is Yes" reads worse than "Already downloaded" / "Not already downloaded",
    // and "Is a season finale" negates to "Is not a season finale".
    if (c.value !== "false") return label;
    return /^is\s/i.test(label)
      ? label.replace(/^is\s/i, "Is not ")
      : `Not ${label.charAt(0).toLowerCase()}${label.slice(1)}`;
  }
  const op = short(OPERATORS[def.type].find((o) => o.value === c.operator)?.label ?? c.operator);
  const value = c.value.trim();
  if (value === "") return null; // a half-filled condition says nothing yet
  return `${label} ${op} ${value}`;
}

/** e.g. "Series rating ≥ 8 & Episode number = 1"; "Never: ..." for filters that exclude episodes. */
export function generateFilterName(
  conditions: Condition[],
  action: "Include" | "Exclude",
  fields: FieldDef[],
): string {
  const parts = conditions.map((c) => describe(c, fields)).filter((p): p is string => p !== null);
  if (parts.length === 0) return "";
  const name = (action === "Exclude" ? "Never: " : "") + parts.join(" & ");
  return name.length > MAX_LENGTH ? `${name.slice(0, MAX_LENGTH - 1).trimEnd()}…` : name;
}
