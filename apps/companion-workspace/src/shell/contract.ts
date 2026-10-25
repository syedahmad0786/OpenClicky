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
/** Page to host, on a saved drawing's chat row: the three replay modes, print, and remove from chat. */
export const drawingActions = ["replayWindow", "replayFullScreen", "replayBoard", "print", "remove"] as const;
export const settingsSections = [
  "account", "general", "voice", "microphone", "dictation", "shortcuts", "cursor", "integrations",
  "agents", "routines", "access", "memory", "notes", "clipboard", "tasks", "advanced",
] as const;
export const settingsActions = [
  "signIn", "signOut", "refreshModels", "manageUsage", "secret.edit", "testMic", "downloadWhisper", "downloadWhisperMultilingual", "playTestSound",
  "previewVoice", "clearHistory", "exportMemory", "openNotes", "openClipboard", "openTasks", "chooseAgentFolder",
  "resetAgentFolder", "openPrivacySetting", "routine.run", "routine.pause", "routine.resume", "routine.delete",
  "connector.add", "connector.test", "connector.remove", "chrome.setup", "recordHotkey", "resetHotkeys", "openLogs",
] as const;
// Mirror of apps/windows/OpenClicky.Core/Shell/OnboardingContract.cs; tests/fixtures/onboarding.v1.json lists both.
export const onboardingSteps = [
  "welcome", "colour", "microphone", "screen", "micCheck", "voiceHello", "drawDemo", "circleToAsk", "driveDemo", "textMode", "dictation", "finale",
] as const;
export const onboardingActions = ["start", "next", "back", "skip", "finish", "recheck", "openScreenSettings", "drawDemo", "driveDemo"] as const;
/** settings.patch key: dotted camelCase path, at most 60 chars (the host allow-list decides which keys exist). */
export const settingsPatchKeySchema = z.string().max(60).regex(/^[a-z]+(\.[a-zA-Z]+)+$/);

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
  /** Set on the row of a saved drawing (host: ChatRow.DrawingId); Replay, Print and Remove act on it. */
  drawingId: id.optional(),
}).strict();

const notchCardSchema = z.object({
  id,
  kind: z.enum(cardKinds),
  title: text(160),
  body: text(2_000).optional(),
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

const device = z.object({ id, name: text(160), state: z.enum(["active", "unavailable"]) }).strict();
const hotkey = z.object({ keys: z.array(text(24)).min(1).max(6), trigger: z.enum(["hold", "doubleTap"]) }).strict();
const hex = z.string().regex(/^#[0-9a-fA-F]{6}$/);

const settingsViewSchema = z.object({
  account: z.object({
    provider: z.enum(["chatgpt", "openrouter"]),
    signedInAs: text(160).nullable(),
    model: text(80),
    quickModel: text(80),
    models: z.array(text(80)).max(64),
    openRouterKeySet: z.boolean(),
    openRouterModel: text(120),
  }).strict(),
  voice: z.object({
    voice: text(80),
    voices: z.array(z.object({ id, name: text(80), detail: text(160), available: z.boolean() }).strict()).max(64),
    speed: z.number().min(0.5).max(1.5),
    captions: z.boolean(),
    readReplies: z.boolean(),
    outputDeviceId: text(160).nullable(),
    outputDevices: z.array(device).max(32),
  }).strict(),
  microphone: z.object({
    deviceId: text(160).nullable(),
    devices: z.array(device).max(32),
    whisper: z.enum(["missing", "downloading", "ready"]),
    whisperProgress: z.number().min(0).max(1),
  }).strict(),
  dictation: z.object({
    autoDetect: z.boolean(),
    language: text(16),
    languages: z.array(z.object({ code: text(16), name: text(80), native: text(80) }).strict()).max(128),
    dictionary: z.array(text(60)).max(50),
    cleanup: z.boolean(),
    multilingual: z.enum(["missing", "downloading", "ready"]),
    multilingualProgress: z.number().min(0).max(1),
  }).strict(),
  shortcuts: z.object({ talk: hotkey, text: hotkey, dictate: hotkey, handsFree: hotkey }).strict(),
  cursor: z.object({ colour: hex, hidden: z.boolean(), screenHighlight: z.boolean(), shakeToDetach: z.boolean() }).strict(),
  integrations: z.object({
    chrome: z.enum(["notInstalled", "waiting", "connected"]),
    connectors: z.array(z.object({
      key: id,
      name: text(80),
      host: text(255),
      status: z.enum(["connected", "checking", "error", "needsSignIn"]),
      toolCount: z.number().int().min(0).nullable(),
    }).strict()).max(64),
  }).strict(),
  agents: z.object({
    folder: text(4_096),
    folderIsDefault: z.boolean(),
    speakAnnouncements: z.boolean(),
    cursorUpdates: z.boolean(),
    reach: z.array(z.object({ id, label: text(160), state: z.enum(["allowed", "denied", "unknown"]) }).strict()).max(16),
  }).strict(),
  routines: z.array(z.object({
    id,
    title: text(160),
    schedule: text(160),
    next: iso.nullable(),
    last: iso.nullable(),
    status: z.enum(["scheduled", "running", "failed", "paused"]),
  }).strict()).max(100),
  access: z.object({
    mode: z.enum(["preview", "confirmEach", "auto", "full"]),
    denyList: text(2_000),
    rememberHistory: z.boolean(),
  }).strict(),
  advanced: z.object({ useLocalProxy: z.boolean(), proxyUrl: text(2_048), forceGdiCapture: z.boolean() }).strict(),
}).strict();

export const shellStateSchema = z.object({
  version: z.literal(1),
  theme: z.enum(["dark", "light"]),
  accent: hex,
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
  notch: z.object({ tab: z.enum(notchTabs), open: z.boolean(), section: z.enum(settingsSections) }).strict(),
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
  settings: settingsViewSchema,
  /** Omitted when no tour runs. */
  onboarding: z.object({
    step: z.enum(onboardingSteps),
    index: z.number().int().min(0).max(11),
    count: z.literal(12),
    stepDone: z.boolean(),
    busy: z.boolean(),
  }).strict().optional(),
}).strict();

export type ShellState = z.infer<typeof shellStateSchema>;
export type JobCard = z.infer<typeof jobCardSchema>;
export type ChatRow = z.infer<typeof chatRowSchema>;
export type DrawingAction = (typeof drawingActions)[number];
export type NotchCard = z.infer<typeof notchCardSchema>;
export type DocumentView = z.infer<typeof documentViewSchema>;
export type SettingsView = z.infer<typeof settingsViewSchema>;
export type SettingsSection = (typeof settingsSections)[number];
export type SettingsAction = (typeof settingsActions)[number];
export type NotchTab = (typeof notchTabs)[number];
export type GeneralKey = (typeof generalKeys)[number];
export type OnboardingStep = (typeof onboardingSteps)[number];
export type OnboardingAction = (typeof onboardingActions)[number];

export const hostMessageSchema = z.discriminatedUnion("kind", [
  z.object({ kind: z.literal("session.bootstrap"), token: z.string().min(32).max(256) }).strict(),
  z.object({ kind: z.literal("shell.state"), state: shellStateSchema }).strict(),
  z.object({ kind: z.literal("chat.delta"), rowId: id, text: text(8_000) }).strict(),
  z.object({ kind: z.literal("caption.word"), text: text(200) }).strict(),
  z.object({ kind: z.literal("caption.clear") }).strict(),
  z.object({ kind: z.literal("mic.level"), level: z.number().min(0).max(1) }).strict(),
  z.object({ kind: z.literal("composer.focus") }).strict(),
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
  | { kind: "drawing.action"; id: string; action: DrawingAction }
  | { kind: "update.check" }
  | { kind: "update.install" }
  | { kind: "link.open"; target: (typeof linkTargets)[number] }
  | { kind: "settings.section"; section: SettingsSection }
  | { kind: "settings.patch"; key: string; value: unknown }
  | { kind: "settings.action"; action: SettingsAction; arg?: string }
  | { kind: "app.quit" }
  | { kind: "onboarding"; action: OnboardingAction };

/** "Hold" or "Double-tap", for hints that name the talk shortcut. */
export const talkGesture = (state: ShellState) => (state.settings.shortcuts.talk.trigger === "doubleTap" ? "Double-tap" : "Hold");

export function parseShellState(value: unknown): ShellState | null {
  const parsed = shellStateSchema.safeParse(value);
  return parsed.success ? parsed.data : null;
}

export function parseHostMessage(value: unknown): HostMessage | null {
  const parsed = hostMessageSchema.safeParse(value);
  return parsed.success ? parsed.data : null;
}
