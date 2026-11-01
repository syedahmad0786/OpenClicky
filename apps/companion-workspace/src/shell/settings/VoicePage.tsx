import type { ReactNode } from "react";
import type { ShellState } from "../contract";
import { Chip, Group, PillButton, RadioList, Row, Tile, Toggle } from "../ui";
import { act, Page, patch } from "./common";

const SPEEDS = [0.5, 0.75, 1, 1.25, 1.5];

/** One simple outline per voice (24x24, stroke only). Unknown ids (Windows voices) get a speaker. */
const GLYPHS: Record<string, ReactNode> = {
  "windows-default": <><rect x="3" y="5" width="18" height="12" rx="2" /><path d="M9 21h6M12 17v4" /></>,
  alloy: <><circle cx="12" cy="12" r="8" /><circle cx="12" cy="12" r="3" /></>,
  ash: <path d="M12 3l8 16H4z" />,
  ballad: <path d="M3 12c3-6 6-6 9 0s6 6 9 0" />,
  cedar: <><path d="M12 3l6 7h-3l4 6H5l4-6H6z" /><path d="M12 16v5" /></>,
  coral: <><circle cx="8" cy="14" r="4" /><circle cx="16" cy="14" r="4" /><circle cx="12" cy="8" r="4" /></>,
  echo: <path d="M7 9a4 4 0 0 1 0 6M11 6a8 8 0 0 1 0 12M15 3a12 12 0 0 1 0 18" />,
  marin: <path d="M3 10c3-3 6-3 9 0s6 3 9 0M3 16c3-3 6-3 9 0s6 3 9 0" />,
  sage: <path d="M5 19C5 9 11 4 20 4c0 9-5 15-15 15zM5 19l8-8" />,
  shimmer: <path d="M12 3l2 7 7 2-7 2-2 7-2-7-7-2 7-2z" />,
  verse: <path d="M5 7h14M5 12h10M5 17h12" />,
};
const SPEAKER = <path d="M4 10v4h4l5 4V6L8 10zM16 9a4 4 0 0 1 0 6" />;

function VoiceGlyph({ id }: { id: string }) {
  return (
    <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      {GLYPHS[id] ?? SPEAKER}
    </svg>
  );
}

export function VoicePage({ state }: { state: ShellState }) {
  const v = state.settings.voice;
  const choose = (id: string) => { act("previewVoice", id); patch("voice.voice", id); };
  const needsKey = v.voices.some((voice) => !voice.available);
  return (
    <Page title="Voice" subtitle="How OpenClicky sounds when it talks back.">
      <Group title="Speech speed">
        <div className="settings-chips">
          {SPEEDS.map((n) => <Chip key={n} selected={Math.abs(v.speed - n) < 1e-9} onClick={() => patch("voice.speed", n)}>{n}×</Chip>)}
        </div>
      </Group>
      <Group title="Voice" className="settings-tiles">
        <div role="group" aria-label="Voice" className="settings-grid">
          {v.voices.map((voice) => (
            <Tile key={voice.id} icon={<VoiceGlyph id={voice.id} />} name={voice.name} descriptor={voice.available ? voice.detail : "Needs your OpenAI key"}
              selected={voice.id === v.voice} className={voice.available ? undefined : "is-unavailable"}
              onClick={() => (voice.available ? choose(voice.id) : act("secret.edit", "openai"))} />
          ))}
        </div>
        <Row title="OpenAI key" description="Used only for OpenAI voices. With one selected, the text of each reply sentence (and the voice preview) is sent to api.openai.com to be spoken; the audio is not kept. Windows keeps the key in Credential Manager.">
          <PillButton onClick={() => act("secret.edit", "openai")}>{needsKey ? "Add key" : "Change"}</PillButton>
        </Row>
      </Group>
      <Group title="Captions">
        <Row title="Captions" description="Show the words on screen as OpenClicky speaks.">
          <Toggle label="Captions" checked={v.captions} onChange={(value) => patch("voice.captions", value)} />
        </Row>
        <Row title="Read replies aloud" description="Speak answers instead of only showing them.">
          <Toggle label="Read replies aloud" checked={v.readReplies} onChange={(value) => patch("voice.readReplies", value)} />
        </Row>
      </Group>
      <Group title="Output device">
        <RadioList name="output-device" label="Output device" value={v.outputDeviceId ?? ""} onChange={(id) => patch("voice.outputDeviceId", id === "" ? null : id)}
          options={[{ value: "", label: "System default" },
            ...v.outputDevices.map((d) => ({ value: d.id, label: d.name, description: d.state === "unavailable" ? "Not connected" : undefined, disabled: d.state === "unavailable" }))]} />
        <Row title="Play a test sound" description="Hear it on the selected device.">
          <PillButton onClick={() => act("playTestSound")}>Play a test sound</PillButton>
        </Row>
      </Group>
    </Page>
  );
}
