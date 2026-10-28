import { useState } from "react";
import type { ShellState } from "../contract";
import { Group, PillButton, Row, Toggle } from "../ui";
import { act, Page, patch, useDraft } from "./common";

// The host (SettingsCatalog.LoopbackUrl) accepts http(s) on localhost, 127.0.0.1 or [::1], no credentials, at most 200 characters.
const LOCAL_PROXY = /^https?:\/\/(127\.0\.0\.1|localhost|\[::1\])(:\d{1,5})?(\/\S*)?$/i;
const validProxy = (value: string) => {
  if (value.length > 200 || !LOCAL_PROXY.test(value)) return false;
  try { new URL(value); return true; } catch { return false; } // rejects ports above 65535
};

export function AdvancedPage({ state }: { state: ShellState }) {
  const a = state.settings.advanced;
  const [url, setUrl] = useDraft(a.proxyUrl);
  const [error, setError] = useState(false);
  const commit = () => {
    const value = url.trim();
    // The host rejects an empty address, so nothing is posted for it.
    if (!validProxy(value)) { setError(true); return; }
    setError(false);
    if (value !== a.proxyUrl) patch("advanced.proxyUrl", value);
  };
  return (
    <Page title="Advanced" subtitle="Options for troubleshooting.">
      <Group title="Network">
        <Row title="Use local proxy" description="Send OpenClicky's requests through a proxy on this PC.">
          <Toggle label="Use local proxy" checked={a.useLocalProxy} onChange={(v) => patch("advanced.useLocalProxy", v)} />
        </Row>
        <div className="settings-field">
          <label className="settings-field__label" htmlFor="settings-proxy">Proxy URL</label>
          <input id="settings-proxy" className="settings-input" maxLength={200} spellCheck={false} placeholder="http://127.0.0.1:8080"
            value={url} aria-invalid={error} onChange={(e) => { setUrl(e.target.value); setError(false); }} onBlur={commit} />
          {error ? <span role="alert" className="settings-field__error">Enter a local address on this PC: http(s)://127.0.0.1, localhost or [::1], optionally with a port.</span> : null}
        </div>
      </Group>
      <Group title="Compatibility">
        <Row title="Compatibility screen capture" description="Use the older capture method if screenshots come out black or blank.">
          <Toggle label="Compatibility screen capture" checked={a.forceGdiCapture} onChange={(v) => patch("advanced.forceGdiCapture", v)} />
        </Row>
      </Group>
      <Group title="Support">
        <Row title="Open logs" description="Show the folder with OpenClicky's log files."><PillButton onClick={() => act("openLogs")}>Open logs</PillButton></Row>
      </Group>
    </Page>
  );
}
