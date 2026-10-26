import { useEffect, useState } from "react";
import { listenToHost } from "../bridge";
import type { ShellState } from "../contract";
import { Badge, Group, Meter, PillButton, RadioList, Row, Toggle } from "../ui";
import { act, Page, patch } from "./common";

const BARS = 16;
const HOLD_MS = 1_000;

/** Latest `mic.level` from the host; drops to 0 after a second without an update. */
export function useMicLevel() {
  const [level, setLevel] = useState(0);
  useEffect(() => {
    let timer: ReturnType<typeof setTimeout> | undefined;
    const stop = listenToHost((message) => {
      if (message.kind !== "mic.level") return;
      setLevel(message.level);
      clearTimeout(timer);
      timer = setTimeout(() => setLevel(0), HOLD_MS);
    });
    return () => { stop(); clearTimeout(timer); };
  }, []);
  return level;
}

export function MicrophonePage({ state }: { state: ShellState }) {
  const m = state.settings.microphone;
  const lit = Math.min(BARS, Math.ceil(useMicLevel() * BARS)); // any sound lights at least one bar
  return (
    <Page title="Microphone" subtitle="Which microphone OpenClicky listens to.">
      <Group title="Input device">
        <RadioList name="mic-device" label="Input device" value={m.deviceId ?? ""} onChange={(id) => patch("microphone.deviceId", id === "" ? null : id)}
          options={[{ value: "", label: "System default" },
            ...m.devices.map((d) => ({ value: d.id, label: d.name, description: d.state === "unavailable" ? "Not connected" : undefined, disabled: d.state === "unavailable" }))]} />
      </Group>
      <Group title="Test">
        <Row title="Test microphone" description="Say something and watch the bars move.">
          <PillButton onClick={() => act("testMic")}>Test microphone</PillButton>
        </Row>
        <div className="settings-meter"><Meter segments value={lit} max={BARS} label="Microphone level" /></div>
      </Group>
      <Group title="Local voice">
        <Row title="Local voice recognition" description="Understands you on this PC, even offline.">
          {m.whisper === "missing" ? <PillButton variant="primary" onClick={() => act("downloadWhisper")}>Download local voice (142 MB)</PillButton> : null}
          {m.whisper === "downloading" ? <Badge tone="muted">{`Downloading… ${Math.round(m.whisperProgress * 100)}%`}</Badge> : null}
          {m.whisper === "ready" ? <Badge>Ready</Badge> : null}
        </Row>
        {m.whisper === "downloading" ? <div className="settings-meter settings-meter--progress"><Meter value={m.whisperProgress} max={1} label="Download progress" /></div> : null}
        <Row title="More accurate English voice" description="Hears names and unusual words better when you talk or dictate in English. A 465 MB download that runs on this PC; replies can take a moment longer.">
          {m.accurate === "missing" ? <PillButton onClick={() => act("downloadWhisperAccurate")}>Download (465 MB)</PillButton> : null}
          {m.accurate === "downloading" ? <Badge tone="muted">{`Downloading… ${Math.round(m.accurateProgress * 100)}%`}</Badge> : null}
          {m.accurate === "ready"
            ? <Toggle label="Use the more accurate English voice" checked={m.useAccurate} onChange={(value) => patch("microphone.accurate", value)} /> : null}
        </Row>
        {m.accurate === "downloading"
          ? <div className="settings-meter settings-meter--progress"><Meter value={m.accurateProgress} max={1} label="Accurate voice download progress" /></div> : null}
      </Group>
    </Page>
  );
}
