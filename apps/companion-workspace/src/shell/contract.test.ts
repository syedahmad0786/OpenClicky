import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { parseHostMessage, parseShellState, settingsActions, settingsPatchKeySchema, settingsSections, shellStateSchema } from "./contract";

const fixture = JSON.parse(readFileSync(resolve(__dirname, "../../../../tests/fixtures/shell-state.v1.json"), "utf8"));

describe("shell contract", () => {
  it("parses the v1 fixture", () => {
    const state = parseShellState(fixture);
    expect(state).not.toBeNull();
    expect(new Set(state!.cards.map((card) => card.kind)).size).toBe(12);
    expect(new Set(state!.jobs.map((job) => job.state)).size).toBe(7);
    expect(state).toEqual(fixture);
  });

  it("rejects unknown fields and bad values", () => {
    expect(parseShellState({ ...fixture, extra: 1 })).toBeNull();
    expect(parseShellState({ ...fixture, version: 2 })).toBeNull();
    expect(parseShellState({ ...fixture, accent: "blue" })).toBeNull();
    expect(parseShellState({ ...fixture, notch: { tab: "nope", open: true } })).toBeNull();
  });

  it("enforces list caps", () => {
    const job = fixture.jobs[0];
    expect(shellStateSchema.safeParse({ ...fixture, jobs: Array(81).fill(job) }).success).toBe(false);
    expect(shellStateSchema.safeParse({ ...fixture, jobs: [{ ...job, bullets: ["a", "b", "c", "d", "e"] }] }).success).toBe(false);
    const file = fixture.jobs[0].files[0];
    expect(shellStateSchema.safeParse({ ...fixture, jobs: [{ ...job, files: Array(7).fill(file) }] }).success).toBe(false);
    const row = fixture.chat.rows[0];
    expect(shellStateSchema.safeParse({ ...fixture, chat: { rows: Array(201).fill(row) } }).success).toBe(false);
    const action = { id: "x", label: "X", style: "primary" };
    expect(shellStateSchema.safeParse({ ...fixture, cards: [{ id: "c", kind: "tip", title: "t", actions: Array(4).fill(action) }] }).success).toBe(false);
    expect(shellStateSchema.safeParse({ ...fixture, document: { ...fixture.document, content: "x".repeat(200_001) } }).success).toBe(false);
  });

  it("accepts null featured and document", () => {
    expect(parseShellState({ ...fixture, featured: null, document: null })).not.toBeNull();
  });

  it("parses host messages", () => {
    expect(parseHostMessage({ kind: "shell.state", state: fixture })?.kind).toBe("shell.state");
    expect(parseHostMessage({ kind: "chat.delta", rowId: "r", text: "hi" })?.kind).toBe("chat.delta");
    expect(parseHostMessage({ kind: "caption.word", text: "hi" })?.kind).toBe("caption.word");
    expect(parseHostMessage({ kind: "session.bootstrap", token: "t".repeat(32) })?.kind).toBe("session.bootstrap");
  });

  it("fixture covers every settings section", () => {
    const state = parseShellState(fixture)!;
    expect(state.notch.section).toBe("general");
    expect(Object.keys(state.settings).sort()).toEqual(
      ["account", "advanced", "access", "agents", "cursor", "dictation", "integrations", "microphone", "routines", "shortcuts", "voice"].sort(),
    );
    expect(state.settings.microphone.devices.map((d) => d.state)).toEqual(["active", "unavailable"]);
    expect(state.settings.integrations.connectors.map((c) => c.toolCount)).toEqual([12, null]);
    expect(state.settings.routines.map((r) => r.next)).toEqual(["2026-10-25T08:00:00Z", null]);
  });

  it("requires notch.section and settings, and rejects unknown values", () => {
    const { section: _section, ...noSection } = fixture.notch;
    expect(parseShellState({ ...fixture, notch: noSection })).toBeNull();
    expect(parseShellState({ ...fixture, notch: { ...fixture.notch, section: "billing" } })).toBeNull();
    const { settings: _settings, ...noSettings } = fixture;
    expect(parseShellState(noSettings)).toBeNull();
    expect(parseShellState({ ...fixture, settings: { ...fixture.settings, extra: 1 } })).toBeNull();
    expect(parseShellState({ ...fixture, settings: { ...fixture.settings, access: { ...fixture.settings.access, mode: "yolo" } } })).toBeNull();
    expect(parseShellState({ ...fixture, settings: { ...fixture.settings, cursor: { ...fixture.settings.cursor, colour: "red" } } })).toBeNull();
    expect(parseShellState({ ...fixture, settings: { ...fixture.settings, shortcuts: { ...fixture.settings.shortcuts, talk: { keys: [], trigger: "hold" } } } })).toBeNull();
  });

  it("caps settings lists", () => {
    const dictionary = (words: string[]) => ({ ...fixture, settings: { ...fixture.settings, dictation: { ...fixture.settings.dictation, dictionary: words } } });
    expect(parseShellState(dictionary(Array(50).fill("w")))).not.toBeNull();
    expect(parseShellState(dictionary(Array(51).fill("w")))).toBeNull();
    expect(parseShellState(dictionary(["x".repeat(40)]))).not.toBeNull();
    expect(parseShellState(dictionary(["x".repeat(41)]))).toBeNull();
    const speed = (value: number) => ({ ...fixture, settings: { ...fixture.settings, voice: { ...fixture.settings.voice, speed: value } } });
    expect(parseShellState(speed(0.5))).not.toBeNull();
    expect(parseShellState(speed(3))).toBeNull();
  });

  it("knows every sidebar section and settings action", () => {
    expect(settingsSections).toHaveLength(16);
    expect(settingsActions).toHaveLength(28);
    expect(settingsSections).not.toContain("billing");
    expect(settingsActions).not.toContain("formatDisk");
  });

  it("validates settings.patch keys", () => {
    for (const key of ["voice.speed", "cursor.screenHighlight", "a." + "b".repeat(58)]) expect(settingsPatchKeySchema.safeParse(key).success).toBe(true);
    for (const key of ["speed", "Voice.speed", "voice..speed", "voice.speed.", "voice.sp eed", "voice.speed\n", "a." + "b".repeat(59)])
      expect(settingsPatchKeySchema.safeParse(key).success).toBe(false);
  });

  it("parses mic.level", () => {
    expect(parseHostMessage({ kind: "mic.level", level: 0 })?.kind).toBe("mic.level");
    expect(parseHostMessage({ kind: "mic.level", level: 1 })?.kind).toBe("mic.level");
    expect(parseHostMessage({ kind: "mic.level", level: 1.01 })).toBeNull();
    expect(parseHostMessage({ kind: "mic.level", level: -0.1 })).toBeNull();
    expect(parseHostMessage({ kind: "mic.level" })).toBeNull();
  });

  it("rejects unknown kinds", () => {
    expect(parseHostMessage({ kind: "shell.explode" })).toBeNull();
    expect(parseHostMessage({ kind: "chat.delta", rowId: "r", text: "hi", extra: true })).toBeNull();
    expect(parseHostMessage({ kind: "session.bootstrap", token: "short" })).toBeNull();
    expect(parseHostMessage(null)).toBeNull();
  });
});
