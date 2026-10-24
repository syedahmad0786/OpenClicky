import { afterEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import type { ShellState } from "../contract";
import { Notch } from "./Notch";

const fixture = JSON.parse(readFileSync(resolve(__dirname, "../../../../../tests/fixtures/shell-state.v1.json"), "utf8")) as ShellState;
const open = (tab: ShellState["notch"]["tab"]): ShellState => ({ ...fixture, notch: { tab, open: true } });

afterEach(() => { cleanup(); vi.restoreAllMocks(); delete window.chrome; });

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
