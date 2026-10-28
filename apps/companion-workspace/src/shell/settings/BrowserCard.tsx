import { useEffect, useState } from "react";
import type { ShellState } from "../contract";
import { Badge, Group, PillButton, Row } from "../ui";
import { act } from "./common";

type Integrations = ShellState["settings"]["integrations"];

const PRIVACY = "When an agent uses the browser, page text, screenshots and its requests go to your model provider. Agents never type passwords and never read cookies or saved passwords. Blocked sites are a convenience, not a guarantee.";

/** Every card button goes through chrome.setup with a JSON arg (the host parses it); "disconnect" stays a plain string. */
const send = (arg: object) => act("chrome.setup", JSON.stringify(arg));
const disconnect = () => act("chrome.setup", "disconnect");

function Pick({ label, value, options, onChange }: {
  label: string; value: string; options: { value: string; text: string }[]; onChange: (value: string) => void;
}) {
  return (
    <select className="settings-input" aria-label={label} value={value} disabled={options.length === 0} onChange={(e) => onChange(e.target.value)}>
      {options.map((o) => <option key={o.value} value={o.value}>{o.text}</option>)}
    </select>
  );
}

/** After Connect the host opens the browser and moves this card to "waiting"; until then the button shows it is busy. */
const CONNECT_PENDING_MS = 8_000;

function Chooser({ browser }: { browser: Integrations["browser"] }) {
  const [pickedBrowser, setPickedBrowser] = useState<string | null>(null);
  const [pickedProfile, setPickedProfile] = useState<string | null>(null);
  const [connecting, setConnecting] = useState(false);
  const [noAnswer, setNoAnswer] = useState(false);
  useEffect(() => {
    if (!connecting) return;
    // Nothing came back (the card would have moved on to "waiting"): say so and let the user try again.
    const t = setTimeout(() => { setConnecting(false); setNoAnswer(true); }, CONNECT_PENDING_MS);
    return () => clearTimeout(t);
  }, [connecting]);
  const pick = () => { setConnecting(false); setNoAnswer(false); };
  const known = (id: string | null) => browser.browsers.find((b) => b.id === id);
  const chosen = known(pickedBrowser) ?? known(browser.browserId) ?? browser.browsers[0];
  const profiles = chosen?.profiles ?? [];
  const has = (dir: string | null) => profiles.some((p) => p.dir === dir);
  // The stored profile only applies to the stored browser; a different browser starts at its first profile.
  const profile = has(pickedProfile) ? pickedProfile! : chosen?.id === browser.browserId && has(browser.profileDir) ? browser.profileDir! : (profiles[0]?.dir ?? "");
  return (
    <>
      <Row title="Browser">
        <Pick label="Browser" value={chosen?.id ?? ""} options={browser.browsers.map((b) => ({ value: b.id, text: b.name }))}
          onChange={(id) => { setPickedBrowser(id); setPickedProfile(null); pick(); }} />
      </Row>
      <Row title="Profile" description="Agents use the sites you're signed in to in this profile, in their own tab group.">
        <Pick label="Profile" value={profile} options={profiles.map((p) => ({ value: p.dir, text: p.name }))}
          onChange={(dir) => { setPickedProfile(dir); pick(); }} />
      </Row>
      <div className="settings-actions">
        {noAnswer ? <span role="alert" className="settings-field__error settings-actions__error">Didn't hear back from the browser. Try again.</span> : null}
        <PillButton variant="primary" disabled={!chosen} busy={connecting}
          onClick={() => { if (!chosen) return; setNoAnswer(false); setConnecting(true); send({ op: "connect", browser: chosen.id, profile }); }}>
          {connecting ? "Connecting…" : "Connect"}
        </PillButton>
      </div>
    </>
  );
}

function Steps({ folder }: { folder: string }) {
  return (
    <>
      <Row title="Extension"><Badge tone="warn">Waiting for the extension…</Badge></Row>
      <ol className="settings-steps">
        <li>In the extensions page that opened, turn on Developer mode (top right).</li>
        <li>
          Click Load unpacked and choose this folder:
          <code>{folder}</code>
          <span className="settings-steps__buttons">
            <PillButton size="sm" onClick={() => send({ op: "copyPath" })}>Copy path</PillButton>
            <PillButton size="sm" onClick={() => send({ op: "showFolder" })}>Show folder</PillButton>
          </span>
        </li>
        <li>That's it. This card turns green when the extension says hello.</li>
      </ol>
      <div className="settings-actions">
        <PillButton onClick={() => send({ op: "openExtensions" })}>Open extensions page</PillButton>
        <PillButton variant="outline" onClick={disconnect}>Cancel</PillButton>
      </div>
    </>
  );
}

function Connected({ chrome, browser }: { chrome: "connected" | "working"; browser: Integrations["browser"] }) {
  const b = browser.browsers.find((x) => x.id === browser.browserId);
  const p = b?.profiles.find((x) => x.dir === browser.profileDir);
  return (
    <>
      <Row title="Browser" description={b && p ? `${b.name} · ${p.name}` : undefined}>
        <Badge tone={chrome === "working" ? "warn" : "ok"}>{chrome === "working" ? "Working" : "Connected"}</Badge>
      </Row>
      {browser.instances.length > 1 ? browser.instances.map((i) => (
        <Row key={i.id} title={i.label}>
          {i.selected
            ? <Badge tone="muted">In use</Badge>
            : <PillButton size="sm" onClick={() => send({ op: "select", id: i.id })}>Use this one</PillButton>}
        </Row>
      )) : null}
      <Row title="Disconnect" tone="danger">
        <PillButton size="sm" variant="danger" onClick={disconnect}>Disconnect</PillButton>
      </Row>
    </>
  );
}

const GET = [["chrome", "Get Chrome"], ["edge", "Get Edge"], ["brave", "Get Brave"]] as const;

export function BrowserCard({ chrome, browser }: { chrome: Integrations["chrome"]; browser: Integrations["browser"] }) {
  return (
    <Group title="Browser">
      {chrome === "noBrowser" ? (
        <>
          <Row title="No supported browser found" description="OpenClicky works with Chrome, Edge, Brave, Vivaldi and Chromium. Install one, then come back here." />
          <div className="settings-actions">
            {GET.map(([id, text]) => <PillButton key={id} onClick={() => send({ op: "get", browser: id })}>{text}</PillButton>)}
          </div>
        </>
      ) : null}
      {chrome === "notInstalled" ? <Chooser browser={browser} /> : null}
      {chrome === "waiting" ? <Steps folder={browser.extensionFolder} /> : null}
      {chrome === "connected" || chrome === "working" ? <Connected chrome={chrome} browser={browser} /> : null}
      <p className="settings-line settings-note">{PRIVACY}</p>
    </Group>
  );
}
