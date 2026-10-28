import { afterEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { parseShellState, type JobCard, type ShellState } from "../contract";
import { Notch } from "./Notch";

// E13 shell check. Surface.Smoke cannot inject shell state, so this renders the real card JSON that ShellStateBuilder produced
// (C# WorkNativeAcceptanceTests.ShellCheck_... compares the builder's output to this fixture byte for byte).
const base = JSON.parse(readFileSync(resolve(__dirname, "../../../../../tests/fixtures/shell-state.v1.json"), "utf8")) as ShellState;
const card = JSON.parse(readFileSync(resolve(__dirname, "../../../../../tests/fixtures/shell-job-card.work.v1.json"), "utf8")) as JobCard;
const state: ShellState = { ...base, notch: { ...base.notch, tab: "home", open: true }, featured: card, jobs: [card] };

afterEach(() => { cleanup(); vi.restoreAllMocks(); delete window.chrome; });

it("a finished job with a plan, points, a stat and next steps passes the contract and renders on the card", () => {
  const postMessage = vi.fn();
  window.chrome = { webview: { postMessage, addEventListener: () => {}, removeEventListener: () => {} } };
  expect(parseShellState(state)).not.toBeNull();

  render(<Notch state={state} />);
  expect(screen.getByText("Compare note-taking apps")).toBeInTheDocument();
  expect(screen.getByText("Compared three note-taking apps and saved a table.")).toBeInTheDocument();
  expect(screen.getByText("3")).toBeInTheDocument(); // the stat value
  expect(screen.getByText(/apps compared/)).toBeInTheDocument();
  expect(screen.getByText("3 of 3 steps")).toBeInTheDocument();
  expect(screen.getAllByRole("checkbox").map((b) => b.getAttribute("aria-checked"))).toEqual(["true", "true", "true"]);
  for (const point of card.bullets) expect(screen.getByText(point)).toBeInTheDocument();
  for (const next of card.next) expect(screen.getByText(next)).toBeInTheDocument();
  expect(screen.getByLabelText("Ask a follow-up…")).toBeInTheDocument(); // a done job takes a follow-up

  fireEvent.click(screen.getByRole("button", { name: "Open comparison.md" }));
  const opened = postMessage.mock.calls.map((c) => c[0]).filter((m) => m.kind === "file.open");
  expect(opened).toEqual([{ kind: "file.open", path: card.files[0].path }]);
});
