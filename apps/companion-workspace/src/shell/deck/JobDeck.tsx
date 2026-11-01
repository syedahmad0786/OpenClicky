import { useState } from "react";
import type { JobCard } from "../contract";
import { PillButton } from "../ui";
import { JobCardView } from "./JobCardView";
import "./deck.css";

export const VISIBLE_PER_SIDE = 3;
export const EXPANDED_PER_SIDE = 8;
/** Degrees, alternating lean; the right side mirrors the left. */
const TILTS = [-4, 3, -5, 4, -3, 5, -4, 3];

function Side({ side, jobs }: { side: "left" | "right"; jobs: JobCard[] }) {
  const [expanded, setExpanded] = useState(false);
  if (!jobs.length) return null;
  const shown = jobs.slice(0, expanded ? EXPANDED_PER_SIDE : VISIBLE_PER_SIDE);
  const hidden = jobs.length - VISIBLE_PER_SIDE;
  const sign = side === "left" ? 1 : -1;
  return (
    <div className={`notch-deck notch-deck--${side}`} data-expanded={expanded}>
      {shown.map((job, i) => <JobCardView key={job.id} job={job} tilt={sign * TILTS[i]} layer={shown.length - i} />)}
      {hidden > 0 ? (
        <PillButton
          size="sm" data-region className="deck-more" aria-expanded={expanded}
          title={expanded ? "Show fewer jobs" : `Show ${hidden} more ${hidden === 1 ? "job" : "jobs"}`}
          onClick={() => setExpanded(!expanded)}
        >
          {expanded ? "Show fewer" : `+${hidden} more`}
        </PillButton>
      ) : null}
    </div>
  );
}

/**
 * Jobs other than the featured one (host order, newest first), as paper cards on both sides of the open panel. Dealt
 * alternately left and right so the newest sits nearest the panel on its side. Beyond three a side shows a
 * "+N more" pill; expanding shows up to eight per side (ponytail: no paging for the rest).
 */
export function JobDeck({ jobs, featuredId }: { jobs: JobCard[]; featuredId: string | null }) {
  const rest = jobs.filter((job) => job.id !== featuredId); // the host lists jobs newest first
  return (
    <>
      <Side side="left" jobs={rest.filter((_, i) => i % 2 === 0)} />
      <Side side="right" jobs={rest.filter((_, i) => i % 2 === 1)} />
    </>
  );
}
