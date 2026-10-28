# HeyClicky 1.1.1 — background agent work: clean-room feature inventory

Scope: companions, jobs, work shelf, routines, proactive layer, agent runtime and tools, learned how-tos / memory. Written in our own words for OpenClicky (MIT). No HeyClicky code, prompt, instruction or skill text is reproduced; identifiers, tool names, enum values, numeric constants and short labels are cited as evidence only.

## Method and evidence

- Target: `%LOCALAPPDATA%\HeyClickyApp\current` (1.1.1, win-x64). Nothing was launched; no network contact.
- REA (`mcp__rea__*`) Evidence:
  - `HeyClicky.Agents.dll` members — summary `ev_ba64b58f57c0…`, parent `ev_78f4fd49ccd3…` (489 types, 3698 methods, 2092 user strings; sha256 `3aef8bd7…`, MVID `1130d3d8-…`).
  - `HeyClicky.Contracts.dll` members — summary `ev_f536c1db1e5e…`, parent `ev_4b9d39bf54ec…`.
  - H1 harness JS (extracted from the Bun-compiled `HarnessRuntime\clicky-harness.exe`, see below) — summary `ev_812af2ad933c…`, parent `ev_6c76305c59a4…` (one 468 KB minified file, parsed inertly; graph coverage partial).
- `HeyClicky.dll` (members exceed REA's budget) and `HeyClicky.Core.dll`: read with my own `System.Reflection.Metadata` dumper (type/field/property/method signatures, `#US` strings) plus an IL **operand scan** (per method: `ldc.*` constants, `ldstr` literals, call/field targets). No decompilation to source; no target code loaded. Working dumps: `rea-agents\work\*.types.txt`, `*.strings.txt`, `*.il.txt`.
- Harness: the exe is a Bun single-file executable (`---- Bun! ----` trailer, module `B:/~BUN/root/clicky-harness-x64.exe`). The embedded JS bundle (`// @bun`, file offset 86158856, 468244 bytes) was copied to `rea-agents\work\harness-js\bundle.js` and read as text only.
- Labels: **O** = observed in shipped metadata/strings/IL operands; **I** = inferred from names/order of constants; runtime behaviour not seen.

---

## 1. Companions ("Clickys")

| Feature | Detail | Evidence | O/I |
|---|---|---|---|
| Record | `ClickyInfo(Slug, Name, RoleText, OneLiner, ThreadId, CreatedAt, IsArchived)` (C# record). | `HeyClicky.Agents.Clickys.ClickyInfo` | O |
| Store file | `clickys.json` in the agent state dir; unreadable file → start fresh. | `ClickyStore::.ctor`, strings `clickys.json`, `clickys.json unreadable, starting fresh:` | O |
| Store state | `Clickys[]`, `ThreadTitles{thread→title}`, `InstructionFingerprintByThread{thread→hash}`, `FiledThreadOwners{thread→slug}`, `DisplayPrompts{key→display}`, `HiddenThreadIds[]`. | `ClickyStore+State` | O |
| Operations | `Found(name, role, oneLiner)` (create), `Rename`, `Replace`, `Archive`, `Unarchive`, `BindThread/UnbindThread`, `ForThread`, `OwnsThread`, `FileThread(thread, slug)` (move a thread under a Clicky), `HideThread/IsThreadHidden/ClearHiddenThreads`, `SetThreadTitle/TitleForThread`, `RecordDisplayPrompt/DisplayFor` (show a short version of a long generated prompt), `AppendNotes(markdown)`, `DeleteAll()` on sign-out. | `ClickyStore` methods | O |
| Slugs | `Slugify` lowercases and replaces non-slug chars with `-`; fallback slug `clicky`; default name `Clicky Buddy`. | `ClickyStore::Slugify`, strings | O / I (fallback use) |
| Workspace layout | `<ProjectsRoot>\agents\<slug>\` with `output\`, `tmp\` and a seeded `AGENTS.md` (only written if missing). `ProjectsRoot` default is `~\Clickys`. | `ClickyStore::WorkspacePath/EnsureWorkspace`, `App.Settings.AgentFolders` strings `Clickys`, `~` | O |
| Instructions/notes file | `AGENTS.md` = identity (name, role, speak in first person), a standing-preferences section the agent maintains, dated notes; it is the Clicky's notes file (`NotesPath` == `AGENTS.md`). The app appends via `AppendNotes`. Content is generated text (not copied here). | `SeededAgentsFile`, `NotesPath`, strings | O |
| Instructions carried once per thread | On first turn of a thread the Clicky's instructions are carried in and fingerprinted (`MarkThreadStartedWithInstructions`, `InstructionsToCarry`, `Fingerprint`); re-sent only when changed. | `ClickyStore` | O (methods) / I (exact rule) |
| Standing context ledger | Per-thread fingerprints for 4 sections: `UserMemory`, `AgentInstructions`, `ActiveSkills`, `LearnedHowTos`; remembers ≤ 2000 threads; persisted under defaults key `clicky.codexAgent.standingContextCarried.v1`. | `Turns.StandingContextLedger(+Section,+Ledger)` | O |
| Brain-side view | Voice brain sees `AgentClickyInfo(Slug, Name, RoleText, OneLiner, ThreadId, IsArchived)` and can create a Clicky via `IAgentToolHost.CreateClicky(name, roleText, oneLiner)`; tool results mention `founded_new_clicky`, `known_slugs`, `unknown_clicky`. | `Core.Tools.AgentClickyInfo`, `IAgentToolHost`, `AgentTools` strings | O |
| Per-Clicky work list | `ClickyAppStore` keeps `works.json` per Clicky folder (≤ 60 pieces) → `ClickyWorkShelf`. Preference flags `ClickyAppsPreferences.IsEnabled`, `ShowsEdgeShelf`. Live status phases `Idle/Working/NeedsYou/Finished/Failed`. | `Jobs.ClickyAppStore`, `ClickyAppLiveStatus+Phases` | O |
| Restart interruptions | Running turns noted per slug (`Slug, ThreadId, StartedAt, InterruptedAt, IsInChat`), kept 7 days (defaults key `clicky.clickys.runningTurns.v1`); `TakeInterruptionsForChat` offers them back in chat. | `Jobs.ClickyRestartInterruptions` + `.cctor` (`FromDays 7`) | O |

## 2. Jobs and the work shelf

### Job model
| Field group | Detail | Evidence | O/I |
|---|---|---|---|
| Identity | `Id` (GUID "N"), `Title`, `Prompt`, `ThreadId`, `FolderPath`, `CreatedAt`, `FinishedAt`, `RailKey` = `job:` + id. | `Jobs.ClickyJob`, `CreateJob` (`Guid::NewGuid`, `"N"`) | O |
| Outcome | `ClickyJobOutcome`: `Working, Done, Failed, Stopped`. Plus `Summary`, `ReplyText`, `ArtifactEntries`, `LatestReplyArtifactEntries`, `FilesFromLatestReply`, `ResultCard` (`ClickyAnswer`). | `ClickyJobOutcome`, `ClickyJob` | O |
| Runs | `Runs[]` of `ClickyJobRun(ThreadId, StartedAt, Ask, FinishedAt, Outcome, Summary)`; `BeginRun(jobId, ask, threadId)`; `AllThreadIds`. | `ClickyJobRun`, `ClickyJobStore::BeginRun` | O |
| Shelf flags | `IsSeen`, `IsDismissed`, `IsArchived?`, `FollowedUpOnLaterDay?`, `CloudColorName`, `SuggestionCardId?` (job created from a proactive card), `WaitsForChrome?`, `SkipsChrome?`, `IsWaitingToStart`, `IsHeldForChrome`, `IsQuickActionWithoutOutput`. | `ClickyJob` | O |
| Kind | `ClickyJobKind`: `Pdf, Document, Sheet, Slides, Image, Video, Page, Code, Message, Research`, inferred from artifacts + title + prompt. | `ClickyJobKind`, `ClickyJobKinds.Infer` | O |
| Rail phase | `ClickyJobRailPhase`: `Queued, Working, NeedsYou, Done, Failed, Stopped`. `NeedsYou` when held for Chrome, awaiting extra-usage approval, pending user step, needs Chrome, pending computer-use request, or a saved `WaitingStep`. | `ClickyJobRailPhase`, `AgentService::RailPhase` IL | O |
| Store | Defaults key `clicky.jobs.v1`; keeps ≤ 80 jobs (jobs with routines are protected from trimming; oldest trimmed first). Lists: `Jobs`, `RailJobs` (not dismissed, by `CreatedAt`), `ArchivedJobs`. | `ClickyJobStore` consts, `TrimKeptJobsLocked`, rail lambdas | O |
| Store ops | `CreateJob(title, prompt, projectsRoot, startsWorking)`, `CreateWaitingJob(title, pitch, suggestionCardId, …)`, `BeginWaitingJob`, `RelinkWaitingJobsToRestoredCards`, `CreateJobForMovedRoutine`, `RecordThreadId`, `MarkWorking`, `MarkEnded(outcome, summary, reply, artifacts)`, `SetResultCard`, `SetWaitingStep`, `SetWaitsForChrome`, `SetSkipsChrome`, `MarkSeen`, `KeepOnDock` (pin), `Dismiss`, `Archive`, `Unarchive`, `RemoveAllForSignOut`. Fires `Changed`. | `ClickyJobStore` | O |
| Colour tag | 4 "cloud colours" `red, blue, yellow, green`; new job takes the least-used colour (also counting colours used by Clickys via `CloudColorsInUseByClickys`); `SeededCloudColor(seed)` for deterministic choice. | `ClickyJobStore::.cctor`, `LeastUsedCloudColor` | O |
| UI hooks | `AgentService.NoteJobCardClosed` → `MarkSeen`; `PinJob` → `KeepOnDock`; `DismissJob` → `Dismiss`; `AppHost.StartWaitingJobNamed` also dismisses. `WorkNotchUpdates` builds notch content for jobs (`Answered`, `UserStep`, `ConnectChrome`, `Retry`, `RetryJob`, `StoppedDetail`, `IsRoutineReturn`). | call sites in `Agents.il.txt` / `App.il.txt` | O |

### Folder layout
| Item | Detail | Evidence | O/I |
|---|---|---|---|
| Job folder | Created with `output\` (deliverables) and `tmp\` (scratch) under the projects root, in a `Jobs` area; failure logged as "couldn't make … in …". Exact folder name is title-based (`FolderSlug(title)`). | `CreateJob` strings `Jobs`,`output`,`tmp`; `ClickyWorkFolderFiles.FolderSlug` | O (subfolders) / I (naming) |
| Index | `WORK.md` (app-maintained list of the user's work, newest first) with an app-owned runs block between `<!-- clicky:runs -->` and `<!-- /clicky:runs -->` (per run: date `yyyy-MM-dd HH:mm`, outcome text `done / failed / stopped by the user / working now`, one-line summary). `state.md` exists as a name but `WritesStateFiles = False`. | `ClickyWorkFolderFiles` consts/strings | O |
| Live note | `note.md` in the job folder; on a new run the previous one is moved to `.clicky\previous-note.md` (`RetireNote`). Writer modes `ApplyPatch`, `FileTools`, `NoteTools`. | `ClickyWorkFolderFiles`, `ClickyLiveNoteWriter` | O |
| Helper files | In the `Jobs` helper area: `about-the-user.md` (user profile) and `note-guide.md` (note-writing guide), written only when changed. | `ClickyHelperFiles` | O |
| Brief | A fixed job brief tells the agent its cwd is the job folder, `output/` vs `tmp/`, and that `../WORK.md` lists other work; also ignore invented save paths. | `ClickyJobBrief.WorkFolderBrief` (text not copied) | O |
| Task assets | `<CodexHome>\task-assets\<taskGuid>` for per-task attachments. | `CodexHomeManager::TaskAssetsDirectory` | O |
| Artifact selection | `ClickyMainArtifacts` picks "main" files vs supporting/scratch, detects project folders (marker names/extensions, source folder names), follows references from HTML-like pages. | `Jobs.ClickyMainArtifacts` fields/methods | O (names) / I (rules) |

### Structured final result
Parsed from tagged blocks in the agent's last message (`StructuredFinalReply.Parse`; tags case-normalised): `SUMMARY`, `NEXT_ACTIONS`, `DONE_TITLE`, `ARTIFACTS`, `ROUTINE`, `COMPUTER_USE_REQUEST`, `WAIT_FOR`, `LEARNED`, `ANSWER`. Fields: `Text, NextActions, DoneTitle, Artifacts, Summary, Routine (AgentRoutineRequest), ComputerUseRequest, UserStep (ClickyJobUserStep), LearnedHowTos, Answer (ClickyAnswer)`. **O**
- `WAIT_FOR` → `ClickyJobUserStep{ step, page, done_when_url_contains, AskedAt }` (caps 600/200/160 chars, I). **O**
- `ROUTINE` → `AgentRoutineRequest{ action ∈ create|update|pause|resume|delete, title, task, every_minutes, new_title }`. **O**
- `ANSWER` → `ClickyAnswer` result card (JSON): `line`, `numbers[]{value,label,was,of,better(higher|lower)}`, `numbers look ∈ Hero|Tiles|Changes|Progress`, `list[]{text,detail}` + `ranked`, `done[]{did,what,link}`, `draft{text,to}`, `ask[]{answer,detail,file,reply}` (choices), `note`, `next{label,open}`, `quiet`; block order `Numbers, List, Done, Draft, Ask`; stored as `ReportJson`. **O**
- `ClickyWorkReplyParts.Parse` strips the metadata blocks and returns (summary, title, reply, file paths), expanding `~` and recognising Windows paths. **O**

### Follow-ups, steer, interrupt, waiting steps
| Feature | Detail | Evidence | O/I |
|---|---|---|---|
| Routing | `ClickyJobMessageRoute(TargetThreadId, NewRunFollowsThreadId, ResumesFinishedRun)`: a follow-up goes into the live thread (steer) or starts a new run that resumes a finished one. | `ClickyJobMessageRoute`, `AgentService.JobMessageRoute`, `SubmitThreadFollowUpAsync` | O |
| Steer | Codex `turn/steer {threadId, input, expectedTurnId, responsesapiClientMetadata}`; harness `POST /steer`. Event `SteerAccepted`. | `CodexSession::SteerAsync`, harness strings | O |
| Interrupt/stop | Codex `turn/interrupt {threadId, turnId}`; harness `/cancel`; `StopJobAsync`, `StopAsync`. | `CodexSession::Interrupt` | O |
| User-step watcher | Every 2.5 s reads the frontmost browser address; when it contains `done_when_url_contains` raises `StepDone(jobId, …)` → `ContinueJobAfterUserStep`; gives up after 6 h. | `ClickyJobStepWatcher` (+`.cctor` 2.5 s / 6 h) | O |
| Chrome gating | Jobs can wait for / skip the Chrome extension (`WaitsForChrome`, `SkipsChrome`, `NeedsChromeConnection`). | `ClickyJob`, `AgentService.MarkNeedsChromeConnection` | O |
| Pending requests | Computer-use and extra-usage requests persisted (`clicky.agent.pendingRequests.v1`, max age 7 days) and restored after restart. Kinds `ComputerUse`, `ExtraUsage`. | `ClickyPendingAgentRequest(Store)` | O |
| Retry | `RetryFailedTask`, `CanRetryAfterFailure`, `CanRetryAfterInterruption`. | `AgentService` | O |

### Live progress
`AgentTask` carries status (`Starting, Running, WaitingForUser, Finished, Failed, Stopped`), plan steps (`AgentPlanStep{Text, Status, IsDone, IsNow}` from the plan tool), streaming commentary, reasoning-summary "thinking" heading, activity rows (`Commentary, Thinking, ToolCall, ShellOutput, FileChange, Error`), token counts. Events: `StatusChanged … ExtraUsageApproved` (17 kinds). `ClickyLiveSteps` turns tool activity into short step phrases of kinds `Search, Read, Look, Math, Note, File, Apps, Mac` (keeps 12), with a "Finalizing" step appended to plans. **O**

### Work shelf (per-Clicky "made / quick answers / question")
`ClickyWorkShelf(works)`: classifies `ClickyWorkPiece{Id, Slug, ThreadId, ClientTurnKey, Ask, Title, Summary, Reply, ArtifactPaths, FinishedAt, Failed, IsRoutineRun, Links}` into `Made`, `QuickAnswers`, and one `OpenQuestion` (reply ends with a question to the user); routine runs counted per routine. Limits: first answer 160 chars, full follow-up answer 1600, question 700. **O** (rules for question detection: I)

## 3. Routines

| Feature | Detail | Evidence | O/I |
|---|---|---|---|
| Record | `ClickyRoutine{Id, ClickySlug, JobId, ScheduledMinuteOfDay?, ScheduledWeekday?, ScheduledWeekdays[], Title, TaskPrompt, IntervalSeconds, CreatedAt, IsPaused, NextRunAt, RunCount, LastRunStartedAt, LastRunFinishedAt, LastRunSummary, LastRunFailed, PendingRetryAt, LastRunTaskId, ConsecutiveFailedRunCount, WaitingForNetworkSince}`; derived `CadenceLabel`, `ScheduleLabel`, `IsPausedAfterRepeatedFailures`. | `Routines.ClickyRoutine` | O |
| Storage | `routines.json` (indented JSON) in state dir. | `RoutineScheduler::.ctor/Load` | O |
| Schedule types | (a) fixed interval 2 min … 10080 min (1 week); (b) set time of day (minute 0–1439, default 540 = 09:00) every day; (c) set time on one or more weekdays (0 = Sunday … 6). `NormalizedSchedule`, `NextSetTime`, `OnWeekdayAfter`. Labels "Every N min / Every hour / Every N hours / Every day / Every N days". | `ClickyRoutine` consts, `NormalizedSchedule`, `CadenceLabelFor` | O |
| Ownership | Routine is owned by a Clicky and/or a job; Clicky-owned routines are migrated onto jobs (`MoveClickyRoutinesOntoJobs`, `CreateJobForMovedRoutine`). | `RoutineScheduler`, `IRoutineHost` | O |
| Poll | Timer every 30 s; re-entrancy guarded. | `.cctor` `PollInterval 30 s` | O |
| Busy owner | Skip/hold when the owning Clicky or job is busy or awaiting extra usage. | `IRoutineHost.IsClickyBusy/IsJobBusy/Is…AwaitingExtraUsage`, `IsOwnerBusy` | O |
| Signed-in | Evaluation only when signed in. | `EvaluateAsync` IL (`get_IsSignedIn`) | O |
| Network | If no network (`NetworkInterface.GetIsNetworkAvailable`), due routines are marked "waiting for the internet" (`WaitingForNetworkSince`). | `EvaluateAsync`, `MarkWaitingForNetwork` | O |
| Wake gate | Hold due runs during dark wake / "display off after sleep" until a full wake (display on); fail-open after a settle delay; on full wake: settle overdue routines (3 min `OverdueSettleDelay`) then evaluate. | `RoutineWakeGate`, `HandleFullWake`, `SettleOverdue` | O |
| Stale / reconcile | A run in flight > 2 h counts as stale; unfinished runs reconciled after restart; interruption reasons "app stopped" vs "agent server disconnected". | `StaleRunAfter 2 h`, `ReconcileUnfinishedRuns`, consts | O |
| Retry/backoff | Failed run → retry after 300 s (`FailureRetryDelaySeconds`); blocked launch retried after 60 s; 3 consecutive failed runs → auto-pause (`source=repeated_failures`). Out-of-messages → next run after allowance reset (fallback 24 h). Transient connectivity failures don't count toward the cap (I). | `ClickyRoutine` consts, `RecordOutcome`, `NextRunAfterAgentMessageLimit`, `NoteTaskEnded` | O / I |
| Keep-awake | While routines are armed **and** on AC power, `PowerCreateRequest` + `PowerSetRequest(SystemRequired)` with reason text; cleared otherwise. | `RoutineKeepAwake` P/Invoke + `Update` | O |
| Missed runs | Overdue routines are "settled" after wake rather than fired in a burst (I: one catch-up then reschedule). | `SettleOverdue`, `RescheduleAfterCadenceChange` | I |
| Prompt | Each run gets a header line (title, run number, cadence, start time) and the stored task with any leading cadence phrase stripped; routines listed in a "[Your routines]" note for the agent. | `PromptHeaderLine`, `TaskPromptStrippingLeadingCadence`, `RoutinesTool.ContextNote` | O |
| Events | `clicky_routine_run_ended` (`routine_id, run_number, outcome, duration_seconds`), `clicky_routine_pause_changed`, plus created/updated/deleted/dispatched. | `NoteTaskEnded`, `Track*` | O |
| App-side tool `routines` | `RoutinesTool.RunAsync(json)` (used by the voice brain's `routines` tool). Params: `action` ∈ `create, update, pause, resume, delete, run_now, status`; `title`, `task`, `every_minutes`, `at` (time like 9am / 14:30), `days` (weekday names), `routine_id`, `codexThreadId` (attach to an existing job), `first_run` (run now). Results: `created/updated/paused/resumed/deleted/started/not_started` + summary with next-run time. | `RoutinesTool` strings | O (names) / I (exact mapping) |
| Codex path | Codex jobs request routines via the `<ROUTINE>` final-reply block (`action, title, task, every_minutes, new_title`). | `AgentRoutineRequest` | O |
| H1 harness tool `routines` | `action` ∈ `create, update, pause, resume, list`; `title`, `task`, `every_minutes`, `time_of_day`, `timezone`, `new_title`. | harness `bundle.js` | O |
| From suggestions | `RegisterApprovedSuggestion(...)` turns an approved proactive card into a routine. | `RoutineScheduler` | O |

## 4. Proactive layer

Release gates (important): `ProactiveLanePause.IsPausedForRelease = True`, `ProactiveCoordinator.IsMorningInvitationPaused = True`, `MorningSuggestionsInvitationHasASurface = False` → the suggestion lane and morning invitation appear **switched off in 1.1.1** (O, consts).

| Feature | Detail | Evidence | O/I |
|---|---|---|---|
| Morning greeting | Window 07:00–12:00 local, once per day (`DayKey`), poll 15 s, arm delay 10 s, only if user active in last 30 s, hold 5 s after a stream; message picked randomly from 18 messages + 17 kaomoji. Time-of-day salutation: morning 05–12, afternoon 12–17, evening 17–22, else neutral. | `MorningGreetingPolicy`, `TimeOfDayGreeting` | O |
| Morning suggestions invitation | Shown 30 s after the greeting; 5 min snooze if pre-empted; 3 consecutive dismissal days → rest 5 days. Notice holds card ids and a not-before time. | `MorningSuggestionsInvitationPolicy`, `MorningSuggestionsNotice`, `MorningDismissalHistory` | O |
| Morning paper | `GoodMorningBrief`: optional research job prepared for the morning, saved to a research file, played from the notch with headline lines. | `App.OneBrain.GoodMorningBrief` | O (names) |
| Suggestion cards | `ProactiveSuggestionCard{Id, Title, Description, AgentPrompt, CheckReason, ServerSuggestionId, ClickySlug, NewClicky{Name,Role,Description}, Routine{EveryMinutes,Title}, IntegrationName, CreatedAt, ViewedAt, ExpiresAt}`; daily expiry 24 h. Check reasons `Morning, Debug, HomeSpace, Onboarding, Conversation`. Resolutions `Approved, Denied, Expired, Replaced` (deny reason ≤ 500 chars) kept in history and sent back as context. | `Core.Proactive.*`, `ProactiveCoordinator.DenyReasonMaximumCharacters` | O |
| Generation | Server-side job: inputs = connected integrations, recent history, Clickys (slug, name, role, routines, threads), custom connectors, existing routines; status `Queued/Running/Succeeded/Failed` polled every 4 s for 15 min then 30 s; status sync 60 s; receipts kept 6 h. | `ProactiveRequestInputs`, `ProactiveJobStatus`, coordinator consts | O / I (server) |
| Cadence | `ProactiveCadence` Daily(1)/ThreeTimes(3)/FiveTimes(5) with per-cadence hour lists; slot retry backoff 5 min → 15 min → 1 h → 3 h by consecutive failures. | `ProactiveCadenceRules`, `ProactiveSlotDayState.RetryDelay` | O / I (hour values from static array data) |
| Approval flow | Approve (optionally with typed or voice "adjustment"), deny, skip; approval can found a new Clicky and/or a routine; queued tasks wait (`Ready, Starting, Paused, WaitingForClicky, WaitingForEarlierTask`). Bracketed marker messages identify "suggested task approved" and "new Clicky founded" in threads. | `QueuedProactiveTask`, `SuggestionApprovalMarker`, `ClickyFoundingMarker`, `SuggestionVoiceAction` | O |
| Activity-based integration suggestions | Sample foreground app/site every 2.5 s; credit ≤ 30 s per sample; suggest after 10 s dwell (Spotify 7.5 s); ≤ 3 prompts per 24 h; "not now" snooze 48 h; never-ask list; card auto-hides after 18 s; pending connection verified for 5 min; catalog refresh 30 min. "Popular" catalog (20): notion, linear, github, gmail, googlecalendar, googledrive, googledocs, googlesheets, slack, jira, asana, trello, clickup, airtable, hubspot, salesforce, zendesk, discord, dropbox, zoom — each with host and process-name matchers; long tail catalog (~70 more). | `IntegrationSuggestionRules` + `.cctor`, coordinator consts | O |
| Suppression | Reasons: signedOut, permissionsIncomplete, onboardingIncomplete, quietContext, notchExpanded, spotifyVoiceTipAlreadyActive, suggestionAlreadyActive, voiceBusy, onboardingIntroActive, onboardingTutorialEngaged, postOnboardingQuietPeriod (5 min); ambient quiet: Do Not Disturb, mic in use by another app. | `SuppressionReason`, `AmbientQuietReason` | O |
| Meeting countdown | From calendar connector: `UpcomingCalendarEvent{Title, StartsAt, EndsAt, IsAllDay, MeetingUrl, Location, HtmlLink, DirectionsUrl}`; primary action `Join/Open/Directions`; shown from 75 s before start; tick 15 s; refresh if stale 5 min (30 s when the notch is expanded); not-connected recheck 30 min; failed fetch backoff 60 s; announced once per event. | `MeetingCountdownRules`, `UpcomingCalendarEvent`, `MeetingCountdownSurface` | O |
| Notifications | Server notifications with severity, mark read/all read, polling. | `AgentNotification(Severity)`, coordinator methods | O |
| Notch surfaces | `MorningSuggestionsSurface`, `IntegrationSuggestionSurface`, `MeetingCountdownSurface`, `AgentTaskSurface`, `ClickyUpdateSurface`; contract events `NotchLiveEvent+MorningSuggestions/IntegrationSuggestion/MeetingCountdown/AgentTask/AgentMultiActivity/ClickyUpdate/AgentPeek`. | `App.Notch.Surfaces.*`, `Contracts.NotchLiveEvent+*` | O |

## 5. Agent runtime

### Process model
- Two interchangeable back ends behind `AgentService` (O): **Codex app-server** (`CodexRuntime\bin\codex.exe app-server`, v0.152.1, target x86_64-pc-windows-msvc, plus `codex-code-mode-host.exe`, `codex-command-runner.exe`, `codex-windows-sandbox-setup.exe`, `rg.exe` on PATH) and an **agent harness** (`AgentHarness`: `Codex`, `Pi`, `H1`; place `Mac` (= this PC) or `Cloud`; model choice `Standard`, `Luna` = gpt-6-luna, `Haiku` = claude-haiku-5-5). **Default harness is H1 on this PC** (`DefaultHarness = 2`, adopted once via `ClickyAgentHarnessH1DefaultAdopted.v1`; server can override). Pi = "pi-coding-agent 0.87.1". Thread ids get a harness prefix (`harness-…`).
- Codex spawn: env `CODEX_HOME`, PATH prefixed with `codex-path`, `OPENAI_API_KEY` = backend session token, `CLICKY_WORKER_BASE_URL`, optional `OBSIDIAN_VAULT_PATH`, `CLICKY_BROWSER_MCP_TOKEN`, custom-connector env; `GEMINI_API_KEY` removed (I). Assigned to a kill-on-close Job Object. Restarted when idle if MCP table changes or the session token rotates; kept while turns are active. (`CodexAppServerProcess::Start`, `PrepareRuntimeLockedAsync`, `KillOnCloseJob`) O
- Harness runner: `clicky-harness.exe` (Bun-compiled), loopback `127.0.0.1:<port>` with bearer token; env `CLICKY_HARNESS_TOKEN`, `CLICKY_HARNESS_STATE`, `CLICKY_H1_STATE_DIR`; dev override `HEYCLICKY_AGENT_HARNESS_RUNNER` / Node + `runner.mjs` (nvm/fnm/Volta discovery); 20 s start timeout; start-failure cooldown. HTTP API: `POST /turn`, `GET /events?job=…&after=…&wait=20000` (long-poll event stream with `seq`), `POST /steer`, `POST /cancel`, `POST /prewarm`, `GET /health`. Turn body fields include `instructions, model, workspace, skills, mcp[{name,url,token,headers}], note, planTool, deviceHelper, sessionToken, harness, place, leaseMetadata, images`. Events: `commentary, thinking, plan, tool (start/end), web_search, answer (delta), note preview/saved, stats, turn_end`. Defaults in bundle: model gpt-6-luna, `maxSteps 120`, wall clock 30 min, bash timeout 300 s, prewarm on. (`AgentHarnessRunnerProcess`, `AgentHarnessRunnerRuntime`, `AgentHarnessEvent+*`, bundle.js) O

### Codex JSON-RPC used (O, `CodexSession`)
- Client → server: `initialize` (clientInfo name `ClickyApp`, title `Clicky`, version `1.0.0`, capability `experimentalApi`), `initialized`, `account/login/start` (type apiKey), `thread/start` (cwd, model, approvalPolicy never, sandbox danger-full-access), `thread/resume` (`excludeTurns`), `thread/list` (limit, cursor, cwd), `thread/read`, `thread/archive`, `turn/start` (threadId, input, cwd, model, effort, approvalPolicy, sandboxPolicy dangerFullAccess, `responsesapiClientMetadata`), `turn/steer` (expectedTurnId), `turn/interrupt`, `config/mcpServer/reload`, `mcpServerStatus/list`, `mcpServer/oauth/login`.
- Notifications handled: `turn/started`, `turn/completed`, `turn/plan/updated`, `thread/tokenUsage/updated`, `thread/closed`, `item/started`, `item/completed`, `item/agentMessage/delta`, `item/reasoning/summaryTextDelta`, `item/commandExecution/outputDelta`, `item/fileChange/outputDelta`, `item/fileChange/patchUpdated`, `mcpServer/startupStatus/updated`, `mcpServer/oauthLogin/completed`, `error` (with `willRetry`).
- Server requests auto-answered: `item/commandExecution/requestApproval`, `item/fileChange/requestApproval` → accept; `item/permissions/requestApproval` → grant for scope `turn`.
- Turn outcomes: `Completed, Failed, Interrupted, ExtraEffortRequired, Disconnected`. Waits ≤ 20 s for awaited MCP servers (composio) before a turn (2 s grace if unreported).

### config.toml written into CODEX_HOME (O, `BuildConfigToml`)
`model`, `model_reasoning_effort`, `model_provider = "clicky"` (`[model_providers.clicky]` name/base_url/env_key/wire_api/http_headers → their backend), `approval_policy = "never"`, `sandbox_mode = "danger-full-access"`, `personality = "friendly"`, credential stores, `log_dir`, `sqlite_home`, history persistence, `developer_instructions`, `model_catalog_json` (bundled catalog), `include_collaboration_mode_instructions = false`, `model_auto_compact_token_limit`; `[projects.<path>] trust_level`; `[notice] hide_full_access_warning`; `[features] apps = true, fast_mode = false, goals = false, multi_agent = true`; `[tools.update_plan] enabled = true`; `[mcp_servers.composio]` (url, bearer env var, headers, startup 20 s, tool timeout 120 s); loopback browser MCP (startup 20 s, tool timeout 900 s); custom connectors; `[[skills.config]] path, enabled`.

### Defaults / concurrency / compaction / effort (O, `AgentRuntimeOptions::.ctor`)
Model `gpt-6-luna` (env `CLICKY_LOCAL_CODEX_MODEL`), reasoning effort `high` (env `CLICKY_LOCAL_CODEX_REASONING_EFFORT`, validated by `ValidEffort`), auto-compact token limit 160 000, max concurrent tasks 5, session-token refresh margin 30 min (I: order of constants), routines enabled. Per-task slot (`AgentTask.HoldsSlot`). Folders: `codex-home`, `agents`, runtime dirs `CodexRuntime`, `HarnessRuntime`, `BundledResources`.

### Models offered (`BundledResources\codex-model-catalog.json`, O)
| slug | display | visibility | default effort | efforts |
|---|---|---|---|---|
| gpt-6-luna | GPT-6-Luna | list | medium | low–max |
| gpt-6-sol | GPT-6-Sol | list | medium | low–ultra |
| gpt-6.1-sol | GPT-6.1-Sol | list | medium | low–ultra |
| gpt-6-astra | GPT-6-Astra | list | low | low–ultra |
| gpt-5.6-sol | GPT-5.6-Sol | list | low | low–ultra (code-mode only, fast/ultrafast tiers) |
| gpt-5.6-terra | GPT-5.6-Terra | list | medium | low–ultra |
| gpt-5.6-luna | GPT-5.6-Luna | list | medium | low–max |
| gpt-5.5 | GPT-5.5 | list | medium | low–xhigh |
| gpt-5.2 | GPT-5.2 | list | medium | low–xhigh |
| gpt-5.4, gpt-5.4-mini, gpt-daybreak-blue-latest, gpt-daybreak-red-latest, codex-auto-review | — | hide | — | — |
Context window 272 000 (max 872 000 for 6.x/5.6). Effort scale: low, medium, high, xhigh, max, ultra.

### App's own loopback MCP server (O, `Core.Browser.LoopbackMcpServer`, `ClickyBrowserBridge::EnsureAgentEndpoint`)
Server name `clicky_browser`, protocol `2025-03-26`, methods `initialize`, `notifications/initialized`, `ping`, `tools/list`, `tools/call`; bearer token via `CLICKY_BROWSER_MCP_TOKEN`. **Two tools only:**
- `browser` — drive the user's Chrome through the extension. `operation` ∈ `status, tabs, open, snapshot, extract, batch, click, fill, press, scroll, wait_for, back, upload, download, close, keep, dialog, screenshot, click_at, hover_at, drag_at, type, javascript`; other params `tabId`, `accept`, `query`, `why`, `save`, `steps[]` (batch of `open/click/fill/press/scroll/wait_for/back/upload`). Read-only ops: status, tabs, snapshot, extract, screenshot, wait_for, keep. Keys allowed for `press`: Enter, Tab, Escape, arrows, Backspace, Delete, PageUp/Down, Home, End. Tab groups get an emoji by topic keywords.
- `mac_step` — ask for a step on the PC that only the screen can do (`step` required, `ask`, `why`) → computer-use request card.
(Other capabilities — routines, notes, memory — are not on this MCP for Codex; they go via reply blocks, the H1 harness's own tools, or the voice brain's tools.)

### H1 harness tools (O, bundle.js)
`read`, `write`, `edit` (path + edits[{oldText,newText}]), `bash`, `powershell`, `grep`, `find`, `ripgrep`, `exec` (code mode: script calls other tools, ≤ 20 000 chars returned), `run_script`, `fetch_url` (url, offset, max_chars), `web_search` (query, purpose find|verify, freshness day|week|month|year, count), `view_image`, `update_plan` (plan[{step,status pending|in_progress|completed}], explanation), `progress` (note), `ask_user` (question), `routines` (see §3), live-note tools `note` (md), `note_add` (after, md), `note_replace` (find, md), `note_remove` (find, why), `note_tick` (item, next), `note_answer` (line, big, aside), device helper tools `device_open` (target), `device_read_file` (path), `device_save_file` (source, destination), `device_list_folder` (path), `device_find_files` (query, folder), plus MCP servers passed per turn (composio, browser).

### Voice-brain agent-control tools (O, `Core.Tools.AgentTools.Names`, `BrainToolCallPolicy`)
`sessions_spawn` (start a job/hand-off), `sessions_send` (message a running job), `session_status`, `sessions_interrupt`, `sessions_yield`, `sessions_answer_request` (approve/decline computer-use or extra usage), `clicky_memory`, `clicky_artifacts`, `open_artifact`, `reveal_artifact`, `open_agent_dashboard`, `skip_onboarding_interview`, `save_drawing`, `show_result`, `routines`. Also local/worker tools: `file_read`, `file_list`, `file_write`, `shell_exec`, `copy_to_clipboard`, `insert_text`, `open_url`, `save_memory`, `play_on_spotify`, `google_calendar_find_events`, `google_calendar_create_event`. Parallel-safe: file_read, file_list, clicky_memory, session_status, clicky_artifacts, google_calendar_find_events. `SpawnRepeatGuard` blocks duplicate spawns (`already_started`).

### Skills shipped (names only, O)
- Bundled: `clicky-browser`, `vercel-deploy`. Hidden bundled: `nano-banana-slides`, `cua-driver` (not present in this build's BundledResources). Blocked Codex system skills: `imagegen`, `openai-docs`, `plugin-creator`, `review-agent`, `skill-creator`, `skill-installer`.
- Integration skills (synced only for connected toolkits): `clickup`, `dropbox`, `gmail`, `googledocs`, `googletasks`, `hubspot`, `jira`, `linear`, `microsoft_teams`, `outlook`, `salesforce`.
- Also `clicky-model-instructions.md` (Codex model instructions; Mac-worded, adapted for Windows by `WindowsInstructionAdapter`) and multi-account rules for connectors.

## 6. Learned how-tos and memory

| Feature | Detail | Evidence | O/I |
|---|---|---|---|
| Capture | Lines from the reply's `LEARNED` block saved as `ClickyLearnedHowTo{Text, LearnedAt, JobId}`; de-duplicated (`SameHowTo`), each ≤ 240 chars, keep ≤ 60. Defaults key `clicky.jobs.learnedHowTos.v1`. | `Jobs.ClickyLearnedHowTos` | O |
| Injection | Prompt section for a new ask = 10 newest + 5 most related (word overlap after removing ~45 stop-words); framed as "may be out of date". Fingerprinted per thread in the standing context ledger. | `PromptSection`, `Choose`, `Words`, `IgnoredWords` | O |
| User memory | Backend profile markdown fetched per user (`Profile/NoProfile/Failed`), cached on disk per user id (`UserMemoryCache`), written to `about-the-user.md` and injected as a user-memory section. Brain tool `save_memory` posts new facts (`SaveMemoryRequest`). | `Backend.UserMemoryCache`, `Core.Worker.UserMemoryResponse/SaveMemoryRequest`, `ClickyHelperFiles.WriteUserProfile` | O |
| Companion notes | Each Clicky's `AGENTS.md` is its long-term memory (agent edits it; app appends). | §1 | O |
| Data sources | `data-sources.json` records which connected sources a thread used (`RecordDataSource/RemoveDataSource`). | `AgentService` | O |
| Turn prompt inputs | `TurnPromptBuilder.Input{AgentLaunchContext, Transcript, WorkspacePath, UserMemoryMarkdown, IsGeneratedTaskBrief, LaunchSource, AgentInstructionsForThreadStartedWithoutThem, IsJob, UserProfileFilePath, LearnedHowTosSection, LiveNoteSection, IsClickyDriveAvailable}`. | `Turns.TurnPromptBuilder+Input` | O |

---

## Open unknowns

1. Exact `RailJobs`/shelf ordering, when `FollowedUpOnLaterDay` is set, and which UI action calls `Archive` (only store-level op seen).
2. Job folder naming (`FolderSlug` vs GUID) and whether job folders live under `<ProjectsRoot>\Jobs\`.
3. `ProactiveCadence` hour lists (static array data not decoded) and the `IsDue` rule.
4. Wake-gate fail-open duration and exact "full wake" heuristics; whether missed interval runs fire once or are skipped.
5. Harness `/turn` request/response schema in full, event `seq` semantics, and how `Cloud` place works (server-side; out of scope).
6. How the voice brain's `routines` arguments map onto `RoutinesTool` (`at`/`days` parsing edge cases, 12/24 h).
7. Server-side behaviour of proactive generation, morning paper research, credit leases (not reproducible; not wanted).
8. `ClickyMainArtifacts` heuristics (marker lists are static sets not read).

## Suggested order for deeper RE (per feature)

1. **Jobs + structured reply** (biggest OpenClicky gap G2): decode `StructuredFinalReply.Parse`, `ClickyWorkReplyParts`, `ClickyAnswer.Parse` IL control flow; then `ClickyWorkFolderFiles.IndexText/RunsBlock` and `CreateJob` path composition.
2. **Routines scheduler** (G3): full IL of `ClickyRoutine.NextSetTime/RegularNextRunAt/RecordOutcome`, `RoutineWakeGate.ShouldHoldForFullWake`, `SettleOverdue`; write property tests from the observed constants.
3. **Companions** (G1): `ClickyStore.InstructionsToCarry` + `StandingContextLedger.NeedsCarrying` (fingerprint algorithm), `MoveThreadIntoClicky`.
4. **Runtime** (G4): `CodexSession.HandleNotification/OnItemCompleted` mapping to activity rows; `AgentService.RunTurnAsync` slot/concurrency logic; harness `/events` loop in `AgentHarnessRunnerRuntime`.
5. **Learned how-tos / memory**: `ClickyLearnedHowTos.Choose` scoring.
6. **Proactive** (G6, lowest — shipped paused): `ProactiveCadenceRules` static arrays, `MorningSuggestionsInvitationPolicy.Decide`, `IntegrationSuggestionRules.Sample`.
