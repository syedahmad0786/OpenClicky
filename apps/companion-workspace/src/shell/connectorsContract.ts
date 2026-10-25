import { z } from "zod";

// Mirror of apps/windows/OpenClicky.Core/Shell/ShellContract.Connectors.cs (and the connector part of ShellContract.cs).
// Wire shape only: the page never receives a secret, a token, a header or an environment value.

const text = (max: number) => z.string().max(max);
const id = z.string().min(1).max(160);

/** connector.add may carry a whole form (headers, variables, arguments) in its arg; every other settings action stays at 512. */
export const MAX_CONNECTOR_ADD_ARG = 8000;

export const connectorStatuses = ["connected", "checking", "error", "needsSignIn", "configured", "signingIn", "tokenRejected", "reconnect"] as const;

export const connectorSchema = z.object({
  key: id,
  name: text(80),
  host: text(255),
  status: z.enum(connectorStatuses),
  toolCount: z.number().int().min(0).nullable(),
  app: id.nullable(),
  label: text(30).nullable(),
  kind: z.enum(["remote", "local", "vault"]),
  auth: z.enum(["browser", "apiKey", "none"]),
  since: z.string().datetime({ offset: true }).nullable(),
}).strict();

export const catalogSchema = z.object({
  id,
  name: text(80),
  description: text(200),
  setup: z.enum(["signIn", "apiKey", "none", "url", "folder"]),
  multiAccount: z.boolean(),
}).strict();

export type Connector = z.infer<typeof connectorSchema>;
export type CatalogItem = z.infer<typeof catalogSchema>;
