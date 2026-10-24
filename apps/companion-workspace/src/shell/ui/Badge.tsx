import type { ReactNode } from "react";
import { cx } from "./cx";

/** Status badge: green by default ("Connected", "GRANTED"); `warn`, `danger` and `muted` for other states. */
export function Badge({ children, tone = "ok" }: { children: ReactNode; tone?: "ok" | "warn" | "muted" | "danger" }) {
  return <span className={cx("oc-badge", `oc-badge--${tone}`)}>{children}</span>;
}
