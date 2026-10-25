import { afterEach, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { onboardingActions, onboardingSteps, parseShellState, type OnboardingStep, type ShellState } from "../contract";
import { Notch } from "../notch/Notch";
import { Onboarding } from "./Onboarding";

const fixtures = resolve(__dirname, "../../../../../tests/fixtures");
const fixture = JSON.parse(readFileSync(resolve(fixtures, "shell-state.v1.json"), "utf8")) as ShellState;
const shared = JSON.parse(readFileSync(resolve(fixtures, "onboarding.v1.json"), "utf8")) as { steps: string[]; actions: string[] };

const tour = (step: OnboardingStep, more: Partial<NonNullable<ShellState["onboarding"]>> = {}, patch: (s: ShellState) => void = () => {}): ShellState => {
  const s = structuredClone(fixture);
  s.onboarding = { step, index: onboardingSteps.indexOf(step), count: 12, stepDone: false, busy: false, ...more };
  patch(s);
  return s;
};

const listeners = new Set<(e: MessageEvent) => void>();
function mockHost() {
  const postMessage = vi.fn();
  window.chrome = { webview: { postMessage, addEventListener: (_t, l) => listeners.add(l), removeEventListener: (_t, l) => listeners.delete(l) } };
  return {
    all: () => postMessage.mock.calls.map((c) => c[0]),
    tour: () => postMessage.mock.calls.map((c) => c[0]).filter((m) => m.kind === "onboarding").map((m) => m.action),
    send: (data: unknown) => act(() => { for (const l of listeners) l({ data } as MessageEvent); }),
  };
}
afterEach(() => { cleanup(); listeners.clear(); delete window.chrome; });

const button = (name: string) => screen.getByRole("button", { name });

it("contract lists match the shared onboarding fixture", () => {
  expect([...onboardingSteps]).toEqual(shared.steps);
  expect([...onboardingActions]).toEqual(shared.actions);
});

it("parseShellState accepts a state with onboarding and the fixture without it", () => {
  expect(parseShellState(fixture)).not.toBeNull();
  expect(parseShellState(tour("driveDemo", { stepDone: true }))?.onboarding).toEqual({ step: "driveDemo", index: 8, count: 12, stepDone: true, busy: false });
  expect(parseShellState({ ...tour("welcome"), onboarding: { ...tour("welcome").onboarding, step: "tea" } })).toBeNull();
  expect(parseShellState({ ...tour("welcome"), onboarding: { ...tour("welcome").onboarding, extra: 1 } })).toBeNull();
});

it("renders nothing without onboarding", () => {
  mockHost();
  const { container } = render(<Onboarding state={fixture} />);
  expect(container).toBeEmptyDOMElement();
});

it("welcome shows step 1 of 12, no Back, and Continue posts next", () => {
  const host = mockHost();
  render(<Onboarding state={tour("welcome")} />);
  const dialog = screen.getByRole("dialog", { name: "OpenClicky tour" });
  const progress = within(dialog).getByRole("progressbar", { name: "Step 1 of 12" });
  expect(progress).toHaveAttribute("aria-valuenow", "1");
  expect(progress).toHaveAttribute("aria-valuemax", "12");
  expect(progress.querySelectorAll(".is-lit")).toHaveLength(1);
  expect(screen.getByRole("heading", { name: "Hi, I'm OpenClicky." })).toBeInTheDocument();
  expect(screen.getByText(/The tour takes about three minutes, and you can skip it at any point\.$/)).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "Back" })).toBeNull();
  fireEvent.click(button("Continue"));
  expect(host.tour()).toEqual(["next"]);
});

it("Skip tour posts skip on every step except the finale", () => {
  const host = mockHost();
  const { rerender } = render(<Onboarding state={tour("welcome")} />);
  for (const step of onboardingSteps.filter((s) => s !== "finale")) {
    rerender(<Onboarding state={tour(step)} />);
    fireEvent.click(button("Skip tour"));
  }
  expect(host.tour()).toEqual(Array(11).fill("skip"));
  rerender(<Onboarding state={tour("finale")} />);
  expect(screen.queryByRole("button", { name: "Skip tour" })).toBeNull();
  expect(screen.queryByRole("button", { name: "Continue" })).toBeNull();
  fireEvent.click(button("Back"));
  expect(host.tour().at(-1)).toBe("back");
});

it("busy disables Back and Continue but not Skip", () => {
  mockHost();
  render(<Onboarding state={tour("drawDemo", { busy: true })} />);
  expect(button("Back")).toBeDisabled();
  expect(button("Continue")).toBeDisabled();
  expect(button("Skip tour")).toBeEnabled();
  expect(button("Show me")).toBeDisabled();
});

it("colour tiles patch cursor.colour and mark the current colour", () => {
  const host = mockHost();
  render(<Onboarding state={tour("colour", {}, (s) => { s.settings.cursor.colour = "#ff4c4c"; })} />);
  expect(screen.getByRole("heading", { name: "Pick my colour" })).toBeInTheDocument();
  const tiles = within(screen.getByRole("group", { name: "Colour" })).getAllByRole("button");
  expect(tiles).toHaveLength(4);
  expect(button("Red")).toHaveAttribute("aria-pressed", "true");
  expect(button("Blue")).toHaveAttribute("aria-pressed", "false");
  fireEvent.click(button("Green"));
  expect(host.all().filter((m) => m.kind === "settings.patch")).toEqual([{ kind: "settings.patch", key: "cursor.colour", value: "#26C281" }]);
});

it("microphone card shows the denied copy and opens the microphone page", () => {
  const host = mockHost();
  const withMic = (state: "allowed" | "denied" | "unknown" | null) => tour("microphone", {}, (s) => {
    s.settings.agents.reach = state ? [{ id: "microphone", label: "Microphone", state }] : [];
  });
  const { rerender } = render(<Onboarding state={withMic("denied")} />);
  expect(screen.getByText("Permission 1 of 2")).toBeInTheDocument();
  expect(screen.getByText("I only listen after you press the talk keys, start a conversation or dictate. Windows decides whether desktop apps may use the microphone, and you choose there.")).toBeInTheDocument();
  expect(screen.getByText("Windows is blocking desktop apps from the microphone. Turn on “Let desktop apps access your microphone”, then check again.")).toBeInTheDocument();
  expect(screen.getByText(/^Dictation always turns speech into text on this PC\./)).toBeInTheDocument();
  fireEvent.click(button("Open microphone settings"));
  fireEvent.click(button("Check again"));
  expect(host.all().filter((m) => m.kind === "settings.action")).toEqual([{ kind: "settings.action", action: "openPrivacySetting", arg: "microphone" }]);
  expect(host.tour()).toEqual(["recheck"]);

  rerender(<Onboarding state={withMic("allowed")} />);
  expect(screen.getByText("Windows lets OpenClicky use the microphone.")).toBeInTheDocument();
  rerender(<Onboarding state={withMic(null)} />);
  expect(screen.getByText("I couldn't read the Windows setting. If talking doesn't work, check it there.")).toBeInTheDocument();
});

it("screen card opens Windows screen settings", () => {
  const host = mockHost();
  render(<Onboarding state={tour("screen")} />);
  expect(screen.getByText("Permission 2 of 2")).toBeInTheDocument();
  expect(screen.getByRole("heading", { name: "Let me see what you see" })).toBeInTheDocument();
  fireEvent.click(button("Open Windows screen settings"));
  expect(host.tour()).toEqual(["openScreenSettings"]);
});

it("mic check ticks after a loud enough level", () => {
  const host = mockHost();
  render(<Onboarding state={tour("micCheck")} />);
  expect(screen.getByText("Listening with Headset")).toBeInTheDocument();
  expect(screen.queryByText("I can hear you.")).toBeNull();
  host.send({ kind: "mic.level", level: 0.1 });
  expect(screen.queryByText("I can hear you.")).toBeNull();
  host.send({ kind: "mic.level", level: 0.3 });
  expect(screen.getByRole("meter", { name: "Microphone level" })).toHaveAttribute("aria-valuenow", "5");
  expect(screen.getByText("I can hear you.")).toBeInTheDocument();

  fireEvent.click(button("Test microphone"));
  fireEvent.click(button("Change microphone"));
  expect(host.all().filter((m) => m.kind !== "onboarding")).toEqual([
    { kind: "settings.action", action: "testMic" },
    { kind: "notch.tab", tab: "settings" },
    { kind: "settings.section", section: "microphone" },
    { kind: "notch.open", open: true },
  ]);
});

it("mic check names the Windows default microphone when no device is chosen", () => {
  mockHost();
  render(<Onboarding state={tour("micCheck", {}, (s) => { s.settings.microphone.deviceId = null; })} />);
  expect(screen.getByText("Listening with the Windows default microphone")).toBeInTheDocument();
});

it("voice hello shows the talk keys and the account note without a provider", () => {
  const host = mockHost();
  const noProvider = (s: ShellState) => { s.settings.account.signedInAs = null; s.settings.account.openRouterKeySet = false; s.status.phase = "listening"; };
  const { container, rerender } = render(<Onboarding state={tour("voiceHello", {}, noProvider)} />);
  expect(container.querySelector(".onboarding__body")).toHaveTextContent("Hold CtrlAlt, say “Hi, I'm …” with your name, then let go. I'll answer out loud.");
  expect([...container.querySelectorAll(".onboarding__body kbd")].map((k) => k.textContent)).toEqual(fixture.hotkeys.talk);
  expect(screen.getByText("Listening…")).toBeInTheDocument();
  expect(screen.getByText("Connect ChatGPT or add an OpenRouter key first, so I can answer.")).toBeInTheDocument();
  expect(screen.getByText("Your words and screenshots go to your model provider.")).toBeInTheDocument();
  fireEvent.click(button("Open Account settings"));
  expect(host.all()).toEqual([{ kind: "notch.tab", tab: "settings" }, { kind: "settings.section", section: "account" }, { kind: "notch.open", open: true }]);

  rerender(<Onboarding state={tour("voiceHello", { stepDone: true }, (s) => { s.settings.shortcuts.talk.trigger = "doubleTap"; })} />);
  expect(container.querySelector(".onboarding__body")).toHaveTextContent(/^Double-tap CtrlAlt,/);
  expect(screen.getByText("Nice to meet you!")).toBeInTheDocument();
  expect(screen.queryByText("Connect ChatGPT or add an OpenRouter key first, so I can answer.")).toBeNull();
});

it("draw demo starts on click and tells how to wipe it when done", () => {
  const host = mockHost();
  const { rerender } = render(<Onboarding state={tour("drawDemo")} />);
  expect(screen.getByText(/This sample runs on this PC; nothing from your screen is sent\.$/)).toBeInTheDocument();
  fireEvent.click(button("Show me"));
  expect(host.tour()).toEqual(["drawDemo"]);
  expect(screen.queryByText("Press Esc to wipe it.")).toBeNull();
  rerender(<Onboarding state={tour("drawDemo", { stepDone: true })} />);
  expect(screen.getByText("Press Esc to wipe it.")).toBeInTheDocument();
});

it("circle to ask offers to turn screen highlight on", () => {
  const host = mockHost();
  render(<Onboarding state={tour("circleToAsk", {}, (s) => { s.settings.cursor.screenHighlight = false; })} />);
  expect(screen.getByText("Screen highlight is off.")).toBeInTheDocument();
  fireEvent.click(button("Turn it on"));
  expect(host.all()).toEqual([{ kind: "settings.patch", key: "cursor.screenHighlight", value: true }]);
});

it("drive demo lists the three steps and ticks them when done", () => {
  const host = mockHost();
  const { container, rerender } = render(<Onboarding state={tour("driveDemo")} />);
  const items = within(screen.getByRole("list")).getAllByRole("listitem");
  expect(items.map((i) => i.textContent)).toEqual(["Click the green button", "Click into the text box", "Type hello"]);
  expect(container.querySelectorAll(".onboarding__check.is-done")).toHaveLength(0);
  fireEvent.click(button("Start the practice run"));
  expect(host.tour()).toEqual(["driveDemo"]);
  rerender(<Onboarding state={tour("driveDemo", { stepDone: true })} />);
  expect(container.querySelectorAll(".onboarding__check.is-done")).toHaveLength(3);
  expect(screen.getByText("All three done, checked in the test window.")).toBeInTheDocument();
});

it("text mode and dictation name their own shortcuts and gestures", () => {
  const host = mockHost();
  const { container, rerender } = render(<Onboarding state={tour("textMode")} />);
  expect(container.querySelector(".onboarding__body")).toHaveTextContent("Double-tap CtrlAltT and a text box opens up here.");
  rerender(<Onboarding state={tour("dictation")} />);
  expect(container.querySelector(".onboarding__body")).toHaveTextContent("Hold CtrlAltD, speak, then let go: your words appear at the cursor. Double-tap CtrlAltH to keep dictating hands-free.");
  expect(screen.getByText("Your voice becomes text on this PC. With Tidy up with AI on, the words, the app name and the text around the cursor go to your model provider.")).toBeInTheDocument();
  expect(screen.getByText("Dictation needs the local voice. Download it in Settings → Microphone.")).toBeInTheDocument();
  fireEvent.click(button("Open Microphone settings"));
  expect(host.all()).toEqual([{ kind: "notch.tab", tab: "settings" }, { kind: "settings.section", section: "microphone" }, { kind: "notch.open", open: true }]);
  rerender(<Onboarding state={tour("dictation", { stepDone: true }, (s) => { s.settings.microphone.whisper = "ready"; })} />);
  expect(screen.queryByRole("button", { name: "Open Microphone settings" })).toBeNull();
  expect(screen.getByText("Nicely done.")).toBeInTheDocument();
});

it("finale's Show me Home posts finish", () => {
  const host = mockHost();
  render(<Onboarding state={tour("finale")} />);
  expect(screen.getByRole("progressbar", { name: "Step 12 of 12" })).toBeInTheDocument();
  fireEvent.click(button("Show me Home"));
  expect(host.tour()).toEqual(["finish"]);
});

it("the tour card is a reported region inside the notch shell", () => {
  mockHost();
  const { container, rerender } = render(<Notch state={tour("welcome")} />);
  expect(container.querySelector(".notch-shell > .onboarding[data-region]")).not.toBeNull();
  rerender(<Notch state={fixture} />);
  expect(container.querySelector(".onboarding")).toBeNull();
});

it("the tour card hides while the notch is open on Settings and is back on Home", () => {
  mockHost();
  const at = (tab: "settings" | "home") => tour("micCheck", {}, (s) => { s.notch = { ...s.notch, open: true, tab }; });
  const { container, rerender } = render(<Notch state={at("settings")} />);
  expect(container.querySelector(".notch--settings")).not.toBeNull();
  expect(container.querySelector(".onboarding")).toBeNull();
  rerender(<Notch state={at("home")} />);
  expect(container.querySelector(".onboarding[data-region]")).not.toBeNull();
});
