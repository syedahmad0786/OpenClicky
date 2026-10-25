# Sub-project E — Work: jobs, results, live progress, concurrency, steering, your-turn steps, how-tos, routines Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring OpenClicky's background work up to HeyClicky's:
- **Jobs:**
  - up to 80, each with a colour tag and a rail phase (Queued, Working, NeedsYou, Done, Failed, Stopped);
  - a job folder with `output/`, `tmp/` and `note.md`, plus a `WORK.md` index;
  - jobs interrupted by a restart are recovered.
- **Results:**
  - a structured final reply that becomes the job's title, summary, points, stat, files and next actions;
  - a live plan checklist and step phrases while the job works;
  - spoken start/finish lines and an update bubble beside the cursor, quiet during calls, presentations and Do Not Disturb.
- **Control:**
  - up to 5 concurrent jobs;
  - steering and stopping a running job, and following up on a finished one.
- **"Your turn" steps:** a job can hand one step to the user, and the browser address is watched to know when it's done.
- **Learned how-tos:** up to 60, with the newest 10 plus the 5 most related added to job prompts.
- **Routines:**
  - created by voice or proposed by a job, always confirmed by the user;
  - a 30 s scheduler with a 2 h stale limit, a 3-minute hold after wake, a network wait, keep-awake on AC, a retry after 300 s, and a pause after 3 failures in a row.
- **Agent folder:** the agent-folder setting is honoured.

**Architecture:**
- Pure logic in `OpenClicky.Core/Work/` (new folder), unit-tested:
  - colour choice, job cap and states;
  - the `WORK.md` index;
  - the result parser;
  - plan/step mapping;
  - job admission and the desktop lease;
  - announcement decisions;
  - the user-step watcher state;
  - how-to store and chooser;
  - routine scheduler decisions;
  - the routine request parser.
- `WorkStore` (Core, SQLite) gains columns through additive migrations.
- The tray's single-slot task fields become per-run `JobRun` objects in `OpenClicky.App/Work/`, with partial files `TrayApplicationContext.Work*.cs`. Do not grow `TrayApplicationContext.cs`.
- OpenWork stays the job engine (a node + opencode server per run). The native `AgentOrchestrator` stays the engine for ad-hoc desktop tasks.
- The shell gets the plan checklist, a steer/follow-up box, Retry, and job cards.

**Tech Stack:**
- .NET 8 WinForms tray app.
- SQLite through the existing `LocalSqliteStore`.
- The OpenWork/opencode runtime (existing).
- `System.Windows.Automation` (UIA).
- Win32 P/Invoke: `SHQueryUserNotificationState`, `PowerCreateRequest`/`PowerSetRequest`/`PowerClearRequest`.
- React + zod shell.
- **No new NuGet or npm packages.**

**Spec:** [design spec](../specs/2026-10-24-heyclicky-parity-design.md) §E · ledger rows E1–E11 ([feature-ledger.md](../../heyclicky-parity/feature-ledger.md)) · behaviour evidence in [agents-inventory](../../research/heyclicky-1.1.1/agents-inventory.md), [ui-inventory](../../research/heyclicky-1.1.1/ui-inventory.md) and [live-ui-notes](../../research/heyclicky-1.1.1/live-ui-notes.md). A research digest for implementers is in the SDD workspace (`research-digest.md`).

## Current code facts (from the survey, 25 October 2026)

### Store
- `OpenClicky.Core/WorkStore.cs` (449 lines, SQLite `%APPDATA%\OpenClicky\openclicky.db`, tables `companions`/`jobs`/`routines`) holds these records:
  - `Companion(Slug, Name, Role, CreatedAt, IsArchived)`;
  - `WorkJob(Id, CompanionSlug, Title, Prompt, CreatedAt, Status, LastRunAt, Summary)`;
  - `Routine(Id, JobId, RoutineCadence {Daily,Weekly,Interval}, MinuteOfDay, WeekdayMask (bit (int)DayOfWeek), IntervalMinutes, IsPaused, NextRunAt, LastRunAt, LastOutcome, ConsecutiveFailures)`.
- **Job status:** `WorkJobStates` has only `ready`/`running`. Finished jobs store `completed`/`failed`/`stopped`.
- **Folders:** job folders are `%APPDATA%\OpenClicky\companions\<slug>\jobs\<jobId>\` with `output\` and `tmp\`. `AppendRunLog` writes `runs.md` (20,000 characters per entry). There is no colour, `note.md`, `WORK.md` or job cap (`ShellStateBuilder.MaxJobs = 80` caps only the view).
- **Schedules:** `RoutineSchedule` (same file) has `Validate`, `NextAfter`, `IsDue`, `AfterRun`, `Describe`. Constants: `MinIntervalMinutes = 5`, `MaxIntervalMinutes = 10080`, retries +15/+30/+45 min, never auto-pauses.
- **Prompt:** `JobPrompt.Build(companion, notes, job, outputs, followUp?, scheduled)` and `JobPrompt.FirstSentence(reply)`.

### Runs (tray)
- **Start:** `StartJob(jobId, followUp, routineId)` (~:730):
  - refuses when `_taskCts != null` ("A task is already open"), so exactly one task at a time;
  - requires ChatGPT sign-in;
  - goes `StartAgentTask` → `RunManagedTaskAsync` (~:844) → `OpenWorkCompanionRunner.RunAsync(...)` with the job folder as the workspace.
- **Finish:** `FinishJob(work, status, text)` (~:780) stores `FirstSentence(answer)` as the summary and the full text in `runs.md`, and handles the routine's `AfterRun`.
- **Single-slot fields:** `_taskCts`, `_activeJob`, `_taskControl`, `_taskSteering`, `_runningTaskId`, `_activeTaskGoal`, `_jobOutputs`.
- **Routine timer:** a 60 s `_routineTimer` runs `RunDueRoutine` (the first due routine only; it skips when busy or with no network). `KeepAwake(bool)` uses `SetThreadExecutionState`, only for routine runs, with no AC check.

### Engine
- `OpenWorkServerService` has a `_runLock` and polls `GetMessagesAsync` every 250 ms; there is no event stream.
- **Progress:** assistant text deltas become "planning" `AgentProgress` events. The final reply is the last assistant text, used verbatim. There is no structured parsing.
- **Prompt:** `OpenCodeCompanionConfiguration.ToConfigJson()` sets `steps = 40` and one system prompt.
- **Steering:** only through `WindowsDesktopToolHost` (appended to observations). There is no message injection into a running session.
- **Desktop tools:** `DesktopToolSession` serializes its own calls. It has no `routines`, user-step or how-to tools.

### Progress records
- `AgentProgress(Step, Status, Message, Tool?)` and `TaskRunRecord` (via `TaskHistoryStore`, `MaxRuns = 20`).
- The shell's job bullets and live line are the last 4 events of the job's latest run record, which are lost after 20 runs.

### Shell
- **Contract:**
  - `JobCard(Id, Title, JobState {Queued,Working,NeedsYou,Done,Failed,Stopped,New}, JobColour {Red,Blue,Yellow,Green}, Summary, JobStat? Stat, Bullets ≤4, Files ≤6, LiveLine, Plan ≤24, UpdatedAt)`;
  - `PlanStep(Text, Done)`, `JobStat(Value, Label)`, `ShellStatus(Phase, JobsWorking, JobsNeedYou)`;
  - `RoutineView(..., RoutineStatus {Scheduled,Running,Failed,Paused})`;
  - `SettingsAgents(Folder, FolderIsDefault, SpeakAnnouncements, CursorUpdates, Reach)`.
- **Mapping:** `ShellStateBuilder.MapJob` never emits NeedsYou. `Plan` is always `[]` and `Stat` is always null. The colour is an FNV hash of the id (`ShellStateBuilder.Colour`, ~:188).
- **Rendering:**
  - `JobDeck.tsx` shows 3 cards per side and "+N more".
  - `HomeTab.tsx` shows `FeaturedJob` (summary, stat, live line, bullets, files, Stop/Dismiss), but does not render `plan`.
  - `NotchCards.tsx` renders any card; nothing creates `JobUpdate` cards.
- **Intents:**
  - `job.open|stop|dismiss`;
  - `settings.action` `routine.run|pause|resume|delete`, `chooseAgentFolder`, `resetAgentFolder`, `openPrivacySetting`;
  - `settings.patch` `agents.speakAnnouncements` and `agents.cursorUpdates`.
- **Already built:** the Agents page (folder picker, two toggles, "what agents can reach" from the Windows consent store) and the Routines page (Run now, Pause/Resume, Delete, an empty state promising voice creation).
- **Stored but unused:** `AppSettings.AgentFolder`, `SpeakJobAnnouncements` and `CursorJobUpdates`. The comment at `TrayApplicationContext.Settings.cs:361` notes that WorkStore ignores the folder.

### Talk, voice and speech
- **Talk actions:** `TalkActionParser` (Core/Talk) parses inline `[OPEN:]`/`[COPY:]`/`[TYPE:]` tags. Fenced blocks are left for the ag-ui visual parser.
- **TTS:** `ITtsClient { SpeakAsync, Stop }` (one utterance at a time).
- **Overlay:** `IOverlayController.ShowResponding(text)` shows the bubble beside the cursor.
- **Quiet detection:** none anywhere.
- **Tests:**
  - `WorkStoreTests`, `ShellStateBuilderTests`, `SettingsServiceTests`, `OpenWorkServerServiceTests`, `OpenCodeTaskClientTests`, `TaskHistoryStoreTests`;
  - vitest `JobDeck.test.tsx`, `notch.test.tsx`, `workPages.test.tsx`;
  - no test covers `StartJob`/`FinishJob`/`RunDueRoutine`;
  - live gate: `OpenClicky.App.SecretStore.Smoke --openwork-live-gate`.

## Task map

| Task | Title | Ledger | Suggested model |
|---|---|---|---|
| E1 | Job store: colours, cap, states, `WORK.md`, `note.md`, restart recovery | E2 | Sol |
| E2 | Structured job result | E3 | Sol |
| E3 | Live plan checklist and step phrases | E4 | Terra |
| E4 | Per-run job state (refactor, still one at a time) | E5, E11 | Sol (Astra reviews) |
| E5 | Up to 5 concurrent jobs, desktop lease, Talk tasks become jobs | E5, E11 | Sol (Astra reviews) |
| E6 | Steer, follow-up and retry from the shell | E5 | Sol |
| E7 | Announcements, update bubble, job cards, quiet detection | E4, E9 | Terra |
| E8 | Your-turn steps and the address watcher | E6 | Terra |
| E9 | Learned how-tos | E7 | Terra |
| E10 | Routine scheduler rules | E8 | Sol |
| E11 | Routine creation by voice and from job results | E8 | Terra |
| E12 | Agent folder honoured | E9 | Terra |
| E13 | Native acceptance | E2–E9, E11 | Terra |

Order: E1 → E2 → E3 → E4 → E5 → E6 → E7 → E8 → E9 → E10 → E11 → E12 → E13. Each task builds on the store columns and run objects of the ones before it.

## Skip rows (recorded in the ledger with these reasons)

| Item | Reason |
|---|---|
| Rich answer cards (numbers with was/of/better, ranked lists, drafts, ask rows, "quiet") | `stat` + `points` + `next` cover the common result shapes. The full card grammar is a large UI surface; revisit after side-by-side review. |
| Computer-use request block ("one step on your PC") | OpenWork jobs already carry the desktop tools under the user's access mode, with Confirm-each per action and the C12 drive consent. A separate request/approve round trip adds nothing. |
| Extra-usage approval ("needs more agent messages") | OpenClicky has no plans or billing. |
| Engine prewarm (`/prewarm`) and server pooling | Each run starts its own OpenWork server (about 30 s budget). Pooling is a performance change to the engine; revisit if start latency is a complaint. |
| Resuming a finished job's engine session | The server is disposed after each run. A follow-up starts a new run with the previous result, the job's `note.md` and `WORK.md` instead. |
| Clicky roster UI with portraits and a starter cast | Not in spec §E. The classic Work panel keeps creating companions. The starter cast belongs with H (onboarding). |
| Chrome-extension wait states (held for Chrome, needs the extension) | Belong to G (browser). |
| A how-to list/edit UI | Not in the spec. How-tos are capped at 60 and age out; add a Settings list if users ask. |
| Editing routines by voice (update/pause/resume/delete/status) | The Settings Routines list already offers Run now, Pause/Resume and Delete. Creation is by voice (E11). |
| Earcons for launch/done/needs-you | We have no sound set of our own (same reason as C's skip). |

## Global Constraints

- Everything from sub-projects A, S, B, C and D.
- **Clean room:** match behaviour, layout and feel only. Never copy HeyClicky code, prompt text, tag syntax or wording. All copy, prompt text, markers and formats in this plan are our own; use them exactly.
- **Limits:**
  - jobs: up to 80 (trim the oldest finished jobs that have no routine; never trim queued, working or needs-you jobs);
  - concurrent jobs: 5;
  - how-tos: 60 of 240 characters, and a prompt gets the newest 10 plus the 5 most related;
  - user step: watched every 2.5 s, gives up after 6 h;
  - routines: 30 s tick, 2 h stale limit, 3 min hold after wake, retry after 300 s, pause after 3 failures in a row.
- **Colours:** `red`, `blue`, `yellow`, `green`. A new job takes the least-used colour among jobs that are not finished-and-dismissed, with ties broken by the order red, blue, yellow, green.
- **Safety rules (unchanged and binding):**
  - Confirm-each stays the default. Auto and Full need a visible warning. A running job's access snapshot never silently widens, and OpenClicky never enables Full on the user's behalf.
  - **Screen access:** only one run at a time may use the screen and input (the desktop lease, E5). Esc stops the run that holds the lease, plus any native task.
  - **Routines and links need the user:** a routine is never created without the user pressing **Schedule** on a confirm card (it is a standing rule). A model-provided page link is opened only when the user presses **Open page**, and only for `http`/`https`.
  - **Files:** file paths from a job result are accepted only if they resolve inside that job's folder and exist.
  - **Untrusted data:** model output, web content, UIA text and the browser address are untrusted. They are never logged in full, and they never grant permission.
- **What leaves the machine:** unchanged from today; job prompts and screenshots go to the user's model provider as they already do. New prompt content added in E9 (how-tos) stays on that path. The address watcher (E8) reads the browser address locally only and sends nothing.
- **Tests:** pure logic gets xUnit tests in `tests/OpenClicky.Core.Tests`; shell changes get vitest. The fixtures (`tests/fixtures/shell-state.v1.json`, `shell-enums.v1.json`) are updated when the contract changes.
- **Build gate:**
  - `dotnet build OpenClicky.sln -c Release --no-restore` with 0 warnings;
  - `dotnet test tests/OpenClicky.Core.Tests --no-restore` green;
  - `dotnet test tests/OpenClicky.Input.Tests --no-restore` green;
  - `pnpm --dir apps/companion-workspace test` green.
- **Dev environment (C: is nearly full):**
  - set `TEMP`/`TMP=D:\tmp-build`, `NUGET_PACKAGES=D:\tmp-build\nuget-packages` and `NUGET_HTTP_CACHE_PATH=D:\tmp-build\nuget-http`;
  - build with `--no-restore`;
  - never click or type into the user's apps or move their windows;
  - a user-started OpenClicky may be running, so don't kill it;
  - smokes run from their built `.exe`.
- **Commits:** explicit paths only, never `git add -A`. End each message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

- **Two jobs that both need the screen** must never interleave clicks or typing. The second waits, showing "Waiting for the screen", and Stop or Esc on either releases the lease. Tests: `DesktopLeaseTests.SecondHolderWaits`, `ReleasedOnCancel`, `ReleasedOnException`.
- **A hostile or broken result block** must not reach the user. That covers:
  - a file path outside the job folder (`..\..\Windows\…`, an absolute path elsewhere);
  - a `javascript:` or `file:` page link;
  - thousands of points or next actions;
  - malformed JSON;
  - two result blocks.
  The bad parts are dropped or capped, and malformed JSON leaves the reply text intact. Tests: `JobResultTests.RejectsPathsOutsideJobFolder`, `RejectsNonHttpPage`, `CapsLists`, `MalformedJsonKeepsReply`, `LastBlockWins`.
- **Waking after a long sleep with many routines overdue.** Nothing starts for 3 minutes. Then each overdue routine runs once (no burst of missed runs), at most 5 at a time, and the rest queue. Tests: `RoutineSchedulerTests.HoldsThreeMinutesAfterWake`, `MissedRunsRunOnce`, `RespectsConcurrencyCap`.
- **OpenClicky closes or crashes while jobs run.** On the next start those jobs are not stuck "Working". They show Stopped ("Interrupted when OpenClicky closed"), and a pending your-turn step from the last 7 days is restored. Tests: `WorkStoreTests.RecoverInterruptedMarksWorkingAndQueued`, `RecoverKeepsRecentUserStep`.
- **The user edits `WORK.md` by hand.** Text outside the runs block survives every rewrite, and a missing or broken marker pair is repaired by appending a fresh block, not by deleting the user's text. Tests: `WorkIndexTests.PreservesTextOutsideRunsBlock`, `RepairsMissingMarkers`.

---

### Task E1: Job store: colours, cap, states, `WORK.md`, `note.md`, restart recovery

**Files:**
- Create: `apps/windows/OpenClicky.Core/Work/JobColours.cs`, `apps/windows/OpenClicky.Core/Work/WorkIndex.cs`
- Modify: `apps/windows/OpenClicky.Core/WorkStore.cs` (records, migration, cap, recovery), `apps/windows/OpenClicky.Core/Shell/ShellStateBuilder.cs` (`MapJob`, `Colour`), the tray's `StartJob`/`FinishJob` and startup (wherever `_work` is created)
- Test: `tests/OpenClicky.Core.Tests/JobColoursTests.cs`, `WorkIndexTests.cs`, `WorkStoreTests.cs`, `ShellStateBuilderTests.cs`

**Interfaces:**
- Produces:
  - `WorkJobStates` constants: `Ready = "ready"`, `Queued = "queued"`, `Running = "running"`, `NeedsYou = "needs-you"`, `Completed = "completed"`, `Failed = "failed"`, `Stopped = "stopped"`, plus `static bool IsActive(string status)` (queued, running, needs-you).
  - `WorkJob` gains `string Colour` and `string FolderPath` (both required; existing rows are migrated).
  - SQLite migration: an additive `ALTER TABLE jobs ADD COLUMN colour TEXT NOT NULL DEFAULT ''`, `folder TEXT NOT NULL DEFAULT ''`, `result_json TEXT NOT NULL DEFAULT ''`, `user_step_json TEXT NOT NULL DEFAULT ''`, guarded by `PRAGMA table_info`. On open:
    - an empty colour is filled with the colour the old hash gave (so existing cards keep their colour);
    - an empty folder is filled with the old default path.
  - `WorkStore.MaxJobs = 80`. `CreateJob` trims beyond it: delete the oldest jobs whose status is not active and that have no routine (rows only; folders stay on disk, as `DeleteJob` does today).
  - `WorkStore.SetJobState(string jobId, string status, DateTimeOffset at)`, `SetJobResult(string jobId, string resultJson)`, `SetUserStep(string jobId, string? userStepJson)`.
  - `IReadOnlyList<WorkJob> WorkStore.RecoverInterrupted(DateTimeOffset now)`:
    - marks every queued or running job Stopped with summary "Interrupted when OpenClicky closed.";
    - leaves needs-you jobs needs-you if their user step is under 7 days old, otherwise marks them Stopped with the same summary;
    - returns the changed jobs.
  - `static string JobColours.LeastUsed(IEnumerable<string> inUse)`.
  - `static string WorkIndex.Render(string existing, IReadOnlyList<WorkIndexJob> jobs, IReadOnlyList<WorkIndexRun> runs)`, with `record WorkIndexJob(string Title, string RelativeFolder, string Status)` and `record WorkIndexRun(DateTimeOffset At, string Outcome, string Summary)`.
  - `WorkStore.WriteIndex()`, which writes `<root>\WORK.md`.

- [ ] **Step 1: Write failing tests**
  - `JobColoursTests`:
    - an empty set gives `red`;
    - {red} gives `blue`;
    - {red, red, blue, yellow, green} gives `blue`;
    - unknown strings are ignored.
  - `WorkIndexTests`:
    - `RendersJobsNewestFirst`.
    - `RunsBlockBetweenMarkers`: the block sits between `<!-- openclicky:runs -->` and `<!-- /openclicky:runs -->`, one line per run as `yyyy-MM-dd HH:mm · <outcome> · <summary>`, at most 50 lines, newest first. The outcome words are `done`, `failed`, `stopped` and `working`.
    - `PreservesTextOutsideRunsBlock`.
    - `RepairsMissingMarkers`: with no markers, or only an opening marker, the existing text is kept and a fresh block is appended.
    - A summary containing a newline or a marker string is flattened and defused.
  - `WorkStoreTests`:
    - `CreateJobTakesLeastUsedColourAndFolder`.
    - `MigratesOldRowsWithHashColourAndDefaultFolder`: build a database with the old schema in a temp folder.
    - `CapTrimsOldestFinishedWithoutRoutine`: create 81 jobs. The oldest finished job without a routine goes; a job with a routine or an active job is never trimmed.
    - `RecoverInterruptedMarksWorkingAndQueued`.
    - `RecoverKeepsRecentUserStep`: a needs-you step 6 days old is kept; one 8 days old becomes Stopped.
  - `ShellStateBuilderTests.MapsJobStates`, extended: `queued` → Queued, `needs-you` → NeedsYou, and the colour comes from `WorkJob.Colour`.
- [ ] **Step 2: Run them and see them fail.** `dotnet test tests/OpenClicky.Core.Tests --no-restore --filter "JobColoursTests|WorkIndexTests|WorkStoreTests|ShellStateBuilderTests"`.
- [ ] **Step 3: Implement.**
  - **`WORK.md` layout:**
    - first line `# Work`;
    - a blank line, then the job list as `- <title> — <relative folder> — <status word>`, where the status words are `queued`, `working`, `needs you`, `done`, `failed`, `stopped` and `ready`;
    - a blank line, then the runs block.
    - If the file exists, only the job list (between `# Work` and the runs block) and the runs block are regenerated; any other text is kept.
  - **`note.md`:** `JobPrompt.Build` tells the agent its working note is `note.md` in its folder. Before each new run of a job, the tray moves an existing `note.md` to `.openclicky\previous-note.md` (overwriting).
  - **Startup:** the tray calls `RecoverInterrupted` and `WriteIndex`. Every job state change and `FinishJob` calls `WriteIndex` after its store write.
- [ ] **Step 4: Run the tests and see them pass**, plus the full Core suite. Build Release with 0 warnings.
- [ ] **Step 5: Commit** `feat(work): job colours, 80-job cap, needs-you/queued states, WORK.md index, restart recovery`.

### Task E2: Structured job result

**Files:**
- Create: `apps/windows/OpenClicky.Core/Work/JobResult.cs`
- Modify: `apps/windows/OpenClicky.Core/WorkStore.cs` (`JobPrompt.Build` adds the result instructions), `apps/windows/OpenClicky.Providers/OpenCodeCompanionConfiguration.cs` (system prompt), the tray's `FinishJob`, `ShellStateBuilder` (bullets, stat, files, next from the stored result)
- Test: `tests/OpenClicky.Core.Tests/JobResultTests.cs`, `ShellStateBuilderTests.cs`, `OpenCodeCompanionConfigurationTests.cs`

**Interfaces:**
- Consumes: `SetJobResult` and `WorkJob.FolderPath` (E1).
- Produces:
  - `record JobResult(string Reply, string? Title, string? Summary, IReadOnlyList<string> Points, JobStat? Stat, IReadOnlyList<string> Next, IReadOnlyList<string> Files, RoutineRequest? Routine, UserStep? YourTurn, IReadOnlyList<string> Learned)`, with `static JobResult Parse(string reply, string jobFolder)`, `string ToJson()` and `static JobResult? FromJson(string json)`.
  - `record RoutineRequest(string Title, string Task, int? EveryMinutes, int? AtMinuteOfDay, int WeekdayMask)`.
  - `record UserStep(string Step, string? Page, string? DoneWhenUrlContains, DateTimeOffset AskedAt)`.
  - `const string JobResult.FenceTag = "openclicky-result"`.
  - `string JobResult.Instructions`: the prompt text below.

- [ ] **Step 1: Write failing tests** `JobResultTests`:
  - `ParsesAllFields`.
  - `StripsBlockFromReply`.
  - `LastBlockWins`.
  - `MalformedJsonKeepsReply`: the reply is unchanged and every field is empty or null.
  - `CapsLists`, with these caps:
    - title 120;
    - summary 300;
    - points 4 of 200;
    - next 3 of 160;
    - files 6;
    - learned 5 of 240;
    - step 600;
    - page 2048;
    - done-when 200.
  - `RejectsPathsOutsideJobFolder`: `..\x`, an absolute path elsewhere, and a path that does not exist are all dropped. A relative path `output/report.pdf` that exists is resolved to a full path.
  - `RejectsNonHttpPage`: `javascript:`, `file:` and `data:` are dropped, and `https://` is kept.
  - `RoutineCadence`, three cases:
    - `{"every_minutes":60}` gives EveryMinutes 60;
    - `{"at":"08:30","days":["mon","wed"]}` gives AtMinuteOfDay 510 and the Mon/Wed mask (bit `(int)DayOfWeek`);
    - `at` with no `days` means every day (mask 127).
    Invalid input (minutes outside 5–10080, a bad time) drops the routine.
  - `RoundTripsJson`.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
  - **Block format (our own):** a fenced block whose info string is `openclicky-result`, containing one JSON object with optional keys:
    - `title` and `summary`;
    - `points` (string array) and `stat` (`{"value","label"}`);
    - `next` (string array) and `files` (string array of paths relative to the job folder);
    - `routine`: `{"title","task","every_minutes"}` or `{"title","task","at":"HH:MM","days":[...]}`, where days are `sun`…`sat` (case-insensitive);
    - `your_turn`: `{"step","page","done_when_url_contains"}`;
    - `learned` (string array).
  - Unknown keys are ignored.
  - **`Instructions` text (use exactly):**
    > When you finish, end your reply with a fenced block tagged openclicky-result holding one JSON object. Use "title" for a short name of what you made or did, "summary" for one sentence, "points" for up to four key findings, "stat" for one headline number as {"value","label"} if there is one, "next" for up to three suggested next steps, and "files" for the paths of files you saved under output/. If the user must do one step themselves (sign in, approve, pay), add "your_turn" with "step", the "page" to open and "done_when_url_contains" text that appears in the address once they are done. If the user asked for something to repeat, add "routine" with "title", "task" and either "every_minutes" or "at" (HH:MM) plus optional "days". Add "learned" for short, reusable lessons about how to do things on this PC. Leave out anything that does not apply.
  - **`FinishJob`:** parses the reply, then:
    - stores `ToJson()` via `SetJobResult`;
    - stores `Summary ?? FirstSentence(Reply)` as the summary;
    - writes `Reply` (without the block) to `runs.md` and chat.
  - **Hand-offs:** E8 handles `YourTurn`, E9 handles `Learned` and E11 handles `Routine`. In this task they are only stored.
  - **`ShellStateBuilder`:**
    - `Bullets` = result points;
    - `Stat` = result stat;
    - `Files` = result files, falling back to today's output listing;
    - the title shown is the stored job title.
  - **New `JobCard` field `Next` (string list, at most 3):** add it to the C# record, zod and the fixtures, and render it in `FeaturedJob` as plain text lines under the bullets.
- [ ] **Step 4: Run the tests and see them pass**, plus the full Core suite and vitest. Build Release with 0 warnings.
- [ ] **Step 5: Commit** `feat(work): structured job result with safe paths and links`.

### Task E3: Live plan checklist and step phrases

**Files:**
- Create: `apps/windows/OpenClicky.Core/Work/JobProgress.cs`
- Modify: `apps/windows/OpenClicky.Providers/OpenWorkServerService.cs` and/or `OpenCodeTaskClient` (surface tool parts from polled messages), `apps/windows/OpenClicky.App/OpenWorkCompanionRunner.cs` (report them), the tray's progress handling for jobs, `ShellStateBuilder` (plan + live line), `apps/companion-workspace/src/shell/notch/HomeTab.tsx` and `deck/JobCardView.tsx`
- Test: `tests/OpenClicky.Core.Tests/JobProgressTests.cs`, `OpenWorkServerServiceTests.cs`, vitest `notch.test.tsx`, `JobDeck.test.tsx`

**Interfaces:**
- Produces:
  - `static IReadOnlyList<PlanStep> JobProgress.FromTodos(JsonElement todos)`: accepts the opencode `todowrite` tool input; at most 24 steps; a step is Done for status `completed`/`cancelled`.
  - `static string? JobProgress.Phrase(string tool, JsonElement? input)`: a short step phrase.
  - `record JobLiveState(IReadOnlyList<PlanStep> Plan, IReadOnlyList<string> Steps, string? LiveLine)`: keeps the last 12 phrases, and the live line is the newest.
  - `JobLiveState.Apply(...)`.

- [ ] **Step 1: Write failing tests** `JobProgressTests`:
  - `TodosBecomePlanSteps`, including the cap of 24 and statuses.
  - `PhrasesForKnownTools`, one per row in the phrase table in Step 3.
  - `UnknownToolGivesNull`.
  - `KeepsLast12Steps`.
  - vitest:
    - `FeaturedJob` renders the plan as a checklist with the text "2 of 5 steps";
    - `JobCardView` shows a thin progress bar when the plan is not empty.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
  - **Tool parts:** while polling, the engine side emits, for each new tool part, the tool name and input. Verify the part shape against `OpenCodeTaskClient`'s message model and the existing test fixtures, and extend the model if tool parts are dropped today. Map `todowrite` to the plan; every other tool goes to `Phrase`.
  - **Phrase table:**

    | Tool | Phrase |
    |---|---|
    | `openclicky_observe_screen` | Looking at the screen |
    | `openclicky_uia` | Reading the window |
    | `openclicky_click` | Clicking |
    | `openclicky_type_text` | Typing |
    | `openclicky_hotkey` | Pressing keys |
    | `openclicky_scroll` | Scrolling |
    | `openclicky_open_app` | Opening `<app>` |
    | `openclicky_write_output` | Saving `<file name>` |
    | `openclicky_list_outputs` | Checking saved files |
    | `openclicky_connector_call` | Using `<connector>` |
    | `webfetch` | Reading a web page |
    | `websearch` | Searching the web |
    | `bash` | Running a command |
    | `read` | Reading a file |
    | `edit` / `write` | Editing a file |
    | `grep` / `glob` / `list` | Looking through files |

    Placeholders take the input's app, file or connector name, truncated to 40 characters. Match on the tool name's suffix if the engine prefixes it differently.
  - **Shell:** while a job works, the shell shows `Plan`, `LiveLine` = the newest phrase, and `Bullets` = the last 4 phrases. When it finishes, E2's result bullets replace them.
  - **Storage:** live state is kept per run in memory (E4 makes it per job), and the last plan is stored in the job's `result_json` on finish so the checklist survives.
- [ ] **Step 4: Run the tests and see them pass**, plus the full Core suite and vitest. Build Release with 0 warnings.
- [ ] **Step 5: Commit** `feat(work): live plan checklist and step phrases for jobs`.

### Task E4: Per-run job state (refactor, still one at a time)

**Files:**
- Create: `apps/windows/OpenClicky.App/Work/JobRun.cs`, `apps/windows/OpenClicky.App/Work/TrayApplicationContext.Work.cs`
- Modify: `apps/windows/OpenClicky.App/TrayApplicationContext.cs`: move `StartJob`, `FinishJob`, `RunJobAsync`, `RunDueRoutine`, `KeepAwake`, the job half of `RunManagedTaskAsync`, and the job-related `IShellHost` members into the partial.
- Test: `tests/OpenClicky.Core.Tests/` (any Core helper extracted); SecretStore smoke `--openwork-live-gate` unchanged; manual check listed in E13

**Interfaces:**
- Produces:
  - `sealed class JobRun` (App), one per running job, holding:
    - `WorkJob Job`, `string Ask`, `string? RoutineId`, `CancellationTokenSource Cts`, `AgentTaskControl Control`;
    - `string? Steering` and `int SteeringRevision`;
    - `JobLiveState Live`;
    - `IReadOnlyList<string> Outputs`, `DateTimeOffset StartedAt`, `AgentAccessMode Access` (the snapshot).
  - `IReadOnlyDictionary<string, JobRun> _jobRuns` (keyed by job id) replaces `_activeJob`/`_jobOutputs` for jobs.
  - `IShellHost.StopJob(id)` stops that job's run wherever it is.

- [ ] **Step 1:** List every read and write of `_taskCts`, `_activeJob`, `_taskControl`, `_taskSteering`, `_steeringRevision`, `_runningTaskId`, `_activeTaskGoal` and `_jobOutputs`. Put the list in the report.
- [ ] **Step 2: Implement.**
  - **Job runs:** each OpenWork job run gets a `JobRun`. Everything that reads the single fields for a job now reads that run.
  - **Native tasks:** ad-hoc native tasks (Talk "do X" on `AgentOrchestrator`) keep the existing single-slot fields.
  - **Admission:** still one job or task at a time. The admission check is now "no native task and no job run". The behaviour must be identical to today.
  - **Access mode:** snapshot it once per `JobRun` at start; settings changes never widen a running job. `OnSettingsApplied` still cancels runs whose snapshot is wider than the new mode, as today for tasks.
  - **Live state:** progress events for a job go into `JobRun.Live` (E3) and `TaskRunRecord` as today.
- [ ] **Step 3: Verify.**
  - Build Release with 0 warnings; run the Core and Input suites.
  - Run `OpenClicky.App.SecretStore.Smoke --openwork-live-gate` only if the report shows it runs offline or with the existing test credentials. Otherwise, say so and skip it; never sign in.
  - Run the Agent.Smoke modes that exist for tasks.
- [ ] **Step 4: Commit** `refactor(work): per-job run state`.

### Task E5: Up to 5 concurrent jobs, desktop lease, Talk tasks become jobs

**Files:**
- Create: `apps/windows/OpenClicky.Core/Work/JobAdmission.cs`, `apps/windows/OpenClicky.Core/Work/DesktopLease.cs`
- Modify: `apps/windows/OpenClicky.Providers/OpenWorkServerService.cs` (the `_runLock` becomes per instance, and each run uses its own instance or port), `DesktopToolSession.cs` (desktop tools take the lease), `apps/windows/OpenClicky.App/Work/TrayApplicationContext.Work.cs`, the Talk-to-task path, Esc handling, `ShellStateBuilder` (queued live line)
- Test: `tests/OpenClicky.Core.Tests/JobAdmissionTests.cs`, `DesktopLeaseTests.cs`, `OpenWorkServerServiceTests.cs`

**Interfaces:**
- Consumes: `JobRun` (E4) and the E1 states.
- Produces:
  - `sealed class JobAdmission(int max = 5)` with `bool TryStart(string jobId)`, `void Enqueue(string jobId)`, `string? Finish(string jobId)` (returns the next queued id to start), `IReadOnlyList<string> Queued` and `int Running`.
  - `sealed class DesktopLease` with `Task<IDisposable> AcquireAsync(string holder, CancellationToken ct)`, `string? Holder` and `event Action? Changed`.
  - `const string DesktopLease.NativeTaskHolder = "native-task"`.

- [ ] **Step 1: Write failing tests**
  - `JobAdmissionTests`:
    - `StartsUpToFive`;
    - `SixthQueues`;
    - `FinishStartsNextInOrder`;
    - `SameJobNeverTwice`: starting or queueing a job that is running or queued is refused.
  - `DesktopLeaseTests`:
    - `SecondHolderWaits`;
    - `ReleasedOnCancel`: a cancelled waiter does not take it, and the holder's dispose releases it;
    - `ReleasedOnException`;
    - `HolderReported`.
  - `OpenWorkServerServiceTests`: two services run concurrently without sharing the lock (use the existing fakes).
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
  - **Admission:**
    - `StartJob` asks `JobAdmission`. When full, the job is set to Queued (E1) with the live line "Waiting for a free slot". `FinishJob` starts the next queued job.
    - Routines (E10) and the user go through the same admission.
    - A native task still refuses when any native task is open, but it no longer refuses because jobs are running.
  - **Desktop lease:**
    - Every desktop tool (`observe_screen`, `uia`, `list_apps`, `open_app`, `click`, `type_text`, `hotkey`, `scroll`, `wait`) acquires the lease for the duration of the call, with the job id as holder.
    - While a job waits for it, the live line is "Waiting for the screen".
    - A native `AgentOrchestrator` task holds the lease for its whole run (holder `native-task`).
    - The C12 drive consent card, Esc pill and take-back belong to the lease holder only.
    - Esc stops the current lease holder (job or native task), plus a talk turn as today.
  - **Talk tasks become jobs:** a Talk request that today starts an OpenWork ad-hoc task now creates a job under a built-in companion and runs it as a job, so it appears in the deck. The companion has slug `openclicky`, name "OpenClicky" and role "General helper"; create it on first use. The job title is the first 60 characters of the request; when the result arrives, E2's `title` replaces it. Native desktop tasks stay tasks.
  - **Concurrent engine:** each concurrent job gets its own `OpenWorkServerService` instance (its own port, node process and lock). Check the runtime's port selection and keep the existing per-app Windows Job Object so every child is killed with the app.
- [ ] **Step 4: Run the tests and see them pass**, plus the full Core and Input suites. Build Release with 0 warnings.
- [ ] **Step 5: Commit** `feat(work): up to five concurrent jobs with a desktop lease`.

### Task E6: Steer, follow-up and retry from the shell

**Files:**
- Modify: `apps/windows/OpenClicky.Core/Shell/ShellContract.cs` (intents), `ShellController.cs`, `IShellHost.cs`, `apps/windows/OpenClicky.Providers/OpenCodeTaskClient.cs` / `OpenWorkServerService.cs` (send a message into a running session), `apps/windows/OpenClicky.App/Work/TrayApplicationContext.Work.cs`, `apps/companion-workspace/src/shell/contract.ts` and `notch/HomeTab.tsx`
- Test: `tests/OpenClicky.Core.Tests/ShellControllerTests.cs`, `ShellContractTests.cs`, `OpenCodeTaskClientTests.cs`, vitest `notch.test.tsx`

**Interfaces:**
- Produces:
  - intents `job.steer {id, text}` (text 1–2000 characters), `job.followUp {id, text}` and `job.retry {id}`;
  - `IShellHost.SteerJob(string id, string text)`, `FollowUpJob(string id, string text)` and `RetryJob(string id)`;
  - `OpenWorkServerService.SendToRunningAsync(string text, CancellationToken ct)`.

- [ ] **Step 1: Write failing tests**
  - The intent parser accepts the three intents with valid ids and text lengths, and rejects empty or too-long text.
  - `ShellController` routes each to the host.
  - `OpenCodeTaskClientTests`: sending to a running session posts a user message to that session; use the client's existing HTTP fake.
  - vitest:
    - `FeaturedJob` shows a text box with placeholder "Tell this job something…" while Working or Queued; Enter posts `job.steer`.
    - When Done, the placeholder is "Ask a follow-up…" and Enter posts `job.followUp`.
    - Failed/Stopped jobs show a **Try again** button that posts `job.retry`.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
  - **Steer:**
    - Steer sends a user message into the job's running opencode session through the session message API, prefixed "Update from the user: ". Check the endpoint in the opencode client and the runtime: prefer the asynchronous prompt route if it exists, else the regular message route.
    - It is also recorded as `JobRun.Steering` so desktop observations still carry it.
    - If the job is Queued, the text is appended to its Ask before start.
  - **Follow-up and retry:**
    - Follow-up starts a new run with the existing `followUp` argument (through admission).
    - Retry starts a new run with the job's last Ask.
  - **Native steering:** the voice "change task" command steers the current lease holder.
- [ ] **Step 4: Run the tests and see them pass**, plus the full Core suite and vitest. Build Release with 0 warnings.
- [ ] **Step 5: Commit** `feat(work): steer running jobs, follow up and retry from the shell`.

### Task E7: Announcements, update bubble, job cards, quiet detection

**Files:**
- Create: `apps/windows/OpenClicky.Core/Work/JobAnnouncer.cs`, `apps/windows/OpenClicky.App/Work/QuietDetector.cs`
- Modify: `apps/windows/OpenClicky.Core/IOverlayController.cs` (`ShowUpdate`), `apps/windows/OpenClicky.Overlay/BuddyOverlayController.cs`, `apps/windows/OpenClicky.App/Work/TrayApplicationContext.Work.cs`, `TalkFeedback` card routes
- Test: `tests/OpenClicky.Core.Tests/JobAnnouncerTests.cs`, `QuietStateTests.cs`

**Interfaces:**
- Produces:
  - `enum JobEvent { Started, Done, NeedsYou, Failed, Stopped }`.
  - `record QuietState(bool Quiet, string? Reason)`.
  - `record Announcement(string? Speak, string? Bubble, NotchCard? Card)`.
  - `static Announcement JobAnnouncer.For(JobEvent e, string title, string jobId, bool speakSetting, bool bubbleSetting, QuietState quiet, bool talkBusy)`.
  - `static QuietState QuietState.From(int notificationState, bool micInUseByOtherApp)`.
  - `void IOverlayController.ShowUpdate(string text)`, with a no-op default.
  - Card ids `job-update:<jobId>` with action `open`.

- [ ] **Step 1: Write failing tests**
  - `JobAnnouncerTests`:
    - **Speech copy:**
      - Started → "On it: `<title>`.";
      - Done → "`<title>` is ready.";
      - NeedsYou → "`<title>` needs you.";
      - Failed → "`<title>` didn't finish.";
      - Stopped → nothing.
    - **Bubble:** the same text when `bubbleSetting`.
    - **Card:** Done, NeedsYou and Failed get a card:
      - Kind `JobUpdate`, or `NeedsYou` for NeedsYou;
      - Title = the job title;
      - Body = "Ready to view", "Needs you" or "Didn't finish";
      - action Open (id `open`);
      - AutoDismissMs 10000, or none for NeedsYou.
    - **Muting:**
      - when quiet, there is no speech and no bubble, but the card stays;
      - when `talkBusy`, there is no speech (and the bubble still shows);
      - with the settings off, there is no speech or bubble respectively.
    - Titles are cut to 80 characters.
  - `QuietStateTests`:
    - `notificationState` 2 (busy), 3 (full-screen D3D), 4 (presentation) or 6 (quiet time) → quiet, with reasons "Busy", "Full screen", "Presenting" and "Quiet hours";
    - mic in use → quiet with reason "On a call";
    - 5 (accepts notifications) and no mic → not quiet.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
  - **Quiet detection:**
    - `QuietDetector` (App) calls `SHQueryUserNotificationState`.
    - It reads `HKCU\Software\Microsoft\Windows\CapabilityAccessManager\ConsentStore\microphone`: any `NonPackaged` subkey other than OpenClicky's own executable, or any packaged app key, with `LastUsedTimeStart` > `LastUsedTimeStop` means "mic in use by another app".
    - It returns a `QuietState`.
  - **Overlay:** `BuddyOverlayController.ShowUpdate` shows the message bubble for 4 s, then fades. It is skipped while a talk turn shows its own bubble.
  - **Tray:**
    - announce on job start, finish, fail and needs-you, using `SpeakJobAnnouncements`/`CursorJobUpdates`;
    - speak through `ITtsClient` only when TTS is idle;
    - the Open card action opens the job (`job.open` behaviour).
- [ ] **Step 4: Run the tests and see them pass**, plus the full Core suite. Build Release with 0 warnings. Run the Overlay.Smoke if `ShowUpdate` touched shared overlay code.
- [ ] **Step 5: Commit** `feat(work): spoken job lines, cursor updates and job cards, quiet during calls and presentations`.

### Task E8: Your-turn steps and the address watcher

**Files:**
- Create: `apps/windows/OpenClicky.Core/Work/UserStepWatch.cs`, `apps/windows/OpenClicky.Agent/BrowserAddressReader.cs`
- Modify: `apps/windows/OpenClicky.Agent/DictationContextReader.cs` (reuse the address-bar walk through `BrowserAddressReader`), `apps/windows/OpenClicky.App/Work/TrayApplicationContext.Work.cs`, `TalkFeedback` card routes, `apps/windows/OpenClicky.Core/Work/` card factory
- Test: `tests/OpenClicky.Core.Tests/UserStepWatchTests.cs`, `JobCardsTests.cs`

**Interfaces:**
- Consumes: `JobResult.YourTurn` (E2) and `SetUserStep` (E1).
- Produces:
  - `sealed class UserStepWatch(UserStep step, DateTimeOffset startedAt)` with `UserStepStatus Check(DateTimeOffset now, string? currentAddress)`, where `enum UserStepStatus { Waiting, Done, TimedOut }`.
  - `static readonly TimeSpan UserStepWatch.Poll = 2.5 s` and `Limit = 6 h`.
  - `static string? BrowserAddressReader.Read(TimeSpan budget)`: the foreground window's address-bar text, for the browser processes in D3's list, else null.
  - `JobCards.YourTurn(WorkJob job, UserStep step)`: Id `your-turn:<jobId>`, Kind `NeedsYou`, Title "Your turn: `<title>`", Body = step, actions **Open page** (id `open-page`, only when `Page` is set) and **I'm done** (id `done`), no auto-dismiss.

- [ ] **Step 1: Write failing tests**
  - `UserStepWatchTests`:
    - `DoneWhenAddressContainsText`: case-insensitive.
    - `WaitingOtherwise`.
    - `TimedOutAfterSixHours`.
    - `NoDoneTextNeverAutoCompletes`: with no `DoneWhenUrlContains`, only **I'm done** completes the step.
    - `NullAddressIsWaiting`.
  - `JobCardsTests`: copy and actions exactly as above; with no page there is no Open page action.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
  - **On a `YourTurn` result:**
    - the job goes to NeedsYou;
    - the step is stored via `SetUserStep`;
    - the card is shown;
    - E7 announces NeedsYou;
    - a watch timer polls every 2.5 s off the UI thread with a 300 ms read budget.
  - **Done** (address match or **I'm done**):
    - clear the step;
    - remove the card;
    - start a follow-up run (through admission) with the follow-up text "The user finished this step: `<step>`. Carry on with the job."
  - **Timed out:** the job goes to Stopped with summary "Waited six hours for your step; stopped."
  - **Open page:** opens the URL with the shell, `http`/`https` only.
  - **Restart:** steps recovered by E1's `RecoverInterrupted` restart their watch, with the time limit counted from `AskedAt`.
  - **Privacy:** the address is never logged or stored.
- [ ] **Step 4: Run the tests and see them pass**, plus the full Core suite. Build Release with 0 warnings. Re-run Agent.Smoke `--dictation` (shared address code).
- [ ] **Step 5: Commit** `feat(work): your-turn steps with an address watcher`.

### Task E9: Learned how-tos

**Files:**
- Create: `apps/windows/OpenClicky.Core/Work/HowTos.cs`
- Modify: `apps/windows/OpenClicky.Core/WorkStore.cs` (`JobPrompt.Build` takes the chosen how-tos), the tray's `FinishJob` (store `Learned`) and `StartJob` (choose)
- Test: `tests/OpenClicky.Core.Tests/HowTosTests.cs`, `WorkStoreTests.JobPrompt_*`

**Interfaces:**
- Consumes: `JobResult.Learned` (E2).
- Produces:
  - `record HowTo(string Text, DateTimeOffset LearnedAt, string JobId)`.
  - `sealed class HowToStore(string path)` with `IReadOnlyList<HowTo> All`, `void Add(string text, string jobId, DateTimeOffset at)` and `void Save()`. It is a JSON file at `%APPDATA%\OpenClicky\howtos.json` (under the agent root once E12 lands).
  - `static IReadOnlyList<HowTo> HowToChooser.Choose(IReadOnlyList<HowTo> all, string query, int newest = 10, int related = 5)`.

- [ ] **Step 1: Write failing tests** `HowTosTests`:
  - `AddDedupesCaseInsensitive`.
  - `CapsAt240Chars`.
  - `KeepsSixtyNewest`.
  - `ChooseNewestTenPlusFiveRelated`: related means the most shared non-stop words with the query, ties broken by newer first, and items already in the newest 10 are excluded.
  - `StopWordsIgnored`.
  - `EmptyQueryGivesNewestOnly`.
  - `CorruptFileStartsEmpty`.
  - `JobPrompt` includes the section "Things learned on earlier jobs (may be out of date):" with one bullet per chosen how-to, and no section when there are none.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
  - **Stop words:** a list of about 45 common English words, our own list: a, an, the, and, or, but, to, of, in, on, at, for, with, from, by, is, are, was, were, be, it, this, that, these, those, i, you, my, your, me, we, our, do, does, did, how, what, when, where, which, who, can, should, will, just, then.
  - **Tokens:** lower-case, letters and digits only, length ≥ 2.
  - **Query:** the job's title, prompt and follow-up.
- [ ] **Step 4: Run the tests and see them pass**, plus the full Core suite. Build Release with 0 warnings.
- [ ] **Step 5: Commit** `feat(work): learned how-tos fed into job prompts`.

### Task E10: Routine scheduler rules

**Files:**
- Create: `apps/windows/OpenClicky.Core/Work/RoutineScheduler.cs`, `apps/windows/OpenClicky.App/Work/PowerKeepAwake.cs`
- Modify: `apps/windows/OpenClicky.Core/WorkStore.cs` (`RoutineSchedule` constants and `AfterRun`), `apps/windows/OpenClicky.App/Work/TrayApplicationContext.Work.cs` (30 s timer, wake hold, network wait, stale runs, keep-awake), `ShellContract.cs` `RoutineStatus` (add `Waiting`), `SettingsService.RoutineViews`, `contract.ts`, `RoutinesPage.tsx`, fixtures
- Test: `tests/OpenClicky.Core.Tests/RoutineSchedulerTests.cs`, `WorkStoreTests.AfterRun_*`, `SettingsServiceTests`, vitest `workPages.test.tsx`

**Interfaces:**
- Produces:
  - `record RoutineTick(DateTimeOffset Now, bool NetworkUp, DateTimeOffset? HoldUntil, IReadOnlySet<string> ActiveJobIds)`.
  - `record RoutineDecision(IReadOnlyList<Routine> Start, IReadOnlyList<Routine> WaitingForNetwork)`.
  - `static RoutineDecision RoutineScheduler.Decide(IReadOnlyList<Routine> routines, RoutineTick tick)`.
  - `RoutineScheduler.Tick = 30 s`, `WakeHold = 3 min`, `StaleAfter = 2 h`, `RetryAfter = 300 s` and `PauseAfterFailures = 3`.
  - `RoutineSchedule.AfterRun` is rewritten:
    - success → the next slot, failures reset to 0;
    - failure → `NextRunAt = finishedAt + 300 s` and failures + 1;
    - at the 3rd failure in a row → `IsPaused = true`, `LastOutcome = "Paused after 3 failed runs"`;
    - a missed run still runs once and never bursts.

- [ ] **Step 1: Write failing tests**
  - `RoutineSchedulerTests`:
    - `HoldsThreeMinutesAfterWake`: nothing starts before `HoldUntil`.
    - `MissedRunsRunOnce`.
    - `SkipsRoutineWhoseJobIsActive`.
    - `NetworkDownReportsWaiting`.
    - `PausedNeverStarts`.
    - `RespectsConcurrencyCap`: the decision may return more than 5; admission queues the rest, so test that the tray passes them all through `JobAdmission`. Unit-test the ordering (oldest `NextRunAt` first).
  - `WorkStoreTests`: `AfterRun_RetriesAfter300s`, `AfterRun_PausesAfterThreeFailures`, `AfterRun_SuccessResetsFailures`.
  - `SettingsServiceTests`: status mapping:
    - paused → Paused, with the last outcome as text;
    - running → Running;
    - waiting for network → Waiting ("Waiting for the internet");
    - failures > 0 → Failed;
    - else Scheduled.
  - vitest: the Waiting badge renders.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
  - **Timer:** 30 s, re-entrancy guarded.
  - **Wake hold:** `SystemEvents.PowerModeChanged` with Resume sets `HoldUntil = now + 3 min`.
  - **Stale runs:** each tick cancels any routine-started job run older than 2 h, which counts as a failed run with outcome "Took longer than 2 hours".
  - **Keep-awake:**
    - `PowerKeepAwake` holds a `PowerCreateRequest` + `PowerSetRequest(PowerRequestSystemRequired)` while at least one unpaused routine exists and the PC is on AC power (`SystemInformation.PowerStatus.PowerLineStatus == Online`);
    - it is re-evaluated each tick and on `PowerModeChanged` StatusChange, and cleared otherwise;
    - this replaces the per-run `SetThreadExecutionState`.
  - **Routines page:** the empty-state text stays. Add the footer note "Routines run while your PC is awake and OpenClicky is open."
- [ ] **Step 4: Run the tests and see them pass**, plus the full Core suite and vitest. Build Release with 0 warnings.
- [ ] **Step 5: Commit** `feat(work): routine scheduler — 30 s tick, wake hold, network wait, retry, auto-pause, keep-awake on AC`.

### Task E11: Routine creation by voice and from job results

**Files:**
- Create: `apps/windows/OpenClicky.Core/Work/RoutineRequests.cs`
- Modify: the Talk system prompt and reply handling (where `TalkActionParser` runs, in `TalkOrchestrator`; make sure the block is never spoken or shown), `apps/windows/OpenClicky.App/Work/TrayApplicationContext.Work.cs`, `TalkFeedback` card routes
- Test: `tests/OpenClicky.Core.Tests/RoutineRequestsTests.cs`, `TalkOrchestrator`/`StreamingReplySpeech` tests for not speaking the block

**Interfaces:**
- Consumes: `RoutineRequest` and its JSON shape (E2), `RoutineSchedule.Describe`, `WorkStore.AddRoutine`/`CreateJob`, and the built-in `openclicky` companion (E5).
- Produces:
  - `const string RoutineRequests.FenceTag = "openclicky-routine"`.
  - `static (string Text, RoutineRequest? Request) RoutineRequests.Extract(string reply)`.
  - `static NotchCard RoutineRequests.ConfirmCard(string requestId, RoutineRequest r)`:
    - Id `routine-confirm:<requestId>`, Kind `NeedsYou`;
    - Title "Schedule this?";
    - Body "`<title>` · `<cadence words>`", then a new line and the task (cut to 200 characters);
    - actions **Schedule** (id `schedule`) and **Not now** (id `dismiss`);
    - no auto-dismiss.
  - `const string RoutineRequests.TalkInstructions` (text below).

- [ ] **Step 1: Write failing tests**
  - `RoutineRequestsTests`:
    - `ExtractsFencedBlockAndStripsIt`.
    - `InvalidBlockIsStrippedButIgnored`.
    - `ConfirmCardCopy`, using `Describe`'s wording for "every day at 08:30", "every 60 minutes" and weekday sets.
    - `OnlyOneRequestPerReply`.
  - Talk: a streaming reply containing the block never speaks or shows any of it (the existing `StreamingReplySpeech`/clean helpers).
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
  - **`TalkInstructions` (use exactly; append to the Talk system prompt):**
    > If the user asks you to do something on a schedule (for example "every weekday at 8:30" or "every two hours"), do not do it now. Reply briefly that you can set it up, and add a fenced block tagged openclicky-routine holding one JSON object with "title", "task" (what to do each time, written as an instruction) and either "every_minutes" or "at" (HH:MM, 24-hour) with optional "days" (sun, mon, tue, wed, thu, fri, sat). The user confirms before anything is scheduled.
  - **Flow:** a request (from Talk or from a job's `routine` result) shows `ConfirmCard`.
  - **Schedule:**
    - for a job result, add the routine to that job;
    - for Talk, create a job under the `openclicky` companion with title = request title and prompt = task, then add the routine;
    - show the toast "Scheduled: `<title>` — `<cadence words>`.";
    - push the Settings Routines list.
  - **Not now:** discard the request.
  - Requests are held in memory only; a restart drops unconfirmed ones.
- [ ] **Step 4: Run the tests and see them pass**, plus the full Core suite. Build Release with 0 warnings.
- [ ] **Step 5: Commit** `feat(work): schedule routines by voice or from a job, always confirmed`.

### Task E12: Agent folder honoured

**Files:**
- Modify: `apps/windows/OpenClicky.Core/WorkStore.cs` (root from settings for new companions and jobs; `WriteIndex` and `howtos.json` at the root), the tray's `_work` creation and `OnSettingsApplied`, `Settings/TrayApplicationContext.Settings.cs` (remove the stale comment at ~:361)
- Test: `tests/OpenClicky.Core.Tests/WorkStoreTests.cs`

**Interfaces:**
- Consumes: `WorkJob.FolderPath` (E1).
- Produces: `WorkStore.SetRoot(string? agentFolder)`. Null or empty means the default `%APPDATA%\OpenClicky`. The database stays at `%APPDATA%\OpenClicky\openclicky.db`.

- [ ] **Step 1: Write failing tests**
  - `NewJobsUseAgentFolder`: after `SetRoot(temp)`, `CreateJob` makes `<temp>\companions\<slug>\jobs\<id>\` and stores that `FolderPath`.
  - `OldJobsKeepTheirFolder`.
  - `IndexWrittenAtCurrentRoot`.
  - `ResetToDefault`.
  - `UnwritableFolderFallsBackToDefault`: the store reports it, and the tray shows a toast.
- [ ] **Step 2: Run them and see them fail.**
- [ ] **Step 3: Implement.**
  - The tray calls `SetRoot(_settings.AgentFolder)` at start and when the setting changes.
  - Companion `AGENTS.md` notes for an existing companion are read from where they already are. A new companion is seeded under the current root.
  - Nothing is moved on disk.
- [ ] **Step 4: Run the tests and see them pass**, plus the full Core suite. Build Release with 0 warnings.
- [ ] **Step 5: Commit** `feat(work): new jobs live in the chosen agent folder`.

### Task E13: Native acceptance

- [ ] **Step 1: Build gate.**
  - `dotnet build OpenClicky.sln -c Release --no-restore` gives 0 warnings.
  - Core, Input and vitest suites are green.
  - The existing smokes still pass (Overlay, Agent `--dictation`/`--snap`/`--harvest`/`--integrity`, SecretStore dictation modes).
- [ ] **Step 2: Automated native checks (no sign-in, nothing outside the sandbox).**
  - **Store check:** with a temp store, create 3 jobs, record runs with a structured result reply (fake), then render `WORK.md`. Inspect the real file: the runs block, the job list, and text outside the block that survives.
  - **Shell check:** using the shell-state builder, a job with a plan, points, stat and next renders through the Surface.Smoke shell page if it supports state injection. If it doesn't, a vitest snapshot of the real state JSON stands in, and the report says which.
- [ ] **Step 3: User-only checks.** These need sign-in and real jobs, so put them on the user's list:
  1. Say "research the three best note-taking apps and save a comparison". A job card appears with a live checklist. When it finishes, the card shows the summary, points and the saved file; Open works.
  2. Start two more jobs while the first runs. All three run, and a job that needs the screen waits with "Waiting for the screen".
  3. Type into the job's box while it works: the job takes the update. After it's done, ask a follow-up; Try again on a stopped job works.
  4. Ask for a job that needs a sign-in. The "Your turn" card appears; Open page opens the site; signing in (or I'm done) continues the job.
  5. Say "every weekday at 8:30, summarise my unread email": the Schedule this? card appears; Schedule adds it to Settings → Routines; Run now works.
  6. During a call (mic in use) or a presentation, a job finishing makes no sound and no cursor bubble, but the card appears.
  7. Quit OpenClicky while a job runs, start it again: the job shows Stopped ("Interrupted when OpenClicky closed").
- [ ] **Step 4:** Update ledger rows E2–E9 and E11 (→ Built, with evidence paths), add the Skip rows from this plan with their reasons (use IDs that don't collide with task names, for example E-S1…), mark E1 and E10 as covered by the Skips/P, and update `docs/feature-plan.md`.

---

## Self-review notes

- **Spec coverage (§E):**
  - Jobs up to 80, colour tags and rail phases, folders with `output/`, `tmp/`, `note.md` and the `WORK.md` index → E1.
  - Structured final reply → E2.
  - Live plan and progress → E3.
  - Spoken start/finish, the update bubble, and quiet during calls/screen share/DND → E7. Screen share is approximated by presentation mode, full-screen D3D and mic-in-use; the research found no other signal.
  - Steer and interrupt → E6 (interrupt = Stop).
  - Up to 5 concurrent jobs on OpenWork → E4 + E5.
  - The "your turn" watcher → E8.
  - How-tos (60; newest 10 + 5 related) → E9.
  - Routines created by voice through a routines tool → E11, as a confirmed request rather than an autonomous tool, because a routine is a standing rule.
  - Settings list Run now/Pause/Delete → already built; scheduler rules → E10.
  - Agent settings: the folder → E12; what agents can reach → already built.
- **Type names used across tasks:**
  - `WorkJobStates`, `WorkJob.Colour/FolderPath` (E1);
  - `JobResult`/`RoutineRequest`/`UserStep` (E2), used by E8, E9 and E11;
  - `JobLiveState`/`PlanStep` (E3), used by E4;
  - `JobRun` (E4), used by E5–E8 and E10;
  - `JobAdmission`/`DesktopLease` (E5), used by E6, E8, E10 and E11;
  - `JobAnnouncer`/`QuietState` (E7), used by E8;
  - `HowToStore` (E9);
  - `RoutineScheduler` (E10);
  - `RoutineRequests` (E11).
- **Proportion:** the plan pins copy, formats, limits and decision rules; bodies are left to the implementers.
