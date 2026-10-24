import type { ReactNode } from "react";
import { cx } from "./cx";

export interface RadioOption<T extends string> {
  value: T;
  label: ReactNode;
  description?: ReactNode;
  disabled?: boolean;
}

export interface RadioListProps<T extends string> {
  name: string;
  label: string;
  options: readonly RadioOption<T>[];
  value: T;
  onChange: (value: T) => void;
}

/** Native radios (arrow keys and focus come from the browser). */
export function RadioList<T extends string>({ name, label, options, value, onChange }: RadioListProps<T>) {
  return (
    <div role="radiogroup" aria-label={label} className="oc-radio-list">
      {options.map((option) => (
        <label key={option.value} className={cx("oc-radio", option.disabled && "is-disabled")}>
          <input type="radio" name={name} value={option.value} checked={option.value === value} disabled={option.disabled}
            onChange={() => onChange(option.value)} />
          <span className="oc-radio__dot" aria-hidden="true" />
          <span className="oc-radio__text">
            <span className="oc-radio__label">{option.label}</span>
            {option.description ? <span className="oc-radio__desc">{option.description}</span> : null}
          </span>
        </label>
      ))}
    </div>
  );
}
