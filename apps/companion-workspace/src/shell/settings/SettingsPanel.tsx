import { useEffect, useState } from "react";
import { postPage } from "../bridge";
import type { SettingsSection, ShellState } from "../contract";
import { AccessPage } from "./AccessPage";
import { AccountPage } from "./AccountPage";
import { AdvancedPage } from "./AdvancedPage";
import { Page } from "./common";
import { GeneralPage } from "./GeneralPage";
import { SECTION_TITLES, Sidebar } from "./Sidebar";
import "./settings.css";

function renderPage(section: SettingsSection, state: ShellState) {
  switch (section) {
    case "account": return <AccountPage state={state} />;
    case "general": return <GeneralPage state={state} />;
    case "access": return <AccessPage state={state} />;
    case "advanced": return <AdvancedPage state={state} />;
    default: return <Page title={SECTION_TITLES[section]}><p className="settings-page__soon">Coming next</p></Page>;
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
