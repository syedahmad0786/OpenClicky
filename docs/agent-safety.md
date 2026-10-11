# Agent safety

OpenClicky Agent can move the mouse, type, and send hotkeys. Treat it like handing someone your keyboard for a few seconds.

## Defaults

- Every mutating action (`click`, `type_text`, `hotkey`, `scroll`) shows a **Yes/No** confirmation dialog.
- Max **8** steps per agent run.
- Screenshots stay in memory; BYO OpenRouter keys stay on the client (or optional localhost proxy).
- No credential scraping tools.

## Recommended practice

1. Start with reversible tasks (Notepad, Settings search).  
2. Keep confirmations enabled (there is no “always approve” yet — by design).  
3. Do not run agents over banking, password managers, or production consoles.  
4. Prefer saying `openclicky agent …` with a narrow task.

## Deny-list (soft)

The model prompt forbids inventing credentials and casual `alt+f4`. Future builds may hard-block window titles matching banking/password managers.
