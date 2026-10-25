import { useCallback, useEffect, useRef, useState } from "react";
import type { ShellState } from "../contract";
import { Badge, Group, PillButton, Row, SearchField, Segmented } from "../ui";
import { act, ConfirmRow, Page } from "./common";

type Connector = ShellState["settings"]["integrations"]["connectors"][number];
type Auth = "browser" | "apiKey" | "none";

const STATUS = {
  connected: { tone: "ok", text: "Connected" },
  checking: { tone: "muted", text: "Checking…" },
  error: { tone: "danger", text: "Error" },
  needsSignIn: { tone: "warn", text: "Needs sign-in" },
  configured: { tone: "muted", text: "Added" },
  signingIn: { tone: "muted", text: "Signing in…" },
  tokenRejected: { tone: "danger", text: "Token rejected" },
  reconnect: { tone: "warn", text: "Reconnect required" },
} as const;

const AUTH_OPTIONS = [
  { value: "browser", label: "Sign in with browser" },
  { value: "apiKey", label: "API key" },
  { value: "none", label: "None" },
] as const;

const MAX_ARG = 512; // the host rejects a connector.add arg longer than this
const LOCAL_HOSTS = new Set(["localhost", "127.0.0.1", "[::1]"]);
/** https:// anywhere, http:// only for this PC; no whitespace, backslashes, #fragments or user:password@ tricks (as ConnectorRegistry.ValidateUrl). */
function validUrl(raw: string) {
  if (!raw || /[\s\\#]/.test(raw)) return false;
  try {
    const u = new URL(raw);
    if (!u.hostname || u.username || u.password) return false;
    return u.protocol === "https:" || (u.protocol === "http:" && LOCAL_HOSTS.has(u.hostname));
  } catch { return false; }
}

function BrowserCard({ chrome }: { chrome: ShellState["settings"]["integrations"]["chrome"] }) {
  return (
    <Group title="Browser">
      <Row title="Chrome" description="Works with Chrome.">
        {chrome === "notInstalled" ? <PillButton variant="primary" onClick={() => act("chrome.setup")}>Set up Chrome</PillButton> : null}
        {chrome === "waiting" ? <Badge tone="warn">Waiting for the extension…</Badge> : null}
        {chrome === "connected" ? <Badge>Connected</Badge> : null}
      </Row>
      {chrome === "connected" ? (
        <Row title="Disconnect Chrome" tone="danger">
          <PillButton size="sm" variant="danger" aria-label="Disconnect Chrome" onClick={() => act("chrome.setup", "disconnect")}>Disconnect</PillButton>
        </Row>
      ) : null}
    </Group>
  );
}

function ConnectorRow({ c, removing, onRemoving }: { c: Connector; removing: boolean; onRemoving: (on: boolean) => void }) {
  const status = STATUS[c.status];
  const trigger = useRef<HTMLButtonElement>(null);
  return (
    <>
      <Row title={c.name}
        description={(
          <>
            <span>{c.host}</span>
            {c.toolCount === null ? null : <span className="settings-line">{c.toolCount === 1 ? "1 tool" : `${c.toolCount} tools`}</span>}
          </>
        )}>
        <Badge tone={status.tone}>{status.text}</Badge>
        <PillButton size="sm" aria-label={`Test ${c.name}`} onClick={() => act("connector.test", c.key)}>Test</PillButton>
        <PillButton size="sm" variant="danger" ref={trigger} aria-label={`Remove ${c.name}`} onClick={() => onRemoving(true)}>Remove</PillButton>
      </Row>
      {removing ? (
        <ConfirmRow title={`Remove ${c.name}? Agents will lose access to it.`} confirmLabel="Remove connector"
          onKeep={() => { onRemoving(false); trigger.current?.focus(); }}
          onConfirm={() => { onRemoving(false); act("connector.remove", c.key); }} />
      ) : null}
    </>
  );
}

const PENDING_MS = 8_000; // a cancelled key prompt sends no toast and adds nothing: let the user try again

/**
 * Stays open after Add (typed values kept) until the host's next state holds a connector with that name; a toast
 * means the host refused or needs something, so the form unlocks for another try.
 */
function AddConnector({ connectors, toasts, onClose }: { connectors: Connector[]; toasts: string[]; onClose: () => void }) {
  const [name, setName] = useState("");
  const [url, setUrl] = useState("");
  const [auth, setAuth] = useState<Auth>("browser");
  const [pending, setPending] = useState<{ keys: Set<string>; toasts: Set<string>; name: string } | null>(null);
  useEffect(() => {
    if (!pending) return;
    if (connectors.some((c) => !pending.keys.has(c.key) && c.name.trim().toLowerCase() === pending.name.toLowerCase())) onClose();
    else if (toasts.some((id) => !pending.toasts.has(id))) setPending(null);
  }, [pending, connectors, toasts, onClose]);
  useEffect(() => {
    if (!pending) return;
    const t = setTimeout(() => setPending(null), PENDING_MS);
    return () => clearTimeout(t);
  }, [pending]);
  const cleanName = name.trim();
  const cleanUrl = url.trim();
  const arg = JSON.stringify({ name: cleanName, url: cleanUrl, auth });
  const urlOk = validUrl(cleanUrl);
  const tooLong = arg.length > MAX_ARG;
  const canAdd = auth !== "browser" && cleanName.length > 0 && cleanName.length <= 60 && urlOk && !tooLong && !pending;
  const add = () => {
    setPending({ keys: new Set(connectors.map((c) => c.key)), toasts: new Set(toasts), name: cleanName });
    act("connector.add", arg);
  };
  return (
    <Group title="New connector">
      <div className="settings-field">
        <label className="settings-field__label" htmlFor="settings-connector-name">Name</label>
        <input id="settings-connector-name" className="settings-input" maxLength={60} placeholder="Linear" value={name} onChange={(e) => setName(e.target.value)} />
      </div>
      <div className="settings-field">
        <label className="settings-field__label" htmlFor="settings-connector-url">Server URL</label>
        <input id="settings-connector-url" className="settings-input" maxLength={400} spellCheck={false} placeholder="https://mcp.example.com/mcp"
          value={url} aria-invalid={cleanUrl !== "" && !urlOk} onChange={(e) => setUrl(e.target.value)} />
        {cleanUrl !== "" && !urlOk
          ? <span role="alert" className="settings-field__error">Use an address that starts with https:// (http:// only works for 127.0.0.1 or localhost).</span> : null}
        {tooLong ? <span role="alert" className="settings-field__error">That name and address are too long together.</span> : null}
      </div>
      <div className="settings-field">
        <span className="settings-field__label">Authentication</span>
        <Segmented label="Authentication" options={AUTH_OPTIONS} value={auth} onChange={setAuth} />
        {auth === "browser" ? <span className="settings-field__hint">Browser sign-in arrives with the connectors update.</span> : null}
        {auth === "apiKey" ? <span className="settings-field__hint">OpenClicky asks for the key in a secure prompt after you add the connector.</span> : null}
      </div>
      <div className="settings-actions">
        <PillButton size="sm" variant="outline" onClick={onClose}>Cancel</PillButton>
        <PillButton size="sm" variant="primary" disabled={!canAdd} onClick={add}>{pending ? "Adding…" : "Add connector"}</PillButton>
      </div>
    </Group>
  );
}

export function IntegrationsPage({ state }: { state: ShellState }) {
  const { chrome, connectors } = state.settings.integrations;
  const [query, setQuery] = useState("");
  const [adding, setAdding] = useState(false);
  const [removing, setRemoving] = useState<string | null>(null);
  const closeAdd = useCallback(() => setAdding(false), []);
  const toasts = state.cards.filter((c) => c.kind === "toast").map((c) => c.id);
  const q = query.trim().toLowerCase();
  const shown = q ? connectors.filter((c) => c.name.toLowerCase().includes(q) || c.host.toLowerCase().includes(q)) : connectors;
  return (
    <Page title="Integrations" subtitle="Apps and services your agents can use.">
      <BrowserCard chrome={chrome} />
      <div className="settings-toolbar">
        <div className="settings-toolbar__search"><SearchField label="Search connectors" value={query} onChange={setQuery} /></div>
        {adding ? null : <PillButton onClick={() => setAdding(true)}>Add custom connector</PillButton>}
      </div>
      {adding ? <AddConnector connectors={connectors} toasts={toasts} onClose={closeAdd} /> : null}
      <Group title="Connected">
        {shown.length === 0
          ? <Row title={connectors.length === 0 ? "No connectors yet" : "No connectors match your search"} />
          : shown.map((c) => <ConnectorRow key={c.key} c={c} removing={removing === c.key} onRemoving={(on) => setRemoving(on ? c.key : null)} />)}
      </Group>
    </Page>
  );
}
