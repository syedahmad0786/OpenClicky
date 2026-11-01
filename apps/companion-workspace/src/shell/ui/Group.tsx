import type { ReactNode } from "react";
import { cx } from "./cx";
import { SectionHeader } from "./SectionHeader";

/** Raised card of hairline-separated rows, optionally under a small-caps header. */
export function Group({ title, action, children, className }: { title?: ReactNode; action?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <section className={cx("oc-group", className)}>
      {title ? <SectionHeader action={action}>{title}</SectionHeader> : null}
      <div className="oc-group__card">{children}</div>
    </section>
  );
}
