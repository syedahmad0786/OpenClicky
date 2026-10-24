import { z } from "zod";

// Mirror of apps/windows/OpenClicky.Core/Shell/ShellContract.cs. Keep both in step;
// tests/fixtures/shell-state.v1.json is parsed by both test suites.

const text = (max: number) => z.string().max(max);
const label = z.string().trim().min(1).max(160);
const id = z.string().min(1).max(160);
const iso = z.string().datetime({ offset: true });

export const jobStates = ["queued", "working", "needsYou", "done", "failed", "stopped", "new"] as const;
export const cardKinds = [
  "activity", "textResponse", "jobUpdate", "needsYou", "mutedSpeaker", "micTrouble",
  "clipboard", "dictionaryLearned", "documentAccess", "appUpdated", "tip", "toast",
] as const;
export const notchTabs = ["home", "chat", "settings", "document"] as const;
export const generalKeys = ["showInTaskbar", "showInRecordings", "launchAtLogin", "paused"] as const;
export const linkTargets = ["whatsNew", "bug", "feature"] as const;

const jobCardSchema = z.object({
  id,
  title: label,
  state: z.enum(jobStates),
  colour: z.enum(["red", "blue", "yellow", "green"]),
  summary: text(2_000).optional(),
  stat: z.object({ value: text(40), label: text(80) }).strict().optional(),
  bullets: z.array(text(400)).max(4),
  files: z.array(z.object({
    name: text(260),
    path: text(4_096),
    kind: z.enum(["doc", "pdf", "sheet", "image", "audio", "video", "link", "folder", "other"]),
  }).strict()).max(6),
  liveLine: text(400).optional(),
  plan: z.array(z.object({ text: text(400), done: z.boolean() }).strict()).max(24),
  updatedAt: iso,
}).strict();

const chatRowSchema = z.object({
  id,
  role: z.enum(["user", "assistant", "event"]),
  text: text(32_000),
  at: iso,
  jobId: id.optional(),
}).strict();

const notchCardSchema = z.object({
  id,
  kind: z.enum(cardKinds),
  title: text(160),
  body: text(1_000).optional(),
  actions: z.array(z.object({ id, label, style: z.enum(["primary", "secondary", "danger"]) }).strict()).max(3),
  autoDismissMs: z.number().int().min(0).max(600_000).optional(),
}).strict();

const documentViewSchema = z.object({
  path: text(4_096),
  name: text(260),
  kind: z.enum(["markdown", "text", "csv", "pdf"]),
  content: text(200_000),
  truncated: z.boolean(),
}).strict();

export const shellStateSchema = z.object({
  version: z.literal(1),
  theme: z.enum(["dark", "light"]),
  accent: z.string().regex(/^#[0-9a-fA-F]{6}$/),
  reducedMotion: z.boolean(),
  user: z.object({ displayName: text(80) }).strict(),
  hotkeys: z.object({
    talk: z.array(text(24)).max(6),
    text: z.array(text(24)).max(6),
    dictate: z.array(text(24)).max(6),
    handsFree: z.array(text(24)).max(6),
  }).strict(),
  status: z.object({
    phase: z.enum(["idle", "listening", "thinking", "speaking", "working", "paused"]),
    jobsWorking: z.number().int().min(0),
    jobsNeedYou: z.number().int().min(0),
  }).strict(),
  notch: z.object({ tab: z.enum(notchTabs), open: z.boolean() }).strict(),
  featured: jobCardSchema.nullable(),
  jobs: z.array(jobCardSchema).max(80),
  chat: z.object({ rows: z.array(chatRowSchema).max(200) }).strict(),
  // The notch shows at most 6 cards; the host trims. The contract allows one of every kind (12).
  cards: z.array(notchCardSchema).max(12),
  apps: z.object({ connected: z.array(z.object({ id, name: text(80) }).strict()).max(64) }).strict(),
  general: z.object({
    showInTaskbar: z.boolean(),
    showInRecordings: z.boolean(),
    launchAtLogin: z.boolean(),
    paused: z.boolean(),
  }).strict(),
  update: z.object({
    current: text(40),
    available: text(40).nullable(),
    state: z.enum(["idle", "checking", "available", "downloading", "ready", "upToDate", "error"]),
    progress: z.number().min(0).max(1),
  }).strict(),
  document: documentViewSchema.nullable(),
}).strict();

export type ShellState = z.infer<typeof shellStateSchema>;
export type JobCard = z.infer<typeof jobCardSchema>;
export type ChatRow = z.infer<typeof chatRowSchema>;
export type NotchCard = z.infer<typeof notchCardSchema>;
export type DocumentView = z.infer<typeof documentViewSchema>;
export type NotchTab = (typeof notchTabs)[number];
export type GeneralKey = (typeof generalKeys)[number];

export const hostMessageSchema = z.discriminatedUnion("kind", [
  z.object({ kind: z.literal("session.bootstrap"), token: z.string().min(32).max(256) }).strict(),
  z.object({ kind: z.literal("shell.state"), state: shellStateSchema }).strict(),
  z.object({ kind: z.literal("chat.delta"), rowId: id, text: text(8_000) }).strict(),
  z.object({ kind: z.literal("caption.word"), text: text(200) }).strict(),
]);

export type HostMessage = z.infer<typeof hostMessageSchema>;

/** Page to host. The host re-validates every one of these (ShellIntentParser). */
export type PageMessage =
  | { kind: "bootstrap.request" }
  | { kind: "ready" }
  | { kind: "surface.regions"; rects: { x: number; y: number; w: number; h: number; r: number }[] }
  | { kind: "surface.focus"; wanted: boolean }
  | { kind: "notch.tab"; tab: NotchTab }
  | { kind: "notch.open"; open: boolean }
  | { kind: "talk.toggle" }
  | { kind: "text.submit"; text: string }
  | { kind: "general.set"; key: GeneralKey; value: boolean }
  | { kind: "job.open" | "job.stop" | "job.dismiss"; id: string }
  | { kind: "file.open" | "file.reveal"; path: string }
  | { kind: "card.action"; cardId: string; actionId: string }
  | { kind: "card.dismiss"; cardId: string }
  | { kind: "update.check" }
  | { kind: "update.install" }
  | { kind: "link.open"; target: (typeof linkTargets)[number] }
  | { kind: "app.quit" };

export function parseShellState(value: unknown): ShellState | null {
  const parsed = shellStateSchema.safeParse(value);
  return parsed.success ? parsed.data : null;
}

export function parseHostMessage(value: unknown): HostMessage | null {
  const parsed = hostMessageSchema.safeParse(value);
  return parsed.success ? parsed.data : null;
}
