import type { ReactNode } from "react";

export function SectionHeader({ children, action }: { children: ReactNode; action?: ReactNode }) {
  return (
    <div className="oc-section-header">
      <h3 className="oc-section-header__title">{children}</h3>
      {action}
    </div>
  );
}
