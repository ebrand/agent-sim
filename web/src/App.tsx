import { useEffect, useState } from "react";

const API = "http://localhost:8765";

type Config = {
  instantConstruction: boolean;
  immigrationEnabled: boolean;
  serviceEmigrationEnabled: boolean;
  foundingPhaseEnabled: boolean;
  gateBootstrapOnUtilities: boolean;
  seed: number;
  startingTreasury: number;
};

type ConfigKey = keyof Config;

const TOGGLE_KEYS: ConfigKey[] = [
  "instantConstruction",
  "immigrationEnabled",
  "serviceEmigrationEnabled",
  "foundingPhaseEnabled",
  "gateBootstrapOnUtilities",
];

const LABELS: Record<ConfigKey, string> = {
  instantConstruction: "Instant construction (no 7-day build)",
  immigrationEnabled: "Monthly immigration",
  serviceEmigrationEnabled: "Service-pressure emigration",
  foundingPhaseEnabled: "Founding-phase subsidies (first year)",
  gateBootstrapOnUtilities: "Bootstrap requires Generator + Well",
  seed: "Seed",
  startingTreasury: "Starting treasury",
};

export function App() {
  const [config, setConfig] = useState<Config | null>(null);
  const [dirty, setDirty] = useState<Partial<Config>>({});
  const [status, setStatus] = useState<string>("connecting…");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    fetchConfig();
  }, []);

  async function fetchConfig() {
    setStatus("loading…");
    setError(null);
    try {
      const res = await fetch(`${API}/config`);
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const cfg = (await res.json()) as Config;
      setConfig(cfg);
      setDirty({});
      setStatus("loaded");
    } catch (e) {
      setError(`Failed to load config — is the Unity sim running on ${API}? (${(e as Error).message})`);
      setStatus("error");
    }
  }

  async function applyChanges() {
    if (!config) return;
    if (Object.keys(dirty).length === 0) return;
    setStatus("applying…");
    setError(null);
    try {
      const res = await fetch(`${API}/config`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(dirty),
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      // Re-read so the UI reflects what Unity actually accepted.
      await fetchConfig();
      setStatus("applied");
    } catch (e) {
      setError(`Failed to apply — ${(e as Error).message}`);
      setStatus("error");
    }
  }

  function setBool(key: ConfigKey, val: boolean) {
    if (!config) return;
    setConfig({ ...config, [key]: val });
    setDirty({ ...dirty, [key]: val });
    setStatus("dirty");
  }

  return (
    <div style={pageStyle}>
      <header style={headerStyle}>
        <h1 style={{ margin: 0, fontSize: 22 }}>AgentSim Tweaker</h1>
        <span style={statusStyle(status)}>{status}</span>
      </header>

      {error && <div style={errorStyle}>{error}</div>}

      {config && (
        <>
          <section style={cardStyle}>
            <h2 style={sectionTitle}>Hot-swappable toggles</h2>
            <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
              {TOGGLE_KEYS.map((key) => (
                <label key={key} style={toggleStyle}>
                  <input
                    type="checkbox"
                    checked={config[key] as boolean}
                    onChange={(e) => setBool(key, e.target.checked)}
                  />
                  <span>{LABELS[key]}</span>
                </label>
              ))}
            </div>
          </section>

          <section style={cardStyle}>
            <h2 style={sectionTitle}>Read-only (requires sim restart)</h2>
            <dl style={dlStyle}>
              <dt>{LABELS.seed}</dt>
              <dd>{String(config.seed)}</dd>
              <dt>{LABELS.startingTreasury}</dt>
              <dd>${config.startingTreasury.toLocaleString()}</dd>
            </dl>
          </section>

          <div style={actionsStyle}>
            <button
              onClick={applyChanges}
              disabled={Object.keys(dirty).length === 0}
              style={primaryBtn(Object.keys(dirty).length === 0)}
            >
              Apply
            </button>
            <button onClick={fetchConfig} style={secondaryBtn}>
              Reload
            </button>
          </div>
        </>
      )}
    </div>
  );
}

const pageStyle: React.CSSProperties = {
  maxWidth: 540,
  margin: "32px auto",
  fontFamily: "system-ui, -apple-system, sans-serif",
  color: "#e6e9ef",
  background: "#161922",
  borderRadius: 8,
  padding: 24,
};

const headerStyle: React.CSSProperties = {
  display: "flex",
  alignItems: "center",
  justifyContent: "space-between",
  marginBottom: 16,
};

const cardStyle: React.CSSProperties = {
  background: "#1f2330",
  borderRadius: 6,
  padding: 16,
  marginBottom: 12,
};

const sectionTitle: React.CSSProperties = {
  margin: "0 0 12px 0",
  fontSize: 14,
  fontWeight: 600,
  textTransform: "uppercase",
  letterSpacing: 0.5,
  color: "#9aa3b2",
};

const toggleStyle: React.CSSProperties = {
  display: "flex",
  alignItems: "center",
  gap: 10,
  fontSize: 14,
  cursor: "pointer",
};

const dlStyle: React.CSSProperties = {
  margin: 0,
  display: "grid",
  gridTemplateColumns: "max-content 1fr",
  gap: "6px 16px",
  fontSize: 13,
};

const actionsStyle: React.CSSProperties = {
  display: "flex",
  gap: 8,
  justifyContent: "flex-end",
  marginTop: 4,
};

function primaryBtn(disabled: boolean): React.CSSProperties {
  return {
    padding: "8px 18px",
    background: disabled ? "#3a3f4e" : "#4f6dd6",
    color: disabled ? "#9aa3b2" : "white",
    border: "none",
    borderRadius: 4,
    fontSize: 14,
    fontWeight: 500,
    cursor: disabled ? "default" : "pointer",
  };
}

const secondaryBtn: React.CSSProperties = {
  padding: "8px 18px",
  background: "transparent",
  color: "#9aa3b2",
  border: "1px solid #3a3f4e",
  borderRadius: 4,
  fontSize: 14,
  cursor: "pointer",
};

function statusStyle(status: string): React.CSSProperties {
  const color =
    status === "error" ? "#e57373"
      : status === "applied" || status === "loaded" ? "#7bc47f"
      : "#9aa3b2";
  return { fontSize: 12, color, fontFamily: "monospace" };
}

const errorStyle: React.CSSProperties = {
  background: "#3a1a1a",
  border: "1px solid #6a2727",
  color: "#f8b1b1",
  padding: 12,
  borderRadius: 4,
  marginBottom: 12,
  fontSize: 13,
};
