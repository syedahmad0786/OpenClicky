import { useRef, type KeyboardEvent, type ReactNode } from "react";
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
  /**
   * Arrow keys check the next radio as they move. Options for which this returns false are not committed that way,
   * only by a click, Space or Enter, so browsing a list with the keyboard cannot choose a risky one.
   */
  commitOnArrow?: (value: T) => boolean;
}

/** Native radios (arrow keys and focus come from the browser). */
export function RadioList<T extends string>({ name, label, options, value, onChange, commitOnArrow }: RadioListProps<T>) {
  const arrowing = useRef(false);
  const onKeyDown = (event: KeyboardEvent) => {
    arrowing.current = event.key.startsWith("Arrow");
    const target = event.target;
    if (event.key === "Enter" && target instanceof HTMLInputElement && target.type === "radio" && !target.disabled) {
      event.preventDefault();
      onChange(target.value as T);
    }
  };
  return (
    <div role="radiogroup" aria-label={label} className="oc-radio-list" onKeyDown={onKeyDown} onKeyUp={() => { arrowing.current = false; }}
      // A keyup lost to a focus change must not leave a later click treated as an arrow press.
      onPointerDown={() => { arrowing.current = false; }}>
      {options.map((option) => (
        <label key={option.value} className={cx("oc-radio", option.disabled && "is-disabled")}>
          <input type="radio" name={name} value={option.value} checked={option.value === value} disabled={option.disabled}
            onChange={() => { if (arrowing.current && commitOnArrow && !commitOnArrow(option.value)) return; onChange(option.value); }} />
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
