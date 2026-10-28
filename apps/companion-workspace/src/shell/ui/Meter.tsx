export interface MeterProps {
  value: number;
  max: number;
  /** Accessible name. */
  label: string;
  /** Draw `max` discrete bars (the first `value` lit) instead of one continuous fill. */
  segments?: boolean;
}

export function Meter({ value, max, label, segments = false }: MeterProps) {
  const pct = max > 0 ? Math.min(100, Math.max(0, (value / max) * 100)) : 0;
  return (
    <div role="meter" aria-label={label} aria-valuemin={0} aria-valuemax={max} aria-valuenow={value} className={segments ? "oc-meter oc-meter--bars" : "oc-meter"}>
      {segments
        ? Array.from({ length: max }, (_, i) => <span key={i} className={i < value ? "oc-meter__bar is-lit" : "oc-meter__bar"} />)
        : <div className="oc-meter__fill" style={{ transform: `scaleX(${pct / 100})` }} />}
    </div>
  );
}
