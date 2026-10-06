import { useEffect, useState } from "react";
import { api, type Settings, type TestResult } from "../api";
import Icon, { BusyIcon } from "../components/Icon";

const LEVEL_LABELS: Record<string, string> = {
  Verbose: "Verbose",
  Information: "Info",
  Warning: "Warning",
  Error: "Error",
};

/** The outcome of a connection test, remembered with the exact values that were tested. */
type Tested<T> = { values: T; result: TestResult };

function TestStatus({ result }: { result: TestResult }) {
  return <span className={result.ok ? "ok" : "error"}>{result.message}</span>;
}

/** A dropdown of the choices Sonarr offers (blank = Sonarr's first), or a text box when they couldn't be loaded. */
function ChoiceField({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: string;
  options: string[] | undefined;
  onChange: (v: string) => void;
}) {
  return (
    <label>
      {label}
      {options ? (
        <select value={value} onChange={(e) => onChange(e.target.value)}>
          <option value="">(first one in Sonarr)</option>
          {value && !options.includes(value) && <option value={value}>{value} (not found in Sonarr)</option>}
          {options.map((o) => (
            <option key={o} value={o}>
              {o}
            </option>
          ))}
        </select>
      ) : (
        <input value={value} placeholder="(first one in Sonarr)" onChange={(e) => onChange(e.target.value)} />
      )}
    </label>
  );
}

export default function SettingsPage() {
  const [s, setS] = useState<Settings | null>(null);
  const [sonarrUrl, setSonarrUrl] = useState("");
  const [apiKey, setApiKey] = useState("");
  const [omdbKey, setOmdbKey] = useState("");
  const [sonarrTest, setSonarrTest] = useState<Tested<string> | null>(null);
  const [omdbTest, setOmdbTest] = useState<Tested<string> | null>(null);
  const [testing, setTesting] = useState<"sonarr" | "omdb" | null>(null);
  const [saving, setSaving] = useState(false);
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const [choices, setChoices] = useState<{ rootFolders: string[]; qualityProfiles: string[] } | null>(null);
  const [choicesError, setChoicesError] = useState("");

  const load = async () => {
    const loaded = await api.settings();
    setS(loaded);
    setSonarrUrl(loaded.sonarrUrl);
    setApiKey("");
    setOmdbKey("");
    setSonarrTest(null);
    setOmdbTest(null);
    // The dropdown choices come from the saved Sonarr connection.
    api
      .sonarrOptions()
      .then((o) => {
        setChoices(o);
        setChoicesError("");
      })
      .catch((e) => {
        setChoices(null);
        setChoicesError((e as Error).message);
      });
  };
  useEffect(() => {
    load();
  }, []);
  if (!s) return null;
  const patch = (p: Partial<Settings>) => setS({ ...s, ...p });

  // A test only counts for the exact values it was run with: editing a field afterwards invalidates it.
  const sonarrSnapshot = `${sonarrUrl.trim()}\n${apiKey.trim()}`;
  const sonarrDirty = sonarrUrl.trim() !== s.sonarrUrl || apiKey.trim() !== "";
  const sonarrPassed = sonarrTest?.values === sonarrSnapshot && sonarrTest.result.ok;
  const needSonarrTest = sonarrDirty && !sonarrPassed;

  const omdbDirty = omdbKey.trim() !== "";
  const omdbPassed = omdbTest?.values === omdbKey.trim() && omdbTest.result.ok;
  const needOmdbTest = omdbDirty && !omdbPassed;

  const canSave = !saving && !needSonarrTest && !needOmdbTest;
  const canTestSonarr =
    testing === null && sonarrUrl.trim() !== "" && (apiKey.trim() !== "" || s.hasApiKey);
  const canTestOmdb = testing === null && (omdbKey.trim() !== "" || s.omdbConfigured);

  const testSonarr = async () => {
    setTesting("sonarr");
    const snapshot = sonarrSnapshot;
    try {
      const result = await api.testSonarr({ sonarrUrl: sonarrUrl.trim(), apiKey: apiKey.trim() });
      setSonarrTest({ values: snapshot, result });
    } catch (e) {
      setSonarrTest({ values: snapshot, result: { ok: false, message: (e as Error).message } });
    } finally {
      setTesting(null);
    }
  };

  const testOmdb = async () => {
    setTesting("omdb");
    const snapshot = omdbKey.trim();
    try {
      const result = await api.testOmdb({ omdbApiKey: snapshot });
      setOmdbTest({ values: snapshot, result });
    } catch (e) {
      setOmdbTest({ values: snapshot, result: { ok: false, message: (e as Error).message } });
    } finally {
      setTesting(null);
    }
  };

  const save = async () => {
    setSaving(true);
    setMsg(null);
    try {
      await api.saveSettings({
        sonarrUrl: sonarrUrl.trim(),
        apiKey: apiKey.trim(),
        omdbApiKey: omdbKey.trim(),
        logLevel: s.logLevel,
        sonarrRootFolderPath: s.sonarrRootFolderPath,
        sonarrQualityProfile: s.sonarrQualityProfile,
        autoSyncEnabled: s.autoSyncEnabled,
        syncIntervalMinutes: s.syncIntervalMinutes,
        searchOnMonitor: s.searchOnMonitor,
        skipUnmonitoredSeries: s.skipUnmonitoredSeries,
      });
      await load();
      setMsg({ ok: true, text: "Saved." });
    } catch (e) {
      // The server re-checks changed connection settings, so it can still refuse (for example if Sonarr went away).
      setMsg({ ok: false, text: (e as Error).message });
    } finally {
      setSaving(false);
    }
  };

  const levels = s.logLevels.includes(s.logLevel) ? s.logLevels : [s.logLevel, ...s.logLevels];

  return (
    <div className="card form">
      <h3>Sonarr</h3>
      <label>
        Sonarr URL
        <input
          placeholder="http://localhost:8989"
          value={sonarrUrl}
          onChange={(e) => setSonarrUrl(e.target.value)}
        />
      </label>
      <label>
        Sonarr API key
        <input
          type="password"
          autoComplete="off"
          placeholder={s.hasApiKey ? "(unchanged)" : ""}
          value={apiKey}
          onChange={(e) => setApiKey(e.target.value)}
        />
      </label>
      <div className="row">
        <button disabled={!canTestSonarr} onClick={testSonarr}>
          <BusyIcon busy={testing === "sonarr"} name="plug" />
          {testing === "sonarr" ? "Testing…" : "Test connection"}
        </button>
        {sonarrTest?.values === sonarrSnapshot && <TestStatus result={sonarrTest.result} />}
        {needSonarrTest && sonarrTest?.values !== sonarrSnapshot && (
          <span className="hint">Test the connection to enable Save.</span>
        )}
      </div>

      <h3>New series added from Filtarr</h3>
      <ChoiceField
        label="Root folder"
        value={s.sonarrRootFolderPath}
        options={choices?.rootFolders}
        onChange={(v) => patch({ sonarrRootFolderPath: v })}
      />
      <ChoiceField
        label="Quality profile"
        value={s.sonarrQualityProfile}
        options={choices?.qualityProfiles}
        onChange={(v) => patch({ sonarrQualityProfile: v })}
      />
      <p className="hint">
        Used by "Add to Sonarr". Leave on the first option to use whatever Sonarr lists first.
        {choicesError && ` Couldn't load the choices from Sonarr (${choicesError}); you can type values instead.`}
      </p>

      <h3>OMDb (episode ratings)</h3>
      <label>
        OMDb API key
        <input
          type="password"
          autoComplete="off"
          placeholder={s.omdbConfigured ? "(configured, unchanged)" : "free key from omdbapi.com"}
          value={omdbKey}
          onChange={(e) => setOmdbKey(e.target.value)}
        />
      </label>
      <div className="row">
        <button disabled={!canTestOmdb} onClick={testOmdb}>
          <BusyIcon busy={testing === "omdb"} name="key" />
          {testing === "omdb" ? "Testing…" : omdbKey.trim() ? "Test key" : "Test saved key"}
        </button>
        {omdbTest?.values === omdbKey.trim() && <TestStatus result={omdbTest.result} />}
        {needOmdbTest && omdbTest?.values !== omdbKey.trim() && (
          <span className="hint">Test the key to enable Save.</span>
        )}
        {!s.omdbConfigured && !omdbDirty && (
          <span className="hint">Without a key, episode rating filters match nothing.</span>
        )}
      </div>

      <h3>Logging</h3>
      <label>
        Log level
        <select value={s.logLevel} onChange={(e) => patch({ logLevel: e.target.value })}>
          {levels.map((l) => (
            <option key={l} value={l}>
              {LEVEL_LABELS[l] ?? l}
            </option>
          ))}
        </select>
      </label>
      <p className="hint">Applies as soon as you save; no restart needed. Warning is the default.</p>

      <h3>Sync</h3>
      <label className="inline">
        <input
          type="checkbox"
          checked={s.searchOnMonitor}
          onChange={(e) => patch({ searchOnMonitor: e.target.checked })}
        />
        Search for newly monitored episodes that have no file
      </label>
      <label className="inline">
        <input
          type="checkbox"
          checked={s.skipUnmonitoredSeries}
          onChange={(e) => patch({ skipUnmonitoredSeries: e.target.checked })}
        />
        Skip series that are unmonitored in Sonarr
      </label>
      <label className="inline">
        <input
          type="checkbox"
          checked={s.autoSyncEnabled}
          onChange={(e) => patch({ autoSyncEnabled: e.target.checked })}
        />
        Sync automatically every
        <input
          type="number"
          min={1}
          style={{ width: 80 }}
          value={s.syncIntervalMinutes}
          onChange={(e) => patch({ syncIntervalMinutes: Number(e.target.value) })}
        />{" "}
        minutes
      </label>

      <div className="row">
        <button className="primary" disabled={!canSave} onClick={save}>
          <BusyIcon busy={saving} name="save" />
          {saving ? "Saving…" : "Save"}
        </button>
        {msg && <span className={msg.ok ? "ok" : "error"}>{msg.text}</span>}
      </div>
      <p className="hint">
        Values saved here override <code>appsettings.json</code>, which stays as the fallback. Port:{" "}
        <code>{s.port}</code> · Data directory: <code>{s.dataDirectory}</code> (set in{" "}
        <code>appsettings.json</code>; changing them needs a restart).
      </p>
    </div>
  );
}
