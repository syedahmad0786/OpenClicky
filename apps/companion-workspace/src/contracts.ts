import { z } from "zod";

const shortText = z.string().trim().min(1).max(160);
const finiteNumber = z.number().finite();

export const artifactSchema = z.union([
  z.object({
    kind: z.literal("chart"), id: shortText, title: shortText, followupId: shortText.optional(),
    series: z.array(z.object({ label: shortText, value: finiteNumber })).min(1).max(12),
  }).strict(),
  z.object({
    kind: z.literal("diagram"), id: shortText, title: shortText, followupId: shortText.optional(),
    nodes: z.array(z.object({ id: shortText, label: shortText, detail: z.string().max(600) }).strict()).min(1).max(12),
    edges: z.array(z.object({ from: shortText, to: shortText }).strict()).max(18),
  }).strict().superRefine((value, context) => {
    const ids = new Set(value.nodes.map((node) => node.id));
    if (ids.size !== value.nodes.length || value.edges.some((edge) => !ids.has(edge.from) || !ids.has(edge.to))) {
      context.addIssue({ code: "custom", message: "Diagram references must be unique and connected to known nodes." });
    }
  }),
  z.object({
    kind: z.literal("calculator"), id: shortText, title: shortText, followupId: shortText.optional(),
    fields: z.array(z.object({ id: shortText, label: shortText, value: finiteNumber, min: finiteNumber, max: finiteNumber, step: finiteNumber.positive() }).strict()).min(1).max(6),
  }).strict().superRefine((value, context) => {
    const ids = new Set(value.fields.map((field) => field.id));
    if (ids.size !== value.fields.length || value.fields.some((field) => field.min > field.max || field.value < field.min || field.value > field.max)) {
      context.addIssue({ code: "custom", message: "Calculator values must stay inside their ranges." });
    }
  }),
  z.object({
    kind: z.literal("scene3d"), id: shortText, title: shortText, followupId: shortText.optional(),
    shape: z.enum(["cube", "pyramid"]), color: z.string().regex(/^#[0-9a-fA-F]{6}$/), rotation: finiteNumber.min(-360).max(360),
  }).strict(),
]);

export type Artifact = z.infer<typeof artifactSchema>;

export const hostMessageSchema = z.discriminatedUnion("kind", [
  z.object({ kind: z.literal("session.bootstrap"), token: z.string().min(32).max(256) }).strict(),
  z.object({ kind: z.literal("session.snapshot"), sessionId: shortText, artifacts: z.array(artifactSchema).max(12), progress: z.string().max(240).optional() }).strict(),
  z.object({ kind: z.literal("progress"), text: z.string().max(240) }).strict(),
  z.object({ kind: z.literal("message.delta"), messageId: shortText, text: z.string().max(8_000) }).strict(),
  z.object({ kind: z.literal("message.complete"), messageId: shortText, role: z.enum(["user", "assistant"]), text: z.string().max(32_000) }).strict(),
  z.object({ kind: z.literal("message.clear") }).strict(),
  z.object({ kind: z.literal("composer.prefill"), text: z.string().max(8_000) }).strict(),
  z.object({ kind: z.literal("artifact.show"), artifact: artifactSchema }).strict(),
  z.object({ kind: z.literal("approval.result"), requestId: shortText, approved: z.boolean(), note: z.string().max(300).optional() }).strict(),
  z.object({ kind: z.literal("runtime.error"), message: z.string().max(500) }).strict(),
]);

export type HostMessage = z.infer<typeof hostMessageSchema>;
export type UiMessage =
  | { kind: "bootstrap.request" }
  | { kind: "ready" }
  | { kind: "run.cancel" }
  | { kind: "artifact.changed"; artifact: Artifact }
  | { kind: "artifact.save"; artifact: Artifact }
  | { kind: "approval.request"; requestId: string; action: string; detail: string }
  | { kind: "desktop.action"; requestId: string; action: string; arguments: Record<string, string> };

export function parseHostMessage(value: unknown): HostMessage | null {
  const parsed = hostMessageSchema.safeParse(value);
  return parsed.success ? parsed.data : null;
}
