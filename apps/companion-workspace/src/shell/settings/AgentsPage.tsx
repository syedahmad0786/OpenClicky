import type { ShellState } from "../contract";
import { Badge, Group, PillButton, Row, Toggle } from "../ui";
import { act, Page, patch } from "./common";

const MAX_SHOWN = 48;
/** "C:\Users\me\…\Agents": keeps the start and the last folder visible. */
export const middleEllipsis = (path: string) =>
  path.length <= MAX_SHOWN ? path : `${path.slice(0, 20)}…${path.slice(path.length - (MAX_SHOWN - 21))}`;

const REACH = {
  allowed: { tone: "ok", text: "Allowed" },
  denied: { tone: "danger", text: "Denied" },
  unknown: { tone: "muted", text: "Unknown" },
} as const;

export function AgentsPage({ state }: { state: ShellState }) {
  const a = state.settings.agents;
  return (
    <Page title="Agents" subtitle="Where agents work and what they announce.">
      <Group title="Workspace">
        <Row title="Agent folder" description={<span className="settings-path" title={a.folder}>{middleEllipsis(a.folder)}</span>}>
          <PillButton size="sm" onClick={() => act("chooseAgentFolder")}>Change</PillButton>
          {a.folderIsDefault ? null : <PillButton size="sm" variant="outline" onClick={() => act("resetAgentFolder")}>Reset</PillButton>}
        </Row>
      </Group>
      <Group title="Announcements">
        <Row title="Speak when a job starts or finishes" description="Speaks a short line when a job starts or finishes. Uses your selected voice — OpenAI voices send the line to OpenAI.">
          <Toggle label="Speak when a job starts or finishes" checked={a.speakAnnouncements} onChange={(v) => patch("agents.speakAnnouncements", v)} />
        </Row>
        <Row title="Show updates beside the cursor">
          <Toggle label="Show updates beside the cursor" checked={a.cursorUpdates} onChange={(v) => patch("agents.cursorUpdates", v)} />
        </Row>
      </Group>
      <Group title="What agents can reach">
        <div role="group" aria-label="What agents can reach">
          {a.reach.map((r) => (
            <button key={r.id} type="button" className="oc-row settings-reach" onClick={() => act("openPrivacySetting", r.id)}>
              <span className="oc-row__text"><span className="oc-row__title">{r.label}</span></span>
              <span className="oc-row__trailing"><Badge tone={REACH[r.state].tone}>{REACH[r.state].text}</Badge></span>
            </button>
          ))}
        </div>
      </Group>
    </Page>
  );
}
