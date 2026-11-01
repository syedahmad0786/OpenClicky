import { afterEach, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import type { NotchCard } from "../contract";
import { NotchCards } from "./NotchCards";

afterEach(() => { cleanup(); vi.useRealTimers(); vi.restoreAllMocks(); delete window.chrome; });

function mockHost() {
  const postMessage = vi.fn();
  window.chrome = { webview: { postMessage, addEventListener: () => {}, removeEventListener: () => {} } };
  return { posts: (kind: string) => postMessage.mock.calls.map((c) => c[0]).filter((m) => m.kind === kind) };
}
const card = (id: string, patch: Partial<NotchCard> = {}): NotchCard => ({ id, kind: "toast", title: `Card ${id}`, actions: [], ...patch });

it("cards are newest first: the first three show, index 0 on top, each a reported region", () => {
  mockHost();
  const { container } = render(<NotchCards cards={[card("a"), card("b"), card("c"), card("d")]} />);
  const titles = [...container.querySelectorAll("[data-region] .notch-card__title")].map((n) => n.textContent);
  expect(titles).toEqual(["Card a", "Card b", "Card c"]);
});

it("reports no region while inactive (panel closing), but keeps the cards", () => {
  mockHost();
  const { container } = render(<NotchCards cards={[card("a")]} active={false} />);
  expect(container.querySelector(".notch-card")).not.toBeNull();
  expect(container.querySelector("[data-region]")).toBeNull();
});

it("focus from a mouse click does not freeze the countdown", () => {
  vi.useFakeTimers();
  const host = mockHost();
  render(<NotchCards cards={[card("m", { autoDismissMs: 1000, actions: [{ id: "x", label: "Go", style: "secondary" }] })]} />);
  fireEvent.focus(screen.getByRole("button", { name: "Go" })); // not :focus-visible
  act(() => { vi.advanceTimersByTime(1000); });
  expect(host.posts("card.dismiss")).toEqual([{ kind: "card.dismiss", cardId: "m" }]);
});

it("posts card.action for a button and card.dismiss for the close button", () => {
  const host = mockHost();
  render(<NotchCards cards={[card("n", { kind: "needsYou", actions: [{ id: "a", label: "Fare A", style: "primary" }] })]} />);
  fireEvent.click(screen.getByRole("button", { name: "Fare A" }));
  expect(host.posts("card.action")).toEqual([{ kind: "card.action", cardId: "n", actionId: "a" }]);
  fireEvent.click(screen.getByRole("button", { name: "Dismiss notification" }));
  expect(host.posts("card.dismiss")).toEqual([{ kind: "card.dismiss", cardId: "n" }]);
  expect(screen.queryByText("Card n")).toBeNull();
});

it("auto-dismisses after autoDismissMs, pausing while the pointer is over the card", () => {
  vi.useFakeTimers();
  const host = mockHost();
  render(<NotchCards cards={[card("t", { autoDismissMs: 3000 })]} />);
  const el = screen.getByText("Card t").closest("[data-region]")!;

  act(() => { vi.advanceTimersByTime(2000); });
  fireEvent.pointerEnter(el);
  act(() => { vi.advanceTimersByTime(10_000); });
  expect(host.posts("card.dismiss")).toEqual([]);
  expect(screen.getByText("Card t")).toBeInTheDocument();

  fireEvent.pointerLeave(el);
  act(() => { vi.advanceTimersByTime(999); });
  expect(host.posts("card.dismiss")).toEqual([]);
  act(() => { vi.advanceTimersByTime(1); });
  expect(host.posts("card.dismiss")).toEqual([{ kind: "card.dismiss", cardId: "t" }]);
});

it("cards without autoDismissMs stay until dismissed", () => {
  vi.useFakeTimers();
  const host = mockHost();
  render(<NotchCards cards={[card("s")]} />);
  act(() => { vi.advanceTimersByTime(600_000); });
  expect(host.posts("card.dismiss")).toEqual([]);
});

it("a body that overflows takes keyboard focus and is named; a short one does not", () => {
  mockHost();
  vi.spyOn(Element.prototype, "clientHeight", "get").mockReturnValue(100);
  const tall = vi.spyOn(Element.prototype, "scrollHeight", "get").mockReturnValue(400);
  const { unmount } = render(<NotchCards cards={[card("long", { title: "Preview: I would copy this", body: "x".repeat(2_000) })]} />);
  const region = screen.getByRole("region", { name: "Preview: I would copy this, full text" });
  expect(region.tabIndex).toBe(0);
  unmount();
  tall.mockReturnValue(100);
  const { container } = render(<NotchCards cards={[card("short", { body: "short" })]} />);
  const body = container.querySelector(".notch-card__body")!;
  expect(body.hasAttribute("tabindex")).toBe(false);
  expect(screen.queryByRole("region")).toBeNull();
});
