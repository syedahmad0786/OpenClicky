# HeyClicky 1.1.1 live UI observation (2026-10-24, view only, user signed in)

Screenshots: C:\Users\ahmad\.claude\projects\D--OpenClicky\a0f56339-8b43-40f3-afe2-03ed94d59a11\tool-results\mcp-computer-use-blob-*.jpg

## Notch panel (top-centre, dark, rounded bottom corners, ~770x380 real px)
- Collapsed: small dark pill at top centre. Expands on hover / app activation; does not take focus.
- Top row: segmented pair of icon buttons left (waveform/home, cards/chat), gear right.
- Home view: greeting "Evening, <name>." large; sub hint "Hold Ctrl + Alt and tell me what to take off your plate."
  - Featured job result card: cream "paper" card, small "New" tag top, close X, bold title, tinted summary block,
    optional big stat ("86 sec  audio narration"), 2 bullet lines, stacked tilted artifact thumbnails (file name + "Open doc"/"Open PDF"/"Play audio" pills),
    bottom actions: primary blue pill ("Open PDF guide") + secondary white pill with doc icon ("Full report").
  - Bottom row: left pill "Invite & earn" (gift icon); right pill with connected app logos + "+3 Apps +" (opens Settings > Integrations).
- Job carousel around notch: tilted sticky-note cards (cream paper, small handle on top) left and right of the panel;
  "< N more" pill with tooltip "Show the windows on the left". Card states: "+ Ready to view" (blue) or working title only.
- Chat view: tilted artifact cards row at top, then grey rounded assistant bubbles; bottom: wide blue gradient pill
  "Hold Ctrl + Alt to talk" (mic icon) + dark pill input "Ask HeyClicky...".

## Settings (same panel grows to ~920x730 real; dark; left sidebar ~240 px; rounded content cards grouped under small-caps headers)
Sidebar: Account, General, Invite & Earn | HEYCLICKY: Voice, Microphone, Dictation, Shortcuts, Cursor | WORK: Integrations, Agents, Routines | footer version.
- Account: PLAN card (plan, usage meters "Talk: Unlimited", "Agent messages 6/150", upgrade/manage) ; ACCOUNT card: Log out (email), Delete account (red), Quit (red).
- General: BEHAVIOR toggles: Show in taskbar; Show in screen recordings; Launch at login; Pause. UPDATES: App updates (version, Check for updates), What's new link. COMMUNITY links.
- Voice: SPEECH SPEED chips 0.5x 0.75x 1x 1.25x 1.5x; VOICE grid of 10 tiles (icon, name, adjective; selected = outlined + check); "tap a voice to hear it". CAPTIONS toggle (word-by-word captions at bottom of screen). OUTPUT DEVICE radio list + "Play a test sound".
- Microphone: INPUT DEVICE radio list (System Default + devices; unavailable greyed); TEST button + level bars.
- Dictation: Auto-detect language toggle; language search + tile grid (disabled while auto).
- Shortcuts: Push to talk [Ctrl][Alt] Change; Text mode [Ctrl][Ctrl] Change; Dictate [Ctrl][Win]; Hands-free dictation [Ctrl][Shift]; Reset to defaults.
- Cursor: COLOR 4 tiles (red, blue default, yellow, green glowing triangle cursor). VISIBILITY: Hide floating cursor; Screen highlight (draw while holding PTT); Shake to detach.
- Integrations: BROWSER card Chrome (Connected badge, Disconnect; works with Chrome/Brave/Edge/Arc). Search + "Add custom connector". CONNECTED: per service (logo, name, description) with accounts (avatar, account, Connected since date, Disconnect), "+ Add another X account"; expired = "Sign-in expired" + remove + Reconnect. AVAILABLE: rows with Connect; Obsidian "Configure" (local vault).
  - New connector form: Name, Server URL, Authentication segmented [Sign in with browser | API key | None], Advanced expander, Cancel / Add connector.
- Agents: WORKSPACE Agent folder picker. AUTONOMY Auto-approve extra usage. ANNOUNCEMENTS: Speak when a job starts or finishes; Show updates beside the cursor; quiet during calls/screen share/DND note. WHAT AGENTS CAN REACH: Files & Folders; Documents, Pictures, Contacts, Calendar, Microphone, Camera, Location with GRANTED badges (Windows privacy settings).
- Routines: empty state: hold [Ctrl][Alt] and say what to do and when, with an example.
- Invite & Earn: referral (not reproduced).

Visual: near-black panels, rounded 12-16 px cards slightly lighter, white pill secondary buttons, blue gradient primary pills, green Connected/GRANTED badges, red destructive text, small caps grey section headers. Job cards are cream paper with dark ink.
Cursor overlay not captured by screenshots.
