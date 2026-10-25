import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import { postPage } from "../bridge";
import { talkGesture, type JobCard, type ShellState } from "../contract";
import { STOCK } from "../deck/JobCardView";
import { Badge, CloseIcon, KeyCaps, PaperCard, PillButton } from "../ui";

/** Our own copy. 5-11 morning, 12-16 afternoon, 17-21 evening, otherwise neutral. */
export function greetingFor(hour: number, name: string): string {
  const hello = hour >= 5 && hour < 12 ? "Good morning" : hour >= 12 && hour < 17 ? "Good afternoon" : hour >= 17 && hour < 22 ? "Good evening" : "Hi there";
  const who = name.trim();
  return who ? `${hello}, ${who}.` : hello;
}

export function statusLine(working: number, needYou: number): string {
  const parts = [working > 0 && `${working} working`, needYou > 0 && `${needYou} need you`].filter(Boolean);
  return parts.length ? parts.join(" · ") : "Nothing running";
}

const PRIMARY_LABEL: Record<JobCard["state"], string> = {
  queued: "Watch", working: "Watch", needsYou: "Review", done: "View result", failed: "See what happened", stopped: "Open", new: "Open",
};
const running = (job: JobCard) => job.state === "queued" || job.state === "working";

/** The job's checklist, read-only: how many steps are done, then each step with a tick. */
function PlanChecklist({ plan }: { plan: JobCard["plan"] }) {
  const done = plan.filter((step) => step.done).length;
  return (
    <div className="notch-job__plan" role="group" aria-label="Plan">
      <p className="notch-job__plan-count">{done} of {plan.length} steps</p>
      {plan.map((step, i) => (
        <div key={i} role="checkbox" aria-checked={step.done} aria-readonly="true" data-done={step.done} className="notch-job__step">
          <span className="notch-job__tick" aria-hidden="true">{step.done ? "✓" : ""}</span>
          <span className="notch-job__step-text">{step.text}</span>
        </div>
      ))}
    </div>
  );
}

const MAX_JOB_TEXT = 2_000; // host limit for job.steer / job.followUp

/** Tells a running or queued job something (job.steer), or asks a finished one a follow-up (job.followUp). Enter sends. */
function JobInput({ id, kind }: { id: string; kind: "job.steer" | "job.followUp" }) {
  const [draft, setDraft] = useState("");
  const focused = useRef(false);
  // A field that unmounts while focused fires no blur: hand the keyboard focus back here.
  useEffect(() => () => { if (focused.current) postPage({ kind: "surface.focus", wanted: false }); }, []);
  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key === "Enter" && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      const text = draft.trim();
      if (!text) return;
      postPage({ kind, id, text });
      setDraft("");
    } else if (event.key === "Escape") {
      event.currentTarget.blur(); // onBlur gives the focus back
    }
  };
  const placeholder = kind === "job.steer" ? "Tell this job something…" : "Ask a follow-up…";
  return (
    <textarea
      className="notch-job__input" rows={1} maxLength={MAX_JOB_TEXT} placeholder={placeholder} aria-label={placeholder}
      value={draft} onChange={(e) => setDraft(e.target.value)} onKeyDown={onKeyDown}
      onPointerDown={() => postPage({ kind: "surface.focus", wanted: true })}
      onFocus={() => { focused.current = true; postPage({ kind: "surface.focus", wanted: true }); }}
      onBlur={() => { focused.current = false; postPage({ kind: "surface.focus", wanted: false }); }}
    />
  );
}

function FeaturedJob({ job }: { job: JobCard }) {
  const dismissible = !running(job) && job.state !== "needsYou";
  return (
    <PaperCard stock={STOCK[job.colour]} title={job.title} className="notch-job">
      {job.state === "new" ? <span className="notch-job__tag"><Badge tone="muted">New</Badge></span> : null}
      {dismissible ? (
        <button type="button" className="notch-job__close" aria-label="Dismiss" onClick={() => postPage({ kind: "job.dismiss", id: job.id })}><CloseIcon /></button>
      ) : null}
      {job.summary ? <p className="notch-job__summary">{job.summary}</p> : null}
      {job.stat ? <p className="notch-job__stat"><strong>{job.stat.value}</strong> {job.stat.label}</p> : null}
      {job.plan.length ? <PlanChecklist plan={job.plan} /> : null}
      {(job.state === "working" || job.state === "queued") && job.liveLine ? <p className="notch-job__live">{job.liveLine}</p> : null}
      {job.bullets.length ? <ul className="notch-job__bullets">{job.bullets.slice(0, 4).map((b, i) => <li key={i}>{b}</li>)}</ul> : null}
      {job.next.slice(0, 3).map((step, i) => <p key={i} className="notch-job__next">{step}</p>)}
      {job.files.length ? (
        <div className="notch-job__files">
          {job.files.slice(0, 6).map((file, i) => (
            <span key={`${file.path}-${i}`} className="notch-job__file">
              <span className="notch-job__filename" title={file.name}>{file.name}</span>
              <PillButton size="sm" variant="outline" aria-label={`Open ${file.name}`} onClick={() => postPage({ kind: "file.open", path: file.path })}>Open</PillButton>
            </span>
          ))}
        </div>
      ) : null}
      {running(job) ? <JobInput key={`steer-${job.id}`} id={job.id} kind="job.steer" />
        : job.state === "done" ? <JobInput key={`followUp-${job.id}`} id={job.id} kind="job.followUp" /> : null}
      <div className="notch-job__actions">
        <PillButton variant="primary" size="sm" onClick={() => postPage({ kind: "job.open", id: job.id })}>{PRIMARY_LABEL[job.state]}</PillButton>
        {running(job) ? <PillButton variant="outline" size="sm" onClick={() => postPage({ kind: "job.stop", id: job.id })}>Stop</PillButton> : null}
        {job.state === "failed" || job.state === "stopped"
          ? <PillButton variant="outline" size="sm" onClick={() => postPage({ kind: "job.retry", id: job.id })}>Try again</PillButton> : null}
      </div>
    </PaperCard>
  );
}

export function HomeTab({ state, onOpenSettings }: { state: ShellState; onOpenSettings: () => void }) {
  const apps = state.apps.connected.length;
  const talk = state.hotkeys.talk;
  return (
    <>
      <div className="notch-body">
        <h2 className="notch-home__greeting">{greetingFor(new Date().getHours(), state.user.displayName)}</h2>
        <p className="notch-home__hint">
          {talk.length ? <>{talkGesture(state)} <KeyCaps keys={talk} /> and ask for anything</> : "Ask for anything"}
        </p>
        {state.featured ? <FeaturedJob job={state.featured} /> : null}
      </div>
      <footer className="notch-home__footer">
        <span className="notch-home__status">{statusLine(state.status.jobsWorking, state.status.jobsNeedYou)}</span>
        <PillButton size="sm" variant="outline" onClick={onOpenSettings}>{apps > 0 ? `${apps} ${apps === 1 ? "app" : "apps"} +` : "Add apps +"}</PillButton>
      </footer>
    </>
  );
}
