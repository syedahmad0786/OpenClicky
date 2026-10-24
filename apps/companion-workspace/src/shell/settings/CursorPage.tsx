import type { ShellState } from "../contract";
import { ACCENTS, Group, Row, Tile, Toggle } from "../ui";
import { Page, patch } from "./common";

function GlowingCursor({ colour }: { colour: string }) {
  return (
    <svg className="settings-cursor-icon" width="30" height="30" viewBox="0 0 24 24" aria-hidden="true" focusable="false"
      style={{ filter: `drop-shadow(0 0 5px ${colour}) drop-shadow(0 0 10px ${colour})` }}>
      <path d="M12 3l8 17-8-4-8 4z" fill={colour} transform="rotate(-24 12 12)" />
    </svg>
  );
}

const TOGGLES = [
  { key: "hidden", title: "Hide floating cursor", description: "Keep the companion cursor out of sight." },
  { key: "screenHighlight", title: "Screen highlight", description: "Glow around what OpenClicky is looking at." },
  { key: "shakeToDetach", title: "Shake to detach", description: "Shake your mouse to pull the cursor loose." },
] as const;

export function CursorPage({ state }: { state: ShellState }) {
  const c = state.settings.cursor;
  return (
    <Page title="Cursor" subtitle="The companion cursor that follows you.">
      <Group title="Colour" className="settings-tiles">
        <div role="group" aria-label="Colour" className="settings-grid settings-grid--4">
          {ACCENTS.map((a) => (
            <Tile key={a.hex} icon={<GlowingCursor colour={a.hex} />} name={a.name} descriptor={a.hex === ACCENTS[0].hex ? "Default" : undefined}
              selected={a.hex.toLowerCase() === c.colour.toLowerCase()} onClick={() => patch("cursor.colour", a.hex)} />
          ))}
        </div>
      </Group>
      <Group title="Visibility">
        {TOGGLES.map((t) => (
          <Row key={t.key} title={t.title} description={t.description}>
            <Toggle label={t.title} checked={c[t.key]} onChange={(value) => patch(`cursor.${t.key}`, value)} />
          </Row>
        ))}
      </Group>
    </Page>
  );
}
