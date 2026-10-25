import type { CSSProperties } from "react";
import { postPage } from "../bridge";
import type { JobCard } from "../contract";
import type { PaperStock } from "../ui";

/** Paper colour for a job's colour tag. Shared with the featured card in Home. */
export const STOCK: Record<JobCard["colour"], PaperStock> = { red: "blush", blue: "sky", yellow: "butter", green: "mint" };

type Tone = "accent" | "warn" | "plain";
const LABEL: Record<JobCard["state"], { text: string; tone: Tone }> = {
  queued: { text: "Queued", tone: "plain" },
  working: { text: "Working…", tone: "plain" },
  needsYou: { text: "Needs you", tone: "warn" },
  done: { text: "Ready to view", tone: "accent" },
  new: { text: "Ready to view", tone: "accent" },
  failed: { text: "Didn’t finish", tone: "plain" },
  stopped: { text: "Stopped", tone: "plain" },
};

export interface JobCardViewProps {
  job: JobCard;
  /** Degrees; the sign picks the lean. */
  tilt: number;
  /** Stacking order within a side: the card nearest the panel sits on top. */
  layer: number;
}

/** A small tilted paper card for the deck beside the open panel. The whole card is one button (and one region). */
export function JobCardView({ job, tilt, layer }: JobCardViewProps) {
  const { text, tone } = LABEL[job.state];
  const done = job.plan.filter((step) => step.done).length;
  return (
    <button
      type="button" data-region data-colour={job.colour} className={`oc-paper oc-paper--${STOCK[job.colour]} deck-card`}
      style={{ "--tilt": `${tilt}deg`, zIndex: layer } as CSSProperties}
      onClick={() => postPage({ kind: "job.open", id: job.id })}
    >
      <span className="oc-paper__handle" aria-hidden="true" />
      <span className="deck-card__stripe" aria-hidden="true" />
      <span className="deck-card__title">{job.title}</span>
      <span className="deck-card__state" data-tone={tone}>{text}</span>
      {job.plan.length ? (
        <span className="deck-card__progress" role="progressbar" aria-label="Progress" aria-valuemin={0} aria-valuemax={job.plan.length} aria-valuenow={done}>
          <span style={{ width: `${Math.round((done / job.plan.length) * 100)}%` }} />
        </span>
      ) : null}
      {job.state === "working" && job.liveLine ? <span className="deck-card__live">{job.liveLine}</span> : null}
    </button>
  );
}
