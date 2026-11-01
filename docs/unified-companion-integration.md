# Unified companion integration research

Reviewed 11 October 2026. This record separates the native **0.4 implementation now in the worktree** from the larger integration target. Source-backed target behavior is not evidence that an upstream runtime, account connector, or live protocol transport is wired into the application.

## Current 0.4 implementation status

The current native slice includes a task timeline and progress records; task pause, resume, stop, change-task, note, and remember voice commands; explicit local companion memory; local Notes create/read/update/delete, search, and Markdown export; selected-screen-region capture; and the visible selection ring and cursor trail. These features remain in the WinForms application.

Four user-selectable native desktop-task modes are implemented. **Confirm each** is the default. **Auto** first asks the user to approve a generated task plan, then uses conservative English keyword/rationale checks and always confirms hotkeys. Those heuristics can miss a sensitive step. **Full access** shows an explicit warning that it may send, publish, delete, or change visible data and then skips per-action confirmations for the native task loop. Its foreground focus checks and soft window-title deny list reduce mistakes, but they are not a security boundary. Completed actions may not be reversible.

The OpenCode work is currently a bounded protocol adapter, not a complete task runtime UI. Against an already installed OpenCode 1.17.18 process, isolated verification covered loopback health, session creation, message/status reads, and abort. The adapter also has focused contract tests for asynchronous prompt submission, but no live AI prompt, provider authorization, account connection, OpenCode event stream, permission handoff, or managed OpenCode process lifecycle has been proved. AG-UI-shaped output currently exists only as an NDJSON export of stored local task history; there is no live CopilotKit/AG-UI transport.

## Decision

Keep **one native OpenClicky Windows application**. The integration target is to use OpenCode as a separately authenticated, loopback-only task runtime, translate its stream into a small AG-UI-compatible event model, and render the useful parts in the existing .NET UI. Do not embed OpenWork, OpenWhispr, CopilotKit, Nango, Composio, or Open Intelligent UI as additional desktop applications.

For the first account-connection experiment, **Composio Cloud is the recommended default** because its current free plan and managed OAuth produce the smallest one-click path for a personal companion. This recommendation is conditional: Composio stores provider credentials and executes requests in its cloud. If “user-owned” means that tokens must remain in infrastructure Ahmad controls, Nango self-hosting is the relevant alternative, with the limitation that its free edition supplies Auth and Proxy only. Account implementation is paused at the user's request; this comparison records the later decision without opening a connection now.

OpenCode's own documented ChatGPT Plus/Pro sign-in may legitimately power OpenCode. OpenClicky's existing ChatGPT credential may not be copied, exported, or translated into an OpenCode credential. The user must complete OpenCode's browser authorization as a separate consent flow.

The repository referred to as the “GPT repo” was not identified. No design claim or dependency decision can be made for it until its exact owner/name or URL is supplied.

## What each project contributes

| Project and reviewed revision | License at that revision | Useful contribution | Boundary for OpenClicky |
| --- | --- | --- | --- |
| [different-ai/openwork `ad22cdc`](https://github.com/different-ai/openwork/tree/ad22cdc2217370f28f24f0f8758016c2921e3c73) | [MIT outside `ee/`; OpenWork EE License inside `ee/`](https://github.com/different-ai/openwork/blob/ad22cdc2217370f28f24f0f8758016c2921e3c73/LICENSE) | Proven pattern for launching, authenticating, health-checking, and proxying a real OpenCode runtime; its app and server both use `@opencode-ai/sdk` | Reference the MIT runtime pattern. Do not embed its Electron/React app or copy `ee/`. OpenWork is not the task engine; OpenCode is. |
| [anomalyco/opencode `d21b079`](https://github.com/anomalyco/opencode/tree/d21b0790a6c146815f28fea604e41e922f009590) | [MIT](https://github.com/anomalyco/opencode/blob/d21b0790a6c146815f28fea604e41e922f009590/LICENSE) | Headless task runtime, worktree/file tools, sessions, provider auth, permission requests, and SSE events | Run as a loopback child/service. It acts on task files and configured tools. It does not prove or perform Windows UI/browser clicking unless a separate tool is explicitly added. |
| [OpenWhispr/openwhispr `5f9a88b`](https://github.com/OpenWhispr/openwhispr/tree/5f9a88b0481a066c147bfd2c38ea1d9acae502ee) | [MIT](https://github.com/OpenWhispr/openwhispr/blob/5f9a88b0481a066c147bfd2c38ea1d9acae502ee/LICENSE) | Voice-first interaction patterns, offline transcription, dictation, notes, and explicit local/cloud choices | Reuse patterns in the existing .NET audio path. Its Electron 41/React application and native download/build chain are not a drop-in Windows library. |
| [NangoHQ/nango `b309bc1`](https://github.com/NangoHQ/nango/tree/b309bc1716f9a868e7cf3af0a403a7b0da093df2) | [Elastic License 2.0](https://github.com/NangoHQ/nango/blob/b309bc1716f9a868e7cf3af0a403a7b0da093df2/LICENSE), including the hosted/managed-service restriction | OAuth/API-key authorization, token refresh, multiple user or organization connections, proxying, tools, syncs, and triggers | Use as an external backend only after choosing Cloud, free self-host, or paid Enterprise self-host. Do not describe ELv2 as MIT or OSI open source. |
| [ComposioHQ/composio `f9ce499`](https://github.com/ComposioHQ/composio/tree/f9ce49953598e13371c406ac346846696a4d8385) | [MIT SDK/CLI repository](https://github.com/ComposioHQ/composio/blob/f9ce49953598e13371c406ac346846696a4d8385/LICENSE); cloud service terms still apply | Managed OAuth, Connect Links, per-user sessions, tool discovery/execution, triggers, and hosted MCP | Best small cloud MVP, but its MIT client does not make the hosted credential store self-owned. Private VPC, self-hosting, and customer-managed keys are Enterprise arrangements. |
| [CopilotKit/CopilotKit `04bc56a`](https://github.com/CopilotKit/CopilotKit/tree/04bc56aeff506d8d529fe3ebd4b1b442c49fdee5) | [MIT](https://github.com/CopilotKit/CopilotKit/blob/04bc56aeff506d8d529fe3ebd4b1b442c49fdee5/LICENSE) | Reference UI patterns for streaming chat, tool progress, state, and human input | Its ready-made UI is primarily React/Angular/Vue/React Native and expects a CopilotKit runtime. Do not add a second web UI just to get chat controls already present in WinForms. |
| [ag-ui-protocol/ag-ui `2aabfc8`](https://github.com/ag-ui-protocol/ag-ui/tree/2aabfc8cada0b429927e4c256c9cf990a6791e5f) | [MIT](https://github.com/ag-ui-protocol/ag-ui/blob/2aabfc8cada0b429927e4c256c9cf990a6791e5f/LICENSE) | Protocol vocabulary and ordering for runs, text, tools, state, activity, and errors; official .NET SDK exists in the monorepo | Adopt only the event contract needed by the native app. AG-UI is the agent-to-UI protocol, not a task runner, connector, sandbox, or renderer. |
| [CopilotKit/OpenIntelligentUI `f6e4388`](https://github.com/CopilotKit/OpenIntelligentUI/tree/f6e4388b26a64b9a0714943b08a1ce622b924eec) | [MIT](https://github.com/CopilotKit/OpenIntelligentUI/blob/f6e4388b26a64b9a0714943b08a1ce622b924eec/LICENSE) | Patterns for selecting text, registered components, or generated interactive HTML/SVG/Canvas | Its product is Next.js + Python/FastAPI + CopilotKit. Generated UI runs in a sandboxed browser iframe. It is not a native Windows renderer and its 3D scene is generated Three.js/WebGL content, not a general 3D capability supplied by AG-UI. |

OpenWork's split license is material. Its current `ee/` code requires a production subscription except for organizations with up to five total users excluding Enterprise Features, a 30-day internal evaluation, and development/testing. Each covered version converts to MIT two years after publication. The desktop app and core server outside `ee/` are MIT. The smallest design does not need `ee/`.

## Target native architecture

```text
OpenClicky WinForms Home / tray / voice shortcut / task hub
        |
        +-- existing local STT, screen capture, UIA + mode-governed SendInput
        |
        +-- Task coordinator and local approval broker
        |       +-- visible task boundary and permission mode
        |       +-- local task/session metadata and user-managed memory
        |       +-- final-action and sensitive-action gates
        |
        +-- OpenCode bridge over loopback HTTP + SSE
        |       +-- one managed `opencode serve` process
        |       +-- generated HTTP Basic credential
        |       +-- session create/prompt/abort/permission APIs
        |       +-- OpenCode event -> minimal AG-UI event translation
        |
        +-- optional connector backend (decision pending)
                +-- Composio Cloud for smallest one-click MVP, or
                +-- Nango Cloud / self-host for different custody needs
```

In the target architecture, the OpenClicky process remains the owner of user-visible state. It would start OpenCode on `127.0.0.1` with a free port, supply a generated `OPENCODE_SERVER_PASSWORD`, wait for health, and terminate only the child instance it created. The password belongs in memory or the existing Windows-protected secret store and must not appear in settings JSON or logs. Binding beyond loopback is out of scope. The 0.4 adapter currently connects only to an already-running loopback process and does not implement this lifecycle.

OpenWork demonstrates a larger version of this arrangement: its MIT server uses `@opencode-ai/sdk`, launches managed OpenCode instances with `OPENCODE_SERVER_USERNAME` and `OPENCODE_SERVER_PASSWORD`, and proxies sessions and events through its own scoped bearer-token layer. OpenClicky does not need OpenWork's server, database, Electron shell, organization control plane, or two-layer token system for a single local Windows user. The direct OpenCode API is the smaller complete seam.

### OpenCode HTTP contract and remaining spike

The following routes are documented by OpenCode at the reviewed revision:

| Purpose | Method and route | Use in OpenClicky |
| --- | --- | --- |
| Prove the child is ready and record its version | `GET /global/health` | Required before enabling task input |
| Inspect runtime OpenAPI | `GET /doc` | Development diagnostics only; generate no client at runtime |
| Discover providers and their auth methods | `GET /provider`, `GET /provider/auth` | Populate a provider connection screen without reading another app's credentials |
| Start provider-owned OAuth | `POST /provider/{id}/oauth/authorize`, then `POST /provider/{id}/oauth/callback` | Optional future UI around OpenCode's own flow; the user completes browser consent |
| Set credentials matching a provider schema | `PUT /auth/:id` | API-key entry only after an explicit secure UI; never use this to transplant OpenClicky's ChatGPT token |
| Create and read tasks | `POST /session`, `GET /session`, `GET /session/:id` | One OpenCode session per bounded task |
| Send work | `POST /session/:id/prompt_async` | Prefer the asynchronous route so the UI follows one event stream and can cancel |
| Read reconciled history and status | `GET /session/:id/message`, `GET /session/status`, `GET /session/:id/todo`, `GET /session/:id/diff` | Recovery, receipts, and task detail views |
| Stop work | `POST /session/:id/abort` | Target mapping for native Stop; the adapter call is implemented but is not wired into the task UI |
| Answer an OpenCode permission request | `POST /session/:id/permissions/:permissionID` with `{ response, remember? }` | Future broker maps the selected task mode and scope to a response; an event alone grants nothing |
| Receive runtime activity | `GET /event` | SSE; the first event is `server.connected`, followed by bus events |

`POST /session/:id/message` waits for the response, while `prompt_async` accepts the same message body and returns `204`. That body can contain `messageID`, `model`, `agent`, `noReply`, `system`, `tools`, and `parts`. The implementation should use the generated SDK schema or the live `/doc` specification from the pinned binary rather than hand-maintaining a broad API client.

OpenWork's current development branch imports `@opencode-ai/sdk/v2/client` and its proxy code uses the newer `/api/session`, `/api/event`, `/api/permission/request`, and related `/api/...` shapes. Those are real OpenCode endpoints for the OpenCode binary OpenWork pins, but they are not interchangeable with the unprefixed contract in OpenCode's public server page above. The spike must choose one exact OpenCode build, read that build's `/doc`, and keep one matching SDK/route family. Copying a mixture of OpenWork v2 routes and stable documentation would create a brittle client.

OpenCode also publishes file search/read/status and session shell routes. Those are **workspace execution capabilities**. They do not click a visible Windows control, inspect the destination after a click, or operate a browser UI. OpenClicky's existing UI Automation/SendInput path remains a separate local tool family with its own mode handling, focus restoration, recapture, and receipts. Confirm each requests per-action approval; Full access explicitly waives it for the native task. A browser task must use an explicitly selected browser automation capability; a successful HTTP/file tool result is not evidence that a visible browser action occurred.

### Separate ChatGPT authorization

OpenCode's official provider guide explicitly documents:

1. Run its Connect flow and select OpenAI.
2. Choose **ChatGPT Plus/Pro**.
3. Complete authentication in the browser.
4. Select the resulting OpenAI model in OpenCode.

OpenCode stores and refreshes that OAuth credential under its own auth system. This is a supported OpenCode flow and may power the OpenCode runtime. OpenClicky's existing ChatGPT sign-in is a different OAuth client, token store, account binding, and permission contract. Reusing its token in OpenCode would be an unsupported token transplant and is excluded. OpenClicky may launch OpenCode's authorization URL, show its pending state, and verify the OpenCode provider after the user returns; it must not automate browser sign-in or credential entry.

### Target minimal AG-UI-compatible bridge

AG-UI 1.0 defines 31 event types across run/step, text, tool, reasoning, state, activity, subagent, and passthrough families. A future live bridge needs only a smaller first slice:

| Native event | Source | Required fields/behavior |
| --- | --- | --- |
| `RUN_STARTED` | A prompt is accepted for an OpenCode session | Stable OpenClicky task/thread ID and unique run ID |
| `TEXT_MESSAGE_START`, `TEXT_MESSAGE_CONTENT`, `TEXT_MESSAGE_END` | Assistant message parts from the OpenCode event stream | Stable message ID; nonempty deltas; preserve arrival order |
| `TOOL_CALL_START`, `TOOL_CALL_ARGS`, `TOOL_CALL_END` | OpenCode tool part lifecycle | Stable tool-call ID; arguments remain text until complete |
| `TOOL_CALL_RESULT` | OpenCode tool completion or local tool receipt | Result is displayed and retained; it is not treated as visual proof |
| `ACTIVITY_SNAPSHOT` or `ACTIVITY_DELTA` | Todos/status/progress | Drives the task list and progress panel without inventing assistant chat text |
| `STATE_SNAPSHOT` | Reconciled session/task state | Used after reconnect; avoid JSON Patch until a real delta use case exists |
| `RUN_FINISHED` or `RUN_ERROR` | Terminal OpenCode state, disconnect, or validated failure | Exactly one terminal event per run |

`STEP_*`, reasoning, subagents, `STATE_DELTA`, raw passthrough, binary/protobuf transport, and a public AG-UI endpoint can wait. Version 0.4 only exports stored local task history as AG-UI-shaped NDJSON. It does not stream these events, accept `RunAgentInput`, or connect to CopilotKit. If a public endpoint is later required, accept AG-UI `RunAgentInput` by `POST`, respond as `text/event-stream`, and serialize each event as `data: {json}\n\n`. AG-UI input carries `threadId`, `runId`, `messages`, and optional `tools`, `context`, `state`, `forwardedProps`, and `resume`. Frontend tools describe actions the application executes; they do not grant those actions permission.

The official AG-UI monorepo now includes a .NET SDK. A spike should compare its package size and API stability with the handful of local records above. Do not add it merely to serialize seven JSON event shapes.

## Permission modes: implemented native behavior

Version 0.4 applies the selected mode to the existing native desktop task loop. It does not yet provide one cross-engine approval broker for OpenCode and connected-app tools. The selected mode is copied into each task at creation so a later settings change does not alter the running task.

| Mode | Behavior |
| --- | --- |
| **Preview** | Produces the task plan without executing desktop actions. |
| **Confirm each** | Default. Requests confirmation before each native action. Existing focus restoration and visible-destination checks still apply. |
| **Auto** | Shows the generated plan for task-level approval, then runs routine native actions. It requests confirmation when a conservative English keyword/rationale heuristic flags risk and for every hotkey. The heuristic can miss risk, so Confirm each is the strict-control choice. |
| **Full access** | Shows an explicit warning, then runs native actions without individual confirmations. The warning states that the task may send, publish, delete, or change visible data and that Stop cannot undo completed actions. |

The current focus guard and window-title deny list are soft safeguards. A title can be missing, stale, or misleading, and focus checks do not prove the semantic effect of a click or keystroke. They must not be described as a dedicated security broker or a guaranteed hard stop. Full access is an explicit user override of per-action confirmation for the native task.

For the future cross-engine broker, define sensitive/final actions to include sending or publishing content, deleting or overwriting user data, submitting a form, changing access or sharing, purchases/bookings, account connections, credentials, and Windows security changes. That broker should mediate native actions, OpenCode permission responses, and connector tools through one task boundary and one receipt model. These cross-engine gates and hard-stop policies are target requirements, not 0.4 implementation claims.

AG-UI tool-call events would show what the agent requested, while OpenCode permission endpoints control what that runtime may continue. Neither supplies a cross-engine policy by itself. A later broker must decide how the selected mode applies before it returns an OpenCode permission response or invokes a connector tool.

## Voice and memory

OpenWhispr confirms the product value of explicit Dictate, Notes, and Assistant modes, local/cloud speech choices, and preserving the user's original words. OpenClicky already has a native Windows capture, Whisper, shortcut, and spoken-reply path. Replacing it with OpenWhispr would add a second Electron UI, native helper downloads, storage, settings, and shortcut ownership. Reuse its interaction lessons, not its process.

The current native task UI and voice commands can create/control a visible native task, add a local note, or explicitly remember a fact. OpenCode is not yet the task runner behind that UI. The target interaction keeps Dictation, Notes, and Task distinct so one mode never silently becomes another. The off-device disclosure and per-mode preview policy need end-to-end verification when OpenCode or a connector is wired in.

Keep distinct current and future history layers:

- **Current native task history:** local task runs and progress, with an optional AG-UI-shaped NDJSON export.
- **Future OpenCode task history:** OpenCode session messages, todos, and diffs for one bounded task after runtime integration.
- **Companion memory:** small, explicitly added facts/preferences stored locally by OpenClicky, visible and deletable in the native UI.

Do not treat OpenCode sessions as a global personal-memory system, import private chat databases, or silently promote task text into companion memory. CopilotKit Intelligence offers hosted memory features, but none are required for this architecture.

## Account connections: Composio versus Nango

Pricing and product limits below were read from the vendors' official pricing and documentation on 11 October 2026 and can change.

| Question | Composio Cloud | Nango Cloud | Nango free self-host |
| --- | --- | --- | --- |
| Smallest personal setup | Strongest: managed OAuth and a hosted Connect Link; 123 OAuth toolkits currently list a Composio-managed app | Hosted Connect UI and managed apps; still a short backend connect-session flow | Weak: operate Docker/Postgres, set a non-rotatable encryption key, expose correct OAuth callback URLs, and harden the dashboard |
| Current free allowance | 100,000 tool calls/month, 50,000 trigger events/month, three team members; unlimited connected accounts with your own app; managed apps include up to 20,000 of those calls and up to 1,000 free connections | 10 connections, 10 compute hours/month, 10 GB transfer/month | No vendor plan fee for the limited edition, but infrastructure/operations are yours |
| Agent-ready tools | Sessions, search, execution, triggers, and hosted MCP | Prebuilt/custom tools, triggers, syncs, proxy, and MCP on Cloud | No tool calls, prebuilt tools, syncs, triggers, webhooks, or MCP; Auth and Proxy only |
| OAuth app choice | Managed app for the fastest start, or your OAuth app for branding, scopes, and dedicated provider quota | Nango-managed or your OAuth app | Your configuration; free edition cannot customize auth branding |
| Token custody | Composio stores provider credentials and executes requests; owning the OAuth app does not move the token store | Nango Cloud stores credentials and refreshes tokens; credentials are exportable | Tokens are encrypted in your Postgres using your `NANGO_ENCRYPTION_KEY` |
| Multi-account identity | Stable application `user_id`; private connected accounts by default, with explicit shared-account ACL support | Connect session identifies the end user/organization; multiple connections for the same API and user are supported | Single-organization trust model; dashboard accounts are not a hardened boundary between untrusted users |
| Privacy controls on free plan | Seven-day log retention; zero-data-retention is a paid add-on | SOC 2 feature set and credential controls on Cloud; EU cloud is not currently offered | No automatic data/telemetry leaves the instance, but the operator owns hardening, backup, and incident response |
| Self-host path | Private VPC, self-host, or customer-managed-key arrangements are Enterprise | Full BYOC/Self-Managed is Enterprise; free self-host remains Auth + Proxy only | Available under ELv2's restrictions |

### Recommendation and auth steps

For a single-user OpenClicky MVP, use **Composio Cloud with managed OAuth**, provided Ahmad accepts Composio as the credential and tool-execution boundary. Restrict the first session to one toolkit and a small allow-list of read tools. Disable its remote sandbox for the first slice; OpenCode already owns file/task execution and a second code runtime increases the approval surface. Use Composio's current Sessions API rather than legacy direct execution.

The eventual connection flow should be:

1. The user creates/selects the Composio project and obtains its project key themselves. OpenClicky stores it with the existing Windows-protected secret store; it is never placed in settings JSON, logs, or an OpenCode prompt.
2. OpenClicky creates a stable random local user ID. It does not use an email address as an authorization boundary.
3. Its backend creates a Composio session limited to the requested toolkit and allowed tools, with connection management enabled and sandbox disabled.
4. The backend requests a Connect Link. OpenClicky opens it in the system browser and waits; it does not automate sign-in.
5. The user reviews the upstream provider scopes and consents. **A personal Gmail connection requires a separate explicit approval at this point**, even if another Google account or toolkit was previously connected.
6. After callback, the backend verifies the connected-account status and owning user ID, then stores only the Composio session/account identifiers locally. Provider tokens stay in Composio.
7. Once the future cross-engine broker exists, each tool call passes through the selected OpenClicky task mode and produces a verified receipt.

Choose **Nango Cloud** instead if credential export, an eventual Nango migration path, or Nango's integration-code model matters more than the larger Composio free allowance. Its flow is: keep the environment secret key on the backend, `POST /connect/sessions` with a stable `end_user`, optional organization, and `allowed_integrations`; give only the short-lived session token/link to the UI; let the user complete Connect UI; store the returned `connectionId` and integration key; execute from the backend with the secret key. Multiple connections for one user/API need distinct connection IDs.

Choose **Nango free self-host** only when local infrastructure custody is mandatory and Auth + Proxy are sufficient. It cannot replace the agent tool layer in this design. Full Nango self-hosting adds all managed features but requires an Enterprise subscription and substantially more infrastructure. Composio Enterprise and Nango Enterprise should be compared contractually if customer deployments, regulated data, or multi-tenant isolation become requirements.

## What Open Intelligent UI can render

Open Intelligent UI currently chooses among plain text, registered native React components, A2UI for basic structured output, and Open Generative UI for custom HTML/CSS/JavaScript. The custom path streams ordered fields:

`initialHeight` -> `placeholderMessages` -> `css` -> `html` -> `jsFunctions` -> `jsExpressions`

The browser renderer first updates a preview iframe, then starts a Websandbox iframe with injected design-system CSS and a CDN import map. The sandbox exposes validated `sendPrompt` and HTTPS `openLink` callbacks; it has no same-origin access to host storage/APIs. Its 3D airplane demo works because the iframe can import Three.js and use WebGL. A request for “3D” does not cause AG-UI, CopilotKit, or OpenCode to produce a safe native Windows scene automatically.

For OpenClicky, ship structured native cards first: plan, checklist, tool request, approval, result, diff summary, and connection status. If free-form generated UI later has a validated need, use a dedicated WebView2 surface with no host object exposure by default, a strict Content Security Policy, bounded size, no arbitrary navigation, and a tiny validated bridge. That requires a separate threat review and is not part of the first integration.

## Phased implementation

1. **Finish the local runtime spike.** The bounded client and installed OpenCode 1.17.18 check already cover loopback validation, health, session creation, message/status reads, and abort. Next, pin the supported binary, add generated Basic auth and owned-process lifecycle, run a deliberately non-mutating live AI prompt, consume `/event`, reconcile messages, and record the exact binary/source revision and third-party notice.
2. **Build the cross-engine permission broker and live event bridge.** Preserve the implemented native modes, then define how each one maps to OpenCode permission requests and future connector tools. Stream the minimal event set into the native timeline. Test that a denied or lost approval cannot resume a tool and that Full access carries the user's stated task boundary without treating soft title/focus checks as security isolation.
3. **Separate OpenCode provider connection.** Expose OpenCode's own supported provider flow. Verify the connected provider and a live model response without reading or copying tokens. Keep current OpenClicky ChatGPT sign-in independent.
4. **Connector decision and one-tool pilot.** After the custody choice, connect one non-sensitive test account and one read action. Personal Gmail remains a separately named consent. Do not enable general account writes during this phase.
5. **Native structured results.** Extend the implemented task timeline, progress view, Notes, and explicit Memory UI with OpenCode text, tool, approval, diff, and state cards.
6. **Optional generated UI.** Consider a WebView2 sandbox only after native cards prove insufficient. Treat 3D as a browser-rendered artifact feature, not a baseline companion capability.

## Missing decisions and proof

- Identify the “GPT repo” by exact URL.
- Account work is paused. When it resumes, choose the credential boundary before implementation: Composio Cloud, Nango Cloud, or self-hosted Nango Auth + Proxy.
- Name the first external account, exact provider scopes, allowed read/write actions, and retention expectation. Personal Gmail needs its own explicit consent.
- Decide whether OpenCode is bundled, downloaded with integrity verification, or discovered as a user-managed installation. Each option changes update, trust, and support work.
- Confirm whether a browser-rendered artifact inside the native window is acceptable. Without that, Open Intelligent UI's free-form and 3D renderer remains a reference only.
- Define the default task boundary for Auto and Full access: workspace roots, connected accounts, browser profile, time limit, and sensitive final actions.
- A live end-to-end integration spike is still required. The isolated adapter check and source inspection do not prove managed OpenCode launch reliability, AI prompting, event streaming, OAuth completion, cross-engine permissions, or user acceptance.

## Primary sources

- [OpenCode server/API](https://github.com/anomalyco/opencode/blob/d21b0790a6c146815f28fea604e41e922f009590/packages/web/src/content/docs/server.mdx) and [provider authentication](https://github.com/anomalyco/opencode/blob/d21b0790a6c146815f28fea604e41e922f009590/packages/web/src/content/docs/providers.mdx)
- [OpenWork README and licensing](https://github.com/different-ai/openwork/blob/ad22cdc2217370f28f24f0f8758016c2921e3c73/README.md), [server package](https://github.com/different-ai/openwork/blob/ad22cdc2217370f28f24f0f8758016c2921e3c73/apps/server/package.json), and [OpenWork EE license](https://github.com/different-ai/openwork/blob/ad22cdc2217370f28f24f0f8758016c2921e3c73/ee/LICENSE)
- [AG-UI 1.0 event model](https://github.com/ag-ui-protocol/ag-ui/blob/2aabfc8cada0b429927e4c256c9cf990a6791e5f/docs/spec/1.0/basic/index.mdx) and [run input](https://github.com/ag-ui-protocol/ag-ui/blob/2aabfc8cada0b429927e4c256c9cf990a6791e5f/docs/spec/1.0/basic/run-input.mdx)
- [CopilotKit architecture](https://github.com/CopilotKit/CopilotKit/blob/04bc56aeff506d8d529fe3ebd4b1b442c49fdee5/dev-docs/architecture/ARCHITECTURE.md)
- [Open Intelligent UI README](https://github.com/CopilotKit/OpenIntelligentUI/blob/f6e4388b26a64b9a0714943b08a1ce622b924eec/README.md), [architecture](https://github.com/CopilotKit/OpenIntelligentUI/blob/f6e4388b26a64b9a0714943b08a1ce622b924eec/docs/architecture.md), and [generative UI renderer](https://github.com/CopilotKit/OpenIntelligentUI/blob/f6e4388b26a64b9a0714943b08a1ce622b924eec/docs/generative-ui.md)
- [OpenWhispr README](https://github.com/OpenWhispr/openwhispr/blob/5f9a88b0481a066c147bfd2c38ea1d9acae502ee/README.md)
- [Nango pricing](https://nango.dev/pricing/), [connect sessions](https://docs.nango.dev/reference/api/connect/sessions/create), [authorization model](https://docs.nango.dev/guides), and [self-hosting source](https://github.com/NangoHQ/nango/blob/b309bc1716f9a868e7cf3af0a403a7b0da093df2/docs/guides/platform/self-hosting/self-hosting.mdx)
- [Composio pricing](https://composio.dev/pricing), [managed OAuth apps](https://docs.composio.dev/toolkits/managed-auth), [connected accounts](https://docs.composio.dev/reference/api-reference/connected-accounts), [sessions](https://docs.composio.dev/docs/how-composio-works), and [token custody](https://docs.composio.dev/docs/security/token-custody)
