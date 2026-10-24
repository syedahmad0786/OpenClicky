import type { NotchTab, ShellState } from "../contract";
import { IconButton } from "../ui";
import { ChatTab } from "./ChatTab";
import { HomeTab } from "./HomeTab";
import { GearIcon } from "./icons";
import { TabSwitcher } from "./TabSwitcher";

/** The open panel: tab bar on top, the active tab below. Settings and Document bodies arrive in later tasks. */
interface PeekProps { state: ShellState; tab: NotchTab; onTab: (tab: NotchTab) => void; draft: string; onDraft: (text: string) => void }

export function Peek({ state, tab, onTab, draft, onDraft }: PeekProps) {
  return (
    <div className="notch-peek">
      <header className="notch-peek__bar">
        <TabSwitcher tab={tab} onTab={onTab} />
        <IconButton label="Settings" icon={<GearIcon />} active={tab === "settings"} onClick={() => onTab(tab === "settings" ? "home" : "settings")} />
      </header>
      <div className="notch-peek__tab" key={tab}>
        {tab === "home" ? <HomeTab state={state} onOpenSettings={() => onTab("settings")} />
          : tab === "chat" ? <ChatTab state={state} draft={draft} onDraft={onDraft} />
          : tab === "settings" ? <div className="notch-body"><h2 className="notch-home__greeting">Settings</h2></div>
          : <div className="notch-body"><h2 className="notch-home__greeting">Document</h2></div>}
      </div>
    </div>
  );
}
