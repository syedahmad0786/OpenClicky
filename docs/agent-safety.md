# Agent safety

OpenClicky Agent can move the mouse, type, and send hotkeys. Treat it like handing someone your keyboard for a few seconds.

## Defaults

- Every mutating action (`click`, `type_text`, `hotkey`, `scroll`) shows a **Yes/No** confirmation dialog (human only — no auto-confirm).
- After approval closes, OpenClicky restores the prior window and focused control, verifies focus, and settles briefly before input. If the target closed or focus cannot be restored, the task stops. It never falls back to typing into the desktop. The built-in sandbox is the sole trusted input target inside OpenClicky's own process.
- Click coordinates must fall inside a current screenshot or monitor. Unknown tools and coordinate spaces are rejected before approval.
- Escape or Stop cancels pending work and approvals. Declined or failed actions end the run.
- Max **8** steps per agent run.
- Screenshots stay in memory before being sent to the selected provider. ChatGPT OAuth tokens and optional OpenRouter keys stay in protected local storage; screen requests use the selected provider.
- No credential scraping tools.

## Recommended practice

1. Start with reversible tasks (Notepad, Settings search).  
2. Keep confirmations enabled (there is no “always approve” yet — by design).  
3. Do not run agents over banking, password managers, or production consoles.  
4. Prefer saying `openclicky agent …` with a narrow task.

## Deny-list (soft)

Mutating tools are refused when the foreground window title matches banking / password-manager / authenticator fragments (see `AgentDenyList`). The model prompt also forbids inventing credentials and casual `alt+f4`.

The title check runs again after approval. This is a convenience guard, not a security boundary: a sensitive page with an ordinary title can pass it. A local user approval is still required for every input action. Web and accessibility content are treated as untrusted material in the model prompt. Prompt instructions alone do not prevent every possible model mistake.

## Secrets

OpenRouter API keys are stored in **Windows Credential Manager** (`OpenClicky/openrouter-api-key`), not in `settings.json`. ChatGPT OAuth token JSON is larger than CredMan’s 2560-byte blob limit, so it is stored as a **DPAPI-protected file** under `%AppData%\OpenClicky\secrets\` (still never plaintext in `settings.json`).
