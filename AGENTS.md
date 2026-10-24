# OpenClicky contributor guidance

Keep Windows behavior and user-visible proof together. Read affected callers before changing shared code. Prefer existing helpers and platform features; avoid adding a dependency for a small feature. Preserve dirty checkouts and user settings, credentials, history, and logs.

Build with `dotnet build OpenClicky.sln -c Release` and run the relevant tests. New nontrivial logic needs a focused check. For desktop actions, inspect the actual destination controls; input return values and logs alone do not prove success. Unconditional automatic-confirm helpers belong only in the dedicated test harness. The user's explicit 11 October 2026 request authorizes production Preview/Confirm-each/Auto/Full-access choices: Confirm-each stays default, Auto/Full require a visible warning, and a running task's access snapshot must not silently widen. Never enable Full access on the user's behalf or claim that heuristic risk detection/title guards form a security boundary.

Keep provider secrets out of source, settings JSON, logs, and chat. Do not automate sign-in, credential entry, or Windows security changes. Explain which screens/audio leave the machine. A title deny-list is a convenience guard, not a security boundary.

## Model routing

Ahmad's permanent preference, 9 September 2026:

- GPT-6 Astra: orchestration and exceptionally difficult technical, architecture, or security work.
- GPT-5.6 Luna: routine checks, documentation, simple edits, and straightforward fixes.
- GPT-5.6 Terra: ordinary scoped implementation and debugging.
- GPT-5.6 Sol: complex implementation, investigation, and review.

Avoid needless agents, repeated unchanged polling, and redundant tests or context. Delegate only when the user or applicable instructions request it. When delegation is authorized, explicitly select an appropriate model and send only the bounded task's necessary context. Escalate after demonstrated difficulty. If the interface cannot change models, state the limitation rather than claiming a switch.
