import type { SettingsSection } from "../contract";
import { cx } from "../ui/cx";

export const SECTION_TITLES: Record<SettingsSection, string> = {
  account: "Account", general: "General", voice: "Voice", microphone: "Microphone", dictation: "Dictation", shortcuts: "Shortcuts",
  cursor: "Cursor", integrations: "Integrations", agents: "Agents", routines: "Routines", access: "Access & privacy", memory: "Memory",
  notes: "Notes", clipboard: "Clipboard shelf", tasks: "Tasks", advanced: "Advanced",
  proactive: "Proactive",
};

const GROUPS: { title?: string; items: SettingsSection[] }[] = [
  { items: ["account", "general"] },
  { title: "OpenClicky", items: ["voice", "microphone", "dictation", "shortcuts", "cursor"] },
  { title: "Work", items: ["integrations", "agents", "routines", "proactive"] },
  { title: "Extras", items: ["access", "memory", "notes", "clipboard", "tasks", "advanced"] },
];

export function Sidebar({ section, onSection }: { section: SettingsSection; onSection: (section: SettingsSection) => void }) {
  return (
    <nav className="settings-sidebar" aria-label="Settings sections">
      {GROUPS.map((group, i) => (
        <div className="settings-sidebar__section" key={i}>
          {group.title ? <div className="settings-sidebar__group">{group.title}</div> : null}
          {group.items.map((id) => (
            <button key={id} type="button" className={cx("settings-sidebar__item", id === section && "is-active")}
              aria-current={id === section ? "page" : undefined} onClick={() => onSection(id)}>
              {SECTION_TITLES[id]}
            </button>
          ))}
        </div>
      ))}
    </nav>
  );
}
