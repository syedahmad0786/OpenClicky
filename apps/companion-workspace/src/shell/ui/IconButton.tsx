import type { ComponentPropsWithRef, ReactNode } from "react";
import { cx } from "./cx";

export interface IconButtonProps extends Omit<ComponentPropsWithRef<"button">, "aria-label"> {
  /** Accessible name; required because the button has no visible text. */
  label: string;
  icon: ReactNode;
  active?: boolean;
}

export function IconButton({ label, icon, active, className, type = "button", ...rest }: IconButtonProps) {
  return (
    <button type={type} aria-label={label} title={label} aria-pressed={active} className={cx("oc-icon-btn", active && "is-active", className)} {...rest}>
      {icon}
    </button>
  );
}
