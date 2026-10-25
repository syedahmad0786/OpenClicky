import { afterEach, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import ShellApp from "./ShellApp";
import type { ShellState } from "./contract";
import { Notch } from "./notch/Notch";
import { SettingsPanel } from "./settings/SettingsPanel";
import { CloseIcon, IconButton, RadioList } from "./ui";

// Roles, names and keyboard behaviour added by the UI polish pass.
const fixture = JSON.parse(readFileSync(resolve(__dirname, "../../../../tests/fixtures/shell-state.v1.json"), "utf8")) as ShellState;
const open = (tab: ShellState["notch"]["tab"], patch: (s: ShellState) => void = () => {}): ShellState => {
  const s = structuredClone(fixture);
  s.notch = { ...s.notch, tab, open: true };
  patch(s);
  return s;
};

afterEach(() => { cleanup(); vi.useRealTimers(); delete window.chrome; });

function mockHost() {
  const listeners = new Set<(event: MessageEvent) => void>();
  const postMessage = vi.fn();
  window.chrome = { webview: { postMessage,
    addEventListener: (_, l) => { listeners.add(l); }, removeEventListener: (_, l) => { listeners.delete(l); } } };
  const send = (data: unknown) => act(() => { for (const l of listeners) l(new MessageEvent("message", { data })); });
  const posts = (kind: string) => postMessage.mock.calls.map((c) => c[0]).filter((m) => m.kind === kind);
  return { send, posts };
}

it("notch tabs are a tablist: one tab stop, aria-selected, arrow keys switch and move focus, the panel is labelled", () => {
  const host = mockHost();
  render(<Notch state={open("home")} />);
  const list = screen.getByRole("tablist", { name: "Notch tabs" });
  const [home, chat] = within(list).getAllByRole("tab");
  expect(home).toHaveAccessibleName("Home");
  expect(home).toHaveAttribute("aria-selected", "true");
  expect(chat).toHaveAttribute("aria-selected", "false");
  expect([home.tabIndex, chat.tabIndex]).toEqual([0, -1]);
  expect(screen.getByRole("tabpanel", { name: "Home" })).toBeInTheDocument();

  fireEvent.keyDown(home, { key: "ArrowRight" });
  expect(host.posts("notch.tab")).toEqual([{ kind: "notch.tab", tab: "chat" }]);
  expect(chat).toHaveFocus();
  expect(chat).toHaveAttribute("aria-selected", "true");
  expect(screen.getByRole("tabpanel", { name: "Chat" })).toBeInTheDocument();
  fireEvent.keyDown(chat, { key: "ArrowRight" }); // wraps
  expect(host.posts("notch.tab").at(-1)).toEqual({ kind: "notch.tab", tab: "home" });

  // Settings is a toggle button beside the tabs; with it showing, neither tab is selected and Home keeps the tab stop.
  fireEvent.click(screen.getByRole("button", { name: "Settings" }));
  expect(within(list).getAllByRole("tab").map((t) => [t.getAttribute("aria-selected"), t.tabIndex])).toEqual([["false", 0], ["false", -1]]);
  expect(screen.queryByRole("tabpanel")).toBeNull();
});

it("an icon-only button is named by its label and its icon is hidden from assistive tech", () => {
  render(<IconButton label="Close" icon={<CloseIcon />} />);
  const button = screen.getByRole("button", { name: "Close" });
  expect(button.querySelector("svg")).toHaveAttribute("aria-hidden", "true");
  expect(button).toHaveAttribute("title", "Close");
});

it("RadioList is a named radiogroup of native radios (arrow keys come from the browser)", () => {
  render(<RadioList name="x" label="Output device" value="a" onChange={() => {}} options={[{ value: "a", label: "A" }, { value: "b", label: "B" }]} />);
  const group = screen.getByRole("radiogroup", { name: "Output device" });
  const radios = within(group).getAllByRole("radio");
  expect(radios.map((r) => (r as HTMLInputElement).name)).toEqual(["x", "x"]); // one name: the browser's arrow-key group
});

it("one polite live region reads a job's new state and a new card once, not the ones present at load", () => {
  const host = mockHost();
  render(<ShellApp />);
  host.send({ kind: "shell.state", state: fixture });
  const region = screen.getByRole("status");
  expect(region).toHaveAttribute("aria-live", "polite");
  expect(region).toHaveTextContent(/^$/);

  host.send({ kind: "shell.state", state: { ...fixture, jobs: fixture.jobs.map((j) => (j.id === "job-queued" ? { ...j, state: "needsYou" } : j)) } });
  expect(region).toHaveTextContent("Tidy the downloads folder: Needs you");

  const card = { id: "card-new", kind: "toast", title: "Copied", actions: [] } as ShellState["cards"][number];
  host.send({ kind: "shell.state", state: { ...fixture, jobs: fixture.jobs.map((j) => (j.id === "job-queued" ? { ...j, state: "needsYou" } : j)), cards: [card, ...fixture.cards.slice(0, 11)] } }); // the contract caps cards at 12
  expect(region).toHaveTextContent(/^Copied$/);
  expect(document.querySelectorAll("[aria-live]")).toHaveLength(1); // the card stack is not a second region
});

it("a connector being tested shows a busy, disabled Testing… button instead of Test", () => {
  mockHost();
  render(<SettingsPanel state={open("settings", (s) => { s.notch.section = "integrations"; s.settings.integrations.connectors[0].status = "checking"; })} />);
  const busy = screen.getByRole("button", { name: "Testing Notion · Work" });
  expect(busy).toBeDisabled();
  expect(busy).toHaveAttribute("aria-busy", "true");
  expect(busy).toHaveTextContent("Testing…");
});

it("Connect in the browser card stays busy until the host answers or the user changes the choice", () => {
  vi.useFakeTimers();
  const host = mockHost();
  render(<SettingsPanel state={open("settings", (s) => { s.notch.section = "integrations"; s.settings.integrations.chrome = "notInstalled"; })} />);
  fireEvent.click(screen.getByRole("button", { name: "Connect" }));
  const busy = screen.getByRole("button", { name: "Connecting…" });
  expect(busy).toBeDisabled();
  expect(busy).toHaveAttribute("aria-busy", "true");
  fireEvent.click(busy);
  expect(host.posts("settings.action")).toHaveLength(1);
  act(() => { vi.advanceTimersByTime(8_000); }); // nothing came back
  expect(screen.getByRole("button", { name: "Connect" })).toBeEnabled();
});

it("Esc closes the Replay mode choice and returns focus to Replay; Esc in an in-page confirm keeps", () => {
  mockHost();
  render(<Notch state={open("chat")} />);
  const replay = screen.getByRole("button", { name: "Replay" });
  fireEvent.click(replay);
  fireEvent.keyDown(screen.getByRole("button", { name: "Replay in a window" }), { key: "Escape" });
  expect(screen.queryByRole("group", { name: "Replay mode" })).toBeNull();
  expect(replay).toHaveFocus();
  cleanup();

  const host = mockHost();
  render(<SettingsPanel state={open("settings", (s) => { s.notch.section = "access"; })} />);
  fireEvent.click(screen.getByRole("button", { name: "Clear history" }));
  fireEvent.keyDown(screen.getByRole("button", { name: "Cancel" }), { key: "Escape" });
  expect(screen.queryByRole("alert")).toBeNull();
  expect(screen.getByRole("button", { name: "Clear history" })).toHaveFocus();
  expect(host.posts("settings.action")).toEqual([]);
});

it("empty searches say so and offer a way back", () => {
  mockHost();
  render(<SettingsPanel state={open("settings", (s) => { s.notch.section = "integrations"; })} />);
  fireEvent.change(screen.getByRole("searchbox", { name: "Search integrations" }), { target: { value: "zzz" } });
  fireEvent.click(screen.getByRole("button", { name: "Clear search" }));
  expect(screen.getByRole("searchbox", { name: "Search integrations" })).toHaveValue("");
  expect(screen.getByRole("heading", { name: "Connected" })).toBeInTheDocument();
});
