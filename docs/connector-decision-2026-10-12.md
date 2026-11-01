# Account connections — 12 October 2026

Use **Composio's free personal account route first**, with Nango Cloud as an alternative. This is a researched implementation decision, not an active Gmail/Slack connection. ChatGPT sign-in supplies the model; each external account still needs its own OAuth consent and explicit account selection.

| Option | Currently advertised free allowance | Practical tradeoff |
| --- | --- | --- |
| Composio Hobby | 100,000 tool calls/month overall; Composio-managed OAuth has a 20,000-call/month subset and first 1,000 connections free. Free limits are hard-capped. | Managed authorization is the shortest personal setup. Vendor-hosted credentials and tool requests remain cloud services. |
| Nango Cloud Free | 10 connections, 10 compute hours/month, 10 GB/month, with hard limits. | A viable free option now; previous assumptions that Cloud necessarily starts with a paid plan are outdated. |
| Nango self-host | Operate the infrastructure and review the edition/license limits. | More control, substantially more setup; unsuitable as the default novice onboarding path. |

Sources: [Composio pricing](https://composio.dev/pricing), [Nango pricing](https://nango.dev/pricing). These are current vendor terms, not a promise of permanent free service. Model usage, optional paid tools and higher tiers have separate terms.

## One-product setup to implement

1. Connections shows **Connect my tools**, the provider and its current free-plan link. The user creates their own provider account. OpenClicky never ships a shared developer secret in its public binary.
2. A native secret-entry field stores that user's key in Windows Credential Manager/DPAPI, through the existing secret store. Never put keys in source, settings JSON, chat, diagnostics or URLs. A later hosted distribution would require a secure service instead of distributing a service-wide secret.
3. Create a provider session scoped to this local user's random identifier. For new Composio work use the current session API and hosted connection link, not retired managed-OAuth `initiate()` calls. Open the provider's normal browser consent; the user signs in and selects scopes.
4. After consent, re-read connection state from the API. Display the actual account, tool, scopes and status. A callback's `success` string alone is insufficient.
5. First acceptance: one read-only Gmail request, with source links, no sending. Multiple Gmail accounts require explicit account selection per task; a default account must never silently change. Connection identity is included in the task's approval snapshot.
6. Later writes use the same task timeline and named approval as desktop actions. Reject model-supplied credentials, arbitrary connector IDs, or an account outside the approved task. Stop prevents future calls but does not undo completed effects.
7. Show quota/auth failures inline and provide disconnect/revoke instructions. Do not silently upgrade a free plan or retry a write after an uncertain outcome.

Reference: [Composio authentication](https://docs.composio.dev/docs/tools-direct/authenticating-tools) identifies the legacy routes and directs new integrations to sessions. Managed OAuth availability and allowed scopes must be checked for the specific Gmail/Slack toolkit before release. A hosted account's authorization screen remains a necessary user step; “one click” cannot legitimately remove consent.

## Acceptance still required

No connector credentials were entered, accounts connected, email read/sent, campaign published or paid service enabled during this work. The current Windows companion's native task/voice flow runs independently of connectors. Do not label the Connections screen operational until connection identity, two-account isolation, revocation, quota failure and read/write receipts have passed live tests.
