import type { NotchTab, ShellState } from "../contract";
import { IconButton } from "../ui";
import { ChatTab } from "./ChatTab";
import { DocumentTab } from "./DocumentTab";
import { HomeTab } from "./HomeTab";
import { SettingsPanel } from "../settings/SettingsPanel";
import { GearIcon } from "./icons";
import { TAB_PANEL_ID, TabSwitcher, tabId } from "./TabSwitcher";

/** The open panel: tab bar on top, the active tab below. */
interface PeekProps {
  state: ShellState; tab: NotchTab; onTab: (tab: NotchTab) => void; draft: string; onDraft: (text: string) => void;
  focusComposer?: boolean; onComposerFocused?: () => void;
}

export function Peek({ state, tab, onTab, draft, onDraft, focusComposer, onComposerFocused }: PeekProps) {
  return (
    <div className="notch-peek">
      <header className="notch-peek__bar">
        <TabSwitcher tab={tab} onTab={onTab} />
        <IconButton label="Settings" icon={<GearIcon />} active={tab === "settings"} onClick={() => onTab(tab === "settings" ? "home" : "settings")} />
      </header>
      <div className="notch-peek__tab" key={tab} id={TAB_PANEL_ID}
        {...(tab === "home" || tab === "chat" ? { role: "tabpanel", "aria-labelledby": tabId(tab) } : {})}>
        {tab === "home" ? <HomeTab state={state} onOpenSettings={() => onTab("settings")} />
          : tab === "chat" ? <ChatTab state={state} draft={draft} onDraft={onDraft} focusComposer={focusComposer} onComposerFocused={onComposerFocused} />
          : tab === "settings" ? <SettingsPanel state={state} />
          : <DocumentTab doc={state.document} onClose={() => onTab("home")} />}
      </div>
    </div>
  );
}
