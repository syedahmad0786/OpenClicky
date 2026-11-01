import { afterEach, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
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
  return () => postMessage.mock.calls.map((c) => c[0]).filter((m) => m.kind === "settings.action");
}
afterEach(() => { cleanup(); delete window.chrome; vi.useRealTimers(); });
const click = (name: string | RegExp) => fireEvent.click(screen.getByRole("button", { name }));
const type = (label: string, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } });

// ---- Notes

it("Notes: lists notes, adds one, edits, deletes after a confirm and exports", () => {
  const actions = host();
  const { rerender } = render(<SettingsPanel state={make("notes")} />);
  expect(screen.getByRole("heading", { name: "Notes" })).toBeInTheDocument();
  expect(screen.getByText("Dentist")).toBeInTheDocument();
  expect(screen.getByText("2 of 20 notes")).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Add note" })).toBeDisabled(); // no title yet

  type("Title", "Groceries");
  type("Note", "Milk, eggs");
  click("Add note");
  expect(screen.getByRole("button", { name: "Saving…" })).toHaveAttribute("aria-busy", "true");
  // The host saved it: the list changed, so the editor clears.
  rerender(<SettingsPanel state={make("notes", (s) => { s.settings.notes.items.unshift({ id: "n3", title: "Groceries", text: "Milk, eggs", updatedAt: "2026-10-25T08:00:00.000Z" }); })} />);
  expect(screen.getByLabelText("Title")).toHaveValue("");

  click("Edit Dentist");
  expect(screen.getByLabelText("Title")).toHaveValue("Dentist");
  type("Note", "Friday 10am");
  click("Save note");

  click("Delete Gift ideas");
  expect(screen.getByRole("alert")).toHaveTextContent("Delete this note? This cannot be undone.");
  click("Delete");
  click("Export as Markdown");
  expect(actions()).toEqual([
    { kind: "settings.action", action: "note.save", arg: JSON.stringify({ id: "", title: "Groceries", text: "Milk, eggs" }) },
    { kind: "settings.action", action: "note.save", arg: JSON.stringify({ id: "5f0c2a9e7b1d4c3e8a6f9b2d1c0e7a4b", title: "Dentist", text: "Friday 10am" }) },
    { kind: "settings.action", action: "note.delete", arg: "a1b2c3d4e5f60718293a4b5c6d7e8f90" },
    { kind: "settings.action", action: "note.export" },
  ]);
});

it("Notes: a refused save keeps the draft; search filters; empty and locked states", () => {
  vi.useFakeTimers();
  host();
  const { rerender } = render(<SettingsPanel state={make("notes")} />);
  type("Title", "Kept");
  click("Add note");
  act(() => { vi.advanceTimersByTime(4_000); });
  expect(screen.getByRole("button", { name: "Add note" })).not.toHaveAttribute("aria-busy");
  expect(screen.getByLabelText("Title")).toHaveValue("Kept");

  fireEvent.change(screen.getByRole("searchbox", { name: "Search notes" }), { target: { value: "gift" } });
  expect(screen.queryByText("Dentist")).toBeNull();
  expect(screen.getByText("Gift ideas")).toBeInTheDocument();

  rerender(<SettingsPanel state={make("notes", (s) => { s.settings.notes.items = []; })} />);
  expect(screen.getByText(/No notes yet/)).toBeInTheDocument();

  rerender(<SettingsPanel state={make("notes", (s) => { s.settings.notes = { editable: false, error: "The saved notes could not be read.", items: [], limit: 20 }; })} />);
  expect(screen.getByText("Editing is locked")).toBeInTheDocument();
  expect(screen.getByText("The saved notes could not be read.")).toBeInTheDocument();
  expect(screen.getByLabelText("Title")).toBeDisabled();
  expect(screen.queryByText(/No notes yet/)).toBeNull();
});

// ---- Clipboard shelf

it("Clipboard: lists items with pinned and next badges and posts every item action", () => {
  const actions = host();
  render(<SettingsPanel state={make("clipboard")} />);
  expect(screen.getByRole("heading", { name: "Clipboard shelf" })).toBeInTheDocument();
  expect(screen.getByText("Order #4471 ships Monday")).toBeInTheDocument();
  expect(screen.getByText("Pinned")).toBeInTheDocument();
  expect(screen.getByText("Next")).toBeInTheDocument();
  expect(screen.getByText("2 of 100 items")).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Move Order #4471 ships Monday up" })).toBeDisabled();
  expect(screen.getByRole("button", { name: "Move https://example.com/report down" })).toBeDisabled();

  fireEvent.click(screen.getByRole("switch", { name: "Collect copied text" }));
  type("Add text", "Call Sam back");
  click("Add to shelf");
  click("Copy Order #4471 ships Monday");
  click("Unpin Order #4471 ships Monday");
  click("Pin https://example.com/report");
  click("Move https://example.com/report up");
  click("Move Order #4471 ships Monday down");
  click("Delete https://example.com/report"); // no confirm, as the classic shelf
  click("Copy next");
  click("Copy all");
  click("Edit Order #4471 ships Monday");
  type("Edit item", "Order #4471 ships Tuesday");
  click("Save edit");
  click("Clear shelf");
  expect(screen.getByRole("alert")).toHaveTextContent("Delete everything on the clipboard shelf?");
  fireEvent.click(screen.getAllByRole("button", { name: "Clear shelf" }).at(-1)!);
  expect(actions()).toEqual([
    { kind: "settings.action", action: "clipboard.collect", arg: "on" },
    { kind: "settings.action", action: "clipboard.add", arg: "Call Sam back" },
    { kind: "settings.action", action: "clipboard.copy", arg: "12" },
    { kind: "settings.action", action: "clipboard.unpin", arg: "12" },
    { kind: "settings.action", action: "clipboard.pin", arg: "13" },
    { kind: "settings.action", action: "clipboard.moveUp", arg: "13" },
    { kind: "settings.action", action: "clipboard.moveDown", arg: "12" },
    { kind: "settings.action", action: "clipboard.delete", arg: "13" },
    { kind: "settings.action", action: "clipboard.copyNext" },
    { kind: "settings.action", action: "clipboard.copyAll" },
    { kind: "settings.action", action: "clipboard.edit", arg: JSON.stringify({ id: "12", text: "Order #4471 ships Tuesday" }) },
    { kind: "settings.action", action: "clipboard.clear" },
  ]);
});

it("Clipboard: a long item cannot be edited here; empty, collecting and unavailable states", () => {
  host();
  const { rerender } = render(<SettingsPanel state={make("clipboard", (s) => { s.settings.clipboard.items[0].truncated = true; s.settings.clipboard.collecting = true; })} />);
  expect(screen.queryByRole("button", { name: "Edit Order #4471 ships Monday" })).toBeNull();
  expect(screen.getByText(/longer than shown/)).toBeInTheDocument();
  expect(screen.getByRole("switch", { name: "Collect copied text" })).toHaveAttribute("aria-checked", "true");

  rerender(<SettingsPanel state={make("clipboard", (s) => { s.settings.clipboard.items = []; s.settings.clipboard.nextId = null; })} />);
  expect(screen.getByText(/Nothing on the shelf yet/)).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "Copy all" })).toBeNull();

  rerender(<SettingsPanel state={make("clipboard", (s) => {
    s.settings.clipboard = { available: false, collecting: false, error: "The classic clipboard shelf window is open.", items: [], nextId: null, limit: 100 };
  })} />);
  expect(screen.getByText("The classic clipboard shelf window is open.")).toBeInTheDocument();
  expect(screen.queryByRole("switch")).toBeNull();
});

// ---- Tasks

it("Tasks: shows runs with status and steps, and posts pause, resume, stop, export and clear", () => {
  const actions = host();
  const { rerender } = render(<SettingsPanel state={make("tasks", (s) => { s.settings.tasks.active = true; })} />);
  expect(screen.getByRole("heading", { name: "Tasks" })).toBeInTheDocument();
  expect(screen.getByText("Rename the screenshots on the desktop")).toBeInTheDocument();
  expect(screen.getByText("Done")).toBeInTheDocument();
  expect(screen.getByText("Failed")).toBeInTheDocument();
  expect(screen.getByText(/Approve each action/)).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Resume" })).toBeDisabled();

  click("Show steps (2)");
  expect(screen.getByRole("list", { name: "Steps of Rename the screenshots on the desktop" })).toHaveTextContent("Renamed 4 files.");
  click("Pause");
  click("Stop");
  click("Export events of Fill in the timesheet");
  rerender(<SettingsPanel state={make("tasks", (s) => { s.settings.tasks.active = true; s.settings.tasks.paused = true; })} />);
  expect(screen.getByRole("button", { name: "Pause" })).toBeDisabled();
  click("Resume");
  click("Clear history");
  click("Clear history"); // the confirm's button
  expect(actions()).toEqual([
    { kind: "settings.action", action: "task.pause" },
    { kind: "settings.action", action: "task.stop" },
    { kind: "settings.action", action: "task.export", arg: "run-1" },
    { kind: "settings.action", action: "task.resume" },
    { kind: "settings.action", action: "task.clear" },
  ]);
});

it("Tasks: nothing running and no history", () => {
  host();
  render(<SettingsPanel state={make("tasks", (s) => { s.settings.tasks.runs = []; })} />);
  expect(screen.getByText("No task is running.")).toBeInTheDocument();
  expect(screen.getByText(/No tasks yet/)).toBeInTheDocument();
  for (const name of ["Pause", "Resume", "Stop"]) expect(screen.getByRole("button", { name })).toBeDisabled();
  expect(screen.queryByRole("button", { name: "Clear history" })).toBeNull();
});

// ---- Memory

it("Memory: edits the profile and memories, exports and clears", () => {
  const actions = host();
  render(<SettingsPanel state={make("memory")} />);
  expect(screen.getByRole("heading", { name: "Memory" })).toBeInTheDocument();
  expect(screen.getByLabelText("Name")).toHaveValue("Clicky");
  expect(screen.getByText("Learned")).toBeInTheDocument();
  expect(screen.getByText("Added by you")).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Save profile" })).toBeDisabled(); // unchanged

  type("Instructions", "Use British spelling. Keep it short.");
  click("Save profile");
  type("Add a memory", "Has a cat called Miso");
  click("Add memory");
  click("Edit Prefers mornings for meetings.");
  type("Edit memory", "Prefers afternoons.");
  click("Save memory");
  click("Delete Works on the OpenClicky project.");
  click("Delete");
  click("Export");
  click("Clear memory");
  click("Clear memory");
  expect(actions()).toEqual([
    { kind: "settings.action", action: "memory.profile", arg: JSON.stringify({ name: "Clicky", personality: "Warm and brief.", instructions: "Use British spelling. Keep it short." }) },
    { kind: "settings.action", action: "memory.add", arg: "Has a cat called Miso" },
    { kind: "settings.action", action: "memory.update", arg: JSON.stringify({ id: "f1", text: "Prefers afternoons." }) },
    { kind: "settings.action", action: "memory.delete", arg: "f2" },
    { kind: "settings.action", action: "exportMemory" },
    { kind: "settings.action", action: "memory.clear" },
  ]);
});

it("Memory: nothing saved yet, and an unreadable file locks editing but keeps Export", () => {
  host();
  const { rerender } = render(<SettingsPanel state={make("memory", (s) => {
    s.settings.memory = { editable: true, saved: false, error: null, name: "", personality: "", instructions: "", facts: [], limit: 100 };
  })} />);
  expect(screen.getByText(/No memories yet/)).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Export" })).toBeDisabled();

  rerender(<SettingsPanel state={make("memory", (s) => {
    s.settings.memory = { editable: false, saved: true, error: "Memory could not be read.", name: "", personality: "", instructions: "", facts: [], limit: 100 };
  })} />);
  expect(screen.getByText("Memory could not be read.")).toBeInTheDocument();
  expect(screen.getByLabelText("Name")).toBeDisabled();
  expect(screen.getByRole("button", { name: "Add memory" })).toBeDisabled();
  expect(screen.getByRole("button", { name: "Export" })).toBeEnabled();
});
