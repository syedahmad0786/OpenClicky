# HeyClicky 1.1.1 (Windows): UI surface inventory (clean-room)

Target: `%LOCALAPPDATA%\HeyClickyApp\current`, .NET 9 / WinUI 3. All findings come from static reads. Nothing was launched and no server was contacted. Labels quoted below are short control captions; longer copy is paraphrased.

## 0. Method and evidence sources

| Source | What it gave | Evidence |
|---|---|---|
| `makepri dump /dt detailed` on HeyClicky.pri and HeyClicky.Drawing.pri (written via the 8.3 short path, because the long path broke makepri with 0x80070003) | 384 named resources. The only compiled XAML is **`Files/App.xbf`**, which only merges `XamlControlsResources`. Everything else is assets (logos, videos, voice previews, sounds, fonts, JS draw engine, browser extension) | `rea-ui\pri.xml`, `pri-drawing.xml` |
| REA `inspect_managed_members` (HeyClicky.dll) | Failed again: "Managed metadata exceeds the 67108864 byte representation budget" | `ev_260ad7d22d9150dd3c5e9d9528587691ebad90db4635f55ffa3a538876818d38` (parent `ev_5538d30d…`) |
| System.Reflection.Metadata from PowerShell (no assembly load) | Type, field, method and property names, base types, the #US heap, and per-type/method `ldstr` literals from an IL byte scan. Also const field values and `.cctor` literal stores | `rea-ui\*.types.txt`, `*.members.txt`, `*.us.txt`, `*.ts.txt`; scripts `dumpmeta.ps1`, `typestrings.ps1`, `cctorconsts.ps1` |

**Key structural finding (observed):** the UI is built entirely in C# code with no XAML pages. Controls subclass `Grid`/`Panel`/`StackPanel`, plus a custom `Pressable` base. Composition visuals do the drawing, and there are many separate top-level HWND windows. The settings pages are methods on one class, `HeyClicky.App.Settings.SettingsContent` (`AccountPage`, `GeneralPage`, …).

Confidence tags: **[O]** = observed directly in metadata or strings. **[I]** = inferred from names and strings.

---

## 1. Surfaces: windows, panels, overlays, menus

### 1.1 Top-level windows / HWNDs
| Surface | Evidence (type / window title literal) | Notes |
|---|---|---|
| **Notch** (main surface, top-centre of the screen; mimics a laptop notch) | `Notch.NotchView` title "HeyClicky Notch"; `Notch.NotchGeometry`, `NotchSilhouette`, `NotchSystemShape`, `NotchCornerGlow`, `NotchWorkingSweep` [O] | Collapsed it is a small pill or sliver. On hover it expands into the "peek", which has tabs. It follows the display under the cursor (`FollowCursorDisplay`) and keeps itself on top (`KeepOnTop`). Files can be dropped on it ("file(s) dropped on the notch"). It has a "stream mode" that draws it 1.5x as big [O] |
| **Settings window** | `Settings.SettingsWindow` title "HeyClicky Settings", which hosts `SettingsContent` [O] | The same `SettingsContent` also renders inside the notch Settings tab (`UseNotchGrid`, `Notch.Peek.NotchSettingsTab`) [O] |
| **Pre-sign-in intro window** | `Onboarding.IntroWindow` title "HeyClicky" [O] | Welcome, discovery source, colour pick, hatch, Google sign-in (see §4) |
| **Tutorial / tour window** | `Onboarding.TutorialWindow` title "HeyClicky tour"; PanelWidth 500, InterviewPanelWidth 760, StageHeight 416/440, chrome 156 [O] | A step card with progress dots, Back/Continue, "Skip demo" and a music toggle (♫) |
| **Paywall window** | `Billing.PaywallWindow : Shell.OverlayWindow` title "HeyClicky"; `Billing.PaywallCard` [O] | Plan picker |
| **Agents dock ("Arc")** | `Dock.ArcDockView` titles "HeyClicky Agents Arc" / "HeyClicky Agents"; `Dock.ArcDock`, `ArcCardView`, `DeckMoreButton` [O] | Job/agent cards fanned along the right screen edge ("on the right edge of their screen"), with paper-note styling [O/I] |
| **Arc voice pill** | `Notch.Peek.ArcVoicePill` "HeyClicky Arc Voice" [O] | Floating pill while voice is active |
| **Cursor overlay** (companion cursor) | `Platform.Overlay.OverlayHost` "HeyClicky Overlay", `CursorOverlay`, `CursorClusterRenderer`, `CursorMessageBubble`, `ThinkingDots`; `Onboarding.CursorCompanion` "HeyClicky Cursor"; `DetachedClickCatcher` "HeyClicky detached cursor" [O] | See §5.4 |
| **Screen highlight / drawing overlay** | `Platform.Overlay.HighlightOverlay`/`HighlightLayer`; `Wiring.DeferredHighlightOverlay` [O] | Draw on the screen while holding push-to-talk |
| **Hatch flight overlay** | `Onboarding.HatchFlightOverlay` "HeyClicky Flight" [O] | Intro animation, where the mascot flies to the notch |
| **Dock launch flight** | `Dock.DockLaunchFlight` "HeyClicky Launch Flight" [O] | Card flies to the dock when a job starts |
| **Take-over (drive) overlay + consent card** | `Drive.TakeOverOverlay`, `Drive.TakeOverConsentCard` "HeyClicky drive" [O] | Shown while the app controls the mouse and keyboard |
| **Drawing viewer window** | `Notch.Peek.ClickyDrawingWindowViewer` "HeyClicky drawing", `ClickyDrawingWindowView` [O] | Replays a saved drawing; can record it to a video |
| **Board / lesson backdrop** | `Home.LessonService.ShowBackdrop` "HeyClicky board" [O] | White-board replay of a drawing |
| **Save-drawing offer** | `Home.DrawingSaveOffer` "HeyClicky save drawing" [O] | Small panel under the notch with Save and Clear |
| **Print** | `Home.LessonPrinter` "HeyClicky print" [O] | Prints a drawing |
| **Tour job cards** | `OneBrain.ClickyTourJobCards` "HeyClicky tour card" [O] | |
| **One-brain stats HUD** (developer) | `Shell.OneBrainStatsPanel` "HeyClicky one brain stats" [O] | Top-left token stats |
| **Chrome folder strip** | `Browser.ChromeFolderStrip` "HeyClicky for …" [O] | Drag-the-extension helper strip for the manual extension install |
| Hidden helpers | `Home.WebPageSnapshotter` "HeyClicky page preview" (WebView2), `Services.MainThreadStallMonitor` [O] | Not user-facing |

Window plumbing: `Shell.OverlayWindow : Window` (no-activate, topmost, layered, click-through `ignoresMouseEvents`, capture exclusion `excludeFromCapture`/`ShowInScreenRecordings`). `Shell.PanelWindow : OverlayWindow` is a spring-animated resizable panel with ShadowMargin = 32. `Shell.TransparentBackdrop : SystemBackdrop`. `Shell.TaskbarButton` is a separate Win32 window that provides the optional taskbar presence [O].

### 1.2 Tray icon menu (order as in `Shell.TrayIcon.ShowMenu`) [O]
"Open Settings" · "Pause HeyClicky" / "Resume HeyClicky" (toggle) · "Report a Bug…" · "Request a Feature…" · "What's New" · "Check for Updates…" · "Quit HeyClicky". The tooltip comes from `Core.Notch.TrayTooltip` and shows "Signed out" when signed out [O].

### 1.3 Notch peek tabs (`Core.Notch.NotchTab`) [O]
Order: **Home · Chat · Settings · Document**. Document is a transient viewer tab that returns to the previous tab (`tabBeforeDocument`). The switcher is a capsule control, `Notch.Peek.NotchTabSwitcher` (buttons, icons, capsule, planSlot). A "plan slot" sits in the switcher, which is likely the Upgrade/Update pill [I].

- **Home tab** (`Notch.Peek.NotchHomeTab`): time-of-day greeting with the name (`NotchGreeting`, `TopArcGreetingLayout`). Status line showing "N jobs working / N need you", or a hint to hold the talk keys and give it work. "Connected apps" / "Connect apps" pill with app logos. "Invite & Earn" referral button. A home-dock slot that hosts the agent cards [O].
- **Chat tab** (`Notch.Peek.NotchChatView`, `NotchChatComposer`, `ChatMessageRow`, `ChatTextRow`, `ChatHandOffRow`, `ChatDrawingRow`, `ChatSentAttachments`): the conversation history.
  - Composer: placeholder "Ask HeyClicky…", an attach button capped at N files, drag-drop "Drop to attach", paste of images/files, and a voice hint ("Hold X to talk").
  - Rows: date separators; text rows with "Copy the text" and "Typed it in"; hand-off rows ("Started background job …", "is done", "couldn't finish", "needs a yes", "Routine run ·"); drawing rows ("Drew …", "Remove from chat"); action captions such as "Looked at your screen", "Searched the web" or "Started a background job" (`Core.Notch.ChatActionCaption`).
  - Controls: "Earlier messages" loader and a "Jump to the newest message" button.
  - Full-reply view (`NotchFullReply`): "Back to the chat (esc)", follow-up pills ("Send …"), and a footer to hold the keys to follow up.
  - "Read aloud" switch (`ReadAloudSwitch`).
  - Empty state: "Say hi to HeyClicky" plus a line explaining that everything asked lives here. Error state: "Couldn't load your conversation" + "Try again" [O].
- **Settings tab**: the `SettingsContent` grid (see §3) [O].
- **Document tab** (`NotchDocumentViewer`, `NotchDocumentHeaderRow`, `NotchDocumentRenderers`): header with "Open in its app", "Show in Explorer" and "Close (esc)".
  - Renderers: Markdown (`Documents.MarkdownViewer*`, "Copy markdown", "Select All"), spreadsheet (`SpreadsheetGridView`/`SpreadsheetPreview` with a Sum/Average/Count status), PDF (shows "N more pages · Open to read the rest"), and plain text.
  - States: "Loading document…", "This document couldn't be loaded.", "No preview for this … yet." [O]

### 1.4 Notch "surfaces" (transient cards in the notch, all `: Notch.NotchSurface`) [O]
| Surface type | Purpose / visible controls |
|---|---|
| `ActivitySurface` | Strip with phases (`ActivityPhase`): Listening, Thinking, Speaking, Custom, AgentMulti ("Agent working", "N Clickys working"), Docked ("HeyClicky is docked"), Dictation. Animated bars, dots or dashes (`ActivityIndicators`) |
| `TextInputSurface` | Text-mode input with placeholder "ask HeyClicky…". A follow-up variant reads "follow up with agent…" / "Following up with …" |
| `TextResponseSurface` | Streamed answer with "Copy response", "Dismiss now", "Pause auto-dismiss" (countdown chrome) and "ask a follow-up…" |
| `ComposerBox` | Multi-line composer: placeholder "Message HeyClicky", attach, removable attachment chips, drop zone, attachment-cap notice; max 8 visible lines, corner radius 14 |
| `ClickyUpdateSurface` / `AgentTaskSurface` / `HandoffSurface` | Job finished / needs you / didn't finish cards: "Full report", "Open …", routine badge; hand-off "Region queued", "Pick text or voice to send" |
| `OnboardingCardSurface` | Signed-out placeholder (with "Sign in with Google"), permission cards (Step n of m), "Meet HeyClicky", "All set." Its chrome row holds "Check for Updates…", "Log Out" and "Quit HeyClicky" |
| `TutorialInviteSurface` | Invites the user to the quick tour: "Show me", "Start the quick tour", "Quick tour in progress" |
| `MorningSuggestionsSurface` | "Your morning paper is ready", "Read it", "Not now", "I can do N things for you today", "Show me" |
| `IntegrationSuggestionSurface` / `IntegrationConnectedSurface` | "Connect X to HeyClicky?", "Use HeyClicky to:" with use-case carousel (`UseCaseCarousel`), "Never ask again for this integration", "Not now"; "X connected", "Try one of these next" |
| `MeetingCountdownSurface` | "Starting in m:ss / Starting now / Started …", join logos (Meet/Zoom/Teams), "Get directions" |
| `MutedSpeakerSurface` | "Your PC is muted", "Your volume is at zero", "Set volume to 50%", reply copied to clipboard |
| `MicrophoneTroubleSurface` | "HeyClicky can't hear your microphone" + "Mic settings" |
| `DictationClipboardSurface` | "Copied to your clipboard" / "Press Ctrl+V to paste" |
| `DictionaryLearnedSurface` | "Added to your dictionary" + remove-word button |
| `DocumentAccessSurface` | "HeyClicky can't read this document", "Give permission" (opens the file's Security settings or ms-settings privacy-documents), "Don't show again", "Dismiss for now" |
| `AppUpdatedSurface` | "HeyClicky updated", "You're on v…", "See what's new" |
| `AppTipSurface` | Skill/app tips ("Activate Skill"; a Spotify voice tip) with "Got it" |
| `SkillPinnedSurface`, `IntroSkillPingSurface` | Skill pinned/ping toasts |
| `RestingMarkSurface` / `ComputerMark` | Idle notch mark showing working count and alert phase |
| `Notch.Peek.NotchPeekSurface` | The expanded tabbed peek (§1.3) |

### 1.5 Agent dock cards (`Dock.*`) [O]
- **Card states** (`ArcCardState`): Queued, Working, NeedsYou, Failed, Stopped, New, Done, Idle, Pinning, Suggested.
- **Card parts**: a title bar with a red close ("Remove from the dock" / "Stop and remove from the dock"); a plan checklist (`PlanRows`, `PlanStepRow`, `PlanTickBox`, `ChatPlanFold`); a live line (`LiveLineView`, `BreathingText`, `ShimmerSweep`, `TypingDots`); a progress bar ("n of m steps"); and a stop button ("Stop this task", which keeps the window so the task can be retried).
- **Files and output**: file chips/cards (`FileCard`, `FileCardStack`, "Show in File Explorer"); paper stamp and hand-off chips; and the ready-output button ("Open link", "Open PR", "Open PDF/page/sheet/doc/slides/image", "Play video/audio", "Open folder").
- **Footer requests** (`CardFooterRequest`, `FooterRequestRow`): "One step on your PC" → "Let HeyClicky drive" / "Not now"; "Needs more agent messages" → "Always approve" / "Approve once"; "Needs the Chrome extension" → "Install for me" / "I'll install it"; "Your turn" → "I'm done"; "Didn't finish".
- **Context menu**: "Show in chat", "Full report". Next-step buttons: "Type an answer", "Copy again", "Run it now".
- **Empty states**: "No work yet", "Nothing running…" (with a hint to hold the voice keys).
- **Deck navigation**: "Show the windows on the right/left", "+N more". Relative times read "just now", "N minutes ago".

### 1.6 Billing / paywall (`Billing.PaywallCard`) [O]
- Header "Choose your HeyClicky plan", a monthly/yearly interval segment, and an eyebrow line that varies by state (limit hit, payment failed, already on Max).
- Plan cards **Free ($0)**, **Pro** and **Max**, with a feature row per plan:
  - Free: 25 talk messages a month, "plenty of dictation", 25 agent messages a month, and "Use HeyClicky for free".
  - Pro / Max: unlimited talk, unlimited dictation, N agent messages per month. Pro is pitched at everyday use, Max at power users.
- Referral discount banner, a section for an exhausted allowance ("Your agent messages refresh each month"), and an error row.
- Intro video `paywall-intro-v2.mp4` (`HasShownPaywallIntroVideo`).
- Checkout opens in the browser. Kaomoji cursor lines confirm the result. The customer portal opens in the browser.
- The palette follows Windows light or dark mode (`PaywallPalette.Current` reads `…\Themes\Personalize`; `IsDark`, Accent, CardSurface, BenefitSurface, …).

### 1.7 Browser extension surfaces (bundled, `ClickyBrowserExtension/`) [O]
MV3 extension "HeyClicky" v0.6.3. It has a toolbar popup (`popup.html`, which shows a status plus "Show HeyClicky's cursor" and "Privacy") and a `welcome.html` page. The local `ClickyBrowserSetup.html` page reads "HeyClicky needs Chrome" and lists Chrome, Brave, Edge and Arc with a "Get Chrome" link. A native host, `ClickyBrowserHost.exe`, ships with it.

---

## 2. Main-screen behaviour notes
- **Notch geometry** (`NotchGeometry` consts) [O]: CollapsedWidth 60, CollapsedHeight 8, FloatingSliverTopGap 4, ExpandedWidth 532 (onboarding 440), MaxExpandedHeight 280, StripHeight 32, TopCornerRadius 10, ContentHorizontalPadding 18, ActionButtonSize 24, AgentFaceBadgeSize 26, HardwareNotchTopInset 30, Envelope 820×≤900.
- **Home sheet** (`HomeTheme`) [O]: SheetCornerRadius 26, DetachedCornerRadius 12, TopFlareRadius 10, MinWidth 720 × MinHeight 520, AttachedScale 0.7, Open 0.43 s, Close 0.30 s, ContentFade 0.18 s, hover forgiveness 28 px with a 0.15 s grace, and a shadow (opacity 0.18, radius 6, y 3). This implies a Home "sheet" that grows out of the notch and can detach into a window [I].
- **Chat layout** (`ChatRowLayout`) [O]: IconSize 22, ContentLeadingInset 30, ReplyBubbleMax 420, MessageMax 390, card 96 wide with radius 6.
- **Library mode** (`LibraryModeEnabled`, `LibraryModeControlBandHeight` 30) [O]: purpose unknown.
- **Read-aloud**: chat answers are spoken only when the switch is on (`ReadAloudSwitch`, `TypedTurnsRespondWithVoice`) [O/I].

---

## 3. Settings (`SettingsContent`, sidebar built in `SyncSidebar`) [O]

**Sidebar groups, in order**: (account/avatar row) **Account**; **General**; **Invite & Earn**; group header **HEYCLICKY**: **Voice**, **Microphone**, **Dictation**, **Shortcuts**, **Cursor**; group **WORK**: **Integrations**, **Agents**, **Routines**; group **INTERNAL** (only if `ShowsDeveloperSection`): **Developer**. The enum `Core.Notch.NotchSettingsSection` confirms the same 12 sections. A page that fails to build shows "This page couldn't load." The building blocks come from `SK`: Scaffold, Group, MicroLabel, Divider, Row, ToggleRow, ActionRow, Switch, Chip, PillButton, MenuPicker, SearchField, KeyCaps, Tile, Alert.

| Page | Sections and controls |
|---|---|
| **Account** | Signed out: "Sign in with Google" and "You're not signed in". Signed in: **Plan** group (plan label Free/Pro/Max/Canceled, usage meter, plan actions, a placeholder when the servers can't be reached) → **Account** group: "Log out", "Delete account" (permanent-erase description) and "Quit HeyClicky". **Account deletion flow**, a multi-step overlay: a reason picker (Not useful for me / Too buggy / Too expensive / Privacy concerns / Found something better / Other) plus optional feedback, then typing DELETE, then "Delete forever" ("Deleting…"), then a "Done" screen. Error cases: sign-in expired, network dropped, nothing deleted. Local data is moved to the Recycle Bin |
| **General** | **Behavior**: Show in taskbar, Show in screen recordings, Launch at login (HKCU Run with `--background`), Pause HeyClicky. **Community**: WhatsApp community and Reddit links (with logos). **Welcome tour**: "Replay the welcome tour". **Support**: "Request a feature" (external link), "Report a bug" (sends diagnostics). **Updates**: "What's new" (changelog link) plus an app-update row (title, subtitle, button: "Check for updates" → "Checking…" → "Downloading… N%" → "Install & Relaunch"; "You're up to date"; error and non-installed-copy notes) |
| **Invite & Earn** (referral) | Skeleton while loading, error "Try again", and a paused state. Sections: earn pitch, held-code/link card ("Copy link"/"Link copied", "Copy code", "Pick your own link" editor with validation), **Your earnings** (Friends joined, Paying you, Last 30 days, Total paid out, a monthly bar chart `MoneyBarChart`, balance pill, negative-balance note), **What you could make** estimator (`ReferralEstimatorCard`, `GelSlider`), **People you've invited** list (example rows when empty, show more/less), **Getting paid** ("Request payout"/"Email us" mailto, terms link), **Were you invited by a friend?** (friend code + "Apply") |
| **Voice** | Voice picker of 10 voices, each a tile with a name, a short descriptor and an icon: Cedar ("Warm default"), Marin, Alloy, Ash, Ballad, Coral, Echo, Sage, Shimmer, Verse. Tapping a tile plays a bundled mp3 preview. **Speech speed**: 0.5x, 0.75x, 1x, 1.25x, 1.5x. **Captions**: "Captions while HeyClicky talks" (word-by-word at the bottom of the screen). **Output device** group: device list (System Default / Disconnected / Unavailable) plus "Play a test sound" |
| **Microphone** | **Input device** list (System Default, disconnected/unavailable states) and "Test microphone" with level bars. States: "Listening...", "Receiving audio", and "HeyClicky can't hear you. Try different microphone." The list refreshes on audio topology changes |
| **Dictation** | **Language** grid with search ("Search languages"; about 50 languages with native names, and a popular-languages ordering). "Auto-detect language" toggle (code `multi`). **Dictionary**: "Add a word" + "Add", word chips with remove, up to 50 words |
| **Shortcuts** | **Talk to HeyClicky**: Push to talk, Text mode. **Dictation**: Dictate, Hands-free dictation. Each row shows key caps and a "Change" button. The recorder shows "Press your keys…" with Save/Cancel, and a conflict error ("Already used by …"). "Reset to defaults" |
| **Cursor** | **Color**: Red / Blue / Yellow / Green (sets cursor **and** app accent). **Visibility**: "Hide floating cursor" (docks it). **Screen highlight**: draw on screen while holding push-to-talk. **Shake to detach**: shake the mouse to leave the cursor in place, then click it to talk |
| **Integrations** | Search field, "Add custom connector", loading shimmer ("Loading integrations"). Sections: Connected / Available integrations / Search results. Empty states: "Sign in to connect integrations.", "No integrations available yet.", "No more integrations to connect.", "Nothing matches …". Each row has a logo, a status chip (Connected, Partial, Reconnect required, Unavailable, Error, Not connected) and actions (Connect / Configure / Disconnect / Reconnect / Refresh / Change, with a "Keep" confirm). **Multi-account card**: "Connect another X account, like a work and a personal one", account labels (Work, Personal, Other…), a per-app account cap, and handling for expired accounts. A disconnected row animates out ("Disconnected"). A **Browser card** offers Chrome/Brave/Edge/Arc connection with profile picker, status badge (Waiting / Connected / Working), "Install for me" / "I'll install it" / "Stop" / "Disconnect". **Custom connector editor** (MCP): Name; Type (Remote URL or Command on this PC); Server URL or Command + Arguments; Authentication (Sign in with browser / API key / None); Advanced (OAuth client ID, extra headers, environment variables kept encrypted); validation messages; Cancel / "Add connector" / "Save changes". Rows show status (Needs sign-in, Checking, Signing in, Connected, Sign-in failed, Needs a token, Token rejected) with Edit and Remove |
| **Agents** | **Workspace**: Agent folder picker ("HeyClicky default", reset to default). **Autonomy**: "Auto-approve extra usage". **Announcements**: "Speak when a job starts or finishes"; "Show updates beside the cursor". **What agents can reach**: rows reflecting the Windows privacy consent store (Files & Folders, Documents, Pictures, Contacts, Calendar, Microphone, Camera, Location) with Allowed/Denied state; tapping a row opens the matching `ms-settings:privacy-*` page. Empty state: "Nothing granted yet". **New agent features**: "Agent harness" picker (H1 on this PC / Codex) and "Preview HeyClicky drive" |
| **Routines** | List of recurring routines with a status line (Next / Ran / Running now / Last run failed / Paused after N failed runs), cadence words ("Every week"), and "Run now", Pause/Resume, Delete ("Delete it? Its past runs stay in the chat."). There is no add button: the user creates routines by voice, and a voice hint shows an example request. A note says routines run only while the PC is awake and HeyClicky is open |
| **Developer** (internal) | Drawing on screen (Drawing sounds, Pen sounds, JavaScript JIT) · One brain (model picker over a GPT/Claude/DeepSeek list, "reads apps", "Keep screens in history", "Show one brain stats", "Start a fresh session") · Debug toggles (Show agent names, Preview app update, Force plan state) · Debug actions (Preview onboarding tutorial, Preview paywall, Reset paywall video, Show the intro, Dry-run intro reel cues, Reset onboarding, Reset intro, Open logs folder, Relaunch HeyClicky) · Moments/Previews (Upcoming meetings, Remote Tasks, Fetch/Reset suggestions) |

Persisted settings model (`Core.Settings.AppSettings` properties, saved as `settings.json`) [O]:
- Cursor and voice: CursorColorHex (default `#3380FF`), SquadMemberId, VoiceIdentifier, SpeechSpeed, ScreenHighlightEnabled, CursorDocked, ShakeToDetachEnabled.
- App behaviour: LaunchAtLogin, ShowInTaskbar, ShowInScreenRecordings, TypedTurnsRespondWithVoice, StreamMode.
- Dictation: DictationLanguageCode, DictationSpecificLanguageCode, DictationCustomWords.
- Devices: MicrophoneDeviceId/Name, OutputDeviceId.
- Agents: AgentAnnouncementsVoiceEnabled, AgentCursorTextAnnouncementsEnabled, AgentFolderPath, AgentAutoApproveExtraUsage, ShowAgentLabelsInActivityStrip.
- Drawing: DrawingSoundsEnabled, DrawingPenSoundsEnabled, DrawingVoiceCaptions, DrawingSubtitles, KeepDrawingsInChat, SaveDrawings, DrawingJavaScriptJit.
- Integrations and documents: ObsidianVaultPath, DocumentAccessNotice*.
- One brain: OneBrainModel, OneBrainReadsApps, OneBrainKeepsScreensInHistory, ShowOneBrainStats.
- Proactive features: MorningPaperEnabled, ProactiveAgentSuggestionsEnabled, UpcomingMeetingsEnabled, RemoteTasksEnabled, LiveNotesEnabled.
- Other: Onboarding (progress record), Hotkeys/EffectiveHotkeys, HasShownPaywallIntroVideo.

There is no theme setting. Appearance follows Windows light or dark mode [I].

---

## 4. Onboarding / first run

**A. Pre-sign-in intro** (`Core.Onboarding.PreSignInIntroStep`, `Onboarding.IntroWindow`) [O]. Steps in order: WhatIsClicky → WhereClickyLives → ClickyGetsLost → WhereHeardAbout → OpenClicky → Hatched → SkillSelection → ReadyToStart.
- Content: a welcome headline and tagline; a "How'd you find this?" chip grid (Instagram Ad, YouTube Ad, TikTok, X/Twitter, LinkedIn, Reddit, ChatGPT, Claude, Google, A friend, Other with a free-text box); and "Pick a color" (the cursor colour, which hatches the mascot).
- Other beats: a "Sometimes HeyClicky gets lost" lesson and "What should HeyClicky be good at?" (skill selection).
- Footer: "Back" / "Continue" / "Give me a home" / "Sign in with Google".
- Squad packs (`OnboardingCatalog`): Puff Pals, Ki Crew, Arcade All-Stars, Cloud Club, Dreamy Cloud, Storybook Spirits.
- The intro video is `onboarding-intro-v2-windows.mp4` (bundled or hosted). A double Escape skips it.

**B. Notch onboarding cards** (`NotchOnboardingCardKind`: SignedOut → PermissionIntro → Permission → MeetClicky → Ready) [O].
- Permission steps, shown as "Step n of m":
  1. **Microphone**: "Grant Microphone", then the Windows prompt; recovery copy for disabled or organization-blocked access; "Quit & Reopen HeyClicky".
  2. **Screen capture**: "Grant Screen Capture", then pick a display in the system picker. Copy stresses that the screen is captured only on hotkey.
- Then "Meet HeyClicky / I live in your notch now", then "All set." with "Let's start".

**C. Hands-on tutorial** (`OnboardingTutorialStep`, `TutorialWindow`) [O]. Order: MicCheck → SpeakerCheck → VoiceHello → ClickyInterview → DrawDemo → DrawToAsk → ClickyDrive → TextMode → EmailDraft → Dictation → Finale.
- MicCheck: "First, can I hear you?" with a live meter and mic picker.
- SpeakerCheck: shown only when the volume is 0; offers "Set volume to 50%".
- VoiceHello: "This is how you talk to me" (hold the keys and say your name). Status line cycles listening… / thinking… / speaking… / waiting for you….
- ClickyInterview: "I'm curious about you", four spoken or typed questions (`InterviewStage`: "Or type here", "Hold X to answer"). It then builds a starter plan of personalised agents.
- DrawDemo: "Here's how I'd help you", where the app draws on the real screen.
- DrawToAsk: "Circle anything, ask about it" (demo video `clicky-spatial-demo.mp4`).
- ClickyDrive: "I can drive your computer too"; a demo list is ticked off by the app with Esc to take control back (`OnboardingDriveDemo`).
- TextMode: "Not in the mood to talk? Type" (double-tap Ctrl).
- EmailDraft and Dictation: step names only.
- Finale: "There's one more thing…" → "Show me", which reveals the Home in the notch (`HomeTour`) and introduces the starter Clickys (`StarterCast`: Research Buddy, Web builder, plus generated ones).
- Chrome: progress dots ("Go to step n"), "Skip demo", tour music on/off.

**D. Post-tour**: the onboarding paywall (`ShowOnboardingPaywallAsync`), the tutorial invite surface, and a hold-to-talk hint line next to the cursor [O].

**E. Other permission or consent prompts**: the document-access card; the take-over consent card ("Want me to do it?" / "Go ahead" / "Not now", Esc takes control back); extra-usage approval; Chrome extension install ("Install for me" / "I'll install it"); and the integration OAuth through the browser [O].

---

## 5. Visual system cues (names and values only)

1. **Fonts** [O]: Segoe UI Variable Display / Text / Small (UI), Segoe UI Symbol, Segoe Fluent Icons / Segoe MDL2 Assets (glyphs), Segoe Print (printing), and bundled ClickyDraw-Barlow (Regular/Medium/SemiBold) and ClickyDraw-PatrickHand (hand-written ink notes). The spreadsheet viewer uses Cambria/Georgia.
2. **Accent / cursor colours** [O]: Blue `#3380FF` (default), Red `#FF4C4C`, Green `#26C281`, Yellow `#D4A017`. Morning suggestions reuse the same three. The cursor colour doubles as the app accent.
3. **Gel button family** (`UI.GelAppearance`) [O]: PrimaryBlue, SecondaryWhite, SecondaryOutline, Mint, Orange, Peach. Classes: `GelButton`, `GelActionButton`, `GelIconButton`, `GelSurface`, `RingCloseButton`, `ShadowLayer`, `BrandMark`.
4. **Paper/ink design language** (Ink namespace, 269 types) [O]: note paper stocks Sky, Butter, Mint, Periwinkle, Peach, Blush, Kraft, Cream. Hand-drawn elements include wobbly rects, tape, stamps, stickers, receipts, tickets, polaroids, sticky notes and sketch controls (`ClickySketchButton`/`Toggle`/`Slider`/`Segmented`/`Meter`). A paper grain texture is applied. Hex palettes for stocks and status inks live in `ClickyPaperStockStyle`, `ClickyThingStatusStyle` and `ClickySketchToneStyle` (rea-ui notes keep the raw lists).
5. **Companion cursor** (`Platform.Overlay`) [O]:
   - Shape: a rounded triangle (side 16, corner fraction 0.09) with a glow (max 20).
   - States: thinking shows 3 orbiting dots (radius 2.3, distance 8.2, beat 0.72 s, loop 2.16 s); listening shows a 5-bar waveform (2 px bars, 2 px gaps, radius 1.5); a dwell ring is 16 px wide with a 2.5 px line and 32 steps; there is a stop-square state.
   - Label bubble: font size 11, padding 8×4, radius 6, 10 px right and 18 px below the tip.
   - Message bubble: max width 300, font 11, line height 15, radius 6, word-by-word reveal, welcome text in lowercase.
   - Motion: the cursor follows the real cursor on a spring (`OverlayCursorSpring`) and can be docked or detached (shake). Tinted by the cursor colour.
6. **Screen highlighter** [O]: 8 px line, 55 % alpha, 1 s fade, dwell radius 12 / 0.35 s.
7. **Motion** [O]: spring-based (`Design.SwiftSpring`, `SpringCurve`, `NotchSprings` Open/Close/Content/TextInputOpen/Control). Reduced motion (Windows animation setting) swaps in a 0.12 s fade (`UI.MotionPreference`, `Anim.ReducedFadeSeconds`).
8. **Window chrome** [O/I]: borderless custom windows on a fully transparent `SystemBackdrop`. There are no Mica or Acrylic references. DWM attributes are used: WINDOW_CORNER_PREFERENCE, BORDER_COLOR (none), USE_IMMERSIVE_DARK_MODE, SYSTEMBACKDROP_TYPE, TRANSITIONS_FORCEDISABLED. Shadows are drawn in-app (PanelWindow ShadowMargin 32). The notch silhouette is drawn on the system compositor layer.
9. **Light/dark** [I]: follows Windows (`AppsUseLightTheme`). `HomeTheme` has Theme* text colours and the paywall palette has IsDark.
10. **Mascot portraits** (`Portraits`) [O]: packs PuffPals, KiCrew, ArcadeAllStars, CloudClub, ClassicClouds, StorybookSpirits. They are customisable by hairstyle (~60), outfit (20), skin tone (8), colourway (10), expressions (13, e.g. Curious/Listening/Thinking/Happy) and moods (Running/Happy/Sad/Sleepy).
11. **Earcons** [O, names only]: agent-launch/done/close/needs-you, text open/close/send/receive, question, surprised, hatching, home-reveal, reveal-boot, skill-up/down, thumbs-up. There is also a drawing sound set (pen, paper, stamp, tape, whoosh, …).
12. **Icons** (`UI.Icons`) [O]: an internal set mapped to Segoe Fluent glyphs or paths: XMark, Check, ArrowUp, Paperclip, Copy, Mic/MicOff, Speaker/SpeakerMute, Play/Pause, Clock, Link, Calendar, chevrons, More, Refresh, Settings, Info, Keyboard, Video, People, Warning, Send, Pen, Lightbulb, Stop, Folder, Globe, Home, Newspaper, Sparkle, Terminal, Waveform.

---

## 6. Keyboard shortcuts / hotkeys [O unless noted]
Defaults come from `HeyClicky.HotkeyBinding..cctor` IL. Modifier flags are inferred as Ctrl=1, Alt=2, Shift=4, Win=8, consistent with the display strings.

| Action | Default | Trigger |
|---|---|---|
| Push to talk | **Ctrl + Alt** | Hold; release to send (draws a highlight while held if enabled) |
| Text mode | **Ctrl** | Double-tap |
| Dictate | **Ctrl + Win** [I from flags 9] | Hold; release inserts text at the caret |
| Hands-free dictation | **Ctrl + Win** [I] | Double-tap to start, again to stop |
| Escape | Esc | Cancels or dismisses: closes the viewer, takes back the drive, a double Escape skips the intro video or tour reveal |
| Others | — | Ctrl+V paste hint after dictation; space to play/pause a drawing replay; Enter saves a label |

- **Recorder timings** (`ShortcutRecorder`): a tap counts if held at most 0.4 s, a double-tap needs both taps within 0.5 s, and recording times out after 7 s. `ShortcutAction` also has DictationCancel, ProactiveDebugSweep, MorningPreviewChord and SystemScreenshotShortcut.
- **Key handling**: hotkeys come from a low-level keyboard hook (`Platform.Input.ShortcutMonitor`) that masks the Start menu. Ctrl+Win+key system shortcuts cancel dictation.

---

## 7. Other features identified by strings
- **Proactive suggestions**: a "morning paper" (`GoodMorningBrief`, drawn on screen); suggestion cards with Approve, Adjust, Deny and Snooze; integration nudges; and a meeting countdown from Google Calendar.
- **Remote tasks**: "Fix the Task with HeyClicky Agent".
- **Drawings**: drawings can be saved to Home and replayed (window, full-screen or white board), recorded to a video in Downloads, and printed.
- **Take-over / "HeyClicky drive"**: computer control with a consent card, an Esc pill ("HeyClicky is driving … to take it back"), a safety-check stop, and a "window refuses input" (admin app) note.
- **Chrome extension auto-install**: the app drives the browser itself to install the extension.
- **Dictation into any app**: text goes into the focused field, with a clipboard fallback and a custom dictionary.
- **Clicky roster**: a roster of named agents ("Clickys") with portraits, each with its own memory, plus starter tasks (`Portraits.StarterTaskCard`, `ClickyRoster`).
- **Bug report**: a diagnostics zip with redaction, sent to Sentry and to a mail draft.
- **Updates**: delivered through Velopack, which installs on quit or via "Install & Relaunch" and shows a header pill ("Update app"). An upgrade pill reads "See HeyClicky Pro and Max".
- **Deep links**: a protocol registration exists (`RegisterShellIntegration`).
- **Stream mode**: the notch draws 1.5x larger.
- **Accessibility**: automation peers and names (`UI.Accessibility`, `NotchAccessibility.Describe`) and an accessible `Pressable`.

---

## 8. Open unknowns
1. **Exact layout and composition** are unknown: the pixel positions, grid rows, paddings and visual hierarchy of each screen, because the UI is code-built and the IL was not decompiled. Only const fields were read.
2. **Window sizes** for the Settings window, intro window and paywall are unknown. They are not consts, and many sizes are computed at runtime.
3. **Exact hex values** for most `HomeTheme`, `SK`, `ArcColors` and `DS` brushes are unknown. They are built from `Color` structs rather than strings, and light and dark values may differ.
4. The **dictation defaults** (Ctrl+Win hold or double-tap) rest on an assumed modifier bit layout of Ctrl=1/Alt=2/Shift=4/Win=8.
5. **Library mode** and **Live notes**: their purpose and UI entry points could not be determined.
6. **Notch tab icons** and which tab opens by default on hover are unknown. Home is likely the default [I].
7. Whether the **Home sheet** exists as a detachable window or only inside the notch is unclear, given the `HomeTheme` Attached/Detached values.
8. **Plan prices and agent-message quotas for Pro and Max** are unknown, because they come from the server.
9. **The integration catalog** is server-supplied (`/agent/integrations`). The local strings reveal only sample and logo slugs: gmail, googlecalendar, googledrive, googledocs, googlesheets, notion, linear, slack, github, linkedin, obsidian, plus bundled skills for ClickUp, Dropbox, HubSpot, Jira, Outlook, Salesforce and Google Tasks.
10. **Animation curves and timings** beyond the listed constants are unknown (spring parameters live in `.cctor` struct initialisers that were not decoded).
11. **The tutorial steps EmailDraft and Dictation** are known only by enum name; their page copy was not located.
