import { useState } from "react";
import type { ShellState } from "../contract";
import { Badge, Group, KeyCaps, PillButton, Row } from "../ui";
import { act, Page } from "./common";

type Routine = ShellState["settings"]["routines"][number];

const STATUS = {
  scheduled: { tone: "ok", text: "Scheduled" },
  running: { tone: "ok", text: "Running" },
  failed: { tone: "danger", text: "Failed" },
  paused: { tone: "muted", text: "Paused" },
} as const;

const when = (iso: string) =>
  new Date(iso).toLocaleString(undefined, { weekday: "short", day: "numeric", month: "short", hour: "numeric", minute: "2-digit" });

function RoutineItem({ r, confirming, onConfirm }: { r: Routine; confirming: boolean; onConfirm: (on: boolean) => void }) {
  const paused = r.status === "paused";
  return (
    <div className="settings-item">
      <Row title={r.title}
        description={(
          <>
            <span>{r.schedule}</span>
            {r.next ? <span className="settings-line">Next {when(r.next)}</span> : null}
            {r.last ? <span className="settings-line">Last {when(r.last)}</span> : null}
          </>
        )}>
        <Badge tone={STATUS[r.status].tone}>{STATUS[r.status].text}</Badge>
      </Row>
      {confirming ? (
        <Row title="Delete this routine? Its past runs stay in the chat." tone="danger">
          <PillButton size="sm" onClick={() => onConfirm(false)}>Keep</PillButton>
          <PillButton size="sm" variant="danger" onClick={() => { onConfirm(false); act("routine.delete", r.id); }}>Delete routine</PillButton>
        </Row>
      ) : (
        <div className="settings-actions">
          <PillButton size="sm" aria-label={`Run ${r.title} now`} onClick={() => act("routine.run", r.id)}>Run now</PillButton>
          <PillButton size="sm" aria-label={`${paused ? "Resume" : "Pause"} ${r.title}`} onClick={() => act(paused ? "routine.resume" : "routine.pause", r.id)}>
            {paused ? "Resume" : "Pause"}
          </PillButton>
          <PillButton size="sm" variant="danger" aria-label={`Delete ${r.title}`} onClick={() => onConfirm(true)}>Delete</PillButton>
        </div>
      )}
    </div>
  );
}

export function RoutinesPage({ state }: { state: ShellState }) {
  const routines = state.settings.routines;
  const [deleting, setDeleting] = useState<string | null>(null);
  return (
    <Page title="Routines" subtitle="Things OpenClicky does on a schedule, on this PC.">
      {routines.length === 0 ? (
        <div className="settings-empty">
          <p>Hold <KeyCaps keys={state.hotkeys.talk} /> and tell OpenClicky what to do, and when.</p>
          <p className="settings-empty__example">“Every weekday at 8:30, summarise my unread email.”</p>
        </div>
      ) : (
        <Group>
          {routines.map((r) => (
            <RoutineItem key={r.id} r={r} confirming={deleting === r.id} onConfirm={(on) => setDeleting(on ? r.id : null)} />
          ))}
        </Group>
      )}
    </Page>
  );
}
