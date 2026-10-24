export interface MeterProps {
  value: number;
  max: number;
  /** Accessible name. */
  label: string;
}

export function Meter({ value, max, label }: MeterProps) {
  const pct = max > 0 ? Math.min(100, Math.max(0, (value / max) * 100)) : 0;
  return (
    <div role="meter" aria-label={label} aria-valuemin={0} aria-valuemax={max} aria-valuenow={value} className="oc-meter">
      <div className="oc-meter__fill" style={{ width: `${pct}%` }} />
    </div>
  );
}
