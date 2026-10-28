import { useRef, type KeyboardEvent, type ReactNode } from "react";
import { cx } from "./cx";

export interface SegmentedOption<T extends string> {
  value: T;
  label: ReactNode;
  /** Accessible name when `label` is an icon only. */
  title?: string;
}

export interface SegmentedProps<T extends string> {
  options: readonly SegmentedOption<T>[];
  value: T;
  onChange: (value: T) => void;
  label: string;
  className?: string;
}

export function Segmented<T extends string>({ options, value, onChange, label, className }: SegmentedProps<T>) {
  const refs = useRef<Array<HTMLButtonElement | null>>([]);
  const move = (from: number, step: number) => {
    const next = (from + step + options.length) % options.length;
    onChange(options[next].value);
    refs.current[next]?.focus();
  };
  const onKeyDown = (index: number) => (event: KeyboardEvent) => {
    if (event.key === "ArrowRight" || event.key === "ArrowDown") move(index, 1);
    else if (event.key === "ArrowLeft" || event.key === "ArrowUp") move(index, -1);
    else return;
    event.preventDefault();
  };
  const selected = Math.max(0, options.findIndex((o) => o.value === value));
  return (
    <div role="radiogroup" aria-label={label} className={cx("oc-segmented", className)}>
      {options.map((option, i) => (
        <button key={option.value} type="button" role="radio" aria-checked={i === selected} aria-label={option.title}
          tabIndex={i === selected ? 0 : -1} ref={(el) => { refs.current[i] = el; }}
          className={cx("oc-segmented__item", i === selected && "is-selected")}
          onClick={() => onChange(option.value)} onKeyDown={onKeyDown(i)}>
          {option.label}
        </button>
      ))}
    </div>
  );
}
