# Agent safety

OpenClicky Agent can move the mouse, type, and send hotkeys. Treat it like handing someone your keyboard for a few seconds.

## Defaults

- Every mutating action (`click`, `type_text`, `hotkey`, `scroll`) shows a **Yes/No** confirmation dialog (human only — no auto-confirm).
- After approval closes, OpenClicky restores the prior window and focused control, verifies focus, and settles briefly before input. If the target closed or focus cannot be restored, the task stops. It never falls back to typing into the desktop. The built-in sandbox is the sole trusted input target inside OpenClicky's own process.
- Click coordinates must fall inside a current screenshot or monitor. Unknown tools and coordinate spaces are rejected before approval.
- Escape or Stop cancels pending work and approvals. Declined or failed actions end the run.
- The production desktop runner is capped at **24** steps; the shared runner defaults to eight for callers/tests.
- Screenshots stay in memory before being sent to the selected provider. ChatGPT OAuth tokens and optional OpenRouter keys stay in protected local storage; screen requests use the selected provider.
- No credential scraping tools.

## Recommended practice

1. Start with reversible tasks (Notepad, Settings search).  
2. Prefer **Approve each action** for strict control. Review task scope and visible accounts before enabling Auto or Full access.
3. Do not run agents over banking, password managers, or production consoles.  
4. Prefer saying `openclicky agent …` with a narrow task.

## Deny-list (soft)

Mutating tools are refused when the foreground window title matches banking / password-manager / authenticator fragments (see `AgentDenyList`). The model prompt also forbids inventing credentials and casual `alt+f4`.

The title check runs again after approval. This is a convenience guard, not a security boundary: a sensitive page with an ordinary title can pass it. Web and accessibility content are treated as untrusted material in the model prompt. Prompt instructions alone do not prevent every possible model mistake.

## User-selected access modes

Requested explicitly by the user on 11 October 2026. The selected mode is copied when a task starts; it is locked in the UI during that run.

- **Preview:** read/planning steps can run, but the first proposed desktop mutation is shown and blocked.
- **Approve each action:** every click, typing action, hotkey and scroll needs human approval.
- **Auto:** first shows a model-generated task plan for approval, then permits routine steps. All hotkeys and steps whose English rationale/arguments match sensitive words require approval. These conservative heuristics can miss sensitive clicks; Auto does not guarantee every external effect will be identified.
- **Full access:** a warning precedes selection. Desktop input runs without individual prompts and may send, publish, delete or change visible data. Stop cannot undo completed input. The soft title guard and changed-window/revision checks still apply.

Showing Home or starting a voice request pauses input at the next action boundary. Pause cannot retract an action already executing. Resume gives time to select the destination. A foreground-window or user-guidance change during planning discards the old plan; a later mutation checks that context again before execution.

Dedicated automatic-confirm test helpers are not used by production. Production Auto/Full access instead implement the user's explicitly selected policy. No account consent, browser sign-in or credential entry is automated by the integration/setup code.

## Secrets

OpenRouter API keys are stored in **Windows Credential Manager** (`OpenClicky/openrouter-api-key`), not in `settings.json`. ChatGPT OAuth token JSON is larger than CredMan’s 2560-byte blob limit, so it is stored as a **DPAPI-protected file** under `%AppData%\OpenClicky\secrets\` (still never plaintext in `settings.json`).
