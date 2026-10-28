import { useRef, useState } from "react";
import { talkGesture, type ShellState } from "../contract";
import { Badge, Group, KeyCaps, PillButton, Row } from "../ui";
import { act, ConfirmRow, Page } from "./common";

type Routine = ShellState["settings"]["routines"][number];

const STATUS = {
  scheduled: { tone: "ok", text: "Scheduled" },
  running: { tone: "ok", text: "Running" },
  failed: { tone: "danger", text: "Failed" },
  paused: { tone: "muted", text: "Paused" },
  waiting: { tone: "warn", text: "Waiting" },
} as const;

/** null for a date the host sent that does not parse, so no "Next Invalid Date" line shows. */
const when = (iso: string | null) => {
  const date = iso ? new Date(iso) : null;
  return date && !Number.isNaN(date.getTime())
    ? date.toLocaleString(undefined, { weekday: "short", day: "numeric", month: "short", hour: "numeric", minute: "2-digit" }) : null;
};

function RoutineItem({ r, confirming, onConfirm }: { r: Routine; confirming: boolean; onConfirm: (on: boolean) => void }) {
  const paused = r.status === "paused";
  const trigger = useRef<HTMLButtonElement>(null);
  const next = when(r.next);
  const last = when(r.last);
  return (
    <div className="settings-item">
      <Row title={<span className="oc-ellipsis" title={r.title}>{r.title}</span>}
        description={(
          <>
            <span>{r.schedule}</span>
            {next ? <span className="settings-line">Next {next}</span> : null}
            {last ? <span className="settings-line">Last {last}</span> : null}
            {r.note ? <span className="settings-line">{r.note}</span> : null}
          </>
        )}>
        <Badge tone={STATUS[r.status].tone}>{STATUS[r.status].text}</Badge>
      </Row>
      <div className="settings-actions">
        <PillButton size="sm" aria-label={`Run ${r.title} now`} disabled={r.status === "running"} onClick={() => act("routine.run", r.id)}>Run now</PillButton>
        <PillButton size="sm" aria-label={`${paused ? "Resume" : "Pause"} ${r.title}`} onClick={() => act(paused ? "routine.resume" : "routine.pause", r.id)}>
          {paused ? "Resume" : "Pause"}
        </PillButton>
        <PillButton size="sm" variant="danger" ref={trigger} aria-label={`Delete ${r.title}`} onClick={() => onConfirm(true)}>Delete</PillButton>
      </div>
      {confirming ? (
        <ConfirmRow title="Delete this routine? Its past runs stay in the chat." confirmLabel="Delete routine"
          onKeep={() => { onConfirm(false); trigger.current?.focus(); }}
          onConfirm={() => { onConfirm(false); act("routine.delete", r.id); }} />
      ) : null}
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
          <p>{talkGesture(state)} <KeyCaps keys={state.hotkeys.talk} /> and tell OpenClicky what to do, and when.</p>
          <p className="settings-empty__example">“Every weekday at 8:30, summarise my unread email.”</p>
        </div>
      ) : (
        <Group>
          {routines.map((r) => (
            <RoutineItem key={r.id} r={r} confirming={deleting === r.id} onConfirm={(on) => setDeleting(on ? r.id : null)} />
          ))}
        </Group>
      )}
      <p className="settings-note">Routines run while your PC is awake and OpenClicky is open.</p>
    </Page>
  );
}
