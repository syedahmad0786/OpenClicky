import { useCallback, useEffect, useRef, useState, type FocusEvent } from "react";
import { postPage } from "../bridge";
import { NotchCards } from "../cards/NotchCards";
import type { NotchTab, ShellState } from "../contract";
import { JobDeck } from "../deck/JobDeck";
import { cx } from "../ui/cx";
import { Peek } from "./Peek";
import "./notch.css";

/** Timing from the design spec (milliseconds). The same numbers are in notch.css. */
export const NOTCH_TIMING = { graceMs: 150, closeMs: 300, reducedMs: 120 } as const;
/** A panel the host opened (tray, second instance) closes after this long if the pointer never enters it. */
export const HOST_OPEN_IDLE_MS = 6_000;

type Phase = "collapsed" | "open" | "closing";

/**
 * Collapsed pill that opens on hover (or when the host says so). While open or closing, the panel keeps
 * its final layout size, so the region the host sees is always the larger of start/end bounds; the
 * animation itself is a clip-path inside it (see notch.css). Closing relies on the grace period only:
 * there is no invisible forgiveness band, because every reported region swallows clicks.
 */
export function Notch({ state }: { state: ShellState }) {
  const [phase, setPhaseState] = useState<Phase>(state.notch.open ? "open" : "collapsed");
  const [tab, setTab] = useState<NotchTab>(state.notch.tab);
  const [draft, setDraft] = useState(""); // lives here so it survives tab switches and collapsing
  const root = useRef<HTMLDivElement>(null);
  const phaseRef = useRef(phase);
  const grace = useRef<number>(undefined);
  const finish = useRef<number>(undefined);
  const idle = useRef<number>(undefined);
  const inside = useRef(false);
  const closeMs = state.reducedMotion ? NOTCH_TIMING.reducedMs : NOTCH_TIMING.closeMs;

  const setPhase = (next: Phase) => { phaseRef.current = next; setPhaseState(next); };
  const cancelClose = () => {
    window.clearTimeout(grace.current); grace.current = undefined;
    window.clearTimeout(idle.current); idle.current = undefined;
  };
  /** Read live from the document, so it cannot go stale when the focused field unmounts. */
  const composerFocused = () => document.activeElement instanceof HTMLTextAreaElement && !!root.current?.contains(document.activeElement);

  const openPanel = (echo: boolean) => {
    cancelClose();
    window.clearTimeout(finish.current);
    if (phaseRef.current === "open") return;
    setPhase("open");
    if (echo) postPage({ kind: "notch.open", open: true });
  };
  const closePanel = (echo: boolean) => {
    cancelClose();
    if (phaseRef.current !== "open") return;
    setPhase("closing");
    if (composerFocused()) (document.activeElement as HTMLElement).blur(); // its onBlur hands the keyboard focus back
    if (echo) postPage({ kind: "notch.open", open: false });
    finish.current = window.setTimeout(() => setPhase("collapsed"), closeMs);
  };
  const scheduleClose = () => {
    if (composerFocused() || phaseRef.current !== "open") return;
    cancelClose();
    grace.current = window.setTimeout(() => closePanel(true), NOTCH_TIMING.graceMs);
  };

  // Host-driven open/close (hotkey, tray, job events). Not echoed back.
  const hostOpen = state.notch.open;
  const hostHandlers = useRef({ openPanel, closePanel });
  hostHandlers.current = { openPanel, closePanel };
  useEffect(() => {
    if (!hostOpen) { hostHandlers.current.closePanel(false); return; }
    hostHandlers.current.openPanel(false);
    if (inside.current) return;
    // Nobody is pointing at it: do not leave an open panel (and its click-eating region) up forever.
    idle.current = window.setTimeout(() => {
      idle.current = undefined;
      if (!inside.current && !composerFocused()) hostHandlers.current.closePanel(true);
    }, HOST_OPEN_IDLE_MS);
  }, [hostOpen]);
  useEffect(() => setTab(state.notch.tab), [state.notch.tab]);
  useEffect(() => () => { window.clearTimeout(grace.current); window.clearTimeout(finish.current); window.clearTimeout(idle.current); }, []);

  const changeTab = useCallback((next: NotchTab) => { setTab(next); postPage({ kind: "notch.tab", tab: next }); }, []);

  // Hovering the cards counts as inside the open panel (no auto-close), but never opens a collapsed one.
  const onCardsEnter = () => { if (phaseRef.current !== "open") return; inside.current = true; cancelClose(); };
  const onCardsLeave = () => { inside.current = false; scheduleClose(); };

  const onFocus = (e: FocusEvent) => { if (e.target instanceof HTMLTextAreaElement) cancelClose(); };
  const onBlur = (e: FocusEvent) => { if (e.target instanceof HTMLTextAreaElement && !inside.current) scheduleClose(); };

  const expanded = phase !== "collapsed";
  return (
    // The shell stacks the notch and its cards; the deck hangs off the notch itself so hovering a deck card keeps the panel open.
    <div className="notch-shell" data-phase={phase}>
      <div
        ref={root} role="region" aria-label="OpenClicky" data-tab={tab} data-reduced={state.reducedMotion}
        className={cx("notch", phase === "collapsed" ? "notch--collapsed" : phase === "open" ? "notch--open" : "notch--closing", expanded && tab === "settings" && "notch--settings")}
        onPointerEnter={() => { inside.current = true; openPanel(true); }}
        onPointerLeave={() => { inside.current = false; scheduleClose(); }}
        onFocus={onFocus} onBlur={onBlur}
      >
        {/* The panel carries the corner radii, so it is the reported region. */}
        <div className="notch__panel" data-region>
          {expanded ? <Peek state={state} tab={tab} onTab={changeTab} draft={draft} onDraft={setDraft} /> : null}
        </div>
        {phase === "open" && tab === "home" ? <JobDeck jobs={state.jobs} featuredId={state.featured?.id ?? null} /> : null}
      </div>
      <NotchCards cards={state.cards} active={phase !== "closing"} onPointerEnter={onCardsEnter} onPointerLeave={onCardsLeave} />
    </div>
  );
}
