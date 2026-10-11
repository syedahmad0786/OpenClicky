# Agent safety

OpenClicky Agent can move the mouse, type, and send hotkeys. Treat it like handing someone your keyboard for a few seconds.

## Defaults

- Every mutating action (`click`, `type_text`, `hotkey`, `scroll`) shows a **Yes/No** confirmation dialog (human only — no auto-confirm).
- After Yes/No closes, OpenClicky restores foreground to the prior desktop window (or the shell if the prior HWND was ours/invalid) and settles briefly before SendInput. This prevents `win+r` / `type_text` from landing on the confirm UI or tray host.
- Max **8** steps per agent run.
- Screenshots stay in memory; BYO OpenRouter keys stay on the client (or optional localhost proxy).
- No credential scraping tools.

## Recommended practice

1. Start with reversible tasks (Notepad, Settings search).  
2. Keep confirmations enabled (there is no “always approve” yet — by design).  
3. Do not run agents over banking, password managers, or production consoles.  
4. Prefer saying `openclicky agent …` with a narrow task.

## Deny-list (soft)

Mutating tools are refused when the foreground window title matches banking / password-manager / authenticator fragments (see `AgentDenyList`). The model prompt also forbids inventing credentials and casual `alt+f4`.

## Secrets

OpenRouter API keys are stored in **Windows Credential Manager** (`OpenClicky/openrouter-api-key`), not in `settings.json`. ChatGPT OAuth token JSON is larger than CredMan’s 2560-byte blob limit, so it is stored as a **DPAPI-protected file** under `%AppData%\OpenClicky\secrets\` (still never plaintext in `settings.json`).
