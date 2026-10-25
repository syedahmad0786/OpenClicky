import { useEffect, useState, type ReactNode } from "react";
import { MAX_CONNECTOR_ADD_ARG, type Connector } from "../connectorsContract";
import { Group, PillButton, Segmented } from "../ui";
import { act } from "./common";

// Client-side mirror of OpenClicky.Core ConnectorFields / ConnectorRequest (same limits, same copy). The host re-validates everything.

type Kind = "remote" | "local";
type Auth = "browser" | "apiKey" | "none";
/** "Set up" on a catalog app with a url setup (Composio): the editor opens with its name and key auth chosen. */
export type EditorPreset = { app: string; name: string };

const KIND_OPTIONS = [{ value: "remote", label: "Remote URL" }, { value: "local", label: "Command on this PC" }] as const;
const AUTH_OPTIONS = [{ value: "browser", label: "Sign in with browser" }, { value: "apiKey", label: "API key" }, { value: "none", label: "None" }] as const;
const AUTH_HINT: Record<Auth, string> = {
  browser: "Opens your browser to sign in. OpenClicky never sees your password.",
  apiKey: "OpenClicky asks for the key in a secure prompt after you add the connector.",
  none: "For servers that need no account.",
};
const REDIRECT = "http://127.0.0.1:47613/callback";

const LOCAL_HOSTS = new Set(["localhost", "127.0.0.1", "[::1]"]);
/** https:// anywhere, http:// only for this PC; no whitespace, backslashes, #fragments or user:password@ tricks (as ConnectorRegistry.ValidateUrl). */
export function validUrl(raw: string) {
  if (!raw || /[\s\\#]/.test(raw)) return false;
  try {
    const u = new URL(raw);
    if (!u.hostname || u.username || u.password) return false;
    return u.protocol === "https:" || (u.protocol === "http:" && LOCAL_HOSTS.has(u.hostname));
  } catch { return false; }
}

// .NET char.IsControl: C0, DEL and C1.
const CONTROL = /[\u0000-\u001f\u007f-\u009f]/;
const HEADER_NAME = /^[A-Za-z0-9!#$%&'*+.^_`|~-]{1,64}$/;
const VARIABLE_NAME = /^[A-Za-z_][A-Za-z0-9_]{0,63}$/;
const RESERVED_HEADERS = new Set(["host", "content-length", "content-type", "accept", "connection", "transfer-encoding", "mcp-session-id", "mcp-protocol-version"]);
const MAX_VALUE = 4_000;
/** Non-blank, trimmed lines (as ConnectorFields.Lines; .NET Trim and JS trim agree on the characters that matter here). */
const lines = (text: string) => text.split("\n").map((l) => l.trim()).filter((l) => l.length > 0);

export function headersError(text: string, auth: Auth): string | null {
  const shape = "Write each header as Name: value on its own line.";
  const all = lines(text);
  if (all.length > 16) return "Up to 16 headers.";
  const seen = new Set<string>();
  for (const line of all) {
    const colon = line.indexOf(":");
    if (colon < 0) return shape;
    const name = line.slice(0, colon).trim();
    const value = line.slice(colon + 1).trim();
    if (!HEADER_NAME.test(name) || value.length === 0 || value.length > MAX_VALUE || CONTROL.test(value)) return shape;
    const lower = name.toLowerCase();
    if (RESERVED_HEADERS.has(lower) || (lower === "authorization" && auth !== "none")) return `Header ${name} can't be set here.`;
    if (seen.has(lower)) return `Header ${name} is listed twice.`;
    seen.add(lower);
  }
  return null;
}

export function environmentError(text: string): string | null {
  const shape = "Write each variable as NAME=value on its own line.";
  const all = lines(text);
  if (all.length > 32) return "Up to 32 variables.";
  const seen = new Set<string>();
  for (const line of all) {
    const equals = line.indexOf("=");
    if (equals < 0) return shape;
    const name = line.slice(0, equals);
    const value = line.slice(equals + 1);
    if (!VARIABLE_NAME.test(name) || value.length > MAX_VALUE || CONTROL.test(value)) return shape;
    const lower = name.toLowerCase(); // Windows environment names are case-insensitive
    if (seen.has(lower)) return `Variable ${name} is listed twice.`;
    seen.add(lower);
  }
  return null;
}

export function argumentsError(text: string): string | null {
  const all = lines(text);
  if (all.length > 32) return "Up to 32 arguments.";
  if (all.some((l) => l.length > 1_000 || CONTROL.test(l))) return "Each argument must be up to 1,000 characters, without control characters.";
  return null;
}

export function labelError(label: string): string | null {
  const clean = label.trim();
  return clean.length > 30 || CONTROL.test(clean) ? "Keep the label to 30 characters." : null;
}

const PENDING_MS = 8_000; // a cancelled key prompt sends no toast and adds nothing: let the user try again

function Field({ id, label, hint, error, children }: { id: string; label: string; hint?: string; error?: string | null; children: ReactNode }) {
  return (
    <div className="settings-field">
      <label className="settings-field__label" htmlFor={id}>{label}</label>
      {children}
      {error ? <span role="alert" className="settings-field__error">{error}</span> : null}
      {hint ? <span className="settings-field__hint">{hint}</span> : null}
    </div>
  );
}

/**
 * New custom connector (or the Composio preset). Typed header and variable values live only in this component's
 * state, so closing it drops them. Stays open after Add until the host's next state holds a new connector with that
 * name; a new toast means the host refused or needs something, so the form unlocks for another try.
 */
export function ConnectorEditor({ connectors, toasts, preset, onClose }: { connectors: Connector[]; toasts: string[]; preset: EditorPreset | null; onClose: () => void }) {
  const [name, setName] = useState(preset?.name ?? "");
  const [kind, setKind] = useState<Kind>("remote");
  const [url, setUrl] = useState("");
  const [auth, setAuth] = useState<Auth>(preset ? "apiKey" : "browser");
  const [command, setCommand] = useState("");
  const [args, setArgs] = useState("");
  const [advanced, setAdvanced] = useState(false);
  const [clientId, setClientId] = useState("");
  const [headers, setHeaders] = useState("");
  const [env, setEnv] = useState("");
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
  const cleanCommand = command.trim();
  const cleanClientId = auth === "browser" ? clientId.trim() : "";
  const remote = kind === "remote";
  const nameError = cleanName.length > 60 || CONTROL.test(cleanName) ? "Give the connector a name of up to 60 characters." : null;
  const urlOk = validUrl(cleanUrl);
  const commandError = cleanCommand.length > 260 || CONTROL.test(cleanCommand) ? "Give the command to run, like npx." : null;
  const headerError = remote ? headersError(headers, auth) : null;
  const argError = remote ? null : argumentsError(args);
  const envError = remote ? null : environmentError(env);
  // Key order is the host's documented shape; optional keys are left out when empty.
  const body = remote
    ? { ...(preset ? { app: preset.app } : {}), name: cleanName, kind, url: cleanUrl, auth, ...(cleanClientId ? { clientId: cleanClientId } : {}), ...(headers.trim() ? { headers } : {}) }
    : { name: cleanName, kind, command: cleanCommand, ...(args.trim() ? { args } : {}), ...(env.trim() ? { env } : {}) };
  const arg = JSON.stringify(body);
  const tooLong = arg.length > MAX_CONNECTOR_ADD_ARG;
  const ready = remote ? urlOk && !headerError : cleanCommand.length > 0 && !commandError && !argError && !envError;
  const canAdd = cleanName.length > 0 && !nameError && ready && !tooLong && !pending && !(remote && cleanClientId.length > 500);
  const add = () => {
    setPending({ keys: new Set(connectors.map((c) => c.key)), toasts: new Set(toasts), name: cleanName });
    act("connector.add", arg);
  };

  return (
    <Group title="New connector">
      <Field id="settings-connector-name" label="Name" error={nameError}>
        <input id="settings-connector-name" className="settings-input" maxLength={60} placeholder="My server" value={name} onChange={(e) => setName(e.target.value)} />
      </Field>
      {preset ? null : (
        <div className="settings-field">
          <span className="settings-field__label">Type</span>
          <Segmented label="Type" options={KIND_OPTIONS} value={kind} onChange={setKind} />
        </div>
      )}
      {remote ? (
        <>
          <Field id="settings-connector-url" label="Server URL"
            hint={preset ? "Paste the MCP address from your Composio dashboard." : undefined}
            error={cleanUrl !== "" && !urlOk ? "Use an address that starts with https:// (http:// only works for 127.0.0.1 or localhost)." : null}>
            <input id="settings-connector-url" className="settings-input" maxLength={400} spellCheck={false} placeholder="https://mcp.example.com/mcp"
              value={url} aria-invalid={cleanUrl !== "" && !urlOk} onChange={(e) => setUrl(e.target.value)} />
          </Field>
          <div className="settings-field">
            <span className="settings-field__label">Authentication</span>
            <Segmented label="Authentication" options={AUTH_OPTIONS} value={auth} onChange={setAuth} />
            <span className="settings-field__hint">{AUTH_HINT[auth]}</span>
          </div>
        </>
      ) : (
        <>
          <Field id="settings-connector-command" label="Command" error={commandError}
            hint="OpenClicky starts this program on your PC when an agent needs it. Only add programs you trust.">
            <input id="settings-connector-command" className="settings-input" maxLength={260} spellCheck={false} placeholder="npx" value={command} onChange={(e) => setCommand(e.target.value)} />
          </Field>
          <Field id="settings-connector-args" label="Arguments" error={argError}>
            <textarea id="settings-connector-args" className="settings-input settings-input--area" rows={3} spellCheck={false} placeholder="One argument per line"
              value={args} onChange={(e) => setArgs(e.target.value)} />
          </Field>
        </>
      )}
      <div className="settings-field">
        <button type="button" className="settings-disclosure" aria-expanded={advanced} onClick={() => setAdvanced((on) => !on)}>Advanced</button>
      </div>
      {advanced ? (
        <>
          {remote && auth === "browser" ? (
            <Field id="settings-connector-client" label="OAuth client ID"
              hint={`Only for servers that need a registered app. Register this redirect address with it: ${REDIRECT}`}>
              <input id="settings-connector-client" className="settings-input" maxLength={500} spellCheck={false} value={clientId} onChange={(e) => setClientId(e.target.value)} />
            </Field>
          ) : null}
          {remote ? (
            <Field id="settings-connector-headers" label="Extra headers" error={headerError}>
              <textarea id="settings-connector-headers" className="settings-input settings-input--area" rows={3} spellCheck={false} placeholder="One per line, like X-Team: design"
                value={headers} onChange={(e) => setHeaders(e.target.value)} />
            </Field>
          ) : (
            <Field id="settings-connector-env" label="Environment variables" error={envError}>
              <textarea id="settings-connector-env" className="settings-input settings-input--area" rows={3} spellCheck={false} placeholder="One per line, like API_TOKEN=…"
                value={env} onChange={(e) => setEnv(e.target.value)} />
            </Field>
          )}
          <p className="settings-field__hint settings-connector-secret-hint">Saved encrypted on this PC and never shown again. To change them, remove the connector and add it again.</p>
        </>
      ) : null}
      {tooLong ? <p role="alert" className="settings-field__error settings-connector-secret-hint">That's too much to send at once. Shorten the headers or variables.</p> : null}
      <div className="settings-actions">
        <PillButton size="sm" variant="outline" onClick={onClose}>Cancel</PillButton>
        <PillButton size="sm" variant="primary" disabled={!canAdd} onClick={add}>{pending ? "Adding…" : "Add connector"}</PillButton>
      </div>
    </Group>
  );
}
