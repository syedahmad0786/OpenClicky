import { afterEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
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

it("lists the sidebar sections in order with group headers and posts settings.section on click", () => {
  const posts = host();
  const { container } = render(<SettingsPanel state={make("general")} />);
  const names = within(screen.getByRole("navigation", { name: "Settings sections" })).getAllByRole("button").map((b) => b.textContent);
  expect(names).toEqual(["Account", "General", "Voice", "Microphone", "Dictation", "Shortcuts", "Cursor", "Integrations", "Agents", "Routines", "Proactive",
    "Access & privacy", "Memory", "Notes", "Clipboard shelf", "Tasks", "Advanced"]);
  expect([...container.querySelectorAll(".settings-sidebar__group")].map((e) => e.textContent)).toEqual(["OpenClicky", "Work", "Extras"]);
  expect(screen.getByRole("button", { name: "General" })).toHaveAttribute("aria-current", "page");

  fireEvent.click(screen.getByRole("button", { name: "Access & privacy" }));
  expect(posts("settings.section")).toEqual([{ kind: "settings.section", section: "access" }]);
  expect(screen.getByRole("heading", { name: "Access & privacy" })).toBeInTheDocument(); // optimistic
});

it("follows the section the host pushes", () => {
  host();
  const { rerender } = render(<SettingsPanel state={make("general")} />);
  rerender(<SettingsPanel state={make("integrations")} />);
  expect(screen.getByRole("heading", { name: "Integrations" })).toBeInTheDocument();
});

it("General toggles post general.set and the update row follows the update state", () => {
  const posts = host();
  const state = make("general", (s) => { s.general.launchAtLogin = false; s.update = { current: "1.4.0", available: "1.5.0", state: "available", progress: 0 }; });
  const { rerender } = render(<SettingsPanel state={state} />);
  fireEvent.click(screen.getByRole("switch", { name: "Launch at login" }));
  fireEvent.click(screen.getByRole("switch", { name: "Pause OpenClicky" }));
  expect(posts("general.set")).toEqual([
    { kind: "general.set", key: "launchAtLogin", value: true },
    { kind: "general.set", key: "paused", value: !state.general.paused },
  ]);
  fireEvent.click(screen.getByRole("button", { name: "Download v1.5.0" }));
  expect(posts("update.install")).toHaveLength(1);

  const withUpdate = (u: Partial<ShellState["update"]>) => make("general", (s) => { s.update = { ...s.update, ...u }; });
  rerender(<SettingsPanel state={withUpdate({ state: "upToDate", available: null })} />);
  fireEvent.click(screen.getByRole("button", { name: "Check for updates" }));
  expect(posts("update.check")).toHaveLength(1);
  rerender(<SettingsPanel state={withUpdate({ state: "downloading", progress: 0.42 })} />);
  expect(screen.getByRole("button", { name: "Downloading 42%" })).toBeDisabled();
  rerender(<SettingsPanel state={withUpdate({ state: "ready" })} />);
  fireEvent.click(screen.getByRole("button", { name: "Downloaded — open folder" }));
  expect(posts("update.install")).toHaveLength(2);
  rerender(<SettingsPanel state={withUpdate({ state: "error" })} />);
  fireEvent.click(screen.getByRole("button", { name: "Couldn't check — try again" }));
  expect(posts("update.check")).toHaveLength(2);

  fireEvent.click(screen.getByRole("button", { name: "What's new" }));
  fireEvent.click(screen.getByRole("button", { name: "Report a bug" }));
  fireEvent.click(screen.getByRole("button", { name: "Request a feature" }));
  expect(posts("link.open").map((m) => m.target)).toEqual(["whatsNew", "bug", "feature"]);
});

it("General: the update button is disabled and labelled while checking and while downloading", () => {
  const posts = host();
  const withUpdate = (u: Partial<ShellState["update"]>) => make("general", (s) => { s.update = { ...s.update, ...u }; });
  const { rerender } = render(<SettingsPanel state={withUpdate({ state: "checking", available: null })} />);
  const checking = screen.getByRole("button", { name: "Checking…" });
  expect(checking).toBeDisabled();
  fireEvent.click(checking);
  rerender(<SettingsPanel state={withUpdate({ state: "downloading", available: "1.5.0", progress: 0 })} />);
  expect(screen.getByRole("button", { name: "Downloading 0%" })).toBeDisabled();
  rerender(<SettingsPanel state={withUpdate({ state: "downloading", available: "1.5.0", progress: 0.999 })} />);
  const downloading = screen.getByRole("button", { name: "Downloading 100%" });
  expect(downloading).toBeDisabled();
  fireEvent.click(downloading);
  expect(posts("update.check")).toEqual([]);
  expect(posts("update.install")).toEqual([]);
});

it("General page Start tour posts onboarding start, then closes the notch", () => {
  host();
  render(<SettingsPanel state={make("general")} />);
  expect(screen.getByText("Welcome tour")).toBeInTheDocument();
  expect(screen.getByText("Replay the welcome tour")).toBeInTheDocument();
  expect(screen.getByText("Walk through talking, drawing, dictation and the practice run again.")).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Start tour" }));
  expect(postMessage.mock.calls.map((c) => c[0])).toEqual([{ kind: "onboarding", action: "start" }, { kind: "notch.open", open: false }]);
});

it("Account (ChatGPT): sign in / out, model pickers patch, refresh, usage, quit", () => {
  const posts = host();
  const { rerender } = render(<SettingsPanel state={make("account")} />);
  expect(screen.getByText("Signed in as ahmad@example.com")).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Sign out" }));
  fireEvent.click(within(screen.getByRole("radiogroup", { name: "Model" })).getByRole("radio", { name: "gpt-5-mini" }));
  fireEvent.click(within(screen.getByRole("radiogroup", { name: "Quick-reply model" })).getByRole("radio", { name: "gpt-5" }));
  fireEvent.click(screen.getByRole("button", { name: "Refresh models" }));
  fireEvent.click(screen.getByRole("button", { name: "Manage usage" }));
  fireEvent.click(screen.getByRole("button", { name: "Quit OpenClicky" }));
  fireEvent.click(screen.getByRole("radio", { name: "OpenRouter" }));
  expect(posts("settings.action").map((m) => m.action)).toEqual(["signOut", "refreshModels", "manageUsage"]);
  expect(posts("settings.patch")).toEqual([
    { kind: "settings.patch", key: "account.model", value: "gpt-5-mini" },
    { kind: "settings.patch", key: "account.quickModel", value: "gpt-5" },
    { kind: "settings.patch", key: "account.provider", value: "openrouter" },
  ]);
  expect(posts("app.quit")).toHaveLength(1);

  rerender(<SettingsPanel state={make("account", (s) => { s.settings.account.signedInAs = null; })} />);
  fireEvent.click(screen.getByRole("button", { name: "Continue with ChatGPT" }));
  expect(posts("settings.action").at(-1)).toEqual({ kind: "settings.action", action: "signIn" });
});

it("Account (OpenRouter): key status, Change opens the native prompt, model posts on blur", () => {
  const posts = host();
  const state = make("account", (s) => { s.settings.account.provider = "openrouter"; s.settings.account.openRouterKeySet = true; });
  render(<SettingsPanel state={state} />);
  expect(screen.getByText("Saved")).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Change" }));
  expect(posts("settings.action")).toEqual([{ kind: "settings.action", action: "secret.edit", arg: "openrouter" }]);
  const model = screen.getByRole("textbox", { name: "OpenRouter model" });
  fireEvent.blur(model); // unchanged: no post
  expect(posts("settings.patch")).toEqual([]);
  fireEvent.change(model, { target: { value: "openai/gpt-5" } });
  fireEvent.blur(model);
  expect(posts("settings.patch")).toEqual([{ kind: "settings.patch", key: "account.openRouterModel", value: "openai/gpt-5" }]);
});

it("Access: radio posts a patch but the selection stays at the state value; shows the one-line consequences", () => {
  const posts = host();
  render(<SettingsPanel state={make("access")} />);
  expect(screen.getByText("Asks before every click, keystroke or file change.")).toBeInTheDocument();
  const group = screen.getByRole("radiogroup", { name: "Access mode" });
  fireEvent.click(within(group).getByRole("radio", { name: /Full access/ }));
  expect(posts("settings.patch")).toEqual([{ kind: "settings.patch", key: "access.mode", value: "full" }]);
  expect(within(group).getByRole("radio", { name: /Approve each action/ })).toBeChecked();
  expect(within(group).getByRole("radio", { name: /Full access/ })).not.toBeChecked();
});

it("Access: deny list posts on blur only when changed; remember toggle patches; clear history confirms in-page first", () => {
  const posts = host();
  render(<SettingsPanel state={make("access")} />);
  const area = screen.getByRole("textbox", { name: "Protected window titles" });
  expect(area).toHaveAttribute("maxlength", "2000");
  fireEvent.blur(area);
  expect(posts("settings.patch")).toEqual([]);
  fireEvent.change(area, { target: { value: "bank\nvault" } });
  fireEvent.blur(area);
  expect(posts("settings.patch")).toEqual([{ kind: "settings.patch", key: "access.denyList", value: "bank\nvault" }]);

  fireEvent.click(screen.getByRole("switch", { name: "Remember conversations" }));
  expect(posts("settings.patch").at(-1)).toEqual({ kind: "settings.patch", key: "access.rememberHistory", value: false });

  fireEvent.click(screen.getByRole("button", { name: "Clear history" }));
  expect(posts("settings.action")).toEqual([]);
  expect(screen.getByText("Clear everything OpenClicky remembers from your conversations?")).toBeInTheDocument();
  expect(screen.getByRole("alert")).toHaveTextContent("Clear everything OpenClicky remembers");
  expect(screen.getByRole("button", { name: "Cancel" })).toHaveFocus(); // the safe button
  fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
  expect(posts("settings.action")).toEqual([]);
  expect(screen.getByRole("button", { name: "Clear history" })).toHaveFocus(); // back on the trigger
  fireEvent.click(screen.getByRole("button", { name: "Clear history" }));
  fireEvent.click(screen.getByRole("button", { name: "Clear" }));
  expect(posts("settings.action")).toEqual([{ kind: "settings.action", action: "clearHistory" }]);
});

it("Advanced: toggles patch, proxy URL validates and posts on blur, Open logs", () => {
  const posts = host();
  render(<SettingsPanel state={make("advanced")} />);
  fireEvent.click(screen.getByRole("switch", { name: "Use local proxy" }));
  fireEvent.click(screen.getByRole("switch", { name: "Compatibility screen capture" }));
  expect(posts("settings.patch")).toEqual([
    { kind: "settings.patch", key: "advanced.useLocalProxy", value: true },
    { kind: "settings.patch", key: "advanced.forceGdiCapture", value: true },
  ]);
  const url = screen.getByRole("textbox", { name: "Proxy URL" });
  fireEvent.change(url, { target: { value: "http://evil.example.com:8080" } });
  fireEvent.blur(url);
  expect(screen.getByRole("alert")).toHaveTextContent(/127\.0\.0\.1, localhost or \[::1\]/);
  expect(posts("settings.patch")).toHaveLength(2);
  // The host rejects "" and ports above 65535, so neither is posted.
  for (const bad of ["", "  ", "http://localhost:99999"]) {
    fireEvent.change(url, { target: { value: bad } });
    fireEvent.blur(url);
    expect(screen.getByRole("alert")).toHaveTextContent(/Enter a local address/);
  }
  expect(posts("settings.patch")).toHaveLength(2);
  fireEvent.change(url, { target: { value: "http://[::1]:9000" } });
  fireEvent.blur(url);
  expect(screen.queryByRole("alert")).toBeNull();
  expect(posts("settings.patch").at(-1)).toEqual({ kind: "settings.patch", key: "advanced.proxyUrl", value: "http://[::1]:9000" });
  fireEvent.change(url, { target: { value: "http://127.0.0.1:8080" } });
  fireEvent.blur(url);
  expect(screen.queryByRole("alert")).toBeNull();
  expect(posts("settings.patch").at(-1)).toEqual({ kind: "settings.patch", key: "advanced.proxyUrl", value: "http://127.0.0.1:8080" });
  fireEvent.click(screen.getByRole("button", { name: "Open logs" }));
  expect(posts("settings.action")).toEqual([{ kind: "settings.action", action: "openLogs" }]);
});
