import { IconButton } from "../ui";
import type { NotchTab } from "../contract";
import { ChatIcon, WaveIcon } from "./icons";

/** Home / Chat pair. Not a `Segmented`: when Settings or Document is showing, neither is selected. */
export function TabSwitcher({ tab, onTab }: { tab: NotchTab; onTab: (tab: "home" | "chat") => void }) {
  return (
    <div role="group" aria-label="Notch tabs" className="oc-segmented notch-tabs">
      <IconButton label="Home" icon={<WaveIcon />} active={tab === "home"} onClick={() => onTab("home")} />
      <IconButton label="Chat" icon={<ChatIcon />} active={tab === "chat"} onClick={() => onTab("chat")} />
    </div>
  );
}
