import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { HostMessage, ShellState } from "../contract";
import { ACCENTS } from "../ui";
import { SettingsPanel } from "./SettingsPanel";

const fixture = JSON.parse(readFileSync(resolve(__dirname, "../../../../../tests/fixtures/shell-state.v1.json"), "utf8")) as ShellState;
const make = (section: ShellState["notch"]["section"], patch: (s: ShellState) => void = () => {}): ShellState => {
  const s = structuredClone(fixture);
  s.notch = { ...s.notch, tab: "settings", open: true, section };
  patch(s);
  return s;
};

const postMessage = vi.fn();
let hostListener: ((event: MessageEvent) => void) | null = null;
const send = (message: HostMessage) => act(() => hostListener?.({ data: message } as MessageEvent));
function host() {
  postMessage.mockClear();
  hostListener = null;
  window.chrome = { webview: { postMessage, addEventListener: (_t, l) => { hostListener = l; }, removeEventListener: () => { hostListener = null; } } };
  return (kind: string) => postMessage.mock.calls.map((c) => c[0]).filter((m) => m.kind === kind);
}
beforeEach(() => vi.useRealTimers());
afterEach(() => { cleanup(); delete window.chrome; vi.useRealTimers(); });

const TEN = ["windows-default", "alloy", "ash", "ballad", "cedar", "coral", "echo", "marin", "sage", "shimmer"];
const tenVoices = (s: ShellState) => {
  s.settings.voice.voices = TEN.map((id, i) => ({ id, name: id, detail: i === 0 ? "Installed voice" : "Needs your OpenAI key", available: i === 0 || id === "cedar" }));
};

// ---- Voice
it("Voice: speed chips post voice.speed and mark the current speed", () => {
  const posts = host();
  render(<SettingsPanel state={make("voice")} />);
  expect(screen.getByText("How OpenClicky sounds when it talks back.")).toBeInTheDocument();
  expect(["0.5×", "0.75×", "1×", "1.25×", "1.5×"].map((n) => screen.getByRole("button", { name: n }).getAttribute("aria-pressed"))).toEqual(["false", "false", "true", "false", "false"]);
  fireEvent.click(screen.getByRole("button", { name: "1.25×" }));
  expect(posts("settings.patch")).toEqual([{ kind: "settings.patch", key: "voice.speed", value: 1.25 }]);
});

it("Voice: available tile previews and patches; unavailable tiles are dimmed and ask for the OpenAI key", () => {
  const posts = host();
  render(<SettingsPanel state={make("voice", tenVoices)} />);
  const grid = screen.getByRole("group", { name: "Voice" });
  expect(within(grid).getAllByRole("button")).toHaveLength(10);
  expect(grid.querySelector("svg")).not.toBeNull();
  fireEvent.click(screen.getByRole("button", { name: /^cedar/ }));
  expect(posts("settings.action")).toEqual([{ kind: "settings.action", action: "previewVoice", arg: "cedar" }]);
  expect(posts("settings.patch")).toEqual([{ kind: "settings.patch", key: "voice.voice", value: "cedar" }]);
  postMessage.mockClear();
  const alloy = screen.getByRole("button", { name: /^alloy/ });
  expect(alloy).toHaveClass("is-unavailable");
  expect(alloy).toHaveTextContent("Needs your OpenAI key");
  fireEvent.click(alloy);
  expect(posts("settings.action")).toEqual([{ kind: "settings.action", action: "secret.edit", arg: "openai" }]);
  expect(posts("settings.patch")).toEqual([]);
});

it("Voice: the OpenAI key row asks the host for the key and never takes it on the page", () => {
  const posts = host();
  const { unmount } = render(<SettingsPanel state={make("voice", tenVoices)} />);
  fireEvent.click(screen.getByRole("button", { name: "Add key" }));
  expect(posts("settings.action")).toEqual([{ kind: "settings.action", action: "secret.edit", arg: "openai" }]);
  expect(document.querySelector("input[type=password]")).toBeNull();
  unmount();
  render(<SettingsPanel state={make("voice", (s) => { s.settings.voice.voices = s.settings.voice.voices.map((x) => ({ ...x, available: true })); })} />);
  expect(screen.getByRole("button", { name: "Change" })).toBeInTheDocument();
});

it("Voice: toggles patch, output device list posts and test sound acts", () => {
  const posts = host();
  render(<SettingsPanel state={make("voice", (s) => {
    s.settings.voice.outputDevices = [{ id: "spk-1", name: "Speakers", state: "active" }, { id: "spk-2", name: "Dock", state: "unavailable" }];
    s.settings.voice.outputDeviceId = null;
  })} />);
  fireEvent.click(screen.getByRole("switch", { name: "Captions" }));
  fireEvent.click(screen.getByRole("switch", { name: "Read replies aloud" }));
  fireEvent.click(screen.getByRole("radio", { name: /Speakers/ }));
  expect(screen.getByRole("radio", { name: /Dock/ })).toBeDisabled();
  fireEvent.click(screen.getByRole("button", { name: "Play a test sound" }));
  expect(posts("settings.patch")).toEqual([
    { kind: "settings.patch", key: "voice.captions", value: !fixture.settings.voice.captions },
    { kind: "settings.patch", key: "voice.readReplies", value: !fixture.settings.voice.readReplies },
    { kind: "settings.patch", key: "voice.outputDeviceId", value: "spk-1" },
  ]);
  expect(posts("settings.action")).toEqual([{ kind: "settings.action", action: "playTestSound" }]);
});

it("Voice: choosing System default patches the output device to null", () => {
  const posts = host();
  render(<SettingsPanel state={make("voice", (s) => { s.settings.voice.outputDeviceId = "spk-1"; })} />);
  fireEvent.click(screen.getByRole("radio", { name: /System default/ }));
  expect(posts("settings.patch")).toEqual([{ kind: "settings.patch", key: "voice.outputDeviceId", value: null }]);
});

// ---- Microphone
it("Microphone: device list, test button and live level that decays after a second", () => {
  vi.useFakeTimers();
  const posts = host();
  render(<SettingsPanel state={make("microphone")} />);
  expect(screen.getByRole("radio", { name: /USB mic/ })).toBeDisabled();
  fireEvent.click(screen.getByRole("radio", { name: /System default/ }));
  expect(posts("settings.patch")).toEqual([{ kind: "settings.patch", key: "microphone.deviceId", value: null }]);
  fireEvent.click(screen.getByRole("button", { name: "Test microphone" }));
  expect(posts("settings.action")).toEqual([{ kind: "settings.action", action: "testMic" }]);

  const meter = screen.getByRole("meter", { name: "Microphone level" });
  expect(meter).toHaveAttribute("aria-valuenow", "0");
  send({ kind: "mic.level", level: 0.5 });
  expect(meter).toHaveAttribute("aria-valuenow", "8"); // 16 bars, half lit
  expect(meter.querySelectorAll(".is-lit")).toHaveLength(8);
  act(() => { vi.advanceTimersByTime(1_100); });
  expect(meter).toHaveAttribute("aria-valuenow", "0");
});

it("Microphone: whisper row follows missing, downloading and ready", () => {
  const posts = host();
  const withWhisper = (whisper: "missing" | "downloading" | "ready", progress = 0) =>
    make("microphone", (s) => { s.settings.microphone.whisper = whisper; s.settings.microphone.whisperProgress = progress; });
  const { rerender } = render(<SettingsPanel state={withWhisper("missing")} />);
  fireEvent.click(screen.getByRole("button", { name: "Download local voice (142 MB)" }));
  expect(posts("settings.action")).toEqual([{ kind: "settings.action", action: "downloadWhisper" }]);
  rerender(<SettingsPanel state={withWhisper("downloading", 0.4)} />);
  expect(screen.getByText("Downloading… 40%")).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /Download local voice/ })).toBeNull();
  rerender(<SettingsPanel state={withWhisper("ready")} />);
  expect(screen.getByText("Ready")).toBeInTheDocument();
});

it("Microphone: the accurate English voice downloads, shows progress, then switches on and off", () => {
  const posts = host();
  const withAccurate = (accurate: "missing" | "downloading" | "ready", progress = 0, use = false) =>
    make("microphone", (s) => { Object.assign(s.settings.microphone, { accurate, accurateProgress: progress, useAccurate: use }); });
  const { rerender } = render(<SettingsPanel state={withAccurate("missing")} />);
  fireEvent.click(screen.getByRole("button", { name: "Download (465 MB)" }));
  expect(posts("settings.action")).toEqual([{ kind: "settings.action", action: "downloadWhisperAccurate" }]);
  rerender(<SettingsPanel state={withAccurate("downloading", 0.25)} />);
  expect(screen.getByText("Downloading… 25%")).toBeInTheDocument();
  expect(screen.getByRole("meter", { name: "Accurate voice download progress" })).toBeInTheDocument();
  rerender(<SettingsPanel state={withAccurate("ready", 1, false)} />);
  const toggle = screen.getByRole("switch", { name: "Use the more accurate English voice" });
  expect(toggle).toHaveAttribute("aria-checked", "false");
  fireEvent.click(toggle);
  expect(posts("settings.patch")).toEqual([{ kind: "settings.patch", key: "microphone.accurate", value: true }]);
});

// ---- Dictation
it("Dictation: auto-detect toggle posts, language tiles are disabled while auto and filter by search", () => {
  const posts = host();
  const langs = (s: ShellState) => {
    s.settings.dictation.languages = [
      { code: "en", name: "English", native: "English" }, { code: "ur", name: "Urdu", native: "اردو" }, { code: "fr", name: "French", native: "Français" }];
  };
  const { rerender } = render(<SettingsPanel state={make("dictation", (s) => { langs(s); s.settings.dictation.autoDetect = true; })} />);
  expect(screen.getByRole("button", { name: /French/ })).toBeDisabled();
  fireEvent.click(screen.getByRole("switch", { name: "Auto-detect language" }));
  expect(posts("settings.patch")).toEqual([{ kind: "settings.patch", key: "dictation.autoDetect", value: false }]);

  rerender(<SettingsPanel state={make("dictation", (s) => { langs(s); s.settings.dictation.autoDetect = false; })} />);
  fireEvent.change(screen.getByRole("searchbox", { name: "Search languages" }), { target: { value: "fran" } });
  expect(screen.queryByRole("button", { name: /Urdu/ })).toBeNull();
  postMessage.mockClear();
  fireEvent.click(screen.getByRole("button", { name: /French/ }));
  expect(posts("settings.patch")).toEqual([{ kind: "settings.patch", key: "dictation.language", value: "fr" }]);
});

it("Dictation: dictionary adds on Enter, dedupes case-insensitively and removes", () => {
  const posts = host();
  render(<SettingsPanel state={make("dictation", (s) => { s.settings.dictation.dictionary = ["OpenClicky"]; })} />);
  const input = screen.getByRole("textbox", { name: "Add a word" });
  fireEvent.change(input, { target: { value: "  Ahmad " } });
  fireEvent.keyDown(input, { key: "Enter" });
  expect(posts("settings.patch")).toEqual([{ kind: "settings.patch", key: "dictation.dictionary", value: ["OpenClicky", "Ahmad"] }]);
  expect(input).toHaveValue("");
  postMessage.mockClear();
  fireEvent.change(input, { target: { value: "openclicky" } });
  fireEvent.click(screen.getByRole("button", { name: "Add" }));
  expect(postMessage).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "Remove OpenClicky" }));
  expect(posts("settings.patch")).toEqual([{ kind: "settings.patch", key: "dictation.dictionary", value: ["Ahmad"] }]); // builds on the optimistic list
  expect(input).toHaveAttribute("maxlength", "60");
});

it("Dictation: a 60-character word is accepted whole", () => {
  const posts = host();
  render(<SettingsPanel state={make("dictation")} />);
  const input = screen.getByRole("textbox", { name: "Add a word" });
  const word = "w".repeat(60);
  fireEvent.change(input, { target: { value: word } });
  fireEvent.keyDown(input, { key: "Enter" });
  expect(posts("settings.patch")).toEqual([{ kind: "settings.patch", key: "dictation.dictionary", value: ["OpenClicky", "Ahmad", word] }]);
});

it("Dictation: the tidy-up toggle explains what is sent and posts dictation.cleanup", () => {
  const posts = host();
  render(<SettingsPanel state={make("dictation", (s) => { s.settings.dictation.cleanup = true; })} />);
  expect(screen.getByText("Fixes punctuation and filler words. Sends what you said, the app name and the text around the cursor to your model provider. Your voice stays on this PC.")).toBeInTheDocument();
  fireEvent.click(screen.getByRole("switch", { name: "Tidy up with AI" }));
  expect(posts("settings.patch")).toEqual([{ kind: "settings.patch", key: "dictation.cleanup", value: false }]);
});

it("Dictation: dictionary cap of 50 is enforced in the UI", () => {
  const posts = host();
  const words = Array.from({ length: 50 }, (_, i) => `word${i}`);
  render(<SettingsPanel state={make("dictation", (s) => { s.settings.dictation.dictionary = words; })} />);
  const input = screen.getByRole("textbox", { name: "Add a word" });
  fireEvent.change(input, { target: { value: "extra" } });
  expect(screen.getByRole("button", { name: "Add" })).toBeDisabled();
  fireEvent.keyDown(input, { key: "Enter" });
  expect(posts("settings.patch")).toEqual([]);
  expect(screen.getByText("50 of 50 words")).toBeInTheDocument();
});

it("Dictation: auto-detect without the multilingual voice offers the download", () => {
  const posts = host();
  const notice = "Other languages and auto-detect need the multilingual voice (142 MB, runs on this PC). Until then OpenClicky dictates in English.";
  const { rerender } = render(<SettingsPanel state={make("dictation", (s) => { s.settings.dictation.autoDetect = true; s.settings.dictation.multilingual = "missing"; })} />);
  expect(screen.getByText(notice)).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Download multilingual voice" }));
  expect(posts("settings.action")).toEqual([{ kind: "settings.action", action: "downloadWhisperMultilingual" }]);

  rerender(<SettingsPanel state={make("dictation", (s) => {
    s.settings.dictation.autoDetect = true; s.settings.dictation.multilingual = "downloading"; s.settings.dictation.multilingualProgress = 0.42;
  })} />);
  expect(screen.getByText("Downloading… 42%")).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "Download multilingual voice" })).toBeNull();

  rerender(<SettingsPanel state={make("dictation", (s) => { s.settings.dictation.autoDetect = true; s.settings.dictation.multilingual = "ready"; })} />);
  expect(screen.queryByText(notice)).toBeNull();

  rerender(<SettingsPanel state={make("dictation", (s) => { s.settings.dictation.autoDetect = false; s.settings.dictation.language = "en"; s.settings.dictation.multilingual = "missing"; })} />);
  expect(screen.queryByText(notice)).toBeNull();
  rerender(<SettingsPanel state={make("dictation", (s) => { s.settings.dictation.autoDetect = false; s.settings.dictation.language = "fr"; s.settings.dictation.multilingual = "missing"; })} />);
  expect(screen.getByText(notice)).toBeInTheDocument();
});

// ---- Shortcuts
it("Shortcuts: four rows with key caps and trigger, Change posts recordHotkey with the action", () => {
  const posts = host();
  render(<SettingsPanel state={make("shortcuts")} />);
  ["Push to talk", "Text mode", "Dictate", "Hands-free dictation"].forEach((r) => expect(screen.getByText(r)).toBeInTheDocument());
  expect(screen.getAllByText("hold")).toHaveLength(2);
  expect(screen.getAllByText("double-tap")).toHaveLength(2);
  expect(screen.getAllByText("Win")).toHaveLength(2);
  fireEvent.click(screen.getByRole("button", { name: "Change Push to talk" }));
  fireEvent.click(screen.getByRole("button", { name: "Change Hands-free dictation" }));
  expect(posts("settings.action")).toEqual([
    { kind: "settings.action", action: "recordHotkey", arg: "talk" },
    { kind: "settings.action", action: "recordHotkey", arg: "handsFree" },
  ]);
});

it("Shortcuts: row shows Press your keys… until the hotkey changes or 7 s pass", () => {
  vi.useFakeTimers();
  host();
  const { rerender } = render(<SettingsPanel state={make("shortcuts")} />);
  fireEvent.click(screen.getByRole("button", { name: "Change Text mode" }));
  expect(screen.getByText("Press your keys…")).toBeInTheDocument();
  rerender(<SettingsPanel state={make("shortcuts", (s) => { s.settings.shortcuts.text.keys = ["Ctrl", "Shift"]; })} />);
  expect(screen.queryByText("Press your keys…")).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "Change Dictate" }));
  expect(screen.getByText("Press your keys…")).toBeInTheDocument();
  act(() => { vi.advanceTimersByTime(7_100); });
  expect(screen.queryByText("Press your keys…")).toBeNull();
});

it("Shortcuts: Reset to defaults asks in the page first", () => {
  const posts = host();
  render(<SettingsPanel state={make("shortcuts")} />);
  fireEvent.click(screen.getByRole("button", { name: "Reset to defaults" }));
  expect(posts("settings.action")).toEqual([]);
  fireEvent.click(screen.getByRole("button", { name: "Keep my shortcuts" }));
  expect(posts("settings.action")).toEqual([]);
  fireEvent.click(screen.getByRole("button", { name: "Reset to defaults" }));
  fireEvent.click(screen.getByRole("button", { name: "Reset all shortcuts" }));
  expect(posts("settings.action")).toEqual([{ kind: "settings.action", action: "resetHotkeys" }]);
});

// ---- Cursor
it("Cursor: four colour tiles post cursor.colour; toggles patch", () => {
  const posts = host();
  render(<SettingsPanel state={make("cursor")} />);
  expect(ACCENTS.map((a) => a.hex)).toEqual(["#3380FF", "#FF4C4C", "#D4A017", "#26C281"]);
  expect(screen.getByRole("button", { name: /Blue/ })).toHaveAttribute("aria-pressed", "true");
  fireEvent.click(screen.getByRole("button", { name: /Green/ }));
  fireEvent.click(screen.getByRole("switch", { name: "Hide floating cursor" }));
  fireEvent.click(screen.getByRole("switch", { name: "Screen highlight" }));
  fireEvent.click(screen.getByRole("switch", { name: "Shake to detach" }));
  expect(posts("settings.patch")).toEqual([
    { kind: "settings.patch", key: "cursor.colour", value: "#26C281" },
    { kind: "settings.patch", key: "cursor.hidden", value: true },
    { kind: "settings.patch", key: "cursor.screenHighlight", value: false },
    { kind: "settings.patch", key: "cursor.shakeToDetach", value: false },
  ]);
});
