import { useEffect, useState, type ReactNode } from "react";
import { postPage } from "../bridge";
import type { SettingsAction } from "../contract";

export const patch = (key: string, value: unknown) => postPage({ kind: "settings.patch", key, value });
export const act = (action: SettingsAction, arg?: string) => postPage({ kind: "settings.action", action, ...(arg === undefined ? {} : { arg }) });

export function Page({ title, subtitle, children }: { title: string; subtitle?: string; children?: ReactNode }) {
  return (
    <div className="settings-page">
      <h2 className="settings-page__title">{title}</h2>
      {subtitle ? <p className="settings-page__subtitle">{subtitle}</p> : null}
      {children}
    </div>
  );
}

/** Local edit buffer that follows the host value whenever the host pushes a new one. */
export function useDraft(value: string) {
  const [draft, setDraft] = useState(value);
  useEffect(() => setDraft(value), [value]);
  return [draft, setDraft] as const;
}
