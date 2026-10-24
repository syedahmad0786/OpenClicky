import { afterEach, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import type { ShellState } from "../contract";
import { SettingsPanel } from "./SettingsPanel";

const fixture = JSON.parse(readFileSync(resolve(__dirname, "../../../../../tests/fixtures/shell-state.v1.json"), "utf8")) as ShellState;
const make = (section: ShellState["notch"]["section"], patch: (s: ShellState) => void = () => {}): ShellState => {
  const s = structuredClone(fixture);
  s.notch = { ...s.notch, tab: "settings", open: true, section };
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

const addForm = () => {
  fireEvent.click(screen.getByRole("button", { name: "Add custom connector" }));
  return {
    name: screen.getByRole("textbox", { name: "Name" }),
    url: screen.getByRole("textbox", { name: "Server URL" }),
    add: screen.getByRole("button", { name: "Add connector" }),
  };
};

// ---- Integrations
it("Integrations: browser card follows the Chrome state and posts chrome.setup", () => {
  const posts = host();
  const chrome = (c: ShellState["settings"]["integrations"]["chrome"]) => make("integrations", (s) => { s.settings.integrations.chrome = c; });
  const { rerender } = render(<SettingsPanel state={chrome("notInstalled")} />);
  expect(screen.getByText("Works with Chrome, Brave, Edge and Arc.")).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Set up Chrome" }));
  rerender(<SettingsPanel state={chrome("waiting")} />);
  expect(screen.getByText("Waiting for the extension…")).toBeInTheDocument();
  rerender(<SettingsPanel state={chrome("connected")} />);
  expect(screen.getAllByText("Connected", { selector: ".oc-badge" })).toHaveLength(2); // Chrome + the Notion connector
  fireEvent.click(screen.getByRole("button", { name: "Disconnect Chrome" }));
  expect(posts("settings.action")).toEqual([
    { kind: "settings.action", action: "chrome.setup" },
    { kind: "settings.action", action: "chrome.setup", arg: "disconnect" },
  ]);
});

it("Integrations: connector rows show status and tools, search filters by name or host, Test posts the key", () => {
  const posts = host();
  render(<SettingsPanel state={make("integrations", (s) => { s.settings.integrations.connectors.push({ key: "gh", name: "GitHub", host: "api.github.com", status: "error", toolCount: 1 }); })} />);
  expect(screen.getByText("12 tools")).toBeInTheDocument();
  expect(screen.getByText("1 tool")).toBeInTheDocument();
  expect(screen.getByText("Needs sign-in")).toBeInTheDocument();
  expect(screen.getByText("Error")).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Test Notion" }));
  expect(posts("settings.action")).toEqual([{ kind: "settings.action", action: "connector.test", arg: "notion" }]);

  const search = screen.getByRole("searchbox", { name: "Search connectors" });
  fireEvent.change(search, { target: { value: "slack.com" } });
  expect(screen.queryByText("Notion")).toBeNull();
  expect(screen.getByText("Slack")).toBeInTheDocument();
  fireEvent.change(search, { target: { value: "GITHUB" } });
  expect(screen.getByText("GitHub")).toBeInTheDocument();
  expect(screen.queryByText("Slack")).toBeNull();
});

it("Integrations: Remove asks in-page first, then posts connector.remove", () => {
  const posts = host();
  render(<SettingsPanel state={make("integrations")} />);
  fireEvent.click(screen.getByRole("button", { name: "Remove Notion" }));
  expect(posts("settings.action")).toEqual([]);
  fireEvent.click(screen.getByRole("button", { name: "Keep" }));
  fireEvent.click(screen.getByRole("button", { name: "Remove Notion" }));
  fireEvent.click(screen.getByRole("button", { name: "Remove connector" }));
  expect(posts("settings.action")).toEqual([{ kind: "settings.action", action: "connector.remove", arg: "notion" }]);
});

it("Integrations: the add form needs a name and an https (or local http) URL, then posts the JSON arg", () => {
  const posts = host();
  render(<SettingsPanel state={make("integrations")} />);
  const f = addForm();
  expect(f.add).toBeDisabled(); // Sign in with browser is the default and is not available yet
  fireEvent.click(screen.getByRole("radio", { name: "None" }));

  expect(f.add).toBeDisabled(); // empty
  fireEvent.change(f.name, { target: { value: "Linear" } });
  fireEvent.change(f.url, { target: { value: "http://example.com/mcp" } });
  expect(f.add).toBeDisabled();
  expect(screen.getByRole("alert")).toHaveTextContent("https://");
  fireEvent.change(f.url, { target: { value: "ftp://x.com" } });
  expect(f.add).toBeDisabled();
  fireEvent.change(f.url, { target: { value: "https://" } });
  expect(f.add).toBeDisabled();
  fireEvent.change(f.name, { target: { value: "   " } });
  fireEvent.change(f.url, { target: { value: "https://mcp.linear.app/mcp" } });
  expect(f.add).toBeDisabled();

  fireEvent.change(f.name, { target: { value: " Linear " } });
  expect(f.add).toBeEnabled();
  fireEvent.click(f.add);
  expect(posts("settings.action")).toEqual([
    { kind: "settings.action", action: "connector.add", arg: JSON.stringify({ name: "Linear", url: "https://mcp.linear.app/mcp", auth: "none" }) },
  ]);
  expect(screen.queryByRole("textbox", { name: "Name" })).toBeNull(); // form closed
});

it("Integrations: local http addresses are accepted, API key auth is sent, browser sign-in is disabled with a note", () => {
  const posts = host();
  render(<SettingsPanel state={make("integrations")} />);
  const f = addForm();
  expect(screen.getByText("Browser sign-in arrives with the connectors update.")).toBeInTheDocument();
  fireEvent.click(screen.getByRole("radio", { name: "API key" }));
  expect(screen.queryByText("Browser sign-in arrives with the connectors update.")).toBeNull();
  fireEvent.change(f.name, { target: { value: "Local" } });
  fireEvent.change(f.url, { target: { value: "http://127.0.0.1:8123/mcp" } });
  expect(f.add).toBeEnabled();
  fireEvent.change(f.url, { target: { value: "http://localhost/mcp" } });
  expect(f.add).toBeEnabled();
  fireEvent.click(f.add);
  expect(posts("settings.action")).toEqual([
    { kind: "settings.action", action: "connector.add", arg: JSON.stringify({ name: "Local", url: "http://localhost/mcp", auth: "apiKey" }) },
  ]);
});

it("Integrations: the add form refuses input over 512 characters and Cancel closes it without posting", () => {
  const posts = host();
  render(<SettingsPanel state={make("integrations")} />);
  const f = addForm();
  fireEvent.click(screen.getByRole("radio", { name: "None" }));
  fireEvent.change(f.name, { target: { value: "Long" } });
  fireEvent.change(f.url, { target: { value: `https://example.com/${"a".repeat(600)}` } });
  expect(f.add).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
  expect(screen.queryByRole("textbox", { name: "Name" })).toBeNull();
  expect(posts("settings.action")).toEqual([]);
});

// ---- Agents
it("Agents: folder row shows the path, Reset only when it is not the default", () => {
  const posts = host();
  const folder = (isDefault: boolean) => make("agents", (s) => { s.settings.agents.folderIsDefault = isDefault; });
  const { rerender } = render(<SettingsPanel state={folder(true)} />);
  expect(screen.getByText("C:\\Users\\demo\\OpenClicky\\Agents")).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "Reset" })).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "Change" }));
  rerender(<SettingsPanel state={folder(false)} />);
  fireEvent.click(screen.getByRole("button", { name: "Reset" }));
  expect(posts("settings.action")).toEqual([
    { kind: "settings.action", action: "chooseAgentFolder" },
    { kind: "settings.action", action: "resetAgentFolder" },
  ]);
});

it("Agents: long folder paths are shortened in the middle but keep the full path as a tooltip", () => {
  host();
  const long = `C:\\Users\\demo\\${"deeply\\".repeat(20)}Agents`;
  render(<SettingsPanel state={make("agents", (s) => { s.settings.agents.folder = long; })} />);
  const shown = screen.getByTitle(long);
  expect(shown.textContent!.length).toBeLessThan(long.length);
  expect(shown.textContent).toContain("…");
  expect(shown.textContent!.startsWith("C:\\Users")).toBe(true);
  expect(shown.textContent!.endsWith("Agents")).toBe(true);
});

it("Agents: toggles patch, the quiet note shows and reach rows post openPrivacySetting with their id", () => {
  const posts = host();
  render(<SettingsPanel state={make("agents", (s) => { s.settings.agents.reach.push({ id: "mic", label: "Microphone", state: "denied" }); })} />);
  fireEvent.click(screen.getByRole("switch", { name: "Speak when a job starts or finishes" }));
  fireEvent.click(screen.getByRole("switch", { name: "Show updates beside the cursor" }));
  expect(screen.getByText("OpenClicky stays quiet while you're on a call, sharing your screen, or in Do Not Disturb.")).toBeInTheDocument();
  expect(posts("settings.patch")).toEqual([
    { kind: "settings.patch", key: "agents.speakAnnouncements", value: false },
    { kind: "settings.patch", key: "agents.cursorUpdates", value: false },
  ]);
  const reach = screen.getByRole("group", { name: "What agents can reach" });
  expect(within(reach).getByText("Allowed")).toBeInTheDocument();
  expect(within(reach).getByText("Unknown")).toBeInTheDocument();
  expect(within(reach).getByText("Denied")).toBeInTheDocument();
  fireEvent.click(within(reach).getByRole("button", { name: /Camera/ }));
  fireEvent.click(within(reach).getByRole("button", { name: /Microphone/ }));
  expect(posts("settings.action")).toEqual([
    { kind: "settings.action", action: "openPrivacySetting", arg: "camera" },
    { kind: "settings.action", action: "openPrivacySetting", arg: "mic" },
  ]);
});

// ---- Routines
it("Routines: rows show schedule, next/last and status; actions post the routine id", () => {
  const posts = host();
  render(<SettingsPanel state={make("routines")} />);
  expect(screen.getByText("Morning summary")).toBeInTheDocument();
  expect(screen.getByText("Every day at 8:00")).toBeInTheDocument();
  expect(screen.getByText(/^Next /)).toBeInTheDocument(); // r1
  expect(screen.getByText(/^Last /)).toBeInTheDocument(); // r2
  expect(screen.getByText("Scheduled")).toBeInTheDocument();
  expect(screen.getByText("Paused")).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Run Morning summary now" }));
  fireEvent.click(screen.getByRole("button", { name: "Pause Morning summary" }));
  fireEvent.click(screen.getByRole("button", { name: "Resume Weekly backup" }));
  expect(posts("settings.action")).toEqual([
    { kind: "settings.action", action: "routine.run", arg: "r1" },
    { kind: "settings.action", action: "routine.pause", arg: "r1" },
    { kind: "settings.action", action: "routine.resume", arg: "r2" },
  ]);
});

it("Routines: Delete confirms in the page before posting routine.delete", () => {
  const posts = host();
  render(<SettingsPanel state={make("routines")} />);
  fireEvent.click(screen.getByRole("button", { name: "Delete Weekly backup" }));
  expect(screen.getByText("Delete this routine? Its past runs stay in the chat.")).toBeInTheDocument();
  expect(posts("settings.action")).toEqual([]);
  fireEvent.click(screen.getByRole("button", { name: "Keep" }));
  expect(screen.queryByText("Delete this routine? Its past runs stay in the chat.")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "Delete Weekly backup" }));
  fireEvent.click(screen.getByRole("button", { name: "Delete routine" }));
  expect(posts("settings.action")).toEqual([{ kind: "settings.action", action: "routine.delete", arg: "r2" }]);
});

it("Routines: a failed or running routine shows its status, and the empty state names the talk keys", () => {
  host();
  const { rerender } = render(<SettingsPanel state={make("routines", (s) => { s.settings.routines[0].status = "failed"; s.settings.routines[1].status = "running"; })} />);
  expect(screen.getByText("Failed")).toBeInTheDocument();
  expect(screen.getByText("Running")).toBeInTheDocument();
  rerender(<SettingsPanel state={make("routines", (s) => { s.settings.routines = []; s.hotkeys.talk = ["Ctrl", "Alt"]; })} />);
  expect(screen.getByText(/tell OpenClicky what to do, and when\./)).toBeInTheDocument();
  expect(screen.getByText("Ctrl")).toBeInTheDocument();
  expect(screen.getByText("Alt")).toBeInTheDocument();
  expect(screen.getByText("“Every weekday at 8:30, summarise my unread email.”")).toBeInTheDocument();
});

// ---- Memory / Notes / Clipboard / Tasks
it("Memory exports; Notes, Clipboard shelf and Tasks open their native panels", () => {
  const posts = host();
  const { rerender } = render(<SettingsPanel state={make("memory")} />);
  expect(screen.getByText("What OpenClicky has learned about you.")).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Export memories" }));
  rerender(<SettingsPanel state={make("notes")} />);
  expect(screen.getByRole("heading", { name: "Notes" })).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Open" }));
  rerender(<SettingsPanel state={make("clipboard")} />);
  expect(screen.getByRole("heading", { name: "Clipboard shelf" })).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Open" }));
  rerender(<SettingsPanel state={make("tasks")} />);
  expect(screen.getByRole("heading", { name: "Tasks" })).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Open" }));
  expect(posts("settings.action")).toEqual([
    { kind: "settings.action", action: "exportMemory" },
    { kind: "settings.action", action: "openNotes" },
    { kind: "settings.action", action: "openClipboard" },
    { kind: "settings.action", action: "openTasks" },
  ]);
});

// ---- Fix round: strict connector URLs
it.each([
  "https://user@evil.com/mcp", "https://user:pass@host/mcp", "https://@/x", "https://:443/x", "https://good.com\@evil.com/x",
  "javascript:alert(1)", "file:///c:/x", "http://example.com/mcp", "http://localhost.evil.com/mcp", "https://exa mple.com/", "http://user@localhost/x",
])("Integrations: refuses %s", (bad) => {
  const posts = host();
  render(<SettingsPanel state={make("integrations")} />);
  const f = addForm();
  fireEvent.click(screen.getByRole("radio", { name: "None" }));
  fireEvent.change(f.name, { target: { value: "X" } });
  fireEvent.change(f.url, { target: { value: bad } });
  expect(f.add).toBeDisabled();
  expect(screen.getByRole("alert")).toBeInTheDocument();
  fireEvent.click(f.add);
  expect(posts("settings.action")).toEqual([]);
});

it.each(["https://mcp.linear.app/mcp", "https://example.com:8443/a?b=c", "http://localhost:3000/mcp", "http://127.0.0.1/mcp"])("Integrations: accepts %s", (good) => {
  host();
  render(<SettingsPanel state={make("integrations")} />);
  const f = addForm();
  fireEvent.click(screen.getByRole("radio", { name: "None" }));
  fireEvent.change(f.name, { target: { value: "X" } });
  fireEvent.change(f.url, { target: { value: good } });
  expect(f.add).toBeEnabled();
});

// ---- Fix round: focus in confirms
it("Remove confirm: focus goes to Keep, the row is an alert, and Keep returns focus to the Remove button", () => {
  host();
  render(<SettingsPanel state={make("integrations")} />);
  const remove = screen.getByRole("button", { name: "Remove Notion" });
  fireEvent.click(remove);
  expect(screen.getByRole("alert")).toHaveTextContent("Remove Notion?");
  expect(screen.getByRole("button", { name: "Keep" })).toHaveFocus();
  fireEvent.click(screen.getByRole("button", { name: "Keep" }));
  expect(screen.queryByRole("alert")).toBeNull();
  expect(remove).toHaveFocus();
});

it("Delete routine confirm: focus goes to Keep, the row is an alert, and Keep returns focus to Delete", () => {
  host();
  render(<SettingsPanel state={make("routines")} />);
  const del = screen.getByRole("button", { name: "Delete Morning summary" });
  fireEvent.click(del);
  expect(screen.getByRole("alert")).toHaveTextContent("Delete this routine?");
  expect(screen.getByRole("button", { name: "Keep" })).toHaveFocus();
  fireEvent.click(screen.getByRole("button", { name: "Keep" }));
  expect(del).toHaveFocus();
});

// ---- Fix round: routine dates and running state
it("Routines: invalid dates show no Next/Last line and Run now is disabled while running", () => {
  host();
  render(<SettingsPanel state={make("routines", (s) => {
    s.settings.routines[0].next = "not-a-date"; s.settings.routines[0].status = "running";
    s.settings.routines[1].last = "also-bad";
  })} />);
  expect(screen.queryByText(/^Next /)).toBeNull();
  expect(screen.queryByText(/^Last /)).toBeNull();
  expect(screen.queryByText(/Invalid/)).toBeNull();
  expect(screen.getByRole("button", { name: "Run Morning summary now" })).toBeDisabled();
  expect(screen.getByRole("button", { name: "Run Weekly backup now" })).toBeEnabled();
});

// ---- Fix round: unknown section
it("An unknown section from the host renders no page instead of crashing", () => {
  host();
  const { container } = render(<SettingsPanel state={make("nonsense" as ShellState["notch"]["section"])} />);
  expect(container.querySelector(".settings-page")).toBeNull();
  expect(container.querySelector(".settings-sidebar")).not.toBeNull();
});

// ---- Fix round: Access keyboard safety
it("Access: arrow keys never commit Auto or Full, click/Enter/Space do, and safe modes commit on arrow", () => {
  const posts = host();
  render(<SettingsPanel state={make("access")} />);
  const group = screen.getByRole("radiogroup", { name: "Access mode" });
  const full = within(group).getByRole("radio", { name: /Full access/ });
  const auto = within(group).getByRole("radio", { name: /Auto/ });
  fireEvent.keyDown(auto, { key: "ArrowDown" });
  fireEvent.click(full); // what the browser does when an arrow key checks the next radio
  fireEvent.keyUp(full, { key: "ArrowDown" });
  expect(posts("settings.patch")).toEqual([]);

  fireEvent.keyDown(full, { key: "Enter" });
  expect(posts("settings.patch")).toEqual([{ kind: "settings.patch", key: "access.mode", value: "full" }]);
  postMessage.mockClear();
  fireEvent.keyDown(auto, { key: " " });
  fireEvent.click(auto);
  expect(posts("settings.patch")).toEqual([{ kind: "settings.patch", key: "access.mode", value: "auto" }]);
  postMessage.mockClear();
  fireEvent.keyDown(full, { key: "ArrowUp" });
  fireEvent.click(within(group).getByRole("radio", { name: /Preview only/ }));
  expect(posts("settings.patch")).toEqual([{ kind: "settings.patch", key: "access.mode", value: "preview" }]);
});

// ---- Fix round: Dictation quick adds
it("Dictation: two quick adds before the host answers keep both words", () => {
  const posts = host();
  render(<SettingsPanel state={make("dictation", (s) => { s.settings.dictation.dictionary = ["OpenClicky"]; })} />);
  const input = screen.getByRole("textbox", { name: "Add a word" });
  fireEvent.change(input, { target: { value: "Ahmad" } });
  fireEvent.keyDown(input, { key: "Enter" });
  fireEvent.change(input, { target: { value: "Bukhari" } });
  fireEvent.keyDown(input, { key: "Enter" });
  expect(posts("settings.patch").map((m) => m.value)).toEqual([["OpenClicky", "Ahmad"], ["OpenClicky", "Ahmad", "Bukhari"]]);
});

// ---- Fix round: Microphone meter
it("Microphone: any non-zero level lights at least one bar", () => {
  let listener: ((event: MessageEvent) => void) | null = null;
  window.chrome = { webview: { postMessage, addEventListener: (_t, l) => { listener = l; }, removeEventListener: () => {} } };
  render(<SettingsPanel state={make("microphone")} />);
  const meter = screen.getByRole("meter", { name: "Microphone level" });
  act(() => listener?.({ data: { kind: "mic.level", level: 0.01 } } as MessageEvent));
  expect(meter).toHaveAttribute("aria-valuenow", "1"); // 1 of 16 bars
  expect(meter.querySelectorAll(".is-lit")).toHaveLength(1);
});
