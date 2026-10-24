# HeyClicky 1.1.1 (Windows): connectors, browser agent, account/platform, public features

Clean-room inventory for OpenClicky, 24 October 2026. Everything below is written in our own words. No code, prompt text or marketing copy is reproduced beyond short identifiers (type, member, file, route and CDP method names) that are needed as evidence. Nothing was launched, installed or contacted. The only network reads were public heyclicky.com pages.

**Evidence legend**
- `ev_…`: REA Evidence ID, from this session.
- `T:` is a type or member name. `S:` is a string literal. `F:` is a file in the target. `U:` is a public URL.
- **O** = observed in shipped bytes or on a public page. **I** = inferred from names and strings; nothing was seen executing.

**Evidence records**
| Subject | Evidence |
|---|---|
| `ClickyBrowserExtension\` (MV3, 0.6.3), JS graph | `ev_2b4b8135f5e246e077df798b8d2ceb5a5dd6402a83035828b763c4e8ac0a449f` (summary view `ev_da78b2a4…`). Files were also read directly as data. |
| `ClickyBrowserHost\ClickyBrowserHost.exe` | `ev_7c406da411661a5115c53f9600de8f7ed896b2f7bc4c1c9ba4e63da70f0aaf30`. It has no CLI directory, so it is native: a .NET 9 NativeAOT build (its PDB path names `HeyClicky.BrowserHost`, `net9.0-windows…\native`). Its strings are in `host-strings.txt`. |
| `HeyClicky.Core.dll`, scoped to Connectors and Browser types | `ev_43251928250062d53fa54b12f193508238c5b5f55ab91aef13ae76bd9e44cd7d` (user-strings page `ev_e6df98bc…`) |
| `HeyClicky.dll`, scoped to ConnectorAuth, CustomConnector*, UpdateService and Chrome setup | `ev_33fc18336a620e6f26527c4a22cdad256cb44c91703784a0658b099af39a4cfa`. Type selection got past the 64 MB budget that blocked the earlier unscoped run. |
| `HeyClicky.Agents.dll`, scoped to AgentWorkerApi, Integration*, Composio* and CustomConnectorRegistration | `ev_0eb0f4d8102b31b8f0db81c4685038d86b0567018a4dc3ebd02db6f49b185475` |
| Whole-assembly type and #US dumps (System.Reflection.Metadata, no code loaded) | `rea-connect\<Assembly>.types.txt` and `<Assembly>.us.txt`. Line numbers below are cited as `us:L`. |
| Earlier evidence from the behavior spec | Extension `ev_b3d07b1a…`, Agents `ev_d7e57f2b…`, Platform `ev_67843814…` |

---

## 1. Advertised product features (public site)

Sources: U:`https://www.heyclicky.com/` (home and pricing sections), `/changelog`, `/windows`, `/trust`, `/privacy-policy`, `/referral-terms`. The site has no docs or blog pages. All items are **O** on the public site.

### 1.1 Core assistant
- A hotkey opens a voice assistant. It sees the screen only when the hotkey is pressed, answers aloud, and points or draws on screen.
- Agent mode: "heyclicky agent" (later, any clear request) hands work to background agents.
- Dictation into any app. Fn+Ctrl on Mac, a double-tap for hands-free, a learned dictionary, and 50+ languages.
- Drawing: 2D annotations, polygons and hand-drawn style. From 1.1.1, also 3D figures and a draggable 3D model you can ask about part by part. Answer cards can carry photos.
- "One brain" (1.1.1): one assistant hears, sees, answers and hands big work to agents. It remembers earlier screen context and past conversations.
- Guided walkthroughs of up to 15 steps, with target rings and narration, and per-app teaching skills (89 apps, changelog 1.0.28).
- Optional on-screen captions for spoken replies (1.1.1).
- Memory: a profile file and a current-projects file, and 40 recent messages (1.0.49).
- Skills library. About 100 community skills shipped in 1.0.33, were removed in 1.0.49, and are "planned to return".

### 1.2 Named Clickys, jobs and routines
- Named Clickys (1.0.49), each with its own name, face, memory, conversation and file folder. You can create up to five in one request. Character packs are available.
- Background jobs in their own window, with follow-up and resume. Stopping a job within 10 seconds refunds the message.
- Routines (1.0.49, and by request in 1.1.1): create, change, pause or delete them by asking or in Settings > Routines. A routine waits for the machine to wake and the network to return, catches up once after a missed run, and pauses after three consecutive failures.
- Notifications: each Clicky announces when it finishes. It stays quiet during calls and screen shares, and Do Not Disturb silences only routines.
- Proactive suggestions and morning cards. They were paused for rework in 1.1.1. They read connected apps once a day, read-only, and can be turned off.

### 1.3 Connectors
- Google Workspace (Gmail, Drive, Sheets, Calendar) since 1.0.11, with email sending off by default. Notion, Gmail, Linear, Slack "and more" since 1.0.15. Spotify search and playback. Google Calendar read and create.
- Multiple accounts per app (1.0.53): connect several, label them (Work or Personal), and disconnect each separately. Agents choose the right account, or search all of them and say which account each result came from. 1.1.1 adds a Reconnect button per account.
- Custom connectors (1.0.49): add an MCP server by URL or by local command, signed in through the browser or with an API key. Tokens are verified with a real request, and the status reads Checking, Connected or Token rejected (1.0.51).
- Reddit and Monday connectors were paused in 1.0.53.

### 1.4 Browser and computer use
- Chrome extension (1.1.1): agents work in your signed-in Chrome inside their own tab group. The app asks once when a task needs it, and it can install the extension itself. Earlier versions had a separate browser window per task.
- "HeyClicky drive": with permission, it moves the mouse and types while you watch. Moving the mouse, talking or pressing Escape takes control back. The changelog states this for Mac; the Windows binary has an equivalent "take over" flow (see 2.7).
- Computer-use approvals: Allow once, Always allow, Not now, or by voice. Approvals are per conversation.

### 1.5 Platform
- Mac needs macOS 14.2 or later. Windows needs Windows 10 or later. The first Windows release is 1.1.1, dated 23 October 2026.
- Windows install: `HeyClicky-Setup.exe`. The download page lists no system requirements and no feature list.
- Updates: an Update button in Home and a card after each update (Mac changelog).

### 1.6 Plans and credits (inventory only; OpenClicky will not reproduce)
| Plan | Price | Talk | Agent messages | Dictation |
|---|---|---|---|---|
| Free | $0 | 25 a month | 25 a month | Limited |
| Pro | $20 a month | Unlimited | 150 a month | Unlimited |
| Max | $100 a month | Unlimited | 1,000 a month | Unlimited |
- Yearly billing is 20% off. Students get 50% off the first month of Pro. Teams (1.0.44) have mixed seats, are set up manually, and accept promo codes. India has UPI checkout.
- When agent messages run out, talk keeps working and agents pause until the next cycle. An "always approve extra usage" toggle exists.
- Referrals: a username link. The friend gets 25% off the first monthly plan. The referrer earns 25% of the friend's plan price for 12 months, held 45 days, paid manually in cash once the balance reaches $25. The attribution cookie lasts 60 days.

### 1.7 Privacy and trust claims
- Screenshots are not stored. Text summaries and prompts are kept. Processing is in the cloud.
- Model and AI vendors named: Anthropic and OpenAI on the trust page. The privacy policy adds Deepgram and Cerebras.
- PostHog analytics and Sentry crash reports. Neither receives screen content. No analytics opt-out is described.
- No SOC 2 or ISO certification yet. Account deletion is in Settings and is completed within 14 days.
- Extension: it talks only to the desktop app over native messaging. It stores a cursor setting and a random profile ID. It will not fill password, verification-code or payment fields, does not support incognito, and does not reverse actions already taken.
- Local activity tracking was removed in 1.0.39 and 1.0.46.

---

## 2. Browser extension and native host

### 2.1 Manifest (O, F:`ClickyBrowserExtension\manifest.json`)
- MV3, version 0.6.3, minimum Chrome 120, a fixed `key` (so the extension ID is stable), and `homepage_url` set to heyclicky.com.
- Permissions: `debugger`, `tabs`, `nativeMessaging`, `storage`, `alarms`, `tabGroups`, `downloads`. There are **no host permissions and no content scripts**: everything goes through CDP.
- The service worker is the module `background.js`, which imports `core.mjs`. The popup is `popup.html/js`. `welcome.html/js` opens on install. `bubble.js` and `mark.js` hold the presence overlay drawing.
- F:`ClickyBrowserExtensionID.txt` holds two extension IDs. **I**: one is the unpacked build (from the fixed key) and one is the Web Store build, and both are allowed origins in the native manifest.

### 2.2 Messages between the popup/welcome pages and the service worker (O, `background.js`)
`status` replies with the state, connection flags, last error, active jobs and startup problems. The other messages are `connect`, `openApp`, `focusTab{tabId}` and `stop`. The sender must be the extension itself.

Connection states: `working`, `connected`, `profile` (another Chrome profile is selected), `setup` (the host is not found or is forbidden), `offline` and `checking`. The toolbar icon has on and off variants and a badge while a job is working. The popup lists up to three working jobs, each with its name, what it is doing and up to four tabs; clicking a tab focuses it. It also has a Stop control and a cursor-overlay toggle (`storage.local.cursorOverlay`).

### 2.3 Native messaging protocol (O, `background.js`; host side partly I)
- Host name `com.heyclicky.browser`. The manifest file is `com.heyclicky.browser.json`, and its type is `stdio`. The app writes it under HKCU `Software\<browser vendor path>\NativeMessagingHosts\com.heyclicky.browser` with `allowed_origins` (Core `us:5311-5316`, `5411`).
- Extension to host: `hello{protocol:1, profileId}`, where profileId is a random UUID kept in `storage.local`. Then `result{id, result|error}`, `stop` and `openApp`.
- Host to extension: `ready{selected, app}`, which says whether this profile is the selected one and whether the app is reachable; `stop`; and `command{id, …request}`.
- Reconnect: an alarm every 30 seconds, plus on startup and install. When the connection drops, all browser work stops.
- `openApp` asks the host to start the exact app build without Chrome's open-app prompt. The fallback is the deep link `heyclicky://settings/browser`.
- Host to app (**I**, from strings): the host reads `BrowserBridge\connection.json` (`port` and `token`) under `HEYCLICKY_DATA_DIR`, then talks to the app over loopback HTTP at `127.0.0.1:<port>` with the endpoints `browser_poll` and `browser_reply` (Core `us:5411-5433`, host strings). The app side is `T:LoopbackHttpServer` (BearerToken, PathPrefix, Port, FixedTimeEquals). **I**: this is a long-poll relay.
- Tab ownership and keep marks are saved to `storage.session.clickyTabs`, so they survive a service-worker restart.

### 2.4 Operations (O, `core.mjs` `BrowserBridge.execute/dispatch/runTab`)
Every request carries `task`, an identity the app issues per turn; the model's arguments cannot set it. It can also carry `owner` (the Clicky or thread), `groupTitle`, `cursorColor` (#rrggbb) and `say`, a progress line of up to 160 characters.

| Operation | Behavior (our words) |
|---|---|
| `tabs` | Lists normal http(s) tabs and marks the one the user is on. Capped at 500 tabs or about 650 KB. |
| `open {url}` / `open {tabId,url}` | Opens a background tab in the task's group, or navigates a tab the task owns or has claimed. Only http(s) is allowed, with no credentials in the URL. |
| `close` | Closes only tabs this Clicky opened. |
| `snapshot {query?, interactive?}` | Accessibility-tree read, one line per element as role: name (flags) [ref]. Refs stay stable within a document and are retired on navigation. Elements on or near the screen come first when the page is over budget. A query filters by name. |
| `extract` | Main text as markdown plus tables as CSV, read in an isolated world. Limits: 40,000 characters, 20 tables, 200 rows. Field values are excluded. |
| `click {ref, dismiss?}` | Resolves the element and checks whether it is covered. If so, it can dismiss the cover once (Escape, then the cover's close or decline button) and retry. |
| `fill {ref,text}` | Text fields, rich editors (falls back to inserted text) and selects. At most 20,000 characters. |
| `type {text}` | Types into the focused element. It **refuses password fields** and refuses when nothing editable has focus. |
| `press {key}` | Enter, Tab, Escape, the arrow keys, Backspace, Delete, PageUp/PageDown, Home and End. Enter, Backspace and Delete need a snapshot of the current page first. |
| `scroll {up/down}` | Mouse wheel. |
| `wait_for {text or url, gone?, timeout?}` | Bounded wait, 10 seconds by default and 20 at most. |
| `back` | History back. |
| `screenshot {save?}` | JPEG scaled to CSS pixels, with HeyClicky's own overlay hidden during capture. With `save`, the app keeps it in the job's output folder. |
| `click_at / hover_at / drag_at {x,y[,toX,toY]}` | Pixel fallback. Requires a screenshot taken in the current page epoch. Dragging uses 12 interpolated moves. |
| `javascript {code}` | Runs the body of an async function in the page and returns JSON. At most 20,000 characters of code or result, with a 15-second limit. |
| `upload {ref, paths}` | 1 to 10 absolute paths. Handles file inputs, labels and file-chooser buttons (the chooser is intercepted, so no dialog appears). |
| `download {url}` or `{ref}` | Saves to `Downloads/HeyClicky/<task-slug>/`. Downloads Chrome holds as dangerous are reported, not forced. |
| `dialog {accept, text?}` | Answers alert, confirm, prompt or leave-site dialogs. Other actions are refused while a dialog is open. |
| `keep {tabId, why}` | Keeps a tab open past the turn, where `why` is "result" or "waiting". |
| `batch {tabId, steps}` | Up to 20 steps drawn from open, click, fill, press, scroll, wait_for, back and upload. It stops at the first failure and always ends with one fresh snapshot. |
| `end {tidy}` / `sweep {running}` | Lifecycle calls from the app. They end a turn (closing unkept tabs and restoring claimed ones) and reconcile after a reconnect. |

Each single action runs as a one-step batch and returns a **diff snapshot**: what was added or removed since the model's last look, or "unchanged". Per-call budgets come from `LIMITS`: load 8 s, single call 20 s, batch 30 s, download 45 s. The app's own per-call timeouts are 25 s, 45 s and 60 s.

### 2.5 CDP methods used (O)
- `Runtime`: `evaluate`, `callFunctionOn`, `releaseObject`.
- `Input`: `dispatchMouseEvent` (move, press, release, wheel), `dispatchKeyEvent`, `insertText`.
- `Accessibility.getFullAXTree`.
- `DOM`: `resolveNode`, `getContentQuads`, `setFileInputFiles`.
- `Page`: `enable`, `captureScreenshot`, `getLayoutMetrics`, `getFrameTree`, `createIsolatedWorld`, `handleJavaScriptDialog`, `setInterceptFileChooserDialog`, `setWebLifecycleState` (wakes frozen background tabs).
- `Emulation.setFocusEmulationEnabled`, so tabs the user never showed still render and take input.
- `Network.enable`.
- Events handled: `Page.frameNavigated` (main frame only invalidates refs), `Page.navigatedWithinDocument`, `Page.javascriptDialogOpening/Closed`, `Page.fileChooserOpened`, and `Network.requestWillBeSent`, `loadingFinished` and `loadingFailed`. Only non-GET requests count as "saves" that an action waits for.
- An unexpected `debugger.onDetach` with `target_closed` re-attaches, for example after a password manager's frame. A user cancel from Chrome's debugging bar hands the tabs over to the user and stops the job.

### 2.6 Tab grouping and ownership (O)
- One tab group per Clicky, in blue. The title comes from the app (the app adds an emoji and the turn title) and is truncated to 32 characters. Opened tabs go next to the user's current tab, unfocused.
- All Clickys share one work window. A new window is created only when no normal window exists.
- Ownership:
  - A tab the user owns is *claimed* (moved into the group) only when an action changes the page or the user is looking at it. Read-only use of a background tab leaves it in place.
  - Claimed tabs go back to their old group and position at turn end, unless the user moved them.
  - Another running Clicky's tab is refused.
  - When the turn ends tidily, unkept tabs this Clicky opened are closed, except the one the user is viewing or one dragged out of the group. The group then folds.
  - When the user presses Stop, the tabs become the user's ("hand over") and none are closed.
- Presence overlay: a glow at the window edge plus a gliding cursor with a ripple and a speech bubble showing the progress line. It is drawn in an isolated world, outside the accessibility tree, and the user can turn it off.

### 2.7 Setup flow (O strings; ordering I)
- The app looks for Chromium browsers: Chrome (Stable, Beta, Dev, Canary), Chromium, Brave, Edge, Arc (Store package) and Vivaldi. It checks registry App Paths and user-data folders (`T:ChromiumBrowsers`, Core `us:5300-5345`). It registers or unregisters the native manifest per browser and keeps a selected **profile** (`T:ClickyBrowserBridge.Preferences{Enabled,HasConnected,Profile}`, `browser-connection.json`).
- Settings path: Settings → Integrations → Chrome → Connect. The user can switch profiles there.
- The bundled extension folder is synced to an installed folder (`.heyclicky-extension-*`). There is also a Web Store listing URL (`HeyClicky.ChromeWebStoreUrl`).
- When an agent's task needs signed-in web access and Chrome is not connected, the agent asks once. The three answers are **approve**, where HeyClicky drives the install on the user's screen (`T:ChromeAutoInstall.InstallByDriving`); **show_how**, a guided walkthrough with a draggable folder strip (`T:ChromeFolderStrip`); and **decline**, where the job runs without Chrome and the user performs the web step. Held jobs start automatically once Chrome connects (`T:ChromeSetupFlow` Hold, HoldsNewWebsiteJobs).
- The install path covers both the Web Store ("Add to Chrome") and unpacked loading (Developer mode, Load unpacked or drag the folder onto chrome://extensions). `T:ChromeInternalPages` opens chrome:// pages by typing into the address bar with SendInput.
- If no Chromium browser is installed, `ClickyBrowserSetup.html` is shown. It is a local page asking the user to install Chrome, Brave, Edge or Arc, then connect. The page itself still says "Mac", a leftover from the Mac build.
- The extension's welcome page shows connection progress and a hint to pin the extension.

### 2.8 App-side browser plumbing (O)
- `T:LoopbackMcpServer` serves the app's own tools to the agent runtime at `http://127.0.0.1:<port>/mcp` with a bearer token. Tools include `clicky_browser`, the browser tool whose description lists the operations in 2.4, and `mac_step`. The `mac_step` name is a Mac carry-over; on Windows it shows the user a card for one on-screen step and acts with their yes.
- `T:BrowserUploads` validates paths in the app before they reach the extension. It refuses secrets (`.env`, `.pem`, `.key`, `.netrc`, `.git-credentials`, `.npmrc`, `.pypirc`, `.pgpass`, browser profiles, app settings), anything over 100 MB, non-regular files and unreadable files.
- `T:BrowserTurnUsage` records analytics for each turn: operation counts, batch steps, errors, timeouts, tabs kept, screenshots saved, and whether the user stopped the turn.

---

## 3. Connectors

### 3.1 Managed catalog through HeyClicky's backend and Composio (O types and routes; server behavior unknown)
- Catalog model (`T:AgentIntegrationCatalog{ComposioConfigured, Integrations}`):
  - `T:AgentIntegration{Id, DisplayName, Description, Status, ConnectedCount, TotalCount, Toolkits, Accounts, PrimaryToolkitSlug, LogoUrl, ActiveAccounts, HasReachedActiveAccountLimit, IsLocalIntegration, UseCases}`.
  - Toolkit: `{Slug, Name, Logo, IsConnected}`.
  - Account: `{ConnectedAccountId, AccountName, Label, AvatarUrl, AccountNameKind, Status, ConnectedAt, IsActive, IsOrganization}`.
- Backend routes, which OpenClicky must not call:
  - `/agent/integrations?limit=1000`
  - `/agent/integrations/{id}/connect`, which returns `redirect_url` for the browser OAuth.
  - `/disconnect`, `/accounts/{id}/label` and `/status`.
  - `/agent/composio/session`, which returns `enabled`, `proxy_url` and `multi_account_toolkits` (Agents `us:2003-2040`).
- Runtime wiring: the Codex config gets an `[mcp_servers.composio]` entry pointing at a URL provided per session, with a bearer token taken from an environment variable (Agents `us:509-512`, `T:ComposioMcpConfiguration{Url, Toolkits, MultiAccountLines}`). If the session listing fails, the next turn fetches a fresh one (`T:ComposioTurnErrors`).
- Search: fuzzy matching with a Levenshtein-based similarity and a "recommended" ranking (`T:IntegrationSearch`).
- Toolkit slugs seen as strings include gmail, googlecalendar, googledrive, googlesheets, googledocs, notion, linear, slack, github, discord, dropbox, airtable, spotify, instagram, youtube, twitter, reddit, whatsapp, zoom, outlook, tiktok and linkedin. Obsidian is a **local** integration: a Markdown vault reached through a skill with no Composio account (HeyClicky `us:1249`).
- Integration logos are cached on disk.

### 3.2 Account management UI (O strings, HeyClicky `us:1540-1610`)
- Settings → Integrations, described as the apps your agents can reach into. It has a search box, Connected and Available sections, empty states and "Sign in to connect".
- Each account row has Connect, Reconnect, Disconnect and a Remove action for expired connections. Status values include Configured, Partial, Reconnect required, Unavailable, Error and Not connected.
- Multiple accounts: "Add another <app> account". There is a per-app cap with a hint to disconnect one first; **the cap's value is not in strings**. Labels are Work, Personal or Other… (rename inline). An Organization flag and a "since <date>" line are shown. After the browser returns, the app checks for the new account.

### 3.3 Selecting an account for each task (O, Agents `us:550-560`; our summary)
The agent instructions carry a block per toolkit listing its connected accounts. The rules it gives:
- With more than one account, every tool call names the account.
- Reads search **all** accounts unless the user named one, and attribute each result to its account.
- Acting on an existing item uses the account the item lives in.
- Creating or sending uses the named account or the one the context makes obvious. Otherwise the agent asks once and records the answer in the Clicky's notes.

### 3.4 Custom MCP connectors (O types and strings)
- Model: `T:CustomConnector{Id, Name, Kind(remote|local), Url, Command, Arguments, EnvironmentNames, HasApiKey, OAuthClientId, HeaderNames, TokenFromSignIn, DerivedCapabilityDescription, BrowserSignInSupport, CreatedAt}`. The agent side maps it to `CustomConnectorRegistration{ServerKey, DisplayName, CapabilityDescription, Transport(Remote|Stdio), Environment}`.
- Editor fields: Name, Server URL, Authentication (Sign in with browser / API key / None), and for a local server, Command and Arguments. Advanced options: Type (Remote URL / Command on this PC), OAuth client ID (for servers that require a pre-registered app; Meta Ads is the worked example), extra headers (one per line) and environment variables (one per line).
- Validation: the URL must be https (plain http only triggers a warning); header and environment-variable names must be valid; changing the server or client ID clears the saved credentials.
- Storage (`T:CustomConnectorStore`):
  - `connectors.json` holds the list. `connectors.secrets` holds `T:CustomConnectorSecrets{BearerToken, Headers, Environment, RefreshToken, ExpiresAt, TokenEndpoint, RegisteredClientId, RegisteredClientSecret}`, encrypted with **DPAPI** for the current user plus an app entropy value (`Entropy` field; S:"secrets file is DPAPI ciphertext").
  - Headers reach the runtime as environment-variable references, not literals.
- OAuth (`T:McpOAuth`):
  1. A first unauthenticated request; a `WWW-Authenticate` challenge is parsed for `resource_metadata` and `scope`.
  2. Protected-resource metadata is read from `/.well-known/oauth-protected-resource`, giving the authorization server list and `scopes_supported`.
  3. Authorization-server metadata is read from `/.well-known/oauth-authorization-server` and other candidate URLs, giving the authorize, token and registration endpoints.
  4. **Dynamic client registration** (DCR) posts `redirect_uris` and `token_endpoint_auth_method` (the `authorization_code` grant string is present). If no registration endpoint exists, the client uses a pre-entered client ID.
  5. Authorization code with **PKCE S256**. The `resource` parameter is sent. The flow asks only for the resource's declared scopes.
  6. A **loopback callback** server at `http://localhost:<port>/callback`. The E2E string names port 48611. It shows an error when the port is in use, and the redirect URL is shown to the user for pre-registered apps.
  7. Token exchange and refresh. Tokens are refreshed when they near expiry (`RefreshMargin`) before the runtime uses them.
  8. Errors are classified (`T:McpOAuthFailure`), with friendly text for "server only accepts approved clients", origin mismatch and unsupported registration.
- Two sign-in paths (`T:ConnectorAuth`): app-side OAuth as above, or the Codex runtime's own browser sign-in (`mcpServerStatus/list`, `.credentials.json`). App-side OAuth is the fallback when the runtime is unavailable.
- Verification (`T:McpProbe`, `T:McpVerification`):
  - Sends a JSON-RPC `initialize` (protocol 2025-06-18) over streamable HTTP, handling an SSE response and a server session ID (`T:McpProbe.Posted{Status, Body, IsEventStream, SessionId}`).
  - Then calls `tools/list` to build `T:McpToolCatalog{ServerTitle, ToolNames}`.
  - Writes a one-line capability description from the tool names, using a verb/noun tokenizer and plural rules.
  - Results: Reachable(ToolCount), Unauthorized(Status), ServerError(Status) or Unreachable.
  - Also verifies the token at launch.
- Sign-in states (`T:ConnectorSignInKind`): Unknown, Checking, SignInUnavailable, NotNeeded, NeedsSignIn, AwaitingBrowser, SignedIn, Failed and TokenRejected. The UI labels match: Checking, Needs sign-in, Signing in, Sign-in failed, Check failed, Needs a token, Token rejected, and "Finish connecting in your browser."
- Proactive integration suggestions (a "you use X; connect it?" prompt based on the foreground app) exist as `T:IntegrationSuggestionRules` and the notch surfaces. Suggestions are paused in 1.1.1.

---

## 4. Account, sign-in, plans, updates, telemetry, privacy

| Area | Finding | O/I |
|---|---|---|
| Sign-in | `T:SupabaseAuthService`. Google OAuth goes through Supabase's authorize endpoint and redirects to a deep link `heyclicky://…/auth-callback` that carries access and refresh tokens and an optional `referral_code`. Password sign-in exists for local test accounts. Sessions refresh with handling for refresh-token reuse and revocation. The profile comes from the user endpoint. | O |
| Session storage | `auth.bin` (and `auth.localdev.bin`) via `T:AuthTokenStore` and `T:AtomicFile`. **I**: protected with DPAPI, as the connector secrets are. | O / I |
| Device | `device-registration.json`, posted to `/me/devices`. | O |
| Backend | A Worker base URL is overridable only to loopback (`HEYCLICKY_WORKER_BASE_URL`, `endpoints.json`, `use-production-backend.flag`). App config, model policy and starter plan come from that Worker. | O |
| Plans and billing (inventory only) | `T:BillingStateService`: plan, tier (Max/Team), interval, usage meters and reset date, payment-problem state, referral discount, a watcher for plan changes made in the browser, and a cached last-known plan. `T:PaywallPayload` and `PaywallPlanOption`. Routes `/me/plan`, `/billing/checkout`, `/billing/portal`, `/me/referral*`. Agent **turn leases** via `/agent/turn-lease/{status,continue,complete,refund-early-stop}`, with `extra_usage_approval` set to one_time or as_many_as_needed. `T:AgentTurnCostLedger` and `AgentModelPricing` hold token cost per turn. | O |
| Account deletion | `/me/account/delete` requires a fresh sign-in. Settings has "Log out" and "Delete account". | O |
| Updates | Velopack: `Update.exe` in the package root, `sq.version` (id `HeyClickyApp`, channel `win-x64`, Start Menu shortcut, release notes embedded). `T:UpdateService`: check on a timer (S: hourly), check after wake, manual check, download progress, "Install & Relaunch", install on quit, and a `pending-update.json` record that confirms the update landed. Builds not installed by the installer cannot update themselves. The feed URL is an assembly-metadata value, and if it is the `.invalid` placeholder, scheduled checks turn off. A `heyclicky://` protocol is registered as shell integration. `RestartAgent.exe` is present (purpose **I**: relaunch helper). | O |
| Analytics | `T:ClickyAnalytics` with a PostHog sink (`us.i.posthog.com`; key from assembly metadata; batched and requeued), plus Log and Null sinks. It tracks identity, plan properties, onboarding, browser connection and actions, computer-use consent, and app errors. `T:ClickyAITracing` adds trace and session IDs for each agent thread as HTTP headers. **No user-facing analytics opt-out string was found.** | O (absence I) |
| Crash reporting | Sentry (`Sentry.dll`; DSN from metadata). `T:CrashReporter` has an Off mode: setting `HEYCLICKY_SENTRY_OFF=1` or having no DSN keeps crash and error reports in the logs folder. "Report a bug" zips logs and a state snapshot and sends them to Sentry. | O |
| Privacy controls in the app | Log out and Delete account. Quiet during calls and screen shares, and Do Not Disturb for routines. Links into Windows privacy settings that the agents inherit (`ms-settings:privacy-*`). Launch at login. Report a bug. The extension's cursor toggle and Stop. The trust page also mentions a toggle for the daily integration read. | O |

---

## 5. Cross-reference: advertised feature → static evidence

| Advertised feature | Static evidence | Notes |
|---|---|---|
| Chrome extension, own tab group, signed-in sites | Manifest perms; `core.mjs` groups (`GROUP_COLOR`, `openInGroup`); `T:ClickyBrowserBridge` | Strong |
| Installs itself | `T:ChromeAutoInstall`, `ChromeSetupFlow`, `ChromeFolderStrip`, `ChromeInternalPages` | Strong |
| Extension won't fill passwords, codes or cards | `type` refuses password fields; tool text says these are the user's to type | Only password fields are enforced in code; codes and cards are by instruction (**I**) |
| Stop the job anytime | Popup `stop`, `bridge.stop(…, handOver)`, Chrome debugger-bar cancel | Strong |
| Incognito unsupported | `tabs` filters out incognito tabs | Strong |
| Multiple accounts per app, labels | `T:AgentIntegrationAccount.Label`, `/accounts/{id}/label`, UI strings | Strong |
| Agents choose the account or search all | Agents `us:550-560`, `MultiAccountLines` | Strong |
| Per-account Reconnect (1.1.1) | UI strings "Reconnect", "Sign-in expired", "Remove this expired account" | Strong |
| Custom connectors by URL or local command | `T:CustomConnector{Url,Command,Arguments}`, `CustomConnectorTransport.Stdio/Remote` | Strong |
| Browser sign-in or API key | `ConnectorAuth`, `McpOAuth` (DCR, PKCE, loopback) | Strong |
| Token verified with a real request; Checking/Connected/Token rejected | `T:McpProbe`, `McpVerification`, `ConnectorSignInKind` | Strong |
| Gmail, Calendar, Notion, Linear, Slack and more | Toolkit slug strings; Composio session config | Server-side catalog **unknown** |
| Google Calendar read and create | `/agent/realtime/google-calendar/events[/create]` | Strong |
| Spotify | `/agent/realtime/spotify/search` | Strong |
| Reddit and Monday paused | "reddit" slug still present | Pause is server-side **I** |
| Connectors wait after wake for routines | Routine scheduler (behavior spec G3) | Not re-examined here |
| Proactive suggestions (paused) | `T:IntegrationSuggestionRules`, `ProactiveCoordinator`, notch surfaces | Present but paused per changelog |
| Updates and an Update button | `T:UpdateService`, Velopack, `sq.version` | Strong |
| Screenshots never stored | Browser screenshots kept only when `save:true` in a job folder (S: "Not saved: only a job's screenshots are kept in its folder.") | Partly; desktop screenshots are outside this scope |
| PostHog analytics, Sentry crashes | `PostHogAnalyticsSink`, `Sentry.dll`, `CrashReporter` | Strong |
| Account deletion | `/me/account/delete`, Settings strings | Strong |
| Sign-in credentials in Keychain (Mac) | Windows uses `auth.bin` (DPAPI **I**) | Platform difference |
| Plans, credits and paywall | `BillingStateService`, `PaywallPayload`, turn leases | Inventory only |
| Referrals | `ReferralEarningsEstimator`, `/me/referral*`, `ReferralCode` in the auth callback | Inventory only |
| Teams | `IsTeamManagedPlan`, `team.heyclicky.com` | Inventory only |
| Student discount, UPI, yearly 20% | No static evidence beyond yearly fields (`SupportsYearly`, `YearlyPriceDisplay`) | Server-side |
| Captions (1.1.1) | Not searched in this scope | See the voice/UI agent's inventory |
| Dictation, drawing, 3D, Clickys, routines | Outside this scope | See the behavior spec and the other agents |
| "Works with anything, no plugins needed" | n/a (marketing claim) | — |

---

## 6. Open unknowns
1. **Host↔app transport detail.** The `browser_poll` and `browser_reply` semantics (long-poll intervals, the auth header format, how multiple profiles queue) are inferred from strings only. No code was decompiled.
2. **Upload path format.** The extension requires each path to start with `/`. Whether the Windows app converts `C:\…` paths (`BrowserUploads.Normalize`) or whether upload is effectively broken on Windows is unknown.
3. **Maximum accounts per app.** `HasReachedActiveAccountLimit` exists, but the number is server-provided or a constant we did not read.
4. **The Composio catalog.** It lives on the server: which toolkits are enabled, their scopes, and what read-only means for suggestions are not in the binary.
5. **Auth session protection.** DPAPI for `auth.bin` is inferred; only the connector secrets are confirmed as DPAPI.
6. **Update feed URL.** It is an assembly-metadata value we did not read, and the hourly cadence comes from a UI string only. Whether 1.1.1 ships with the placeholder feed (which turns scheduled checks off) is unknown.
7. **Analytics opt-out.** No toggle string was found. The trust and privacy pages describe none. We cannot tell whether the "Off" crash-reporter mode is reachable from the UI.
8. **The second extension ID** in `ClickyBrowserExtensionID.txt`: we infer it is the Web Store build. Unconfirmed.
9. **`RestartAgent.exe`** was not inspected.
10. **Runtime behavior overall.** Timing, server enforcement of turn leases and refunds, and the real OAuth interoperability of custom connectors were not exercised.

### Implications for OpenClicky (our recommendations, not HeyClicky facts)
- The browser operation set in 2.4, together with diff snapshots, stable refs, the claim/keep/tidy tab model and refusing password fields, is a complete, well-bounded spec to compare against OpenClicky's scoped extension (gap G7).
- For custom MCP connectors (G5) the standard pieces are: RFC 9728 resource metadata, RFC 8414 server metadata, RFC 7591 DCR, PKCE S256, an RFC 8707 `resource` parameter, a loopback redirect, DPAPI-protected secrets, and an `initialize` plus `tools/list` probe. All of these can be built from public specs.
- Managed multi-account connectors depend on a hosted broker. OpenClicky should use the user's own keys or OAuth apps instead, following the existing connector decision.
