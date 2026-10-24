import { useEffect, useRef, useState, type ReactNode } from "react";
import { postPage } from "../bridge";
import type { SettingsAction } from "../contract";
import { PillButton, Row } from "../ui";

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

/** In-page confirm: announced as an alert, focus lands on the safe button; the caller puts focus back on its trigger. */
export function ConfirmRow({ title, keepLabel = "Keep", confirmLabel, onKeep, onConfirm }: {
  title: string; keepLabel?: string; confirmLabel: string; onKeep: () => void; onConfirm: () => void;
}) {
  const keep = useRef<HTMLButtonElement>(null);
  useEffect(() => keep.current?.focus(), []);
  return (
    <Row title={title} tone="danger" role="alert">
      <PillButton size="sm" ref={keep} onClick={onKeep}>{keepLabel}</PillButton>
      <PillButton size="sm" variant="danger" onClick={onConfirm}>{confirmLabel}</PillButton>
    </Row>
  );
}
