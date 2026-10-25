# OpenClicky status

## 2026-10-25 — HeyClicky parity (branch `heyclicky-parity`, not pushed)
- Done and reviewed: sub-projects A (shell), S (settings), B (talk), C (companion), D (dictation, commits 2d9dc1e..906133d). Plans in `docs/superpowers/plans/`; per-plan ledgers in `.superpowers/sdd/`.
- D adds: 50 languages + auto-detect with a downloadable multilingual Whisper (`ggml-base.bin`); field context over UIA; "Tidy up with AI" (on by default, one-time disclosure card; sends transcript + app/site + ±2,000 chars around the cursor to the model provider, audio stays local; off in the classic UI); clipboard fallback card; learned single-word dictionary corrections with Undo. Release 0 warnings; Core 1541/1541, Input 48/48; dictation smokes pass.
- Waiting on Ahmad: C13 live acceptance (screenshots + 6 physical checks, deferred 25 Oct) and D's 7 user checks (listed in `docs/heyclicky-parity/feature-ledger.md`).
- Known follow-up: dictating only the first dictionary word on its own is dropped as a Whisper prompt echo (`WhisperPrompt.IsEcho`).
- E (Work: jobs, colours, WORK.md, structured results, live checklist, 5 concurrent jobs + desktop lease, steer/follow-up, announcements, your-turn steps, how-tos, routines, agent folder; commits 9eb52e1..659d7cf) is done and reviewed, including a final fix wave (background jobs no longer block dictation/walkthroughs or react to Esc; your-turn steps can't auto-complete without the user; announcements use the local voice when read-aloud is off). Release 0 warnings, Core 1819, Input 48, vitest 209, listed smokes pass. Waiting on Ahmad: E's 7 user checks (listed in `docs/heyclicky-parity/feature-ledger.md`).
- F (connectors), G (browser), H (onboarding), P (proactive) are built and reviewed on their own branches in worktrees under `.claude/worktrees/` (`parity-f-connectors`, `parity-g-browser`, `parity-h-onboarding`, `parity-p-proactive`); not merged. Merging needs Ahmad's go-ahead and hand wiring (G and P into E's concurrent jobs; F and G both edit Integrations). Note G ruling R1: the old exact-site allow-list is gone — agents can read any signed-in site once the browser is connected (access modes, deny list and Connect step are the gate).
- Next: merge F, G, H, P into `heyclicky-parity` (after approval), re-run the gate, then the combined native acceptance.

## 2026-10-24 — phase two: companions, jobs, routines, connectors
- Reverse-engineered HeyClicky 1.1.1 (static REA, clean room) into `docs/heyclicky-behavior-spec-2026-10-24.md`; committed the 12 Oct phase-one WIP as `eb4729b` on `codex/windows-product`.
- Branch `phase-two/companions-jobs-routines`: WorkStore (companions, jobs, routines), `write_output` job tools, MCP connectors proxied by the broker, Work tab, Connections list, routine timer. Release build 0/0; Core 501/501, Input 25/25.
- Native: Work tab renders in the fresh `bin\x64\Release` build. Not yet done: a real job run, a scheduled run, a live connector.
- Next: in Work, create a job → Run now and verify `output/` + `runs.md`; then add one read-only connector and Test it. Later: OAuth MCP servers, voice-created jobs, proactive suggestions (spec G6–G8).
