import type { CSSProperties, ReactNode } from "react";
import { cx } from "./cx";

export type PaperStock = "cream" | "butter" | "mint" | "sky" | "peach" | "blush";

export interface PaperCardProps {
  stock?: PaperStock;
  /** Rotation in degrees. */
  tilt?: number;
  title?: ReactNode;
  children?: ReactNode;
  className?: string;
}

export function PaperCard({ stock = "cream", tilt = 0, title, children, className }: PaperCardProps) {
  return (
    <div className={cx("oc-paper", `oc-paper--${stock}`, className)} style={{ "--tilt": `${tilt}deg` } as CSSProperties}>
      <span className="oc-paper__handle" aria-hidden="true" />
      {title ? <h4 className="oc-paper__title">{title}</h4> : null}
      {children}
    </div>
  );
}
