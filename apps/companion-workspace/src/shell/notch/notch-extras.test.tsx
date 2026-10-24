import { afterEach, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import type { ShellState } from "../contract";
import { Notch } from "./Notch";

const fixture = JSON.parse(readFileSync(resolve(__dirname, "../../../../../tests/fixtures/shell-state.v1.json"), "utf8")) as ShellState;
const open = (tab: ShellState["notch"]["tab"]): ShellState => ({ ...fixture, notch: { tab, open: true } });

afterEach(() => { cleanup(); vi.useRealTimers(); vi.restoreAllMocks(); delete window.chrome; });

function mockHost() {
  const postMessage = vi.fn();
  window.chrome = { webview: { postMessage, addEventListener: () => {}, removeEventListener: () => {} } };
  return { posts: (kind: string) => postMessage.mock.calls.map((c) => c[0]).filter((m) => m.kind === kind) };
}

it("the deck appears beside the open Home panel only, without the featured job", () => {
  mockHost();
  const { container, rerender } = render(<Notch state={open("home")} />);
  expect(container.querySelectorAll(".deck-card").length).toBeGreaterThan(0);
  expect(container.querySelector(".deck-card[data-region]")).not.toBeNull();
  expect(container.querySelector(".notch__panel .deck-card")).toBeNull();
  const n = fixture.jobs.filter((j) => j.id !== fixture.featured?.id).length;
  expect(container.querySelectorAll(".deck-card")).toHaveLength(Math.min(3, Math.ceil(n / 2)) + Math.min(3, Math.floor(n / 2)));
  rerender(<Notch state={open("chat")} />);
  expect(container.querySelector(".deck-card")).toBeNull();
});

it("cards show under the notch even while it is collapsed", () => {
  mockHost();
  const { container } = render(<Notch state={{ ...fixture, notch: { tab: "home", open: false } }} />);
  expect(container.querySelectorAll(".notch-card[data-region]")).toHaveLength(3);
});

it("the Document tab shows the host's document, and closing it goes back to Home", () => {
  const host = mockHost();
  render(<Notch state={open("document")} />);
  expect(screen.getByRole("heading", { name: "notes.md" })).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Close" }));
  expect(host.posts("notch.tab").at(-1)).toEqual({ kind: "notch.tab", tab: "home" });
});

const rootEl = () => screen.getByLabelText("OpenClicky");

it("hovering a card keeps the open panel and deck up; leaving the cards closes after the grace", () => {
  vi.useFakeTimers();
  const host = mockHost();
  const { container } = render(<Notch state={open("home")} />);
  fireEvent.pointerEnter(rootEl());
  fireEvent.pointerLeave(rootEl());
  fireEvent.pointerEnter(container.querySelector(".notch-cards")!); // pointer moved from the panel onto a card
  act(() => { vi.advanceTimersByTime(1000); });
  expect(rootEl()).toHaveClass("notch--open");
  expect(container.querySelector(".deck-card")).not.toBeNull();
  expect(host.posts("notch.open").at(-1)).not.toEqual({ kind: "notch.open", open: false });

  fireEvent.pointerLeave(container.querySelector(".notch-cards")!);
  act(() => { vi.advanceTimersByTime(150); });
  expect(rootEl()).toHaveClass("notch--closing");
});

it("hovering a card never opens a collapsed panel", () => {
  vi.useFakeTimers();
  const host = mockHost();
  const { container } = render(<Notch state={{ ...fixture, notch: { tab: "home", open: false } }} />);
  fireEvent.pointerEnter(container.querySelector(".notch-cards")!);
  act(() => { vi.advanceTimersByTime(1000); });
  expect(rootEl()).toHaveClass("notch--collapsed");
  expect(host.posts("notch.open")).toEqual([]);
});

it("cards are newest first: index 0 is the top card", () => {
  mockHost();
  const { container } = render(<Notch state={{ ...fixture, notch: { tab: "home", open: false } }} />);
  const titles = [...container.querySelectorAll(".notch-card__title")].map((n) => n.textContent);
  expect(titles).toEqual(fixture.cards.slice(0, 3).map((c) => c.title));
});

it("while the panel closes, cards and deck report no regions", () => {
  vi.useFakeTimers();
  mockHost();
  const { container } = render(<Notch state={open("home")} />);
  fireEvent.pointerLeave(rootEl());
  act(() => { vi.advanceTimersByTime(150); });
  expect(rootEl()).toHaveClass("notch--closing");
  expect(container.querySelector(".notch-card")).not.toBeNull();
  expect(container.querySelector(".notch-card[data-region]")).toBeNull();
  expect(container.querySelector(".deck-card")).toBeNull();
});
