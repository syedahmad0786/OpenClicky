# OpenCode unified runtime spike — 12 October 2026

## Decision

Use an app-owned OpenCode executable as an optional child runtime behind OpenClicky's existing native chat. Do not embed OpenWork's desktop application or open another runtime window. Native Windows control remains OpenClicky's executor; OpenCode is a workspace task engine.

## Verified local state

- `C:\Users\ahmad\.bun\bin\opencode.exe` is installed at version `1.17.18`.
- Its local `serve --help` supports a headless server, `--pure`, an explicit hostname and port, and defaults to loopback.
- A temporary password-protected, loopback-only process started successfully in `D:\OpenClicky`; `GET /global/health` returned healthy version `1.17.18`.
- `GET /provider` reported three connected provider IDs: `zai`, `nvidia550`, and `opencode`. It also returned model IDs and provider defaults. `GET /config` reported no single configured model for this workspace, so the native route must require an explicit provider/model before Run. Only these status APIs were read. No auth/token file was opened, copied, logged, or placed in settings.
- The live check sent no model prompt, started no external task, changed no user setting, and stopped its owned process.
- The finished `OpenCodeWorkspaceService` then passed its own managed start/status/dispose path against the installed runtime. It reported 18 usable `zai` models, one `nvidia550` model, and 11 `opencode` models, and left zero owned fixture processes running.
- A later read-only provider-catalog check found 15 connected models currently declaring both input and output cost as zero. The bounded live UI check may use exact selection `opencode/ling-3.1-flash-free`. This is current provider metadata, not a billing guarantee; label it **reported $0 input/output**. No prompt was sent during this check.
- The first repaired native run surfaced the provider's exact error: `OpenCode 1.18.0 or newer is required to use the free tier`. This establishes that the global 1.17.18 executable cannot support the selected free-tier model.
- The official immutable [`v1.18.0` release](https://github.com/anomalyco/opencode/releases/tag/v1.18.0) publishes `opencode-windows-x64.zip` at 59,153,931 bytes with GitHub release digest `sha256:5a397a5ddea2e4bcdcae781525324833e7c088e4e3406f1c086afa5d480a7c2d`. The verified archive is staged at `artifacts/runtime/opencode-1.18.0/`; its extracted `opencode.exe` reports `1.18.0`, hashes to `bce2f1aefa8f0677d8492e3e5d83e4944444434b7e9b4a03cf3ebcffddf2c62c`, and has a valid Authenticode signature from Anomaly Innovations, Inc.
- A bounded managed start/status/dispose check against that staged executable returned version `1.18.0` and the same three connected provider IDs, sent no model prompt, and left no staged-runtime process running.

## Built in this spike

`OpenCodeWorkspaceService` now:

- accepts an explicit executable path, otherwise prefers `runtime/opencode.exe` beside `Environment.ProcessPath` before checking `AppContext.BaseDirectory` and then a preinstalled user executable; this keeps a single-file app on its published runtime even when .NET extracts managed content to a temporary base directory. It performs no startup download or global installation;
- starts one hidden `opencode serve --pure` child in a selected existing workspace;
- selects an available local port, binds explicitly to `127.0.0.1`, and sets a random server password only in the child environment and in-memory HTTP client;
- pins the implemented contract to OpenCode `1.18.0` and stops on version mismatch;
- sets inline permissions to ask for every tool and deny `external_directory` access;
- verifies health and reads only the connected provider IDs before accepting work;
- creates one session, sends an asynchronous prompt, polls pending permissions/messages/status, and reports each update through one callback-friendly progress stream;
- accepts an exact `{ providerID, modelID }` selection for the prompt, validates it against a connected provider's reported models, and only permits omission when OpenCode reports one unambiguous configured model;
- invokes the supplied confirmation callback for every permission and supports only **Allow once** or **Reject**; it never returns OpenCode's persistent `always` approval;
- exposes abort and stops only the child process it owns.

`OpenCodeTaskClient` now implements the version-specific provider and permission routes in addition to the existing health/session/prompt/message/status/abort routes. Focused tests cover request shapes, the permission reply body, loopback/hidden launch arguments, inline approval policy, and workspace-root rejection.

The service uses bounded polling instead of SSE for the first route. This still delivers incremental message snapshots, state changes, approval requests, and the final result into the native timeline while also recovering permission requests that an event subscriber could miss. Token-by-token text deltas and tool-result cards are not implemented.

## Protocol evidence

The [official OpenCode server documentation](https://opencode.ai/docs/server/) confirms headless `serve`, loopback hosting, `OPENCODE_SERVER_PASSWORD`, health, provider, session, message, status, abort, permission, and event APIs. The [official permission documentation](https://opencode.ai/docs/permissions/) confirms `ask`, `allow`, and `deny`, and that external-directory access is distinct from the working directory.

The exact tagged source is material. OpenCode `1.18.0` defines pending requests as `GET /permission` and replies as `POST /permission/:requestID/reply` with `once`, `always`, or `reject`: [permission API group](https://github.com/anomalyco/opencode/blob/v1.18.0/packages/opencode/src/server/routes/instance/httpapi/groups/permission.ts), [request schema](https://github.com/anomalyco/opencode/blob/v1.18.0/packages/schema/src/v1/permission.ts). Git blob IDs for the permission, session, provider, config, and global route groups are identical between tags `v1.17.18` and `v1.18.0`; the implemented API shapes did not change across this upgrade. This differs from a session-scoped permission route still shown in some public API material, so the managed service refuses other runtime versions until their contract is deliberately tested.

[OpenWork](https://github.com/different-ai/openwork) is useful architecture evidence for an OpenCode-powered coworker UI, owned server lifecycle, scoped workspaces, and protected local transport. OpenClicky does not need OpenWork's Electron/React application, server/database stack, remote organization layer, or a second UI to obtain those capabilities.

## Native integration

Home now exposes this runtime inside the existing **Connections** tab. The user chooses a folder, prepares the owned runtime, and then explicitly chooses one connected provider and exact model. Changing the displayed folder immediately invalidates the prepared runtime selection and provider/model choices; Run stays disabled until that exact folder is prepared. Folder, provider, model, and prepare controls are locked during a run.

The Tray application owns one `OpenCodeWorkspaceService` for the prepared workspace. It reuses that runtime for the same folder, starts a replacement before disposing the previous owned runtime after a successful folder change, and disposes its owned runtime at app exit. It does not open a second app window.

For each OpenCode task:

1. Snapshot the task's access mode at task creation.
2. Pass a permission callback that formats `Permission` plus `Patterns` and invokes the existing `IAgentConfirm.ConfirmAsync` on the UI synchronization context. For this first integration, every OpenCode permission should reach that confirm control regardless of native Auto/Full mode; the service supplies no auto-approve path.
3. Map `OpenCodeTaskUpdateKind` to the existing task timeline, bound stored progress text to 4,000 characters, and append the final nonempty assistant answer plus a session/provider/model receipt to the same Chat. The submitted prompt is validated at 4,000 characters before task/history creation.
4. Route Stop to `AbortAsync` and cancel the task token. Treat OpenCode HTTP/file results as workspace receipts, not proof that a visible desktop control changed.

## Honest limits

The managed lifecycle, provider readiness check, protocol client, permission callback, progress/receipt path, abort path, and native Home/Tray composition are built. Preview mode refuses to execute a workspace task. Native Auto or Full settings do not widen OpenCode access: its task history is labeled `OpenCode ConfirmEach`, and every OpenCode permission uses the explicit non-input confirmation path with Allow once or Reject.

OpenCode cannot pause an external tool that is already executing. Home and voice controls state this directly; the task waits naturally when OpenCode reaches its next permission request, and Stop cancels and aborts the active session.

The first native prompt attempt, session `ses_ed2fbb91dffe9MQqmWd6G7ENzl`, did **not** create the requested `demo.txt`, return assistant text, or request permission. The UI incorrectly reported completion because the runner accepted a busy-to-idle transition without a terminal assistant result. The runner now requires the latest assistant step to contain nonempty text and a terminal finish reason; an empty `stop` and `tool-calls` remain nonterminal. It surfaces assistant provider errors with their details and fails after a five-second idle grace period when no terminal result exists. The runtime event is described as **run finished**, not proof that the requested task succeeded. This fix has focused test coverage and the later native acceptance below.

Provider/model execution passed one bounded native check after packaging: `opencode/ling-3.1-flash-free` requested the normal one-action approval, wrote `artifacts/demo-tests-2026-10-12/workspace/demo.txt` with the two exact requested lines, and returned an answer plus `ses_ed2e50f90ffeXfhrz1hGpVJS1y` in the shared Chat. Task receipt `231a8c9964db4d5b9ab7d8e284a6b276` is completed. The free provider retried repeatedly, so this is not speed or broad reliability acceptance. Retry progress now includes its supplied message; control flow and permission policy are unchanged. Any future authentication remains a user action; OpenClicky does not copy ChatGPT credentials.

The published portable app now includes `opencode.exe`, `LICENSE-opencode.txt`, and `OPENCODE-RUNTIME-NOTICE.txt` under `runtime/`, plus OpenCode's MIT attribution in `THIRD-PARTY-NOTICES.md`. The upstream tag publishes `LICENSE` but no separate `NOTICE`; the provenance notice records the immutable release, asset URL, and verified hashes. `publish-exe.ps1 -OpenCodeRuntimeDirectory` validates the pinned executable hash before copying the package. Native verification found and fixed the .NET extraction-directory lookup issue; the actual executable directory now wins. Unbundled builds still reject installed 1.17.18 before session creation. Latest full build: zero warnings/errors, 266/266 checks.

The selected working directory, `external_directory` deny rule, `--pure`, loopback binding, and per-tool confirmation materially narrow the runtime. They are not an OS sandbox: a model-approved shell command still runs with the user's Windows account. Full-access OpenCode execution is intentionally absent.
