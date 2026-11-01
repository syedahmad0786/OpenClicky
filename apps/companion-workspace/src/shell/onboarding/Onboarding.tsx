import type { ShellState } from "../contract";
import { Badge, PillButton } from "../ui";
import { stepView, tourAction } from "./steps";
import "./onboarding.css";

/**
 * The first-run tour card under the notch. The host owns the flow (step, done, busy); the page only shows the
 * step and posts `onboarding` actions. While a demo runs (busy) Back and Continue wait, but Skip always works.
 */
export function Onboarding({ state }: { state: ShellState }) {
  const tour = state.onboarding;
  if (!tour) return null;
  const view = stepView(state, tour);
  const n = tour.index + 1;
  const finale = tour.step === "finale";
  return (
    <section className="onboarding" data-region role="dialog" aria-label="OpenClicky tour">
      <div className="onboarding__dots" role="progressbar" aria-valuemin={1} aria-valuenow={n} aria-valuemax={tour.count} aria-label={`Step ${n} of ${tour.count}`}>
        {Array.from({ length: tour.count }, (_, i) => <i key={i} className={i === tour.index ? "is-lit" : undefined} />)}
      </div>
      {view.badge ? <Badge tone="muted">{view.badge}</Badge> : null}
      <h2 className="onboarding__title">{view.title}</h2>
      <p className="onboarding__body">{view.body}</p>
      {view.extra}
      <div className="onboarding__footer">
        {tour.step !== "welcome" ? <PillButton size="sm" disabled={tour.busy} onClick={() => tourAction("back")}>Back</PillButton> : null}
        <span className="onboarding__spacer" />
        {finale ? <PillButton size="sm" variant="primary" onClick={() => tourAction("finish")}>Show me Home</PillButton> : (
          <>
            <PillButton size="sm" variant="outline" className="onboarding__skip" onClick={() => tourAction("skip")}>Skip tour</PillButton>
            <PillButton size="sm" variant="primary" disabled={tour.busy} onClick={() => tourAction("next")}>Continue</PillButton>
          </>
        )}
      </div>
    </section>
  );
}
