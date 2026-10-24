import { postPage } from "../bridge";
import type { GeneralKey, ShellState } from "../contract";
import { Group, PillButton, Row, Toggle } from "../ui";
import { Page } from "./common";

const BEHAVIOUR: { key: GeneralKey; title: string; description: string }[] = [
  { key: "showInTaskbar", title: "Show in taskbar", description: "Keep an OpenClicky button on the taskbar." },
  { key: "showInRecordings", title: "Show in screen recordings", description: "Let screen recorders and screen sharing capture the notch and cursor." },
  { key: "launchAtLogin", title: "Launch at login", description: "Start OpenClicky when you sign in to Windows." },
  { key: "paused", title: "Pause OpenClicky", description: "Stops listening and acting until you turn this off." },
];

function UpdateButton({ update }: { update: ShellState["update"] }) {
  switch (update.state) {
    case "available": return <PillButton variant="primary" onClick={() => postPage({ kind: "update.install" })}>Download v{update.available}</PillButton>;
    case "downloading": return <PillButton disabled>Downloading {Math.round(update.progress * 100)}%</PillButton>;
    case "ready": return <PillButton variant="primary" onClick={() => postPage({ kind: "update.install" })}>Downloaded — open folder</PillButton>;
    case "checking": return <PillButton disabled>Checking…</PillButton>;
    case "error": return <PillButton onClick={() => postPage({ kind: "update.check" })}>Couldn't check — try again</PillButton>;
    default: return <PillButton onClick={() => postPage({ kind: "update.check" })}>Check for updates</PillButton>;
  }
}

export function GeneralPage({ state }: { state: ShellState }) {
  const { update } = state;
  return (
    <Page title="General" subtitle="How OpenClicky behaves on this PC.">
      <Group title="Behaviour">
        {BEHAVIOUR.map((b) => (
          <Row key={b.key} title={b.title} description={b.description}>
            <Toggle label={b.title} checked={state.general[b.key]} onChange={(value) => postPage({ kind: "general.set", key: b.key, value })} />
          </Row>
        ))}
      </Group>
      <Group title="Updates">
        <Row title={`OpenClicky ${update.current}`} description={update.state === "upToDate" ? "You're up to date." : update.available ? `Version ${update.available} is available.` : undefined}>
          <UpdateButton update={update} />
        </Row>
        <Row title="What's new" description="Read the release notes for this version.">
          <PillButton onClick={() => postPage({ kind: "link.open", target: "whatsNew" })}>What's new</PillButton>
        </Row>
      </Group>
      <Group title="Help">
        <Row title="Report a bug" description="Tell us what went wrong."><PillButton onClick={() => postPage({ kind: "link.open", target: "bug" })}>Report a bug</PillButton></Row>
        <Row title="Request a feature" description="Tell us what you would like next."><PillButton onClick={() => postPage({ kind: "link.open", target: "feature" })}>Request a feature</PillButton></Row>
      </Group>
    </Page>
  );
}
