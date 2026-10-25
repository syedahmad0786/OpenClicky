import { Fragment, useCallback, useRef, useState } from "react";
import type { ShellState } from "../contract";
import type { CatalogItem, Connector } from "../connectorsContract";
import { Badge, Group, PillButton, Row, SearchField, Segmented } from "../ui";
import { BrowserCard } from "./BrowserCard";
import { act, ConfirmRow, Page } from "./common";
import { ConnectorEditor, labelError, type EditorPreset } from "./ConnectorEditor";

const STATUS = {
  connected: { tone: "ok", text: "Connected" },
  checking: { tone: "muted", text: "Checking…" },
  configured: { tone: "muted", text: "Not checked" },
  signingIn: { tone: "muted", text: "Signing in…" },
  needsSignIn: { tone: "warn", text: "Needs sign-in" },
  reconnect: { tone: "warn", text: "Reconnect required" },
  tokenRejected: { tone: "danger", text: "Key rejected" },
  error: { tone: "danger", text: "Error" },
} as const;

const MAX_ACCOUNTS = 5; // per catalog app (the host enforces it too)
const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
/** "d MMM yyyy" in the user's time zone, with fixed English month names. */
function addedOn(since: string) {
  const d = new Date(since);
  return `Added ${d.getDate()} ${MONTHS[d.getMonth()]} ${d.getFullYear()}`;
}
const titleOf = (c: Connector) => (c.label ? `${c.name} · ${c.label}` : c.name);

type LabelChoice = "work" | "personal" | "other";
const LABEL_OPTIONS = [{ value: "work", label: "Work" }, { value: "personal", label: "Personal" }, { value: "other", label: "Other…" }] as const;
const PRESET_LABELS = { work: "Work", personal: "Personal" } as const;

/** Work / Personal / Other… (a typed label of up to 30 characters); used to add another account and to rename one. */
function LabelPicker({ initial, saveLabel, onCancel, onSave }: { initial: string; saveLabel: string; onCancel: () => void; onSave: (label: string) => void }) {
  const start: LabelChoice = initial === "Work" ? "work" : initial === "Personal" ? "personal" : "other";
  const [choice, setChoice] = useState<LabelChoice>(start);
  const [other, setOther] = useState(start === "other" ? initial : "");
  const label = choice === "other" ? other.trim() : PRESET_LABELS[choice];
  const error = choice === "other" ? labelError(other) : null;
  return (
    <>
      <div className="settings-field">
        <Segmented label="Account label" options={LABEL_OPTIONS} value={choice} onChange={setChoice} />
        {choice === "other" ? (
          <input className="settings-input" aria-label="Label" placeholder="Label" maxLength={30} value={other} onChange={(e) => setOther(e.target.value)} />
        ) : null}
        {error ? <span role="alert" className="settings-field__error">{error}</span> : null}
      </div>
      <div className="settings-actions">
        <PillButton size="sm" variant="outline" onClick={onCancel}>Cancel</PillButton>
        <PillButton size="sm" variant="primary" disabled={label.length === 0 || error !== null} onClick={() => onSave(label)}>{saveLabel}</PillButton>
      </div>
    </>
  );
}

/** What is open under a row: its remove confirm, its rename picker, or an app's "add another account" picker. One at a time. */
type Open = { kind: "remove" | "rename"; key: string } | { kind: "add"; app: string } | null;

function ConnectorRow({ c, multiAccount, open, setOpen }: { c: Connector; multiAccount: boolean; open: Open; setOpen: (o: Open) => void }) {
  const status = STATUS[c.status];
  const trigger = useRef<HTMLButtonElement>(null);
  const title = titleOf(c);
  const vault = c.kind === "vault";
  const signIn = c.auth === "browser" && (c.status === "needsSignIn" || c.status === "reconnect");
  const signInText = c.status === "reconnect" ? "Reconnect" : "Sign in";
  const removing = open?.kind === "remove" && open.key === c.key;
  const renaming = open?.kind === "rename" && open.key === c.key;
  return (
    <>
      <Row title={title}
        description={(
          <>
            <span>{c.host}</span>
            {c.toolCount === null ? null : <span className="settings-line">{c.toolCount === 1 ? "1 tool" : `${c.toolCount} tools`}</span>}
            {c.since ? <span className="settings-line">{addedOn(c.since)}</span> : null}
            {c.status === "signingIn" ? <span className="settings-line">Finish connecting in your browser.</span> : null}
          </>
        )}>
        <Badge tone={status.tone}>{status.text}</Badge>
        {signIn ? <PillButton size="sm" variant="primary" aria-label={`${signInText} ${title}`} onClick={() => act("connector.signIn", c.key)}>{signInText}</PillButton> : null}
        {c.status === "signingIn" || c.status === "checking" ? null
          : <PillButton size="sm" aria-label={`Test ${title}`} onClick={() => act("connector.test", c.key)}>Test</PillButton>}
        {multiAccount ? <PillButton size="sm" aria-label={`Rename ${title}`} onClick={() => setOpen({ kind: "rename", key: c.key })}>Rename</PillButton> : null}
        <PillButton size="sm" variant="danger" ref={trigger} aria-label={`${vault ? "Disconnect" : "Remove"} ${title}`} onClick={() => setOpen({ kind: "remove", key: c.key })}>
          {vault ? "Disconnect" : "Remove"}
        </PillButton>
      </Row>
      {renaming ? (
        <LabelPicker initial={c.label ?? "Work"} saveLabel="Save" onCancel={() => setOpen(null)}
          onSave={(label) => { setOpen(null); act("connector.label", JSON.stringify({ key: c.key, label })); }} />
      ) : null}
      {removing ? (
        <ConfirmRow title={vault ? "Disconnect Obsidian? Agents lose access to your notes from their next task." : `Remove ${title}? Agents lose access to it from their next task.`}
          confirmLabel={vault ? "Disconnect" : "Remove connector"}
          onKeep={() => { setOpen(null); trigger.current?.focus(); }}
          onConfirm={() => { setOpen(null); act("connector.remove", c.key); }} />
      ) : null}
    </>
  );
}

function AvailableRow({ item, onSetUp }: { item: CatalogItem; onSetUp: (preset: EditorPreset) => void }) {
  const add = () => act("connector.add", JSON.stringify({ app: item.id }));
  return (
    <Row title={item.name} description={item.description}>
      {item.setup === "url" ? <PillButton size="sm" aria-label={`Set up ${item.name}`} onClick={() => onSetUp({ app: item.id, name: item.name })}>Set up</PillButton>
        : item.setup === "folder" ? <PillButton size="sm" onClick={add}>Choose vault</PillButton>
        : <PillButton size="sm" aria-label={`Connect ${item.name}`} onClick={add}>Connect</PillButton>}
    </Row>
  );
}

const has = (text: string | null, q: string) => (text ?? "").toLowerCase().includes(q);

export function IntegrationsPage({ state }: { state: ShellState }) {
  const { chrome, browser, connectors, catalog } = state.settings.integrations;
  const [query, setQuery] = useState("");
  const [editing, setEditing] = useState<{ preset: EditorPreset | null } | null>(null);
  const [open, setOpen] = useState<Open>(null);
  const closeEditor = useCallback(() => setEditing(null), []);
  const toasts = state.cards.filter((c) => c.kind === "toast").map((c) => c.id);
  const q = query.trim().toLowerCase();

  // Account rows of one app sit together, in the order the app first appears.
  const shown = q ? connectors.filter((c) => has(c.name, q) || has(c.label, q) || has(c.host, q)) : connectors;
  const groups: Connector[][] = [];
  const byApp = new Map<string, Connector[]>();
  for (const c of shown) {
    if (!c.app) { groups.push([c]); continue; }
    let group = byApp.get(c.app);
    if (!group) { group = []; byApp.set(c.app, group); groups.push(group); }
    group.push(c);
  }
  const connectedApps = new Set(connectors.map((c) => c.app));
  const available = catalog.filter((i) => !connectedApps.has(i.id) && (!q || has(i.name, q) || has(i.description, q)));

  const renderGroup = (group: Connector[]) => {
    const app = group[0].app;
    const item = app ? catalog.find((i) => i.id === app) : undefined;
    const multi = item?.multiAccount === true;
    const accounts = connectors.filter((c) => c.app === app);
    const used = new Set(accounts.map((c) => c.label));
    return (
      <Fragment key={group[0].key}>
        {group.map((c) => <ConnectorRow key={c.key} c={c} multiAccount={multi} open={open} setOpen={setOpen} />)}
        {item && multi && accounts.length < MAX_ACCOUNTS ? (
          open?.kind === "add" && open.app === item.id ? (
            <LabelPicker initial={!used.has("Work") ? "Work" : !used.has("Personal") ? "Personal" : ""} saveLabel="Continue" onCancel={() => setOpen(null)}
              onSave={(label) => { setOpen(null); act("connector.add", JSON.stringify({ app: item.id, label })); }} />
          ) : (
            <div className="settings-add-account">
              <PillButton size="sm" variant="outline" onClick={() => setOpen({ kind: "add", app: item.id })}>{`Add another ${item.name} account`}</PillButton>
            </div>
          )
        ) : null}
      </Fragment>
    );
  };

  return (
    <Page title="Integrations" subtitle="Apps and services your agents can use.">
      <BrowserCard chrome={chrome} browser={browser} />
      <p className="settings-note">When an agent uses an app, its request goes to that app, and what comes back goes to your model provider with the task. Keys and sign-ins stay encrypted on this PC.</p>
      <div className="settings-toolbar">
        <div className="settings-toolbar__search"><SearchField label="Search integrations" value={query} onChange={setQuery} /></div>
        {editing ? null : <PillButton onClick={() => setEditing({ preset: null })}>Add custom connector</PillButton>}
      </div>
      {editing ? <ConnectorEditor key={editing.preset?.app ?? ""} connectors={connectors} toasts={toasts} preset={editing.preset} onClose={closeEditor} /> : null}
      {q && groups.length === 0 && available.length === 0 ? <p className="settings-empty">{`Nothing matches “${query.trim()}”.`}</p> : (
        <>
          {q && groups.length === 0 ? null : (
            <Group title="Connected">
              {groups.length === 0 ? <Row title="Nothing connected yet." /> : groups.map(renderGroup)}
            </Group>
          )}
          {q && available.length === 0 ? null : (
            <Group title="Available">
              {available.length === 0 ? <Row title="Everything here is connected." />
                : available.map((i) => <AvailableRow key={i.id} item={i} onSetUp={(preset) => setEditing({ preset })} />)}
            </Group>
          )}
        </>
      )}
    </Page>
  );
}
