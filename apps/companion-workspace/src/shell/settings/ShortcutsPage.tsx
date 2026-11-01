import { useEffect, useState } from "react";
import type { ShellState } from "../contract";
import { Group, KeyCaps, PillButton, Row } from "../ui";
import { act, Page } from "./common";

type Action = "talk" | "text" | "dictate" | "handsFree";
const ROWS: { action: Action; title: string; description: string }[] = [
  { action: "talk", title: "Push to talk", description: "Hold the keys, speak, let go." },
  { action: "text", title: "Text mode", description: "Double-tap to type a request instead." },
  { action: "dictate", title: "Dictate", description: "Hold the keys to type what you say." },
  { action: "handsFree", title: "Hands-free dictation", description: "Double-tap to start and stop dictating." },
];
const RECORDING_MS = 7_000;

export function ShortcutsPage({ state }: { state: ShellState }) {
  const shortcuts = state.settings.shortcuts;
  const signature = (action: Action) => JSON.stringify(shortcuts[action]);
  // Recording ends when the host reports a different chord for that row, or after 7 s.
  const [recording, setRecording] = useState<{ action: Action; from: string } | null>(null);
  const active = recording && signature(recording.action) === recording.from ? recording.action : null;
  useEffect(() => {
    if (!recording) return;
    const timer = setTimeout(() => setRecording(null), RECORDING_MS);
    return () => clearTimeout(timer);
  }, [recording]);
  const [confirming, setConfirming] = useState(false);
  const change = (action: Action) => { setRecording({ action, from: signature(action) }); act("recordHotkey", action); };
  return (
    <Page title="Shortcuts" subtitle="Keys that call OpenClicky.">
      <Group title="Keys">
        {ROWS.map((r) => (
          <Row key={r.action} title={r.title} description={r.description}>
            {active === r.action ? <span className="settings-recording">Press your keys…</span> : (
              <>
                <KeyCaps keys={shortcuts[r.action].keys} />
                <span className="settings-trigger">{shortcuts[r.action].trigger === "hold" ? "hold" : "double-tap"}</span>
              </>
            )}
            <PillButton size="sm" aria-label={`Change ${r.title}`} onClick={() => change(r.action)}>Change</PillButton>
          </Row>
        ))}
      </Group>
      <Group>
        <Row title="Reset to defaults" description={confirming ? "Put all four shortcuts back the way they came?" : undefined}>
          {confirming ? (
            <span className="settings-confirm">
              <PillButton size="sm" onClick={() => setConfirming(false)}>Keep my shortcuts</PillButton>
              <PillButton size="sm" variant="danger" onClick={() => { setConfirming(false); act("resetHotkeys"); }}>Reset all shortcuts</PillButton>
            </span>
          ) : <PillButton size="sm" onClick={() => setConfirming(true)}>Reset to defaults</PillButton>}
        </Row>
      </Group>
    </Page>
  );
}
