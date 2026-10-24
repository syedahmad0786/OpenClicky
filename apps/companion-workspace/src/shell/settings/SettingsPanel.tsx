import { useEffect, useState } from "react";
import { postPage } from "../bridge";
import type { SettingsSection, ShellState } from "../contract";
import { AccessPage } from "./AccessPage";
import { AccountPage } from "./AccountPage";
import { AdvancedPage } from "./AdvancedPage";
import { AgentsPage } from "./AgentsPage";
import { CursorPage } from "./CursorPage";
import { DictationPage } from "./DictationPage";
import { ClipboardPage, MemoryPage, NotesPage, TasksPage } from "./ExtrasPages";
import { GeneralPage } from "./GeneralPage";
import { IntegrationsPage } from "./IntegrationsPage";
import { MicrophonePage } from "./MicrophonePage";
import { RoutinesPage } from "./RoutinesPage";
import { ShortcutsPage } from "./ShortcutsPage";
import { Sidebar } from "./Sidebar";
import { VoicePage } from "./VoicePage";
import "./settings.css";

/** Compile-time exhaustive: a new section that is not handled above fails to build; an unknown runtime one renders nothing. */
const unknownSection = (section: never): null => (void section, null);

function renderPage(section: SettingsSection, state: ShellState) {
  switch (section) {
    case "account": return <AccountPage state={state} />;
    case "general": return <GeneralPage state={state} />;
    case "voice": return <VoicePage state={state} />;
    case "microphone": return <MicrophonePage state={state} />;
    case "dictation": return <DictationPage state={state} />;
    case "shortcuts": return <ShortcutsPage state={state} />;
    case "cursor": return <CursorPage state={state} />;
    case "access": return <AccessPage state={state} />;
    case "advanced": return <AdvancedPage state={state} />;
    case "integrations": return <IntegrationsPage state={state} />;
    case "agents": return <AgentsPage state={state} />;
    case "routines": return <RoutinesPage state={state} />;
    case "memory": return <MemoryPage />;
    case "notes": return <NotesPage />;
    case "clipboard": return <ClipboardPage />;
    case "tasks": return <TasksPage />;
    default: return unknownSection(section);
  }
}

/** Sidebar + page. The selected section comes from the host; a click shows it at once and tells the host. */
export function SettingsPanel({ state }: { state: ShellState }) {
  const [section, setSection] = useState(state.notch.section);
  useEffect(() => setSection(state.notch.section), [state.notch.section]);
  const choose = (next: SettingsSection) => { setSection(next); postPage({ kind: "settings.section", section: next }); };
  return (
    <div className="settings">
      <Sidebar section={section} onSection={choose} />
      <main className="settings__content">{renderPage(section, state)}</main>
    </div>
  );
}
