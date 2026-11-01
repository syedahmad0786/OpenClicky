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

type Job = ShellState["jobs"][number];
type Card = ShellState["cards"][number];
const withJob = (id: string, state: Job["state"]) => fixture.jobs.map((j) => (j.id === id ? { ...j, state } : j));

it("one polite live region: a carded job change is read once, with the card text; other job changes and new cards are read together", async () => {
  const host = mockHost();
  render(<ShellApp />);
  // announce() flushes in a microtask, so these sends await it.
  const send = (state: ShellState) => act(async () => { host.send({ kind: "shell.state", state }); });
  await send(fixture);
  const region = screen.getByRole("status");
  expect(region).toHaveAttribute("aria-live", "polite");
  expect(region).toHaveTextContent(/^$/); // nothing that was already there at load

  // The host cards needsYou itself (JobAnnouncer): the job line is not read as well, and the card text is not lost.
  const turn = { id: "card-turn", kind: "needsYou", title: "Your turn: Tidy the downloads folder", body: "Pick a folder.", actions: [] } as Card;
  await send({ ...fixture, jobs: withJob("job-queued", "needsYou"), cards: [turn, ...fixture.cards.slice(0, 11)] }); // the contract caps cards at 12
  expect(region).toHaveTextContent(/^Your turn: Tidy the downloads folder\. Pick a folder\.$/);

  // A state the host does not card is read from the job, in the same message as a card that arrives with it.
  const copied = { id: "card-copied", kind: "toast", title: "Copied", actions: [] } as Card;
  await send({ ...fixture, jobs: withJob("job-queued", "working"), cards: [copied, turn, ...fixture.cards.slice(0, 10)] });
  expect(region).toHaveTextContent(/^Copied\. Tidy the downloads folder: Working…$/);
  expect(document.querySelectorAll("[aria-live]")).toHaveLength(1); // the card stack is not a second region
});

it("captions are not announced: the user is already hearing those words", async () => {
  const host = mockHost();
  render(<ShellApp />);
  await act(async () => { host.send({ kind: "shell.state", state: fixture }); });
  vi.useFakeTimers();
  host.send({ kind: "caption.word", text: "Hello" });
  host.send({ kind: "caption.clear" });
  await act(async () => { vi.advanceTimersByTime(5_000); });
  expect(screen.getByRole("status")).toHaveTextContent(/^$/);
});

it("with the chat tab open, the conversation log is busy while a reply streams and settles when the finished row replaces it", async () => {
  const host = mockHost();
  render(<ShellApp />);
  const chat = (rows: ShellState["chat"]["rows"]) => open("chat", (s) => { s.chat = { rows }; });
  const at = "2026-10-24T09:31:00Z";
  await act(async () => { host.send({ kind: "shell.state", state: chat([{ id: "row-1", role: "user", text: "Hi", at }]) }); });
  const log = screen.getByRole("log", { name: "Conversation" });
  expect(log).not.toHaveAttribute("aria-busy");

  await act(async () => { host.send({ kind: "shell.state", state: chat([{ id: "row-1", role: "user", text: "Hi", at }, { id: "live-stream-ab12", role: "assistant", text: "Hel", at }]) }); });
  host.send({ kind: "chat.delta", rowId: "live-stream-ab12", text: "lo there" });
  expect(log).toHaveTextContent("Hello there");
  expect(log).toHaveAttribute("aria-busy", "true");

  await act(async () => { host.send({ kind: "shell.state", state: chat([{ id: "row-1", role: "user", text: "Hi", at }, { id: "row-2", role: "assistant", text: "Hello there!", at }]) }); });
  expect(log).not.toHaveAttribute("aria-busy");
  expect(document.querySelectorAll('[aria-live]:not([role="log"])')).toHaveLength(1); // still one status region beside the log
});

it("a connector being tested shows a busy Testing… button that keeps keyboard focus", () => {
  const host = mockHost();
  const state = (status: "connected" | "checking") => open("settings", (s) => { s.notch.section = "integrations"; s.settings.integrations.connectors[0].status = status; });
  const { rerender } = render(<SettingsPanel state={state("connected")} />);
  const test = screen.getByRole("button", { name: "Test Notion · Work" });
  test.focus();
  fireEvent.click(test);
  rerender(<SettingsPanel state={state("checking")} />);
  const busy = screen.getByRole("button", { name: "Testing Notion · Work" });
  expect(busy).toBe(test); // the same element, so focus stays on it
  expect(busy).toHaveFocus();
  expect(busy).not.toBeDisabled();
  expect(busy).toHaveAttribute("aria-disabled", "true");
  expect(busy).toHaveAttribute("aria-busy", "true");
  fireEvent.click(busy);
  expect(host.posts("settings.action")).toHaveLength(1); // the busy button ignores clicks
});

it("Connect in the browser card stays busy (and focused) until the host answers, and says so when it never does", () => {
  vi.useFakeTimers();
  const host = mockHost();
  render(<SettingsPanel state={open("settings", (s) => { s.notch.section = "integrations"; s.settings.integrations.chrome = "notInstalled"; })} />);
  const connect = screen.getByRole("button", { name: "Connect" });
  connect.focus();
  fireEvent.click(connect);
  const busy = screen.getByRole("button", { name: "Connecting…" });
  expect(busy).toHaveFocus();
  expect(busy).toHaveAttribute("aria-disabled", "true");
  expect(busy).toHaveAttribute("aria-busy", "true");
  fireEvent.click(busy);
  expect(host.posts("settings.action")).toHaveLength(1);
  expect(screen.queryByRole("alert")).toBeNull();

  act(() => { vi.advanceTimersByTime(8_000); }); // nothing came back
  expect(screen.getByRole("alert")).toHaveTextContent("Didn't hear back from the browser. Try again.");
  expect(screen.getByRole("button", { name: "Connect" })).toHaveFocus();
  fireEvent.click(screen.getByRole("button", { name: "Connect" })); // trying again clears the line
  expect(screen.queryByRole("alert")).toBeNull();
  expect(host.posts("settings.action")).toHaveLength(2);
});

it("the notch tabs have a 32px pointer target", () => {
  mockHost();
  render(<Notch state={open("home")} />);
  for (const tab of screen.getAllByRole("tab")) expect(tab).toHaveClass("oc-hit");
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
