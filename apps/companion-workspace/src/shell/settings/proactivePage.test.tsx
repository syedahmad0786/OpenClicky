import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { afterEach, expect, it, vi } from "vitest";
import type { ShellState } from "../contract";
import { SettingsPanel } from "./SettingsPanel";

const fixture = JSON.parse(readFileSync(resolve(__dirname, "../../../../../tests/fixtures/shell-state.v1.json"), "utf8")) as ShellState;
const make = (patch: (s: ShellState) => void = () => {}): ShellState => {
  const s = structuredClone(fixture);
  s.notch = { ...s.notch, tab: "settings", open: true, section: "proactive" };
  s.settings.proactive = { morningBrief: false, suggestions: false, integrationNudges: false, meetingCountdown: false, calendarConnector: null };
  patch(s);
  return s;
};

const postMessage = vi.fn();
function host() {
  postMessage.mockClear();
  window.chrome = { webview: { postMessage, addEventListener: () => {}, removeEventListener: () => {} } };
  return (kind: string) => postMessage.mock.calls.map((c) => c[0]).filter((m) => m.kind === kind);
}
afterEach(() => { cleanup(); delete window.chrome; });

it("Proactive: four toggles render off and the sidebar lists the section", () => {
  host();
  render(<SettingsPanel state={make()} />);
  expect(screen.getByRole("heading", { name: "Proactive" })).toBeInTheDocument();
  expect(screen.getByText("Cards OpenClicky shows without being asked. Each one stays off until you turn it on.")).toBeInTheDocument();
  for (const name of ["Morning brief", "Suggestions", "Connect nudges", "Meeting countdown"])
    expect(screen.getByRole("switch", { name })).toHaveAttribute("aria-checked", "false");
  expect(screen.getByRole("button", { name: "Proactive" })).toHaveAttribute("aria-current", "page");
  expect(screen.getByText("Proactive cards wait while you're on a call, presenting or in a full-screen app.")).toBeInTheDocument();
});

it("Proactive: toggling Suggestions posts settings.patch", () => {
  const posts = host();
  render(<SettingsPanel state={make()} />);
  fireEvent.click(screen.getByRole("switch", { name: "Suggestions" }));
  expect(posts("settings.patch")).toEqual([{ kind: "settings.patch", key: "proactive.suggestions", value: true }]);
});

it("Proactive: the other toggles post their own keys and reflect stored values", () => {
  const posts = host();
  render(<SettingsPanel state={make((s) => { s.settings.proactive.meetingCountdown = true; })} />);
  expect(screen.getByRole("switch", { name: "Meeting countdown" })).toHaveAttribute("aria-checked", "true");
  fireEvent.click(screen.getByRole("switch", { name: "Morning brief" }));
  fireEvent.click(screen.getByRole("switch", { name: "Connect nudges" }));
  fireEvent.click(screen.getByRole("switch", { name: "Meeting countdown" }));
  expect(posts("settings.patch").map((m) => [m.key, m.value])).toEqual([
    ["proactive.morningBrief", true], ["proactive.integrationNudges", true], ["proactive.meetingCountdown", false],
  ]);
});

it("Proactive: Calendar from lists None plus the connectors and posts the chosen key", () => {
  const posts = host();
  render(<SettingsPanel state={make((s) => { s.settings.proactive.calendarConnector = "notion"; })} />);
  const select = screen.getByRole("combobox", { name: "Calendar from" }) as HTMLSelectElement;
  expect([...select.options].map((o) => [o.value, o.textContent])).toEqual([["", "None"], ["notion", "Notion"], ["slack", "Slack"]]);
  expect(select.value).toBe("notion");
  fireEvent.change(select, { target: { value: "slack" } });
  fireEvent.change(select, { target: { value: "" } });
  expect(posts("settings.patch")).toEqual([
    { kind: "settings.patch", key: "proactive.calendarConnector", value: "slack" },
    { kind: "settings.patch", key: "proactive.calendarConnector", value: "" },
  ]);
});

it("Proactive: a stored connector that is no longer listed shows as None", () => {
  host();
  render(<SettingsPanel state={make((s) => { s.settings.proactive.calendarConnector = "gone_cal"; })} />);
  expect((screen.getByRole("combobox", { name: "Calendar from" }) as HTMLSelectElement).value).toBe("");
});
