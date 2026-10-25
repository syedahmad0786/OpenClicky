# Sub-project F — Connectors: Integrations page, multiple accounts, custom MCP (remote or local, browser sign-in), public catalog, Composio, Obsidian Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring OpenClicky's connectors up to HeyClicky's:
- an Integrations page with search, Connected and Available sections and status chips;
- several labelled accounts per app (Work, Personal, Other…), each with its own sign-in, Reconnect and Remove;
- custom MCP connectors over a remote URL or a local command, with browser sign-in (discovery, dynamic client registration, PKCE, `resource`, loopback callback, refresh), an API key, or no auth; extra headers and environment variables kept encrypted; checked with `initialize` + `tools/list`;
- a built-in list of public MCP servers, the user's own Composio URL, and an Obsidian vault.

**Architecture:**
- Pure logic in `OpenClicky.Core/Connectors/` (new folder): the catalog, the request parser for `connector.add`, header/env/argument line parsers, display-status rules. The registry (`Connectors.cs`) gains columns and secret fields.
- Transports and sign-in in `OpenClicky.Providers`:
  - `McpConnector` (abstract) ← `JsonRpcMcpConnector` (initialize, paging, call) ← `RemoteMcpConnector` (HTTP, existing) and `StdioMcpConnector` (new);
  - `ObsidianVaultConnector : McpConnector` (in-process tools, so vault access goes through the same `connector_tools`/`connector_call` path and the same access rules);
  - `McpOAuth` + `McpTokenSource` (browser sign-in and refresh);
  - `ConnectorVerifier` (status from a real `initialize` + `tools/list`).
- Wire contract: additive. New C# members live in a `partial` record file; new TS schemas live in `src/shell/connectorsContract.ts`, imported by `contract.ts`.
- Tray wiring moves to a new partial `OpenClicky.App/Connectors/TrayApplicationContext.Connectors.cs`; the connector members leave `Settings/TrayApplicationContext.Settings.cs`.

**Tech Stack:**
- .NET 8 (WinForms tray, `HttpListener`, `System.Diagnostics.Process`, `System.Text.Json`, SQLite through the existing `LocalSqliteStore`).
- The existing `ISecretStore` (`WindowsCredentialSecretStore`: Credential Manager, DPAPI files above 2,560 bytes).
- The existing `Pkce` helper.
- React + zod shell, vitest.
- **No new NuGet or npm packages.**

**Spec:** [design spec](../specs/2026-10-24-heyclicky-parity-design.md) §F · ledger rows F1–F5 ([feature-ledger.md](../../heyclicky-parity/feature-ledger.md)) · behaviour evidence in [connect-inventory §3](../../research/heyclicky-1.1.1/connect-inventory.md), [ui-inventory](../../research/heyclicky-1.1.1/ui-inventory.md) (Integrations row) and [live-ui-notes](../../research/heyclicky-1.1.1/live-ui-notes.md). The research covered the connector flows well enough; no new reverse engineering was needed.

## Current code facts (from the survey, 25 October 2026)

- **Registry:** `OpenClicky.Core/Connectors.cs`.
  - `ConnectorInfo(Key, DisplayName, Host, CreatedAt)` and `ConnectorSecret(Url, HeaderName?, HeaderValue?)`.
  - `ConnectorRegistry(ISecretStore, dbPath?)` with `MaxConnectors = 20`, `List`, `Add(displayName, url, headerName, headerValue)`, `Secret(key)`, `Remove(key)` and `static ValidateUrl` (https anywhere; http only to loopback; no user-info or fragment).
  - Table `connectors(key, display_name, host, created_at_utc)` in `%AppData%\OpenClicky\openclicky.db`. The secret (JSON) is named `connector-<key>`. Keys come from `WorkStore.Slugify(name)` with `-` → `_`.
- **Secret store:** `WindowsCredentialSecretStore` writes Credential Manager when the UTF-16 blob is ≤ 2,560 bytes, otherwise a DPAPI file `%AppData%\OpenClicky\secrets\<name>.dpapi` (entropy `OpenClicky.SecretStore.v1`). Names must be `[A-Za-z0-9_-]`. Large OAuth JSON already works this way (ChatGPT).
- **Transport:** `OpenClicky.Providers/RemoteMcpConnector.cs` (173 lines).
  - Streamable HTTP, protocol `2025-06-18`, lazy `initialize` + `notifications/initialized` behind a `SemaphoreSlim`, `Mcp-Session-Id` handling, SSE responses (`FromEventStream`), 1 MiB response cap, ≤ 200 tools over ≤ 5 pages, descriptions cut to 500 chars and schemas to 4,000.
  - It sends one optional header (`HeaderName`/`HeaderValue`). A non-2xx throws `HttpRequestException` with no status code callers can read.
  - `RemoteMcpTool(Name, Description, ReadOnly, InputSchemaJson)` and `RemoteMcpCallResult(IsError, Text)`.
- **Agent path:** `DesktopToolSession(..., IReadOnlyList<RemoteMcpConnector>? connectors)` serves `connector_tools`/`connector_call` (`ConnectorToolNames`).
  - Read-only tools run directly. Others: Preview refuses, and otherwise `AgentAccessPolicy.RequiresConfirmation` + `_host.ConfirmAsync`. A decline ends the task.
  - `McpDesktopBridge` lists the tools with `"{key} ({DisplayName})"` in the description.
  - `OpenWorkCompanionRunner.RunAsync(..., connectors)` passes them on. `TrayApplicationContext.OpenConnectors()` (:817) builds the list per job and disposes it after (:893-903).
  - `WorkPanel.cs:331` (classic window) tests a connector with `new RemoteMcpConnector`.
- **Settings page today:**
  - Actions `connector.add` (arg `{name,url,auth}` parsed by `SettingsActionArgs.TryParseConnector` into `ConnectorDraft`; `auth: browser` only toasts "Browser sign-in arrives with the connectors update."; `apiKey` opens `NativeSettings.PromptSecret(title, message, headerDefault)`), `connector.test` (list tools, 30 s) and `connector.remove`.
  - Handlers live in `Settings/TrayApplicationContext.Settings.cs:396-459` with `_connectorChecks` (:21) and `SettingsRuntimeFor` (:44-50). An unchecked connector shows `Connected` (wrong).
  - `ShellIntentParser`: `settings.action` arg capped at `MaxActionArgLength = 512` (`ShellContract.cs:203`, :265). `SettingsAction` logs only the action name and exception type.
  - `ConnectorStatus { Connected, Checking, Error, NeedsSignIn }`, `ConnectorView(Key, Name, Host, Status, ToolCount)`, `SettingsIntegrations(Chrome, Connectors)`. `ShellSettingsBuilder` (:151) caps to 64 and cuts strings.
  - TS: `contract.ts` :119-128 holds the integrations zod object; `IntegrationsPage.tsx` has a browser card, search, a "New connector" form (Name, Server URL, Authentication) and one "Connected" group.
  - Fixtures: `tests/fixtures/shell-state.v1.json` :142-148, `shell-enums.v1.json` (`actions`, `connectorStatus`), checked by `ShellContractTests` (:282) and `contract.test.ts`.
- **Reusable pieces:** `Pkce` (verifier, S256, random token); `ChatGptOAuthService` loopback listener (internal, random port, fixed path; not reused, see Rulings); `ChooseAgentFolder` (`FolderBrowserDialog` under `Modal`); `JobOutputTools.Resolve` (path guard pattern).
- **Gaps:** no OAuth for MCP; no stdio transport; no headers/env; no accounts or labels; no catalog; no Obsidian; no 401 status.

## Task map

| Task | Title | Ledger | Model |
|---|---|---|---|
| F1 | Core: registry v2, secret v2, catalog, request parser, field parsers, status rules | F1–F5 | sonnet |
| F2 | Providers: connector base, remote headers/bearer/401, stdio transport, verifier | F4 | opus |
| F3 | Providers: MCP OAuth (discovery, DCR, PKCE, resource, loopback, refresh) | F4 | opus |
| F4 | Providers: Obsidian vault connector | F5 | sonnet |
| F5 | Wire contract (C# + TS + fixtures + settings builder) | F1, F2 | sonnet |
| F6 | Tray wiring: actions, sign-in, verify, open connectors for jobs, multi-account rules | F1–F5 | opus |
| F7 | Integrations page | F1–F5 | opus |
| F8 | Native acceptance | F1–F5 | sonnet |

Order: F1 → F2 → F3 → F4 → F5 → F6 and F7 (parallel: F6 builds with dotnet, F7 tests with pnpm, disjoint files) → F8. F2 and F3 touch disjoint files but run in sequence, because two concurrent `dotnet build`s in one worktree fight over `obj/`.

## Skip rows (recorded in the ledger with these reasons)

| ID | Item | Reason |
|---|---|---|
| F-S1 | HeyClicky's managed catalog through its backend and Composio broker (`/agent/integrations…`, Composio session) | Never call HeyClicky's backend. Users bring their own Composio MCP URL and key, or connect a public server directly. |
| F-S2 | Account name, avatar and "Organization" flag on account rows | Generic MCP has no identity call. Rows show the label, host and "Added" date instead. |
| F-S3 | Editing a saved custom connector | Secrets are write-only by design: remove and add again. Labels can be renamed. |
| F-S4 | Runtime-side sign-in (Codex `mcpServer/oauth/login`) | OpenClicky proxies every connector itself, so app-side OAuth is the only path. |
| F-S5 | One-line capability description derived from tool names | Agents call `connector_tools` for the real list. Add it only if the tool description gets too long. |
| F-S6 | Logo images per app | No logos are bundled or fetched. Rows use the name; no third-party art. |
| F-S7 | Integration nudges | Belong to sub-project P (spec §P). |

## Rulings (made while planning; the cost if wrong)

- **Secrets never come back to the page.** Typed header and env values travel once in the `connector.add` arg. The state carries names only. The API key keeps its existing native masked prompt. *Cost if wrong:* the user retypes headers to change them (F-S3).
- **A per-action arg cap:** `connector.add` may be up to 8,000 chars; every other action stays at 512. *Cost:* a larger intent from our own page; still validated by the host.
- **Fixed loopback port 47613, redirect `http://127.0.0.1:47613/callback`**, so a pre-registered client ID has a stable redirect to register. Busy port → a clear error. *Cost:* one app on that port blocks sign-in; switch to a random port for DCR-only later.
- **A new small loopback callback in `McpOAuth`** instead of reusing the ChatGPT one. The ChatGPT listener is tied to its own path and random port, and refactoring working sign-in is a risk. *Cost:* about 30 duplicated lines.
- **No default-endpoint fallback** (`/authorize`, `/token` on the server origin) when no OAuth metadata is published. Show "This server doesn't say how to sign in. Try an API key instead." *Cost:* very old servers can't use browser sign-in.
- **PKCE S256 is always sent**, even when the server's metadata omits `code_challenge_methods_supported`. *Cost:* none for compliant servers.
- **Statuses refresh when the Integrations page opens** (every connector not checked this session, in parallel), after each add and sign-in, and on Test. Not at launch. *Cost:* the Home pill can show a connector whose token has quietly expired until the page is opened or a job uses it.
- **Accounts per app: at most 5; total connectors stays 20.** *Cost:* a cap change is one constant.
- **A failed first sign-in of a catalog account removes that account row; a custom connector's row stays** (it holds the user's typed config) with "Needs sign-in". *Cost:* none.
- **Local commands:** arguments one per line; `.cmd`/`.bat` launchers run through `cmd.exe /d /s /c` and their arguments may not contain `" % ^ & | < >`. *Cost:* rare launchers need a wrapper script.
- **The vault must contain a `.obsidian` folder; reparse points inside it are skipped or refused.** *Cost:* a plain Markdown folder can't be added as a vault.

## Global Constraints

- Everything from sub-projects A, S, B, C and D.
- **Clean room:** behaviour and layout only. All copy in this plan is our own; use it exactly. Never copy HeyClicky code, strings, art or logos. Never call HeyClicky's backend, Supabase or Stripe.
- **Safety (`AGENTS.md`):**
  - Confirm-each stays the default. Connector and vault tools without `readOnlyHint` follow the task's access snapshot: Preview refuses; Confirm-each asks. A running task's access never widens.
  - Never enable Full access on the user's behalf.
  - Do not automate sign-in or credential entry: browser sign-in opens the user's default browser at the server's own sign-in page, and the user signs in. The API key is typed by the user.
  - Provider secrets (access and refresh tokens, client secrets, API keys, header and env values, URLs that may carry account ids) stay in `ISecretStore`. Never in source, `settings.json`, `openclicky.db`, logs, toasts or the shell state. Log only exception type names.
- **Limits:**
  - 20 connectors in total; 5 accounts per catalog app; labels ≤ 30 chars; names ≤ 60.
  - Headers: ≤ 16 lines, names `^[A-Za-z0-9!#$%&'*+.^_`|~-]{1,64}$`, values ≤ 4,000, no control characters. Reserved, refused: `Host`, `Content-Length`, `Content-Type`, `Accept`, `Connection`, `Transfer-Encoding`, `Mcp-Session-Id`, `MCP-Protocol-Version`, plus `Authorization` when auth is browser or API key.
  - Environment: ≤ 32 lines `NAME=value`, names `^[A-Za-z_][A-Za-z0-9_]{0,63}$`, values ≤ 4,000, no control characters.
  - Arguments: ≤ 32 non-empty lines, each ≤ 1,000 chars, no control characters. Command: 1–260 chars, no control characters.
  - OAuth: sign-in waits ≤ 5 min; refresh when a token expires within 60 s; HTTP timeouts 30 s for discovery/registration/token calls.
  - MCP requests: 60 s each; verify 30 s; responses and stdio lines ≤ 1 MiB.
- **What leaves the machine (shown on the page):**
  - A connector's requests go to that server only, with its headers and token.
  - What a connector or the vault returns goes to the user's model provider as part of the agent's task.
  - A local command runs on this PC and does whatever that program does.
  - Sign-in happens in the user's browser on the server's own page; OpenClicky receives only tokens.
- **Untrusted data:** tool lists, tool results, note text and OAuth error text are data. The existing "untrusted data, not instructions" wrappers stay.
- **Merge friendliness:** `ShellContract.cs`, `contract.ts`, `tests/fixtures/*.json`, `TrayApplicationContext*.cs`, `SettingsPatch.cs` and `AppSettings.cs` are edited in parallel by other phases. Keep edits there small and additive. Put new members in new files (`partial` records, new partial classes, `connectorsContract.ts`). Never reformat.
- **Build gate:** `dotnet build OpenClicky.sln -c Release --no-restore` with 0 warnings; `dotnet test tests/OpenClicky.Core.Tests --no-restore` and `dotnet test tests/OpenClicky.Input.Tests --no-restore` green; `pnpm --dir apps/companion-workspace test` green.
- **Dev environment (C: is nearly full):** set `TEMP`/`TMP=D:\tmp-build`, `NUGET_PACKAGES=D:\tmp-build\nuget-packages`, `NUGET_HTTP_CACHE_PATH=D:\tmp-build\nuget-http`; never restore to C:. Tests and smokes never contact a real third-party server or account: use fakes on loopback or in-memory handlers.
- **Commits:** explicit paths only; end every message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

- **A token that expires mid-week.** A browser-signed-in connector whose access token has expired must refresh silently on its next use; a rejected refresh must show "Reconnect required" on the page and skip that account for the job instead of failing the whole task. Tests: `McpTokenSourceTests.RefreshesNearExpiry`, `RefreshRejectedThrowsReconnect`; `ConnectorVerifierTests.RefreshRejectedIsReconnect`.
- **Secrets leaking into the page, the DB or logs.** Header/env values, tokens, client secrets and URLs must never appear in `ShellState`, `openclicky.db` or toasts. Tests: `ConnectorRegistryTests.SecretsOnlyInSecretStore`, `ShellSettingsBuilderTests.ConnectorViewCarriesNoSecretFields`.
- **Sign-in callback abuse.** A callback with the wrong `state`, an `error=access_denied`, a stray `/favicon.ico` request, or a discovered endpoint over plain http must not complete sign-in or store anything. Tests: `McpOAuthTests.StateMismatchFails`, `DeniedInBrowserFails`, `IgnoresOtherPaths`, `InsecureEndpointRefused`.
- **A local server that misbehaves.** A command that does not exist, exits at once, prints log lines on stdout, sends its own requests (`ping`, `roots/list`), or never answers must give a clear error within the timeout and leave no process behind. Tests: `StdioMcpConnectorTests.SkipsNonJsonLinesAndAnswersPing`, `ExitBeforeReplyFails`, `TimesOut`, `MissingCommandFails`, `DisposeKillsProcess`.
- **Vault escapes.** `../`, absolute paths, `.obsidian/…`, non-`.md` files and junctions must be refused for read and write. Tests: `ObsidianVaultConnectorTests.RefusesEscapes`, `SkipsReparsePoints`.

---

### Task F1: Core: registry v2, secret v2, catalog, request parser, field parsers, status rules

**Files:**
- Modify: `apps/windows/OpenClicky.Core/Connectors.cs`
- Create: `apps/windows/OpenClicky.Core/Connectors/ConnectorCatalog.cs`, `ConnectorFields.cs`, `ConnectorRequest.cs`, `ConnectorStatusRules.cs`, `McpOAuthFailure.cs`
- Modify (one line each, append-only): `apps/windows/OpenClicky.Core/Shell/ShellContract.cs:26`, `apps/companion-workspace/src/shell/contract.ts:125`, `tests/fixtures/shell-enums.v1.json` (`connectorStatus`)
- Modify: `apps/windows/OpenClicky.Core/Settings/SettingsService.cs` (remove `ConnectorDraft` and `TryParseConnector`, keep `ConnectorAuth`, `ApiKeyHeader`)
- Test: `tests/OpenClicky.Core.Tests/ConnectorRegistryTests.cs`, `ConnectorCatalogTests.cs`, `ConnectorFieldsTests.cs`, `ConnectorRequestTests.cs`, `ConnectorStatusRulesTests.cs`; move the three `TryParseConnector` tests in `SettingsServiceTests.cs` (:189-221, :328) to `ConnectorRequestTests`.

**Interfaces (namespace `OpenClicky.Core`):**
- Produces:
  - `enum ConnectorKind { Remote, Local }`.
  - `ConnectorInfo` stays positional and gains init properties: `ConnectorKind Kind = Remote`, `string? App`, `string? Label`, `ConnectorAuth Auth = None`, `bool SignedIn`.
  - `ConnectorSecret` stays positional (`Url` is `""` for a local command) and gains init properties: `IReadOnlyDictionary<string,string>? Headers`, `string? Command`, `IReadOnlyList<string>? Arguments`, `IReadOnlyDictionary<string,string>? Environment`, `string? OAuthClientId`, `McpOAuthTokens? OAuth`. Old JSON blobs still deserialize.
  - `record McpOAuthTokens(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt, string TokenEndpoint, string ClientId, string? ClientSecret, string? ClientAuthMethod, string Resource, string? Scope)`.
  - `record NewConnector(string DisplayName, ConnectorKind Kind, string Host, string? App, string? Label, ConnectorAuth Auth, ConnectorSecret Secret)`.
  - `ConnectorRegistry` adds: `const int MaxAccountsPerApp = 5`; `ConnectorInfo Add(NewConnector spec)`; `void SetLabel(string key, string? label)`; `void SaveSecret(string key, ConnectorSecret secret)`; `void SetSignedIn(string key, bool signedIn)`; `static IReadOnlySet<string> ReservedKeys` = `{"obsidian"}`. The old `Add(displayName, url, headerName, headerValue)` stays as a wrapper (`WorkPanel` uses it).
  - `enum CatalogSetup { SignIn, ApiKey, None, Url, Folder }`; `record CatalogApp(string Id, string Name, string Description, CatalogSetup Setup, string? Url, string? ApiKeyHeader, bool MultiAccount)`; `static class ConnectorCatalog { IReadOnlyList<CatalogApp> Apps; CatalogApp? Find(string? id); }`.
  - `static class ConnectorFields`: `bool TryParseHeaders(string? text, ConnectorAuth auth, out Dictionary<string,string> headers, out string error)`, `TryParseEnvironment(string? text, out Dictionary<string,string> env, out string error)`, `TryParseArguments(string? text, out List<string> args, out string error)`, `TryValidateCommand(string? command, out string clean, out string error)`, `TryValidateLabel(string? label, out string? clean, out string error)`.
  - `abstract record ConnectorRequest` with `sealed record CatalogAccount(CatalogApp App, string? Label)`, `CustomRemote(string Name, string Url, ConnectorAuth Auth, string? ClientId, IReadOnlyDictionary<string,string> Headers, string? App)`, `CustomLocal(string Name, string Command, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string,string> Environment)`, `VaultSetup()`; and `static bool ConnectorRequest.TryParse(string? arg, out ConnectorRequest? request, out string error)` with `const int MaxArg = 8_000`.
  - `record ConnectorCheck(ConnectorStatus Status, int? ToolCount, string Message)`; `static class ConnectorStatusRules { ConnectorStatus Display(ConnectorInfo info, ConnectorCheck? check, bool signingIn); }`. (`ConnectorStatus` lives in `OpenClicky.Core.Shell`. F1 appends the new values in three places so both enum tests stay green: `ShellContract.cs:26` → `Connected, Checking, Error, NeedsSignIn, Configured, SigningIn, TokenRejected, Reconnect`; `connectorStatus` in `tests/fixtures/shell-enums.v1.json`; and the status `z.enum` at `contract.ts:125`, all in that order.)
  - `enum McpOAuthFailure { NoMetadata, RegistrationUnsupported, ApprovedClientsOnly, PortInUse, Denied, StateMismatch, TokenExchangeFailed, RefreshRejected, TimedOut, InsecureEndpoint }` and `sealed class McpOAuthException(McpOAuthFailure failure) : Exception` with `Failure` and the copy from F3 as `Message`, in `Connectors/McpOAuthFailure.cs`, so F2's verifier and F3's sign-in share them.

- [ ] **Step 1: Write failing tests**
  - `ConnectorRegistryTests` (temp DB path + an in-memory `ISecretStore` fake):
    - `MigratesOldTable`: a DB created with only the four old columns plus one row lists that row with `Kind = Remote`, `Auth = None`, `SignedIn = false`.
    - `AddKeepsMetadataAndSecret`: `Add(NewConnector("Linear", Remote, "mcp.linear.app", "linear", "Work", Browser, secret))` → `List()` returns those values; `Secret(key)` round-trips `Headers`, `OAuth` and `OAuthClientId`.
    - `KeysAvoidCollisionsAndReserved`: two Linear accounts → keys `linear`, `linear_2`; a custom connector named "Obsidian" → `obsidian_2`.
    - `FiveAccountsPerApp`: a sixth account for `linear` throws `InvalidOperationException` with "Up to 5 accounts can be connected for Linear."
    - `SetLabelAndSignedIn`: both persist; `SetLabel(key, null)` clears.
    - `SecretsOnlyInSecretStore`: after adding a connector with header value `s3cret-h`, env value `s3cret-e` and access token `s3cret-t`, the DB file's bytes contain none of those strings, nor the URL path `/u/123`.
    - `OldSecretJsonStillReads`: `{"Url":"https://x.test/mcp","HeaderName":"Authorization","HeaderValue":"Bearer k"}` deserializes with null new fields.
  - `ConnectorCatalogTests`: ids unique and lower-case; every `SignIn`/`ApiKey`/`None` app has an `https://` URL that passes `ValidateUrl`; `composio` is `Url` with `ApiKeyHeader = "x-api-key"`; `obsidian` is `Folder`; `MultiAccount` is true exactly for `SignIn` and `ApiKey` apps.
  - `ConnectorFieldsTests`: headers `"X-Team: design\nX-Env: prod"` → 2 entries; `"Bad Header: x"`, `"Host: x"`, a value with `\u0007`, 17 lines and a duplicate name each fail; `"Authorization: Bearer x"` fails for `ApiKey`/`Browser` and passes for `None`; env `"API_TOKEN=abc=d"` → value `abc=d`, `"1BAD=x"` fails; arguments drop blank lines, 33 lines fail; label "Personal" ok, 31 chars fail, `""` → null.
  - `ConnectorRequestTests`:
    - `{"app":"linear","label":"Work"}` → `CatalogAccount(linear, "Work")`; `{"app":"nope"}` fails with "That app isn't in the list.";
    - `{"name":"My server","kind":"remote","url":"https://x.test/mcp","auth":"browser","clientId":"abc","headers":"X-A: 1"}` → `CustomRemote`;
    - `{"name":"Files","kind":"local","command":"npx","args":"-y\n@scope/server","env":"TOKEN=1"}` → `CustomLocal` with 2 args;
    - `{"app":"composio","name":"Composio","kind":"remote","url":"https://backend.composio.dev/v3/mcp/x","auth":"apiKey"}` → `CustomRemote` with `App = "composio"`;
    - `{"app":"obsidian"}` → `VaultSetup`;
    - a `.cmd`-style rule is not checked here (F2);
    - the moved tests: name trimming, a 61-char name, `http://evil.example` refused, an 8,001-char arg refused.
  - `ConnectorStatusRulesTests`: signingIn wins; a check wins over the stored state; browser auth not signed in → `NeedsSignIn`; anything else with no check → `Configured`.
- [ ] **Step 2: Run them and see them fail.** `dotnet test tests/OpenClicky.Core.Tests --no-restore --filter "Connector"`.
- [ ] **Step 3: Implement.**
  - **Migration:** on open, read `PRAGMA table_info(connectors)` and `ALTER TABLE … ADD COLUMN` each missing column: `kind TEXT NOT NULL DEFAULT 'remote'`, `app TEXT`, `label TEXT`, `auth TEXT NOT NULL DEFAULT 'none'`, `signed_in INTEGER NOT NULL DEFAULT 0`.
  - **Keys:** base = `Slugify(app ?? displayName)` with `-` → `_`; add `_2`, `_3`… past existing and reserved keys. The account cap counts rows with the same `app`; its message uses the catalog name.
  - **Catalog, in this order** (description is our copy; use exactly):

    | id | Name | Description | Setup | URL / header |
    |---|---|---|---|---|
    | linear | Linear | Issues, projects and cycles. | SignIn | `https://mcp.linear.app/mcp` |
    | notion | Notion | Pages, databases and search across your workspace. | SignIn | `https://mcp.notion.com/mcp` |
    | atlassian | Atlassian | Jira issues and Confluence pages. | SignIn | `https://mcp.atlassian.com/v2/mcp` |
    | monday | monday.com | Boards, items and updates. | SignIn | `https://mcp.monday.com/mcp` |
    | github | GitHub | Repositories, issues and pull requests. Uses a personal access token. | ApiKey | `https://api.githubcopilot.com/mcp/`, `Authorization` |
    | sentry | Sentry | Errors, issues and releases. | SignIn | `https://mcp.sentry.dev/mcp` |
    | vercel | Vercel | Projects, deployments and logs. | SignIn | `https://mcp.vercel.com/` |
    | supabase | Supabase | Databases, tables and projects. | SignIn | `https://mcp.supabase.com/mcp` |
    | stripe | Stripe | Payments, customers and documentation. | SignIn | `https://mcp.stripe.com/` |
    | canva | Canva | Find, create and export designs. | SignIn | `https://mcp.canva.com/mcp` |
    | intercom | Intercom | Conversations and contacts. | SignIn | `https://mcp.intercom.com/mcp` |
    | huggingface | Hugging Face | Models, datasets and Spaces. | SignIn | `https://huggingface.co/mcp` |
    | deepwiki | DeepWiki | Questions about public GitHub repositories. No account needed. | None | `https://mcp.deepwiki.com/mcp` |
    | microsoftlearn | Microsoft Learn | Microsoft documentation. No account needed. | None | `https://learn.microsoft.com/api/mcp` |
    | context7 | Context7 | Up-to-date library documentation. No account needed. | None | `https://mcp.context7.com/mcp` |
    | composio | Composio | Your own Composio MCP address and key. | Url | `x-api-key` |
    | obsidian | Obsidian | Notes in a vault on this PC. No account needed. | Folder | — |
  - **Field rules:** exactly the Global Constraints limits. Header names compare case-insensitively for duplicates and reserved names. Error copy: "Write each header as Name: value on its own line.", "Header {name} can't be set here.", "Write each variable as NAME=value on its own line.", "Up to 16 headers.", "Up to 32 variables.", "Up to 32 arguments.", "Give the command to run, like npx.", "Keep the label to 30 characters."
  - **Request parser:** `JsonDocument` with `MaxDepth = 2`; string fields only; `kind` is `remote` or `local` (default `remote`); `auth` is `browser`, `apiKey` or `none` (ignored for `local`); a catalog `app` with Setup `SignIn`/`ApiKey`/`None` needs no other field; `composio` uses the custom-remote fields; the URL passes `ConnectorRegistry.ValidateUrl`.
- [ ] **Step 4: Run the tests and see them pass**, plus the full Core suite. Build Release: 0 warnings (fix the two removed-API call sites in `TrayApplicationContext.Settings.cs` with the smallest change that compiles; F6 rewrites them).
- [ ] **Step 5: Commit** `feat(connectors): registry accounts and labels, secret fields, catalog, request parser`.

### Task F2: Providers: connector base, remote headers/bearer/401, stdio transport, verifier

**Files:**
- Create: `apps/windows/OpenClicky.Providers/McpConnector.cs` (`McpConnector`, `JsonRpcMcpConnector`, `McpHttpException`), `StdioMcpConnector.cs`, `ConnectorVerifier.cs`
- Modify: `RemoteMcpConnector.cs`, `DesktopToolSession.cs` (type of `Connectors`), `McpDesktopBridge.cs` (:141-147), `apps/windows/OpenClicky.App/OpenWorkCompanionRunner.cs` (parameter type), `TrayApplicationContext.cs:817-825` (return type only)
- Test: `tests/OpenClicky.Core.Tests/RemoteMcpConnectorTests.cs`, `StdioMcpConnectorTests.cs`, `ConnectorVerifierTests.cs`; `JobAndConnectorToolsTests` stays green unchanged.

**Interfaces:**
- Consumes: `ConnectorSecret` (F1), `ConnectorCheck`, `ConnectorStatus`, `ConnectorAuth` (F1).
- Produces:
  - `public abstract class McpConnector : IDisposable { protected McpConnector(string key, string displayName); string Key; string DisplayName; abstract Task<IReadOnlyList<RemoteMcpTool>> ListToolsAsync(CancellationToken); abstract Task<RemoteMcpCallResult> CallToolAsync(string name, JsonElement arguments, CancellationToken); abstract void Dispose(); }`.
  - `public abstract class JsonRpcMcpConnector : McpConnector` holding today's `ListToolsAsync`/`CallToolAsync`/`RequestAsync` logic and caps, with `protected abstract Task<JsonDocument?> SendAsync(string method, object? parameters, bool expectResult, CancellationToken)` returning the `result` object (null for notifications).
  - `RemoteMcpConnector : JsonRpcMcpConnector` with `(string key, string displayName, ConnectorSecret secret, HttpMessageHandler? handler = null, Func<CancellationToken, Task<string>>? bearer = null)`.
  - `public sealed class McpHttpException(int statusCode, string message) : HttpRequestException` with `int StatusCode`.
  - `StdioMcpConnector : JsonRpcMcpConnector` with `(string key, string displayName, ConnectorSecret secret)` and `internal (string key, string displayName, Stream serverOutput, Stream serverInput)`; `internal static (string FileName, string Arguments)? StdioMcpConnector.Launch(string command, IReadOnlyList<string> args, string? pathVariable, string? pathExt, out string error)`.
  - `static Task<ConnectorCheck> ConnectorVerifier.CheckAsync(McpConnector connector, ConnectorAuth auth, TimeSpan timeout, CancellationToken ct)`.
  - `DesktopToolSession.Connectors` and the constructor/`RunAsync` parameter become `IReadOnlyList<McpConnector>`.

- [ ] **Step 1: Write failing tests**
  - `RemoteMcpConnectorTests` (fake `HttpMessageHandler`):
    - `SendsAllHeadersAndBearer`: secret with `Headers {X-Team: design}` and `HeaderName/Value` plus `bearer = _ => "tok"` → every request carries `X-Team`, the API-key header, and `Authorization: Bearer tok`; `bearer` is awaited on each request.
    - `UnauthorizedThrowsStatus`: a 401 on `initialize` throws `McpHttpException` with `StatusCode == 401`.
  - `StdioMcpConnectorTests` (in-memory pipes via the internal constructor, plus `powershell.exe` for process tests):
    - `ListsAndCallsOverLines`: a fake server task answering `initialize`, `tools/list` (2 tools), `tools/call` → tools and text returned; the client wrote one JSON object per line, with `notifications/initialized` after `initialize`.
    - `SkipsNonJsonLinesAndAnswersPing`: the server prints `starting…`, then sends `{"jsonrpc":"2.0","id":"s1","method":"ping"}` before its reply → the client answers `{"jsonrpc":"2.0","id":"s1","result":{}}`, and the list call still succeeds; a `roots/list` request gets error `-32601`.
    - `ExitBeforeReplyFails`: `powershell -NoProfile -Command "exit 3"` → `InvalidOperationException` containing "stopped".
    - `TimesOut`: a server that never answers → `OperationCanceledException`/`TimeoutException` within the test's 2 s request timeout (constructor-internal timeout parameter).
    - `MissingCommandFails`: command `no-such-tool-xyz` → `Launch` returns null with "OpenClicky couldn't find no-such-tool-xyz. Check the command, or give its full path."
    - `CmdLauncherRefusesMetacharacters`: a `.cmd` on a temp PATH with arg `a&b` → null with "Arguments for .cmd launchers can't contain \" % ^ & | < or >."; with plain args → `FileName` ends with `cmd.exe` and `Arguments` starts with `/d /s /c "`.
    - `DisposeKillsProcess`: a `powershell` process that sleeps → after `Dispose`, `Process.GetProcessById(pid)` throws or `HasExited`.
  - `ConnectorVerifierTests`: 2 tools → `Connected, 2, "2 tools available."`; 1 tool → "1 tool available."; 401 with `ApiKey` → `TokenRejected, "The server rejected the key."`; 401 with `Browser` → `Reconnect, "Sign in again to keep using it."`; 403 with `None` → `NeedsSignIn, "This server needs you to sign in or add a key."`; 500 → `Error`; a hang past the timeout → `Error, "It did not answer within 30 seconds."` (use a 100 ms timeout in the test; the message names the passed timeout in seconds, rounded up); `McpOAuthException` (F1) with `RefreshRejected` → `Reconnect` with its message (`RefreshRejectedIsReconnect`); any other `McpOAuthException` → `NeedsSignIn` with its message.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
  - **Base move:** move the paging, caps, `initialize` handshake, envelope/error unwrapping and `FromEventStream` out of `RemoteMcpConnector` into `JsonRpcMcpConnector` without changing behaviour; `RemoteMcpConnector` keeps only HTTP sending.
  - **Remote headers:** add `secret.Headers` with `TryAddWithoutValidation`; when `bearer` is set, `Authorization: Bearer {token}` replaces any API-key `Authorization`.
  - **Stdio process:** `UseShellExecute = false`, `CreateNoWindow = true`, redirect all three streams as UTF-8 without BOM, working directory `%USERPROFILE%`, inherited environment plus `secret.Environment`. Start lazily on the first request. Drain stderr and discard it. `Dispose` kills the whole tree (`Kill(entireProcessTree: true)`) and never throws.
  - **Launch resolution:** a path with a directory is used as is; otherwise search the current directory then each `PATH` entry with each `PATHEXT` extension (and the bare name if it already has one). `.exe`/`.com` run directly with `ArgumentList`; `.cmd`/`.bat` run as `%ComSpec% /d /s /c ""<path>" "arg1" …"`.
  - **Stdio framing:** write `JSON + "\n"`; read lines; skip lines that are not JSON objects; reply to server requests as in the tests; ignore other notifications; match responses by numeric `id`; a line over 1 MiB fails the request; default request timeout 60 s.
  - **Bridge description (`McpDesktopBridge`):** list each connector as `{key} ({DisplayName}{" · " + label if any})`, which F6 passes in through a new `McpConnector.Label` init property (`string?`, default null). Append this rule text to the `connector_tools` description, exactly: "Some apps have more than one account. When reading, search every account of that app unless the user named one, and say which account each result came from. To change an existing item, use the account it came from. To create or send something, use the account the user named or the one the context makes clear; if it is still unclear, ask the user once."
- [ ] **Step 4: Run the tests and see them pass**, plus the full Core suite; build Release with 0 warnings.
- [ ] **Step 5: Commit** `feat(connectors): stdio transport, extra headers and bearer tokens, 401-aware verifier`.

### Task F3: Providers: MCP OAuth

**Files:**
- Create: `apps/windows/OpenClicky.Providers/McpOAuth.cs` (`McpOAuth`, `McpOAuthException`, `McpOAuthFailure`, loopback callback), `McpTokenSource.cs`
- Test: `tests/OpenClicky.Core.Tests/McpOAuthTests.cs`, `McpTokenSourceTests.cs`

**Interfaces:**
- Consumes: `McpOAuthTokens` (F1), `Pkce`.
- Produces:
  - `public sealed class McpOAuth(HttpMessageHandler? handler = null, Func<Uri, Task>? openBrowser = null, int callbackPort = McpOAuth.CallbackPort)` with `const int CallbackPort = 47613`, `static string RedirectUri(int port = CallbackPort)` → `http://127.0.0.1:{port}/callback`.
  - `Task<McpOAuthTokens> SignInAsync(Uri mcpUrl, string? clientId, CancellationToken ct)` (the caller passes a 5-minute token).
  - `Task<McpOAuthTokens> RefreshAsync(McpOAuthTokens tokens, CancellationToken ct)`.
  - `static (Uri? ResourceMetadata, string? Scope) ParseChallenge(string? wwwAuthenticate)`.
  - `static IReadOnlyList<Uri> ProtectedResourceCandidates(Uri mcpUrl)`, `static IReadOnlyList<Uri> AuthorizationServerCandidates(Uri issuer)`, `static string CanonicalResource(Uri mcpUrl)`.
  - Throws `McpOAuthException` (F1); the copy below is what F1's `Message` must return (F1 implements it from this list).
  - `public sealed class McpTokenSource(McpOAuthTokens initial, McpOAuth oauth, Action<McpOAuthTokens> save, Func<DateTimeOffset>? now = null)` with `Task<string> GetAsync(CancellationToken ct)`.

- [ ] **Step 1: Write failing tests** (a fake handler plays the MCP server and the authorization server; a fake `openBrowser` reads the authorize URL and issues a real `GET` to the loopback redirect; each test picks a free port and passes it as `callbackPort`):
  - `ParseChallengeReadsMetadataAndScope`: `Bearer resource_metadata="https://mcp.x.test/.well-known/oauth-protected-resource/mcp", scope="read write"` → both; `Basic realm="x"` → nulls.
  - `CandidatesFollowRfcOrder`: `ProtectedResourceCandidates(https://mcp.x.test/mcp)` → `/.well-known/oauth-protected-resource/mcp`, then `/.well-known/oauth-protected-resource`; `AuthorizationServerCandidates(https://as.test/tenant)` → `/.well-known/oauth-authorization-server/tenant`, `/.well-known/openid-configuration/tenant`, `/tenant/.well-known/openid-configuration`; with no path → `/.well-known/oauth-authorization-server`, `/.well-known/openid-configuration`.
  - `CanonicalResourceDropsRootSlashAndFragment`: `https://MCP.x.test/` → `https://mcp.x.test`; `https://mcp.x.test/mcp` stays.
  - `FullFlowWithDynamicRegistration`: 401 challenge → metadata → registration (body has `redirect_uris: ["http://127.0.0.1:{port}/callback"]`, `token_endpoint_auth_method: "none"`, `grant_types` with `authorization_code` and `refresh_token`, `client_name: "OpenClicky"`) → the authorize URL carries `response_type=code`, the registered `client_id`, the redirect, `code_challenge_method=S256`, a 43-char `code_challenge`, `state`, `resource=https://mcp.x.test/mcp` and `scope=read write` → the token request carries `grant_type=authorization_code`, `code`, `code_verifier` matching the challenge, `redirect_uri`, `client_id` and `resource` → the result has the access and refresh tokens, `ExpiresAt` = now + `expires_in`, and `TokenEndpoint`, `ClientId`, `Resource`.
  - `PreRegisteredClientSkipsRegistration`: `clientId: "abc"` → no registration call; `client_id=abc` in the authorize URL.
  - `ResourceFromMetadataWins`: metadata `"resource": "https://mcp.x.test/"` → that exact value is sent.
  - `NoRegistrationEndpointNeedsClientId` → `RegistrationUnsupported`; registration answering 403 → `ApprovedClientsOnly`; no metadata anywhere → `NoMetadata`.
  - `InsecureEndpointRefused`: metadata naming `http://as.test/token` → `InsecureEndpoint`, no token request made.
  - `StateMismatchFails`, `DeniedInBrowserFails` (`?error=access_denied`), `IgnoresOtherPaths` (a `GET /favicon.ico` first gets 404 and the flow still completes with the real callback after it).
  - `PortInUseFails`: a listener already bound to the port → `PortInUse`.
  - `RefreshRotatesTokens`: `RefreshAsync` posts `grant_type=refresh_token`, `refresh_token`, `client_id`, `resource`; a new refresh token in the answer replaces the old one; an absent one keeps the old one; a 400 `invalid_grant` → `RefreshRejected`.
  - `McpTokenSourceTests`: `RefreshesNearExpiry` (expires in 30 s → one refresh, `save` called once, new token returned; two concurrent `GetAsync` calls refresh once), `ValidTokenNoRefresh` (expires in 10 min → no call), `RefreshRejectedThrowsReconnect` (no refresh token or a rejected refresh → `McpOAuthException(RefreshRejected)`).
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
  - **Discovery:** POST an unauthenticated `initialize` to the MCP URL. On 401, read `WWW-Authenticate`. Fetch the protected-resource metadata from the challenge URL or the candidates (first 200 with JSON wins). Take `authorization_servers[0]` (else the MCP origin), `scopes_supported` and `resource`. Fetch the authorization-server metadata from its candidates. Scope = the challenge scope, else `scopes_supported` joined by spaces, else omitted.
  - **Every endpoint** (metadata URLs, authorize, token, registration) must be `https`, or `http` to a loopback host; otherwise `InsecureEndpoint`. No automatic redirects.
  - **Registration:** only when no client ID was given; client secret and `token_endpoint_auth_method` are kept from the answer. Token requests use `client_secret_basic` when that is the registered method, else post `client_secret` in the form when one exists.
  - **Callback:** `HttpListener` on `http://127.0.0.1:{port}/`; bind failure → `PortInUse`. Loop: requests other than `GET /callback` get 404 and the loop continues; the callback checks `state` (ordinal), then `error`, then `code`. Reply pages, exactly: success "You're signed in. You can close this tab and go back to OpenClicky."; failure "Sign-in didn't finish. Go back to OpenClicky to try again." Cancellation of the caller's token → `TimedOut`. The listener is always closed.
  - **Browser:** default `openBrowser` is `Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })`. OpenClicky never types into or reads the browser.
  - **Failure copy (exact):**
    - `NoMetadata`: "This server doesn't say how to sign in. Try an API key instead."
    - `RegistrationUnsupported`: "This server needs an app registered with it first. Add its OAuth client ID under Advanced."
    - `ApprovedClientsOnly`: "This server only accepts apps it has approved. Ask its provider for an OAuth client ID and add it under Advanced."
    - `PortInUse`: "Another app is using port 47613, which sign-in needs. Close it and try again."
    - `Denied`: "Sign-in was cancelled or refused in the browser."
    - `StateMismatch`: "The browser returned an unexpected sign-in response. Try again."
    - `TokenExchangeFailed`: "The server didn't accept the sign-in. Try again."
    - `RefreshRejected`: "Your sign-in has expired. Reconnect to keep using it."
    - `TimedOut`: "Sign-in timed out. Finish signing in within 5 minutes."
    - `InsecureEndpoint`: "The server's sign-in address isn't secure (https), so OpenClicky won't use it."
  - **Token source:** a `SemaphoreSlim` guards refresh; refresh when `ExpiresAt` is within 60 s; `ExpiresAt == null` means never refresh; no refresh token when one is needed → `RefreshRejected`.
- [ ] **Step 4: Run the tests and see them pass**, plus the full Core suite; build Release with 0 warnings.
- [ ] **Step 5: Commit** `feat(connectors): MCP browser sign-in with discovery, registration, PKCE, resource and refresh`.

### Task F4: Providers: Obsidian vault connector

**Files:**
- Create: `apps/windows/OpenClicky.Providers/ObsidianVaultConnector.cs`
- Modify: `apps/windows/OpenClicky.Core/AppSettings.cs` (one property, added after `AgentFolder`)
- Test: `tests/OpenClicky.Core.Tests/ObsidianVaultConnectorTests.cs`

**Interfaces:**
- Consumes: `McpConnector` (F2).
- Produces:
  - `AppSettings.ObsidianVaultPath` (`string?`, default null).
  - `public sealed class ObsidianVaultConnector(string vaultPath) : McpConnector` with key `"obsidian"`, display name `"Obsidian"`, `const int ToolCount = 4`.
  - `public static bool ObsidianVaultConnector.IsVault(string? path)` (folder exists and has a `.obsidian` subfolder).

- [ ] **Step 1: Write failing tests** (temp vault with `.obsidian/`, `Daily/2026-10-25.md`, `Ideas.md`, `image.png`):
  - `ListsFourToolsWithReadOnlyHints`: `list_notes`, `search_notes`, `read_note` are read-only; `write_note` is not.
  - `ListAndRead`: `list_notes {}` → the two `.md` paths with `/`, sorted; `list_notes {"folder":"Daily"}` → one; `read_note {"path":"Ideas.md"}` returns the content.
  - `SearchFindsLines`: `search_notes {"query":"KUBERNETES"}` matches "deploy to kubernetes" case-insensitively, with path and line number; at most 20 results.
  - `WriteCreatesFolders`: `write_note {"path":"Projects/New.md","content":"# New"}` creates the file.
  - `RefusesEscapes`: for read and write, `../x.md`, `C:\x.md`, `/x.md`, `.obsidian/app.json`, `a/.hidden/x.md`, `image.png`, `x.txt`, `a/../b.md` and a path of 9 segments each return `IsError` and touch no file.
  - `SkipsReparsePoints`: a junction inside the vault to an outside folder holding `secret.md` → not listed, not searched, and `read_note` on it is refused.
  - `IsVaultNeedsObsidianFolder`.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
  - **Tool descriptions (exact):**
    - `list_notes`: "List Markdown notes in the user's Obsidian vault, optionally inside one folder."
    - `search_notes`: "Find notes whose name or text contains the query. Returns matching lines."
    - `read_note`: "Read one note by its path from list_notes or search_notes."
    - `write_note`: "Create a note or replace one note's whole text. Read it first if you are changing it."
  - **Limits:** list ≤ 500 paths; search scans ≤ 5,000 files of ≤ 1 MiB, query 1–200 chars, line snippets ≤ 200 chars; read ≤ 200,000 chars (cut with "\n[cut: the note is longer]"); write ≤ 1,000,000 chars, written through a temp file and `File.Move`.
  - **Paths:** relative, `/` or `\`, ≤ 8 segments, ≤ 260 chars, `.md` only, no segment starting with `.`, no `..`, no `:`, no invalid file-name chars, the full path under the vault root, and no existing segment with `FileAttributes.ReparsePoint`. Enumeration skips dot-folders and reparse points.
  - **Results** are JSON text, as `RemoteMcpCallResult`.
- [ ] **Step 4: Run the tests and see them pass**, plus the full Core suite; build Release with 0 warnings.
- [ ] **Step 5: Commit** `feat(connectors): Obsidian vault as a local connector`.

### Task F5: Wire contract (C# + TS + fixtures + settings builder)

**Files:**
- Modify: `apps/windows/OpenClicky.Core/Shell/ShellContract.cs` (make `ConnectorView` and `SettingsIntegrations` `partial`; add `"connector.signIn", "connector.label"` to `SettingsActions`; the per-action arg cap at :265)
- Create: `apps/windows/OpenClicky.Core/Shell/ShellContract.Connectors.cs`
- Modify: `apps/windows/OpenClicky.Core/Settings/ShellSettingsBuilder.cs` (:30, :69, :151-152)
- Create: `apps/companion-workspace/src/shell/connectorsContract.ts`
- Modify: `apps/companion-workspace/src/shell/contract.ts` (:29 actions, :119-128 → `integrations: integrationsSchema`)
- Modify: `tests/fixtures/shell-state.v1.json` (:142-148), `tests/fixtures/shell-enums.v1.json` (`actions`, plus new `connectorKind`, `connectorAuth`, `catalogSetup` keys)
- Test: `ShellContractTests.cs`, `ShellSettingsBuilderTests.cs`, `contract.test.ts`

**Interfaces:**
- Consumes: `ConnectorStatus` (F1), `CatalogApp`/`CatalogSetup`/`ConnectorKind`/`ConnectorAuth` (F1).
- Produces (C#, in `ShellContract.Connectors.cs`):
  - `enum ConnectorViewKind { Remote, Local, Vault }`; `enum ConnectorViewAuth { Browser, ApiKey, None }`.
  - `partial record ConnectorView { string? App; string? Label; ConnectorViewKind Kind = Remote; ConnectorViewAuth Auth = None; string? Since; }` (init properties; `Since` is ISO-8601 or null).
  - `record CatalogView(string Id, string Name, string Description, CatalogSetup Setup, bool MultiAccount)`.
  - `partial record SettingsIntegrations { IReadOnlyList<CatalogView> Catalog { get; init; } = []; }`.
  - `SettingsRuntime.Catalog` (`IReadOnlyList<CatalogView>`, default `[]`) next to `Connectors`.
  - `ShellIntentParser`: `connector.add` arg ≤ `ConnectorRequest.MaxArg` (8,000); other actions ≤ 512.
- Produces (TS, `connectorsContract.ts`): `connectorStatuses`, `connectorSchema`, `catalogSchema`, `integrationsSchema`, `MAX_CONNECTOR_ADD_ARG = 8000`, types `Connector`, `CatalogItem`. Wire shape (strict objects):
  - connector: `{ key: id, name: text(80), host: text(255), status: enum(8 values, F1 order), toolCount: int ≥ 0 | null, app: id | null, label: text(30) | null, kind: "remote" | "local" | "vault", auth: "browser" | "apiKey" | "none", since: iso | null }`;
  - catalog item: `{ id, name: text(80), description: text(200), setup: "signIn" | "apiKey" | "none" | "url" | "folder", multiAccount: boolean }`;
  - integrations: `{ chrome: (unchanged enum), connectors: max 64, catalog: max 32 }`.

- [ ] **Step 1: Write failing tests**
  - `ShellContractTests`: the fixture parses; enum lists match the new `shell-enums` keys; `connector.add` with a 7,999-char arg is accepted and an 8,001-char one refused; `connector.test` with a 513-char arg is refused; `connector.signIn` and `connector.label` are accepted.
  - `ShellSettingsBuilderTests.ConnectorViewCarriesNoSecretFields`: serialize the built `SettingsView` for a connector and assert the JSON property names under a connector are exactly `key,name,host,status,toolCount,app,label,kind,auth,since`; label cut to 30; catalog capped at 32 and description cut to 200.
  - `contract.test.ts`: the fixture parses; a connector missing `kind` fails; an unknown `setup` fails; the enum map at :143 gains `connectorKind`, `connectorAuth` and `catalogSetup` (the test requires its keys to equal the fixture's).
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.** Fixture: change the two connectors to `{"key":"notion","name":"Notion","host":"mcp.notion.com","status":"connected","toolCount":12,"app":"notion","label":"Work","kind":"remote","auth":"browser","since":"2026-10-20T09:00:00Z"}` and a `slack` custom one with `"status":"reconnect","app":null,"label":null,"auth":"browser","since":null,"toolCount":null`; add a `catalog` with `linear` (signIn, multiAccount true) and `obsidian` (folder, false). The builder maps `CatalogApp` → `CatalogView` through `ConnectorCatalog.Apps` when `SettingsRuntime.Catalog` is empty, so the page always gets the list.
- [ ] **Step 4: Run the tests and see them pass:** full Core suite, full vitest suite, build Release with 0 warnings.
- [ ] **Step 5: Commit** `feat(connectors): wire contract for accounts, labels, kinds and the catalog`.

### Task F6: Tray wiring

**Files:**
- Create: `apps/windows/OpenClicky.App/Connectors/TrayApplicationContext.Connectors.cs`
- Modify: `apps/windows/OpenClicky.App/Settings/TrayApplicationContext.Settings.cs` (remove `_connectorChecks`, `AddConnector`, `TestConnectorAsync`, `SetConnectorCheck`, `RemoveConnector`; the switch keeps one line per action calling the partial; `SettingsRuntimeFor` calls `ConnectorViews(connectors)`; `SettingsSection("integrations")` calls `VerifyUncheckedConnectors()`)
- Modify: `apps/windows/OpenClicky.App/TrayApplicationContext.cs` (:817-825 `OpenConnectors` → `OpenConnectorsAsync` in the partial; :893 call site awaits it)
- Modify: `apps/windows/OpenClicky.App/WorkPanel.cs:331` only if F2's constructor change needs it
- Test: Core tests for any helper this task adds; build; F8 runs the smoke

**Interfaces:**
- Consumes: everything from F1–F5.
- Produces:
  - `void ConnectorAction(string action, string? arg)` (`connector.add|signIn|label|test|remove`).
  - `IReadOnlyList<ConnectorView> ConnectorViews(IReadOnlyList<ConnectorInfo>)` (adds the Obsidian row when `ObsidianVaultPath` is set).
  - `void VerifyUncheckedConnectors()`.
  - `Task<IReadOnlyList<McpConnector>> OpenConnectorsAsync(CancellationToken)`.

- [ ] **Step 1: Implement `connector.add`** by `ConnectorRequest.TryParse` (refusal → toast with its error):
  - **`CatalogAccount` SignIn:** add the row (`Auth = Browser`, `App`, `Label`, display name = catalog name, host = URL host) and start sign-in for it; a failed or cancelled first sign-in removes that row.
  - **`CatalogAccount` ApiKey:** `NativeSettings.PromptSecret("{Name} key", message, app.ApiKeyHeader ?? "Authorization")` with the existing message text; cancel adds nothing; header via `SettingsActionArgs.ApiKeyHeader`; then add and verify.
  - **`CatalogAccount` None:** add and verify.
  - **`CustomRemote`:** browser → add (`OAuthClientId`, headers) then sign in; apiKey → prompt (header default `x-api-key` when `App == "composio"`, else `Authorization`) then add and verify; none → add and verify.
  - **`CustomLocal`:** check `StdioMcpConnector.Launch` (error → toast, nothing added); host = the command's file name; add and verify.
  - **`VaultSetup`:** `FolderBrowserDialog` (Description "Choose your Obsidian vault folder") under `Modal`; not a vault → toast "That folder isn't an Obsidian vault. Choose the folder that holds your vault's .obsidian folder."; else save `ObsidianVaultPath` through the settings service and toast "Obsidian vault connected. Agents can now read your notes."
  - Registry errors (`ArgumentException`/`InvalidOperationException`) → toast `SettingsActionArgs.UserMessage(ex)`.
- [ ] **Step 2: Implement sign-in** (`connector.signIn` arg = key, and the add paths above):
  - One at a time: while one runs, toast "Another sign-in is still waiting in your browser." and return.
  - Mark the key signing-in, push, toast "Finish connecting in your browser."
  - Run `McpOAuth.SignInAsync(url, secret.OAuthClientId, 5-minute CTS linked to _lifetime)` off the UI thread.
  - Success → `SaveSecret(key, secret with { OAuth = tokens })`, `SetSignedIn(key, true)`, then verify.
  - `McpOAuthException` → toast its message, `SetSignedIn(key, false)`; other exceptions → toast "Sign-in failed: " + `UserMessage`; log only `connector_signin_error=<TypeName>`.
- [ ] **Step 3: Implement verify, label, test and remove.**
  - **Verify** (`connector.test`, after add/sign-in, and `VerifyUncheckedConnectors` in parallel for every key with no check this session that is not browser-unsigned): set Checking, push, open the connector as for a job, run `ConnectorVerifier.CheckAsync(…, 30 s)`, store the `ConnectorCheck` (only if the key still exists), push. Toast the check's message prefixed by the display name ("Linear: 12 tools available.") only for user-started checks (Test, add, sign-in), not for page-open checks. A `Reconnect` result also calls `SetSignedIn(key, false)`.
  - **Label:** `{key,label}` via `ConnectorFields.TryValidateLabel` → `SetLabel`, push.
  - **Remove:** a registry key → `Remove`, drop its check, toast "Removed {DisplayName}."; key `obsidian` → clear `ObsidianVaultPath`, toast "Obsidian vault disconnected."
  - **Views:** status from `ConnectorStatusRules.Display`; `Kind`, `Auth`, `App`, `Label` from the info; `Since` = `CreatedAt` ISO; the Obsidian row is `key "obsidian", name "Obsidian", host = vault path, Connected with ToolCount 4 when IsVault else Error, app "obsidian", kind Vault, auth None, since null`.
- [ ] **Step 4: Implement `OpenConnectorsAsync`** for jobs:
  - Remote with API key/none → `RemoteMcpConnector`.
  - Browser and signed in → `RemoteMcpConnector` with `bearer = McpTokenSource(…).GetAsync`, whose `save` writes the rotated tokens back with `SaveSecret`; a `RefreshRejected` during the job marks the key Reconnect and `SetSignedIn(false)` on the UI thread.
  - Browser and not signed in → skipped.
  - Local → `StdioMcpConnector`.
  - Vault set and valid → `ObsidianVaultConnector`.
  - Every connector carries `Label` for the bridge description. Load errors log only the type name, as today.
- [ ] **Step 5: Verify.** Build Release with 0 warnings; full Core and Input suites; the SecretStore smoke's existing modes that touch settings still pass. Re-read the diff against this task and the Global Constraints (no secret in a toast, log or state).
- [ ] **Step 6: Commit** `feat(connectors): connect catalog apps and custom servers, browser sign-in, verify on open`.

### Task F7: Integrations page

**Files:**
- Modify: `apps/companion-workspace/src/shell/settings/IntegrationsPage.tsx` (rewrite; keep `BrowserCard` as is, sub-project G owns it)
- Create: `apps/companion-workspace/src/shell/settings/ConnectorEditor.tsx`, `apps/companion-workspace/src/shell/settings/integrations.test.tsx`
- Modify: `settings.css` (append only), `workPages.test.tsx` / `settings.test.tsx` only where they assert the old copy

**Interfaces:**
- Consumes: `connectorsContract.ts` (F5), the actions and arg shapes from F1/F6, `act()` from `common.tsx`, the existing `ui` components (`Group`, `Row`, `Badge`, `PillButton`, `SearchField`, `Segmented`, `ConfirmRow`).

- [ ] **Step 1: Write failing tests** `integrations.test.tsx` (render with a state built from the fixture):
  - `GroupsConnectedAndAvailable`: "Connected" lists Notion · Work and the custom one; "Available" lists Linear and Obsidian but not Notion.
  - `StatusChips`: each of the 8 statuses renders its chip text (table below).
  - `SignInButtonByStatus`: a `reconnect` browser row shows "Reconnect" posting `connector.signIn` with its key; `needsSignIn` shows "Sign in"; `signingIn` shows "Finish connecting in your browser." and no Test.
  - `AddAnotherAccount`: a connected multi-account app under 5 accounts shows "Add another Notion account"; choosing Personal and Continue posts `connector.add` `{"app":"notion","label":"Personal"}`; with 5 accounts the row is gone; "Other…" asks for a name ≤ 30.
  - `RenameLabel` posts `connector.label` `{"key":"notion","label":"Personal"}`.
  - `ConnectAvailable`: Linear "Connect" posts `{"app":"linear"}`; Obsidian "Choose vault" posts `{"app":"obsidian"}`; Composio "Set up" opens the editor with Name "Composio" and API key selected.
  - `SearchFiltersBothSections` and shows `Nothing matches “zzz”.`.
  - `EditorRemoteBrowser`: Name + URL + Sign in with browser + Advanced client ID and headers → posts one `connector.add` with `kind:"remote"`, `auth:"browser"`, `clientId`, `headers`; the redirect hint shows `http://127.0.0.1:47613/callback`.
  - `EditorLocalCommand`: "Command on this PC" shows Command and Arguments, hides URL and Authentication; posts `kind:"local"`, `command`, `args`, `env`.
  - `EditorValidation`: a bad header line shows "Write each header as Name: value on its own line." and disables Add; an arg longer than `MAX_CONNECTOR_ADD_ARG` disables Add with "That's too much to send at once. Shorten the headers or variables."
  - `ShowsWhatLeavesThisPc`: the notice text is present.
- [ ] **Step 2: Run them and see them fail.** `pnpm --dir apps/companion-workspace test -- integrations`.
- [ ] **Step 3: Implement.** Copy (exact):
  - Page: title "Integrations", subtitle "Apps and services your agents can use."
  - Notice under the browser card: "When an agent uses an app, its request goes to that app, and what comes back goes to your model provider with the task. Keys and sign-ins stay encrypted on this PC."
  - Toolbar: search label "Search integrations"; button "Add custom connector".
  - Sections "Connected" (empty: "Nothing connected yet.") and "Available" (empty: "Everything here is connected."); search miss: `Nothing matches “{query}”.`
  - Connected row: title `{name}` or `{name} · {label}`; description `{host}`, then "{n} tools" / "1 tool" when known, then "Added {d MMM yyyy}" when `since`. Actions: Sign in / Reconnect (browser rows in `needsSignIn` / `reconnect`), Test (not while `signingIn` or `checking`), Rename (multi-account apps), Remove (with `ConfirmRow` "Remove {name}? Agents will lose access to it." / "Remove connector"; vault: "Disconnect Obsidian? Agents will lose access to your notes." / "Disconnect").
  - Status chips: connected "Connected" (ok), checking "Checking…" (muted), configured "Not checked" (muted), signingIn "Signing in…" (muted), needsSignIn "Needs sign-in" (warn), reconnect "Reconnect required" (warn), tokenRejected "Key rejected" (danger), error "Error" (danger).
  - Account rows of one app sit together; after the last one, when `multiAccount` and fewer than 5: "Add another {App} account" → inline label picker: Segmented Work / Personal / Other…, a text box for Other (placeholder "Label", max 30), buttons Cancel / Continue. Rename uses the same picker with Save.
  - Available row: title name, description, button by setup: signIn/apiKey/none "Connect", url "Set up", folder "Choose vault".
  - Editor (`ConnectorEditor`): title "New connector"; Name (max 60, placeholder "My server"); Type Segmented "Remote URL" / "Command on this PC".
    - Remote: Server URL (existing validation and error copy); Authentication Segmented "Sign in with browser" / "API key" / "None" with hints "Opens your browser to sign in. OpenClicky never sees your password." / "OpenClicky asks for the key in a secure prompt after you add the connector." / "For servers that need no account."; Composio preset hint under the URL: "Paste the MCP address from your Composio dashboard."
    - Local: Command (placeholder "npx"); Arguments textarea (placeholder "One argument per line"); hint "OpenClicky starts this program on your PC when an agent needs it. Only add programs you trust."
    - Advanced (a disclosure button "Advanced"): OAuth client ID (browser only) with hint "Only for servers that need a registered app. Register this redirect address with it: http://127.0.0.1:47613/callback"; Extra headers textarea (remote; placeholder "One per line, like X-Team: design"); Environment variables textarea (local; placeholder "One per line, like API_TOKEN=…"); hint "Saved encrypted on this PC and never shown again. To change them, remove the connector and add it again."
    - Buttons Cancel / "Add connector" ("Adding…" while pending, keeping today's pending-until-the-row-appears-or-a-toast logic).
  - Client-side checks mirror F1 (line formats, counts, label length) with F1's error copy; the host re-validates everything.
  - Typed header and env values live only in the editor's component state and are cleared on close.
- [ ] **Step 4: Run the tests and see them pass**, plus the full vitest suite.
- [ ] **Step 5: Commit** `feat(connectors): Integrations page with accounts, catalog, custom connector editor`.

### Task F8: Native acceptance

**Files:**
- Create: `apps/windows/OpenClicky.App.SecretStore.Smoke/ConnectorsCheck.cs`
- Modify: `apps/windows/OpenClicky.App.SecretStore.Smoke/Program.cs` (one line: `--connectors`)
- Modify: `docs/heyclicky-parity/feature-ledger.md` (F rows + F-S rows), `docs/feature-plan.md` (Connectors row)

- [ ] **Step 1: Build gate:** Release build 0 warnings; Core, Input and vitest suites green.
- [ ] **Step 2: Automated native check** `OpenClicky.App.SecretStore.Smoke.exe --connectors`, run from the built `.exe`, all on loopback, no third party:
  1. A loopback fake MCP + authorization server (`HttpListener`) with PRM, AS metadata, registration, an authorize endpoint that redirects to the callback with a code, a token endpoint and `tools/list` behind a bearer check. `McpOAuth.SignInAsync` with an `openBrowser` that GETs the authorize URL and follows the redirect to the loopback callback (a test harness for our own fake server; no real sign-in). Then `ConnectorVerifier` → Connected with the fake's tool count; then a forced refresh.
  2. Real `WindowsCredentialSecretStore` round-trip of a `ConnectorSecret` over 2,560 bytes under a smoke-only name (`connector-smoke-<guid>`); assert the DPAPI file exists and the DB written by a temp `ConnectorRegistry` holds no secret strings; delete both.
  3. `StdioMcpConnector` against a `powershell.exe` fake server script written to a temp folder → lists its tools; after `Dispose` the process is gone.
  4. `ObsidianVaultConnector` on a temp vault: list, search, read; `write_note` with `../escape.md` refused and no file outside.
  Print one `PASS`/`FAIL` line per check; exit 0 only when all pass.
- [ ] **Step 3: User-only checks** (need a real account, browser and eyes), listed in the ledger:
  1. Settings → Integrations: Connected / Available sections, search, and the "what leaves this PC" notice look right in dark and light themes.
  2. Connect Linear (or Notion): the browser opens the provider's own sign-in; after signing in, the row shows "Connected" with a tool count.
  3. "Add another … account", label it Personal, sign in with a second account: two rows, labels shown; an agent task "list my Linear issues" says which account each came from.
  4. Add GitHub with a personal access token: Connected; paste a wrong token: "Key rejected".
  5. Add DeepWiki (no account): Connected.
  6. Add a local command (for example `npx -y @modelcontextprotocol/server-everything`): Connected with its tools.
  7. Set up Composio with your own MCP address and key: Connected.
  8. Choose an Obsidian vault; ask an agent to find a note; ask it to create a note: Confirm-each asks before writing.
  9. Revoke the OpenClicky app in a provider's settings, then open Integrations: "Reconnect required"; Reconnect fixes it.
- [ ] **Step 4: Ledger and plan.** F1–F5 → Built with evidence (test classes, the smoke flag, commit range); Native-accepted after the user's checks. Add the F-S1…F-S7 Skip rows. Update the `docs/feature-plan.md` Connectors row (OAuth now supported; stdio; accounts; catalog; Obsidian).
- [ ] **Step 5: Commit** `docs(connectors): F native acceptance, ledger and feature plan`.

---

## Self-review notes

- **Spec coverage (§F):** Integrations page with search, Connected/Available and status chips → F5 + F7; multiple labelled accounts → F1 (registry), F6 (add another, label), F7 (picker), F2 (agent rules); custom MCP remote or local → F1 + F2 + F6 + F7; browser sign-in with discovery, DCR, PKCE, `resource`, loopback callback and refresh → F3 + F6; API key or none → F6; advanced headers/env encrypted → F1 (secret fields) + F7 (editor); verify with initialize + tools/list → F2 (`ConnectorVerifier`) + F6; public servers list → F1 catalog; the user's Composio URL → F1 + F6 + F7; Obsidian vault → F4 + F6 + F7.
- **Types across tasks:** `ConnectorRequest`/`NewConnector`/`McpOAuthTokens`/`ConnectorCheck` (F1); `McpConnector`/`McpHttpException`/`ConnectorVerifier` (F2); `McpOAuth`/`McpTokenSource`/`McpOAuthException` (F3); `ObsidianVaultConnector` (F4); `ConnectorView` partial fields and `CatalogView` (F5) are used by F6 and F7.
- **Proportion:** the plan pins copy, limits, URLs, the wire shape and the decision rules; bodies are left to the implementers.
