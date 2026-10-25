import { afterEach, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import ShellApp from "../ShellApp";
import type { ShellState } from "../contract";
import { Notch } from "./Notch";
import { greetingFor } from "./HomeTab";

const fixture = JSON.parse(readFileSync(resolve(__dirname, "../../../../../tests/fixtures/shell-state.v1.json"), "utf8")) as ShellState;
const closed = (patch: Partial<ShellState> = {}): ShellState => ({ ...fixture, notch: { ...fixture.notch, tab: "home", open: false }, ...patch });
const open = (tab: ShellState["notch"]["tab"] = "home", patch: Partial<ShellState> = {}): ShellState => ({ ...fixture, notch: { ...fixture.notch, tab, open: true }, ...patch });

afterEach(() => { cleanup(); vi.useRealTimers(); vi.restoreAllMocks(); delete window.chrome; });

function mockHost() {
  const listeners = new Set<(event: MessageEvent) => void>();
  const postMessage = vi.fn();
  window.chrome = { webview: { postMessage,
    addEventListener: (_, l) => { listeners.add(l); }, removeEventListener: (_, l) => { listeners.delete(l); } } };
  const send = (data: unknown) => act(() => { for (const l of listeners) l(new MessageEvent("message", { data })); });
  const posts = (kind: string) => postMessage.mock.calls.map((c) => c[0]).filter((m) => m.kind === kind);
  return { postMessage, send, posts };
}
const root = () => screen.getByLabelText("OpenClicky");

it("hover opens the pill; leaving closes it after the 150 ms grace, then the close animation", () => {
  vi.useFakeTimers();
  const host = mockHost();
  render(<Notch state={closed()} />);
  expect(root()).toHaveClass("notch--collapsed");

  fireEvent.pointerEnter(root());
  expect(root()).toHaveClass("notch--open");
  expect(host.posts("notch.open")).toEqual([{ kind: "notch.open", open: true }]);

  fireEvent.pointerLeave(root());
  act(() => { vi.advanceTimersByTime(149); });
  expect(root()).toHaveClass("notch--open");
  act(() => { vi.advanceTimersByTime(1); });
  expect(root()).toHaveClass("notch--closing");
  expect(host.posts("notch.open").at(-1)).toEqual({ kind: "notch.open", open: false });
  act(() => { vi.advanceTimersByTime(299); });
  expect(root()).toHaveClass("notch--closing");
  act(() => { vi.advanceTimersByTime(1); });
  expect(root()).toHaveClass("notch--collapsed");
});

it("coming back inside the grace period keeps the panel open", () => {
  vi.useFakeTimers();
  mockHost();
  render(<Notch state={open()} />);
  fireEvent.pointerLeave(root());
  act(() => { vi.advanceTimersByTime(100); });
  fireEvent.pointerEnter(root());
  act(() => { vi.advanceTimersByTime(1000); });
  expect(root()).toHaveClass("notch--open");
});

it("reduced motion uses the 120 ms fade for the close", () => {
  vi.useFakeTimers();
  mockHost();
  render(<Notch state={open("home", { reducedMotion: true })} />);
  fireEvent.pointerLeave(root());
  act(() => { vi.advanceTimersByTime(150); });
  expect(root()).toHaveClass("notch--closing");
  act(() => { vi.advanceTimersByTime(120); });
  expect(root()).toHaveClass("notch--collapsed");
});

it("the host can open and close the notch without an echo", () => {
  vi.useFakeTimers();
  const host = mockHost();
  const view = render(<Notch state={closed()} />);
  view.rerender(<Notch state={open()} />);
  expect(root()).toHaveClass("notch--open");
  view.rerender(<Notch state={closed()} />);
  expect(root()).toHaveClass("notch--closing");
  expect(host.posts("notch.open")).toEqual([]);
});

it.each([
  [4, "Hi there"], [5, "Good morning"], [11, "Good morning"], [12, "Good afternoon"], [16, "Good afternoon"],
  [17, "Good evening"], [21, "Good evening"], [22, "Hi there"], [0, "Hi there"],
])("greeting at hour %i is %s, with the name when there is one", (hour, text) => {
  expect(greetingFor(hour, "Ahmad")).toBe(`${text}, Ahmad.`);
  expect(greetingFor(hour, "")).toBe(text);
});

it("Home shows the greeting for the local hour, the talk-key hint, status line and apps pill", () => {
  vi.useFakeTimers();
  vi.setSystemTime(new Date(2026, 9, 24, 8, 30));
  mockHost();
  render(<Notch state={open()} />);
  expect(screen.getByRole("heading", { level: 2 })).toHaveTextContent("Good morning, Ahmad.");
  const hint = screen.getByText(/ask for anything/);
  expect(hint).toHaveTextContent("Hold");
  expect(within(hint).getAllByText(/^(Ctrl|Alt)$/)).toHaveLength(2);
  expect(screen.getByText("2 working · 1 need you")).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "2 apps +" })).toBeInTheDocument();
});

it.each([
  [{ jobsWorking: 0, jobsNeedYou: 0 }, "Nothing running"],
  [{ jobsWorking: 3, jobsNeedYou: 0 }, "3 working"],
  [{ jobsWorking: 0, jobsNeedYou: 2 }, "2 need you"],
])("status line for %j", (counts, text) => {
  mockHost();
  render(<Notch state={open("home", { status: { ...fixture.status, ...counts } })} />);
  expect(screen.getByText(text)).toBeInTheDocument();
});

it("the featured job card shows its parts, caps bullets and posts its intents", () => {
  const host = mockHost();
  const featured = { ...fixture.featured!, state: "done" as const, bullets: ["a", "b", "c", "d", "e"] };
  render(<Notch state={open("home", { featured })} />);
  expect(screen.getByText("Summarise the quarterly report")).toBeInTheDocument();
  expect(screen.getByText("3 of 5")).toBeInTheDocument();
  expect(screen.getAllByRole("listitem")).toHaveLength(4);
  expect(screen.getByText("Email the summary to the team")).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Open q3-report.pdf" }));
  expect(host.posts("file.open")).toEqual([{ kind: "file.open", path: "C:\\Users\\demo\\Documents\\q3-report.pdf" }]);
  fireEvent.click(screen.getByRole("button", { name: "View result" }));
  expect(host.posts("job.open")).toEqual([{ kind: "job.open", id: "job-working" }]);
  fireEvent.click(screen.getByRole("button", { name: "Dismiss" }));
  expect(host.posts("job.dismiss")).toEqual([{ kind: "job.dismiss", id: "job-working" }]);
});

it("the featured job renders its plan as a checklist with how many steps are done", () => {
  mockHost();
  const plan = [true, true, false, false, false].map((done, i) => ({ text: `Step ${i + 1}`, done }));
  const { rerender } = render(<Notch state={open("home", { featured: { ...fixture.featured!, plan } })} />);
  expect(screen.getByText("2 of 5 steps")).toBeInTheDocument();
  const boxes = screen.getAllByRole("checkbox");
  expect(boxes).toHaveLength(5);
  expect(boxes.map((b) => b.getAttribute("aria-checked"))).toEqual(["true", "true", "false", "false", "false"]);
  expect(screen.getByText("Step 3")).toBeInTheDocument();
  rerender(<Notch state={open("home", { featured: { ...fixture.featured!, plan: [] } })} />);
  expect(screen.queryByText(/steps$/)).toBeNull();
  expect(screen.queryAllByRole("checkbox")).toHaveLength(0);
});

it("a working job can be stopped", () => {
  const host = mockHost();
  render(<Notch state={open()} />);
  fireEvent.click(screen.getByRole("button", { name: "Stop" }));
  expect(host.posts("job.stop")).toEqual([{ kind: "job.stop", id: "job-working" }]);
});

it.each(["working", "queued"] as const)("a %s job takes an instruction: Enter posts job.steer and clears the box", (state) => {
  const host = mockHost();
  render(<Notch state={open("home", { featured: { ...fixture.featured!, state } })} />);
  const box = screen.getByPlaceholderText("Tell this job something…");
  fireEvent.change(box, { target: { value: "  use the March figures " } });
  fireEvent.keyDown(box, { key: "Enter" });
  expect(host.posts("job.steer")).toEqual([{ kind: "job.steer", id: "job-working", text: "use the March figures" }]);
  expect(box).toHaveValue("");
  fireEvent.keyDown(box, { key: "Enter" }); // empty: nothing posts
  expect(host.posts("job.steer")).toHaveLength(1);
  expect(screen.queryByRole("button", { name: "Try again" })).toBeNull();
});

it("a done job asks for a follow-up: Enter posts job.followUp", () => {
  const host = mockHost();
  render(<Notch state={open("home", { featured: { ...fixture.featured!, state: "done" } })} />);
  expect(screen.queryByPlaceholderText("Tell this job something…")).toBeNull();
  const box = screen.getByPlaceholderText("Ask a follow-up…");
  fireEvent.change(box, { target: { value: "now make it shorter" } });
  fireEvent.keyDown(box, { key: "Enter", shiftKey: true }); // Shift+Enter is a new line
  expect(host.posts("job.followUp")).toEqual([]);
  fireEvent.keyDown(box, { key: "Enter" });
  expect(host.posts("job.followUp")).toEqual([{ kind: "job.followUp", id: "job-working", text: "now make it shorter" }]);
  expect(host.posts("job.steer")).toEqual([]);
});

it.each(["failed", "stopped"] as const)("a %s job offers Try again, which posts job.retry", (state) => {
  const host = mockHost();
  render(<Notch state={open("home", { featured: { ...fixture.featured!, state } })} />);
  fireEvent.click(screen.getByRole("button", { name: "Try again" }));
  expect(host.posts("job.retry")).toEqual([{ kind: "job.retry", id: "job-working" }]);
  expect(screen.queryByPlaceholderText("Tell this job something…")).toBeNull();
});

it("tabs post notch.tab; the gear opens Settings, which grows the panel; the apps pill opens Settings", () => {
  const host = mockHost();
  render(<Notch state={open()} />);
  fireEvent.click(screen.getByRole("button", { name: "Chat" }));
  expect(host.posts("notch.tab")).toEqual([{ kind: "notch.tab", tab: "chat" }]);
  expect(screen.getByPlaceholderText("Ask OpenClicky…")).toBeInTheDocument();
  expect(root()).not.toHaveClass("notch--settings");

  fireEvent.click(screen.getByRole("button", { name: "Settings" }));
  expect(host.posts("notch.tab").at(-1)).toEqual({ kind: "notch.tab", tab: "settings" });
  expect(screen.getByRole("navigation", { name: "Settings sections" })).toBeInTheDocument();
  expect(root()).toHaveClass("notch--settings");

  fireEvent.click(screen.getByRole("button", { name: "Home" }));
  fireEvent.click(screen.getByRole("button", { name: "2 apps +" }));
  expect(host.posts("notch.tab").at(-1)).toEqual({ kind: "notch.tab", tab: "settings" });
});

it("a host tab change switches the tab", () => {
  mockHost();
  const view = render(<Notch state={open("home")} />);
  view.rerender(<Notch state={open("chat")} />);
  expect(screen.getByPlaceholderText("Ask OpenClicky…")).toBeInTheDocument();
});

it("the composer starts as a single-line textarea", () => {
  mockHost();
  render(<Notch state={open("chat")} />);
  expect(screen.getByPlaceholderText("Ask OpenClicky…")).toHaveAttribute("rows", "1");
});

it("Chat renders bubbles and event captions, and shows the empty state", () => {
  mockHost();
  const view = render(<Notch state={open("chat")} />);
  expect(screen.getByText("Summarise the quarterly report")).toHaveAttribute("data-role", "user");
  expect(screen.getByText(/On it/)).toHaveAttribute("data-role", "assistant");
  expect(screen.getByText("Job started")).toHaveAttribute("data-role", "event");
  view.rerender(<Notch state={open("chat", { chat: { rows: [] } })} />);
  expect(screen.getByText("No messages yet")).toBeInTheDocument();
});

it("ChatTab renders drawing row with Replay, Print, Remove", () => {
  const host = mockHost();
  render(<Notch state={open("chat")} />);
  const caption = screen.getByText("Drew a box around the total");
  expect(caption).toHaveAttribute("data-role", "event");
  const row = caption.parentElement!;
  expect(within(row).getByRole("button", { name: "Replay" })).toHaveAttribute("aria-expanded", "false");
  expect(within(row).getByRole("button", { name: "Print" })).toBeInTheDocument();
  expect(within(row).getByRole("button", { name: "Remove" })).toBeInTheDocument();
  // Ordinary rows carry no actions, and nothing was posted by merely showing the row.
  expect(screen.getByText("Job started").closest(".notch-chat__drawing")).toBeNull();
  expect(host.posts("drawing.action")).toEqual([]);
  expect(screen.queryByRole("group", { name: "Replay mode" })).toBeNull();
});

it("drawing.action posted with mode", () => {
  const host = mockHost();
  render(<Notch state={open("chat")} />);
  const id = "0f1e2d3c4b5a69788796a5b4c3d2e1f0";
  const replay = screen.getByRole("button", { name: "Replay" });

  fireEvent.click(replay);
  expect(replay).toHaveAttribute("aria-expanded", "true");
  const modes = screen.getByRole("group", { name: "Replay mode" });
  expect(within(modes).getAllByRole("button").map((b) => b.textContent)).toEqual(["Window", "Full screen", "Board"]);
  fireEvent.click(within(modes).getByRole("button", { name: "Replay full screen" }));
  expect(screen.queryByRole("group", { name: "Replay mode" })).toBeNull(); // picking a mode closes the choice

  fireEvent.click(replay);
  fireEvent.click(screen.getByRole("button", { name: "Replay in a window" }));
  fireEvent.click(replay);
  fireEvent.click(screen.getByRole("button", { name: "Replay on a board" }));
  fireEvent.click(screen.getByRole("button", { name: "Print" }));
  fireEvent.click(screen.getByRole("button", { name: "Remove" }));

  expect(host.posts("drawing.action")).toEqual(["replayFullScreen", "replayWindow", "replayBoard", "print", "remove"]
    .map((action) => ({ kind: "drawing.action", id, action })));
});

it("Enter submits and clears; Shift+Enter and empty input do not", () => {
  const host = mockHost();
  render(<Notch state={open("chat")} />);
  const input = screen.getByPlaceholderText("Ask OpenClicky…") as HTMLTextAreaElement;

  fireEvent.keyDown(input, { key: "Enter" });
  expect(host.posts("text.submit")).toEqual([]);

  fireEvent.change(input, { target: { value: "  what is on my calendar  " } });
  fireEvent.keyDown(input, { key: "Enter", shiftKey: true });
  expect(host.posts("text.submit")).toEqual([]);
  expect(input.value).toBe("  what is on my calendar  ");

  fireEvent.keyDown(input, { key: "Enter" });
  expect(host.posts("text.submit")).toEqual([{ kind: "text.submit", text: "what is on my calendar" }]);
  expect(input.value).toBe("");
});

it("the composer asks for focus on a click and gives it back on Escape and blur", () => {
  const host = mockHost();
  render(<Notch state={open("chat")} />);
  const input = screen.getByPlaceholderText("Ask OpenClicky…");
  fireEvent.pointerDown(input);
  expect(host.posts("surface.focus")).toEqual([{ kind: "surface.focus", wanted: true }]);
  input.focus();
  fireEvent.keyDown(input, { key: "Escape" });
  expect(host.posts("surface.focus").at(-1)).toEqual({ kind: "surface.focus", wanted: false });
  fireEvent.pointerDown(input);
  fireEvent.blur(input);
  expect(host.posts("surface.focus").at(-1)).toEqual({ kind: "surface.focus", wanted: false });
});

it("the talk pill shows the keys and toggles talking", () => {
  const host = mockHost();
  render(<Notch state={open("chat")} />);
  fireEvent.click(screen.getByRole("button", { name: "Hold Ctrl + Alt to talk" }));
  expect(host.posts("talk.toggle")).toEqual([{ kind: "talk.toggle" }]);
});

it("chat.delta appends streamed text to its row", () => {
  const host = mockHost();
  render(<ShellApp />);
  host.send({ kind: "shell.state", state: open("chat") });
  host.send({ kind: "chat.delta", rowId: "row-2", text: " Revenue is up." });
  expect(screen.getByText("On it. I will start with the revenue table. Revenue is up.")).toBeInTheDocument();
  host.send({ kind: "chat.delta", rowId: "nope", text: "ignored" });
  expect(screen.queryByText(/ignored/)).toBeNull();
});

it("posts surface.regions, debounced 16 ms, containing the panel rect, and re-posts when the panel grows", async () => {
  vi.useFakeTimers();
  vi.spyOn(Element.prototype, "getBoundingClientRect").mockImplementation(function (this: Element) {
    const make = (left: number, top: number, width: number, height: number) => ({ left, top, width, height, right: left + width, bottom: top + height, x: left, y: top, toJSON() {} }) as DOMRect;
    if (this.classList.contains("notch__panel")) return this.parentElement!.classList.contains("notch--settings") ? make(500, 0, 920, 720) : make(694, 0, 532, 340);
    return make(0, 0, 0, 0);
  });
  const style = getComputedStyle.bind(window);
  vi.spyOn(window, "getComputedStyle").mockImplementation((el: Element) => (el.classList.contains("notch__panel")
    ? ({ borderTopLeftRadius: "10px", borderTopRightRadius: "10px", borderBottomLeftRadius: "26px", borderBottomRightRadius: "26px" } as CSSStyleDeclaration)
    : style(el)));
  const host = mockHost();
  render(<ShellApp />);
  host.send({ kind: "shell.state", state: open() });
  await act(async () => {});
  expect(host.posts("surface.regions")).toHaveLength(0);
  act(() => { vi.advanceTimersByTime(16); });
  const first = host.posts("surface.regions");
  expect(first).toHaveLength(1);
  expect(first[0].rects).toEqual([{ x: 694, y: 0, w: 532, h: 340, r: 26 }]); // the panel only, with the 26 px sheet radius

  fireEvent.click(screen.getByRole("button", { name: "Settings" }));
  await act(async () => {});
  act(() => { vi.advanceTimersByTime(16); });
  expect(host.posts("surface.regions").at(-1).rects).toContainEqual({ x: 500, y: 0, w: 920, h: 720, r: 26 });
});

it("a composer that unmounts while focused (tab switch) gives the keyboard focus back, and auto-close still works", () => {
  vi.useFakeTimers();
  const host = mockHost();
  render(<Notch state={open("chat")} />);
  const input = screen.getByPlaceholderText("Ask OpenClicky…");
  input.focus();
  expect(host.posts("surface.focus").at(-1)).toEqual({ kind: "surface.focus", wanted: true });
  fireEvent.click(screen.getByRole("button", { name: "Home" }));
  expect(screen.queryByPlaceholderText("Ask OpenClicky…")).toBeNull();
  expect(host.posts("surface.focus").at(-1)).toEqual({ kind: "surface.focus", wanted: false });

  fireEvent.pointerLeave(root());
  act(() => { vi.advanceTimersByTime(150); });
  expect(root()).toHaveClass("notch--closing");
});

it("the host closing the notch while the composer is focused releases the focus and blocks nothing", () => {
  vi.useFakeTimers();
  const host = mockHost();
  const view = render(<Notch state={open("chat")} />);
  screen.getByPlaceholderText("Ask OpenClicky…").focus();
  view.rerender(<Notch state={closed({ notch: { ...fixture.notch, tab: "chat", open: false } })} />);
  expect(root()).toHaveClass("notch--closing");
  expect(host.posts("surface.focus").at(-1)).toEqual({ kind: "surface.focus", wanted: false });
  act(() => { vi.advanceTimersByTime(300); });
  expect(root()).toHaveClass("notch--collapsed");
  fireEvent.pointerEnter(root());
  fireEvent.pointerLeave(root());
  act(() => { vi.advanceTimersByTime(150); });
  expect(root()).toHaveClass("notch--closing");
});

it("a focused composer keeps the panel open until it blurs", () => {
  vi.useFakeTimers();
  mockHost();
  render(<Notch state={open("chat")} />);
  const input = screen.getByPlaceholderText("Ask OpenClicky…");
  input.focus();
  fireEvent.pointerLeave(root());
  act(() => { vi.advanceTimersByTime(1000); });
  expect(root()).toHaveClass("notch--open");
  input.blur();
  act(() => { vi.advanceTimersByTime(150); });
  expect(root()).toHaveClass("notch--closing");
});

it("the draft survives switching tabs and collapsing", () => {
  vi.useFakeTimers();
  mockHost();
  render(<Notch state={open("chat")} />);
  fireEvent.change(screen.getByPlaceholderText("Ask OpenClicky…"), { target: { value: "half a thought" } });
  fireEvent.click(screen.getByRole("button", { name: "Home" }));
  fireEvent.click(screen.getByRole("button", { name: "Chat" }));
  expect(screen.getByPlaceholderText("Ask OpenClicky…")).toHaveValue("half a thought");
  fireEvent.pointerLeave(root());
  act(() => { vi.advanceTimersByTime(450); });
  expect(root()).toHaveClass("notch--collapsed");
  fireEvent.pointerEnter(root());
  expect(screen.getByPlaceholderText("Ask OpenClicky…")).toHaveValue("half a thought");
});

it("dismissing the last hovered card closes the panel after the grace period", () => {
  vi.useFakeTimers();
  mockHost();
  const view = render(<Notch state={closed({ cards: [fixture.cards[0]] })} />);
  fireEvent.pointerEnter(root());
  fireEvent.pointerLeave(root());
  fireEvent.pointerEnter(view.container.querySelector(".notch-cards")!); // the pointer moved onto the card
  act(() => { vi.advanceTimersByTime(1000); });
  expect(root()).toHaveClass("notch--open");

  fireEvent.click(screen.getByRole("button", { name: "Dismiss notification" })); // the card unmounts under the pointer
  expect(view.container.querySelector(".notch-cards")).toBeNull();
  act(() => { vi.advanceTimersByTime(149); });
  expect(root()).toHaveClass("notch--open");
  act(() => { vi.advanceTimersByTime(1); });
  expect(root()).toHaveClass("notch--closing");
});

it("a host-opened panel the pointer never enters closes itself after 6 s and tells the host", () => {
  vi.useFakeTimers();
  const host = mockHost();
  const view = render(<Notch state={closed()} />);
  view.rerender(<Notch state={open()} />);
  act(() => { vi.advanceTimersByTime(5_999); });
  expect(root()).toHaveClass("notch--open");
  act(() => { vi.advanceTimersByTime(1); });
  expect(root()).toHaveClass("notch--closing");
  expect(host.posts("notch.open")).toEqual([{ kind: "notch.open", open: false }]);
});

it("a host-opened Settings panel is deliberate: it does not idle-close, while a Home one does", () => {
  vi.useFakeTimers();
  mockHost();
  const settings = render(<Notch state={closed()} />);
  settings.rerender(<Notch state={open("settings")} />);
  act(() => { vi.advanceTimersByTime(7_000); });
  expect(root()).toHaveClass("notch--open");
  settings.unmount();
  const home = render(<Notch state={closed()} />);
  home.rerender(<Notch state={open("home")} />);
  act(() => { vi.advanceTimersByTime(6_000); });
  expect(root()).toHaveClass("notch--closing");
});

it("a host-opened panel stays open once the pointer enters it", () => {
  vi.useFakeTimers();
  mockHost();
  const view = render(<Notch state={closed()} />);
  view.rerender(<Notch state={open()} />);
  act(() => { vi.advanceTimersByTime(3_000); });
  fireEvent.pointerEnter(root());
  act(() => { vi.advanceTimersByTime(60_000); });
  expect(root()).toHaveClass("notch--open");
});

it("composer.focus (text mode) focuses the chat field, also when it arrives before the chat tab is shown", () => {
  const host = mockHost();
  const { rerender } = render(<Notch state={closed()} />);
  host.send({ kind: "composer.focus" }); // the host posts it right after opening the Chat tab
  rerender(<Notch state={open("chat")} />);
  const field = screen.getByRole("textbox", { name: "Message OpenClicky" });
  expect(field).toHaveFocus();
  expect(host.posts("surface.focus").at(-1)).toEqual({ kind: "surface.focus", wanted: true });

  field.blur();
  fireEvent.click(screen.getByRole("button", { name: "Home" }));
  fireEvent.click(screen.getByRole("button", { name: "Chat" }));
  expect(screen.getByRole("textbox", { name: "Message OpenClicky" })).not.toHaveFocus(); // taken once, not on every visit
  host.send({ kind: "composer.focus" });
  expect(screen.getByRole("textbox", { name: "Message OpenClicky" })).toHaveFocus();
});

it("a composer.focus left over when the notch collapses does not fire on a later open", () => {
  vi.useFakeTimers();
  const host = mockHost();
  const { rerender } = render(<Notch state={open("home")} />);
  host.send({ kind: "composer.focus" }); // the chat tab never showed
  rerender(<Notch state={closed()} />);
  act(() => { vi.advanceTimersByTime(1_000); });
  rerender(<Notch state={open("chat")} />);
  expect(screen.getByRole("textbox", { name: "Message OpenClicky" })).not.toHaveFocus();
});

it("talk hints follow the talk trigger", () => {
  mockHost();
  const state = open("chat");
  render(<Notch state={{ ...state, settings: { ...state.settings, shortcuts: { ...state.settings.shortcuts, talk: { keys: ["Ctrl", "Alt"], trigger: "doubleTap" } } } }} />);
  expect(screen.getByRole("button", { name: "Double-tap Ctrl + Alt to talk" })).toBeInTheDocument();
});
