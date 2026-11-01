import { useRef, type KeyboardEvent } from "react";
import { IconButton } from "../ui";
import type { NotchTab } from "../contract";
import { ChatIcon, WaveIcon } from "./icons";

const TABS = [{ id: "home", label: "Home", Icon: WaveIcon }, { id: "chat", label: "Chat", Icon: ChatIcon }] as const;
export const tabId = (tab: NotchTab) => `notch-tab-${tab}`;
export const TAB_PANEL_ID = "notch-tabpanel";

/**
 * Home / Chat tabs. Not a `Segmented`: when Settings or Document is showing, neither is selected (then Home is the
 * tab stop). Arrow keys move between them and switch at once.
 */
export function TabSwitcher({ tab, onTab }: { tab: NotchTab; onTab: (tab: "home" | "chat") => void }) {
  const refs = useRef<Array<HTMLButtonElement | null>>([]);
  const current = TABS.findIndex((t) => t.id === tab);
  const onKeyDown = (index: number) => (event: KeyboardEvent) => {
    const step = event.key === "ArrowRight" ? 1 : event.key === "ArrowLeft" ? -1 : 0;
    if (!step) return;
    event.preventDefault();
    const next = (index + step + TABS.length) % TABS.length;
    onTab(TABS[next].id);
    refs.current[next]?.focus();
  };
  return (
    <div role="tablist" aria-label="Notch tabs" className="oc-segmented notch-tabs">
      {TABS.map(({ id, label, Icon }, i) => (
        <IconButton key={id} id={tabId(id)} className="oc-hit" label={label} icon={<Icon />} active={tab === id} role="tab" aria-selected={tab === id}
          aria-pressed={undefined} aria-controls={tab === id ? TAB_PANEL_ID : undefined} tabIndex={i === Math.max(current, 0) ? 0 : -1}
          ref={(el: HTMLButtonElement | null) => { refs.current[i] = el; }} onClick={() => onTab(id)} onKeyDown={onKeyDown(i)} />
      ))}
    </div>
  );
}
