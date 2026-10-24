import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { parseHostMessage, parseShellState, shellStateSchema } from "./contract";

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

  it("rejects unknown kinds", () => {
    expect(parseHostMessage({ kind: "shell.explode" })).toBeNull();
    expect(parseHostMessage({ kind: "chat.delta", rowId: "r", text: "hi", extra: true })).toBeNull();
    expect(parseHostMessage({ kind: "session.bootstrap", token: "short" })).toBeNull();
    expect(parseHostMessage(null)).toBeNull();
  });
});
