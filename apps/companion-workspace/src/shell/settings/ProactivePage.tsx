import type { ShellState } from "../contract";
import { Group, Row, Toggle } from "../ui";
import { Page, patch } from "./common";

type Flag = "morningBrief" | "suggestions" | "integrationNudges" | "meetingCountdown";

const CARDS: { flag: Flag; title: string; description: string }[] = [
  {
    flag: "morningBrief", title: "Morning brief",
    description: "Once between 7 am and noon: today's meetings, jobs that need you and routines due. Built on this PC. Plan my day sends the brief to your model provider.",
  },
  {
    flag: "suggestions", title: "Suggestions",
    description: "Once a day, sends the titles of your recent jobs and routines and the names of your connectors to your model provider, which suggests up to three tasks. Nothing runs until you click Do it.",
  },
  {
    flag: "integrationNudges", title: "Connect nudges",
    description: "After 10 seconds in an app you haven't connected, offers to connect it, at most three times a day. Reads the app name and window title on this PC only.",
  },
  {
    flag: "meetingCountdown", title: "Meeting countdown",
    description: "A card with a Join button 75 seconds before each meeting. Reads your calendar through the connector below. Nothing goes to your model provider.",
  },
];

export function ProactivePage({ state }: { state: ShellState }) {
  const p = state.settings.proactive;
  const connectors = state.settings.integrations.connectors;
  // A stored key that is no longer listed would leave the select without a matching option, so it shows as None.
  const selected = connectors.some((c) => c.key === p.calendarConnector) ? p.calendarConnector! : "";
  return (
    <Page title="Proactive" subtitle="Cards OpenClicky shows without being asked. Each one stays off until you turn it on.">
      <Group title="Cards">
        {CARDS.map((c) => (
          <Row key={c.flag} title={c.title} description={c.description}>
            <Toggle label={c.title} checked={p[c.flag]} onChange={(v) => patch(`proactive.${c.flag}`, v)} />
          </Row>
        ))}
      </Group>
      <Group title="Calendar">
        <Row title="Calendar from" description="The connector OpenClicky asks for today's events. Add one in Integrations.">
          <select className="settings-input" aria-label="Calendar from" value={selected}
            onChange={(e) => patch("proactive.calendarConnector", e.target.value)}>
            <option value="">None</option>
            {connectors.map((c) => <option key={c.key} value={c.key}>{c.name}</option>)}
          </select>
        </Row>
      </Group>
      <p className="settings-page__subtitle">Proactive cards wait while you're on a call, presenting or in a full-screen app.</p>
    </Page>
  );
}
