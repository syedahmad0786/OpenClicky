import { afterEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import type { JobCard } from "../contract";
import { JobDeck } from "./JobDeck";

afterEach(() => { cleanup(); vi.restoreAllMocks(); delete window.chrome; });

function mockHost() {
  const postMessage = vi.fn();
  window.chrome = { webview: { postMessage, addEventListener: () => {}, removeEventListener: () => {} } };
  return { posts: (kind: string) => postMessage.mock.calls.map((c) => c[0]).filter((m) => m.kind === kind) };
}
const job = (n: number, patch: Partial<JobCard> = {}): JobCard => ({
  id: `job-${n}`, title: `Job ${n}`, state: "done", colour: "blue", bullets: [], files: [], plan: [],
  updatedAt: new Date(Date.UTC(2026, 0, 1, 0, n)).toISOString(), ...patch,
});
/** Newest first, like the host: job-count down to job-1. */
const jobs = (count: number) => Array.from({ length: count }, (_, i) => job(count - i));

it("alternates sides, newest nearest the panel, tilted 3 to 5 degrees", () => {
  mockHost();
  const { container } = render(<JobDeck jobs={jobs(4)} featuredId={null} />);
  const side = (name: string) => [...container.querySelectorAll(`.notch-deck--${name} .deck-card__title`)].map((n) => n.textContent);
  expect(side("left")).toEqual(["Job 4", "Job 2"]);
  expect(side("right")).toEqual(["Job 3", "Job 1"]);
  for (const el of container.querySelectorAll<HTMLElement>(".deck-card")) {
    const tilt = Math.abs(parseFloat(el.style.getPropertyValue("--tilt")));
    expect(tilt).toBeGreaterThanOrEqual(3);
    expect(tilt).toBeLessThanOrEqual(5);
  }
});

it("skips the featured job", () => {
  mockHost();
  render(<JobDeck jobs={jobs(3)} featuredId="job-3" />);
  expect(screen.queryByText("Job 3")).toBeNull();
  expect(screen.getAllByRole("button", { name: /Job \d/ })).toHaveLength(2);
});

it("collapses beyond visible count into +N more, and expands to show the rest", () => {
  mockHost();
  render(<JobDeck jobs={jobs(9)} featuredId={null} />); // left 5, right 4
  expect(screen.getAllByRole("button", { name: /^Job \d/ })).toHaveLength(6);
  const pills = screen.getAllByRole("button", { name: /more$/ });
  expect(pills.map((p) => p.textContent)).toEqual(["+2 more", "+1 more"]);

  fireEvent.click(pills[0]);
  expect(screen.getAllByRole("button", { name: /^Job \d/ })).toHaveLength(8); // left now shows all 5
  expect(screen.getByRole("button", { name: "Show fewer" })).toBeInTheDocument();
});

it("clicking a card posts job.open", () => {
  const host = mockHost();
  render(<JobDeck jobs={[job(1, { title: "Make slides" })]} featuredId={null} />);
  fireEvent.click(screen.getByRole("button", { name: /Make slides/ }));
  expect(host.posts("job.open")).toEqual([{ kind: "job.open", id: "job-1" }]);
});

it("labels each state in our own words", () => {
  mockHost();
  render(<JobDeck featuredId={null} jobs={[
    job(1, { state: "working", liveLine: "Reading file" }), job(2, { state: "needsYou" }), job(3, { state: "failed" }),
    job(4, { state: "stopped" }), job(5, { state: "new" }),
  ]} />);
  for (const text of ["Working…", "Reading file", "Needs you", "Didn’t finish", "Stopped", "Ready to view"]) expect(screen.getByText(text)).toBeInTheDocument();
});
