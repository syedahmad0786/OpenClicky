import { useEffect, useRef, useState } from "react";
import type { ShellState } from "../contract";
import { Group, PillButton, RadioList, Row, Toggle } from "../ui";
import { act, ConfirmRow, Page, patch, useDraft } from "./common";

type Mode = ShellState["settings"]["access"]["mode"];
const MODES = [
  { value: "preview", label: "Preview only", description: "Shows what it would do; never clicks or types." },
  { value: "confirmEach", label: "Approve each action", description: "Asks before every click, keystroke or file change." },
  { value: "auto", label: "Auto", description: "Approves the plan once, then asks only for risky steps." },
  { value: "full", label: "Full access", description: "Never asks. Use only when you trust the task completely." },
] as const satisfies readonly { value: Mode; label: string; description: string }[];

export function AccessPage({ state }: { state: ShellState }) {
  const a = state.settings.access;
  const [deny, setDeny] = useDraft(a.denyList);
  const [confirming, setConfirming] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);
  const refocus = useRef(false); // Cancel puts focus back on the button that opened the confirm
  useEffect(() => { if (!confirming && refocus.current) { refocus.current = false; trigger.current?.focus(); } }, [confirming]);
  return (
    <Page title="Access & privacy" subtitle="What OpenClicky may do without asking, and what it remembers.">
      <Group title="How much it may do">
        {/* Stays on the host's value: the host may show a warning and refuse. */}
        <RadioList name="access-mode" label="Access mode" options={MODES} value={a.mode} onChange={(m) => patch("access.mode", m)}
          commitOnArrow={(m) => m === "preview" || m === "confirmEach"} />
      </Group>
      <Group title="Protected windows">
        <div className="settings-field">
          <label className="settings-field__label" htmlFor="settings-denylist">Protected window titles</label>
          <span className="settings-field__hint">One per line. OpenClicky will not look at or act in windows whose title contains one of these, on any display. A display showing one is left out of what Talk sends.</span>
          <textarea id="settings-denylist" className="settings-input settings-input--area" rows={5} maxLength={2000} spellCheck={false}
            value={deny} onChange={(e) => setDeny(e.target.value)}
            onBlur={() => { if (deny !== a.denyList) patch("access.denyList", deny); }} />
        </div>
      </Group>
      <Group title="Memory">
        <Row title="Remember conversations" description="Keep past chats so OpenClicky can pick up where you left off.">
          <Toggle label="Remember conversations" checked={a.rememberHistory} onChange={(v) => patch("access.rememberHistory", v)} />
        </Row>
        {confirming ? (
          <ConfirmRow title="Clear everything OpenClicky remembers from your conversations?" keepLabel="Cancel" confirmLabel="Clear"
            onKeep={() => { refocus.current = true; setConfirming(false); }}
            onConfirm={() => { setConfirming(false); act("clearHistory"); }} />
        ) : (
          <Row title="Clear conversation history" description="This cannot be undone." tone="danger">
            <PillButton variant="danger" ref={trigger} onClick={() => setConfirming(true)}>Clear history</PillButton>
          </Row>
        )}
      </Group>
    </Page>
  );
}
