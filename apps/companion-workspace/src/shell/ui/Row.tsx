import type { ReactNode } from "react";
import { cx } from "./cx";

export interface RowProps {
  title: ReactNode;
  description?: ReactNode;
  leading?: ReactNode;
  /** Trailing control (toggle, button, badge...). */
  children?: ReactNode;
  tone?: "default" | "danger";
  role?: "alert";
}

export function Row({ title, description, leading, children, tone = "default", role }: RowProps) {
  return (
    <div role={role} className={cx("oc-row", tone === "danger" && "oc-row--danger")}>
      {leading ? <span className="oc-row__leading">{leading}</span> : null}
      <span className="oc-row__text">
        <span className="oc-row__title">{title}</span>
        {description ? <span className="oc-row__desc">{description}</span> : null}
      </span>
      {children ? <span className="oc-row__trailing">{children}</span> : null}
    </div>
  );
}
