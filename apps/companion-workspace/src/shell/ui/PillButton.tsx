import type { ButtonHTMLAttributes, ReactNode } from "react";
import { cx } from "./cx";

export type PillVariant = "primary" | "secondary" | "outline" | "danger";

export interface PillButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: PillVariant;
  size?: "sm" | "md";
  icon?: ReactNode;
}

export function PillButton({ variant = "secondary", size = "md", icon, className, children, type = "button", ...rest }: PillButtonProps) {
  return (
    <button type={type} className={cx("oc-pill", `oc-pill--${variant}`, size === "sm" && "oc-pill--sm", className)} {...rest}>
      {icon}
      {children}
    </button>
  );
}
