# OpenClicky status

## 2026-10-24 — phase two: companions, jobs, routines, connectors
- Reverse-engineered HeyClicky 1.1.1 (static REA, clean room) into `docs/heyclicky-behavior-spec-2026-10-24.md`; committed the 12 Oct phase-one WIP as `eb4729b` on `codex/windows-product`.
- Branch `phase-two/companions-jobs-routines`: WorkStore (companions, jobs, routines), `write_output` job tools, MCP connectors proxied by the broker, Work tab, Connections list, routine timer. Release build 0/0; Core 501/501, Input 25/25.
- Native: Work tab renders in the fresh `bin\x64\Release` build. Not yet done: a real job run, a scheduled run, a live connector.
- Next: in Work, create a job → Run now and verify `output/` + `runs.md`; then add one read-only connector and Test it. Later: OAuth MCP servers, voice-created jobs, proactive suggestions (spec G6–G8).
