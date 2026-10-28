import { afterEach, expect, it, vi } from "vitest";
import { act, cleanup, render } from "@testing-library/react";
import { CaptionsBar, CLEAR_DELAY_MS, FADE_MS, IDLE_CLEAR_MS } from "./CaptionsBar";

afterEach(() => { cleanup(); vi.useRealTimers(); delete window.chrome; });

function mockHost() {
  const listeners = new Set<(event: MessageEvent) => void>();
  window.chrome = { webview: { postMessage: vi.fn(),
    addEventListener: (_, listener) => { listeners.add(listener); },
    removeEventListener: (_, listener) => { listeners.delete(listener); },
  } };
  return (data: unknown) => act(() => { for (const l of listeners) l(new MessageEvent("message", { data })); });
}
const words = (send: (data: unknown) => void, ...list: string[]) => list.forEach((text) => send({ kind: "caption.word", text }));
// classList, not a ".captions--fading" selector: jsdom's selector engine misses that class.
const fading = (container: HTMLElement) => container.querySelector(".captions")?.classList.contains("captions--fading") ?? false;
const tick = (ms: number) => act(() => { vi.advanceTimersByTime(ms); });

it("shows nothing (and reports no region) until a word arrives, then appends words", () => {
  const send = mockHost();
  const { container } = render(<CaptionsBar />);
  expect(container.querySelector("[data-region]")).toBeNull();
  words(send, "Hello", " ", "there,", "friend.");
  const bar = container.querySelector(".captions")!;
  expect(bar).toHaveAttribute("data-region");
  expect(bar.textContent).toBe("Hello there, friend.");
});

it("caption.clear fades out 1.5 s later and drops the region", () => {
  vi.useFakeTimers();
  const send = mockHost();
  const { container } = render(<CaptionsBar />);
  words(send, "Done.");
  send({ kind: "caption.clear" });
  tick(CLEAR_DELAY_MS - 1);
  expect(fading(container)).toBe(false);
  tick(1);
  expect(fading(container)).toBe(true);
  tick(FADE_MS);
  expect(container.querySelector(".captions")).toBeNull();
  expect(container.querySelector("[data-region]")).toBeNull();
});

it("a word cancels a pending clear (clear arrives per sentence), and a later clear still works", () => {
  vi.useFakeTimers();
  const send = mockHost();
  const { container } = render(<CaptionsBar />);
  words(send, "One.");
  send({ kind: "caption.clear" });
  tick(CLEAR_DELAY_MS - 100);
  words(send, "Two.");
  tick(CLEAR_DELAY_MS * 2);
  expect(container.querySelector(".captions")!.textContent).toBe("One. Two.");
  expect(fading(container)).toBe(false);

  send({ kind: "caption.clear" });
  tick(CLEAR_DELAY_MS + 10);
  expect(fading(container)).toBe(true);
  words(send, "Three."); // a word during the fade brings it back
  expect(fading(container)).toBe(false);
  expect(container.querySelector(".captions")!.textContent).toBe("One. Two. Three.");
  send({ kind: "caption.clear" });
  tick(CLEAR_DELAY_MS + FADE_MS);
  expect(container.querySelector(".captions")).toBeNull();
});

it("words with no caption.clear still go away after a long silence", () => {
  vi.useFakeTimers();
  const send = mockHost();
  const { container } = render(<CaptionsBar />);
  words(send, "Stray");
  tick(IDLE_CLEAR_MS + CLEAR_DELAY_MS + FADE_MS);
  expect(container.querySelector(".captions")).toBeNull();
});

it("keeps only the newest words once the text grows long", () => {
  const send = mockHost();
  const { container } = render(<CaptionsBar />);
  for (let i = 0; i < 200; i++) words(send, `word${i}`);
  const text = container.querySelector(".captions")!.textContent!;
  expect(text.length).toBeLessThanOrEqual(400);
  expect(text.endsWith("word199")).toBe(true);
  expect(text.startsWith("word")).toBe(true); // cut on a word boundary
});
