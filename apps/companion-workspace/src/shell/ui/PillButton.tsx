import type { ComponentPropsWithRef, ReactNode } from "react";
import { cx } from "./cx";

export type PillVariant = "primary" | "secondary" | "outline" | "danger";

export interface PillButtonProps extends ComponentPropsWithRef<"button"> {
  variant?: PillVariant;
  size?: "sm" | "md";
  icon?: ReactNode;
  /**
   * Working on it: announced busy and looks disabled, but stays focusable (aria-disabled, not disabled) so keyboard
   * focus is not dropped to the page when the button changes state under it; clicks are ignored.
   */
  busy?: boolean;
}

export function PillButton({ variant = "secondary", size = "md", icon, className, children, type = "button", busy, onClick, ...rest }: PillButtonProps) {
  return (
    <button type={type} className={cx("oc-pill", `oc-pill--${variant}`, size === "sm" && "oc-pill--sm", className)}
      {...(busy ? { "aria-disabled": true, "aria-busy": true } : {})} onClick={busy ? undefined : onClick} {...rest}>
      {icon}
      {children}
    </button>
  );
}
