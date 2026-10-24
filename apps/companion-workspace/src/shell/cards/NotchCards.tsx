import { useEffect, useRef, useState } from "react";
import { postPage } from "../bridge";
import type { NotchCard } from "../contract";
import { CloseIcon, PillButton, type PillVariant } from "../ui";
import "./cards.css";

export const MAX_VISIBLE_CARDS = 3;
const VARIANT: Record<NotchCard["actions"][number]["style"], PillVariant> = { primary: "primary", secondary: "secondary", danger: "danger" };

/** Pulsing dots for work in progress, a plain dot for everything else; the colour comes from data-kind. */
function Lead({ kind }: { kind: NotchCard["kind"] }) {
  return kind === "activity"
    ? <span className="notch-card__lead notch-card__dots" aria-hidden="true"><i /><i /><i /></span>
    : <span className="notch-card__lead notch-card__dot" aria-hidden="true" />;
}

function CardView({ card, onDismiss, active }: { card: NotchCard; onDismiss: (id: string) => void; active: boolean }) {
  const ms = card.autoDismissMs ?? 0;
  const timer = useRef<number | undefined>(undefined);
  const left = useRef(ms);
  const since = useRef(0);
  const holds = useRef(0); // pointer hover and keyboard focus both pause the countdown
  const focusHeld = useRef(false);
  const dismiss = useRef(onDismiss);
  dismiss.current = onDismiss;

  const start = () => {
    if (!ms) return;
    since.current = Date.now();
    timer.current = window.setTimeout(() => dismiss.current(card.id), Math.max(left.current, 0));
  };
  const hold = () => {
    if (holds.current++ > 0 || timer.current === undefined) return;
    window.clearTimeout(timer.current);
    timer.current = undefined;
    left.current -= Date.now() - since.current;
  };
  const release = () => { if (holds.current > 0 && --holds.current === 0 && ms) start(); };

  useEffect(() => {
    left.current = ms;
    holds.current = 0;
    focusHeld.current = false;
    start();
    return () => window.clearTimeout(timer.current);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- restart only when the card or its delay changes
  }, [card.id, ms]);

  return (
    <div
      className="notch-card" data-region={active || undefined} data-kind={card.kind} role="group" aria-label={card.title}
      onPointerEnter={hold} onPointerLeave={release}
      onFocus={(e) => {
        // Only keyboard focus pauses; a mouse click on a button also focuses it and must not freeze the countdown.
        let keyboard = false;
        try { keyboard = e.target.matches(":focus-visible"); } catch { /* selector unsupported: treat as mouse */ }
        if (keyboard && !focusHeld.current) { focusHeld.current = true; hold(); }
      }}
      onBlur={(e) => {
        if (focusHeld.current && !e.currentTarget.contains(e.relatedTarget as Node | null)) { focusHeld.current = false; release(); }
      }}
    >
      <Lead kind={card.kind} />
      <div className="notch-card__main">
        <p className="notch-card__title">{card.title}</p>
        {card.body ? <p className="notch-card__body">{card.body}</p> : null}
        {card.actions.length ? (
          <div className="notch-card__actions">
            {card.actions.slice(0, 3).map((action) => (
              <PillButton key={action.id} size="sm" variant={VARIANT[action.style]} onClick={() => postPage({ kind: "card.action", cardId: card.id, actionId: action.id })}>
                {action.label}
              </PillButton>
            ))}
          </div>
        ) : null}
      </div>
      <button type="button" className="notch-card__close" aria-label="Dismiss notification" onClick={() => onDismiss(card.id)}><CloseIcon /></button>
    </div>
  );
}

export interface NotchCardsProps {
  /** Newest first (index 0 is the newest); the first three are shown, newest on top. */
  cards: NotchCard[];
  /** False while the panel above is closing: the cards stay mounted (timers, dismissals) but report no region. */
  active?: boolean;
  onPointerEnter?: () => void;
  onPointerLeave?: () => void;
}

/** Cards stacked under the notch. Dismissing hides a card at once and tells the host, which drops it from the next state. */
export function NotchCards({ cards, active = true, onPointerEnter, onPointerLeave }: NotchCardsProps) {
  const [gone, setGone] = useState<ReadonlySet<string>>(new Set());
  const ids = cards.map((c) => c.id).join("\n");
  // Forget dismissals the host has processed, so a card that comes back with the same id shows again.
  useEffect(() => setGone((old) => {
    const live = new Set(ids.split("\n"));
    const kept = [...old].filter((id) => live.has(id));
    return kept.length === old.size ? old : new Set(kept);
  }), [ids]);

  const dismiss = (id: string) => { setGone((old) => new Set(old).add(id)); postPage({ kind: "card.dismiss", cardId: id }); };
  const visible = cards.filter((c) => !gone.has(c.id)).slice(0, MAX_VISIBLE_CARDS);

  // When the last card goes away under the pointer the wrapper unmounts without a pointerleave: report one.
  const hovered = useRef(false);
  const leave = useRef(onPointerLeave);
  leave.current = onPointerLeave;
  const empty = visible.length === 0;
  useEffect(() => { if (empty && hovered.current) { hovered.current = false; leave.current?.(); } }, [empty]);

  if (empty) return null;
  return (
    <div className="notch-cards" aria-live="polite"
      onPointerEnter={() => { hovered.current = true; onPointerEnter?.(); }}
      onPointerLeave={() => { hovered.current = false; onPointerLeave?.(); }}>
      {visible.map((card) => <CardView key={card.id} card={card} onDismiss={dismiss} active={active} />)}
    </div>
  );
}
