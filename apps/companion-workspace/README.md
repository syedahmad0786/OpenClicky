# OpenClicky companion workspace

This package is the single embedded React workspace shown inside OpenClicky's native WebView2 panel. It uses the open-source CopilotKit chat and AG-UI `HttpAgent` transport. The local Node child has no provider account or provider key; it proxies authenticated AG-UI runs to a loopback host endpoint owned by OpenClicky.

The native host starts `pnpm start` with `OPENCLICKY_WORKSPACE_TOKEN` (32+ random characters), `OPENCLICKY_HOST_AGENT_URL`, optional `OPENCLICKY_HOST_AGENT_TOKEN`, and port `0`. The child binds only to loopback and prints one JSON ready record containing its origin. The native host navigates to that nonsecret origin, waits for the trusted page, then sends `session.bootstrap` through WebView2 messaging. CopilotKit keeps the token in memory and sends it in the runtime `Authorization` header.

Host to UI messages are strict JSON objects:

- `{"kind":"session.bootstrap","token":"<ephemeral 32+ characters>"}` is sent after trusted navigation. `WorkspaceReady` fires after this post, so the controller can safely send the snapshot.
- `{"kind":"session.snapshot","sessionId":"...","artifacts":[...],"progress":"..."}` reopens up to the 12 most recent artifacts for that session. Persistence retains older artifacts until explicitly deleted.
- `{"kind":"message.delta","messageId":"...","text":"..."}` and `{"kind":"message.complete","messageId":"...","role":"user|assistant","text":"..."}` mirror voice/history updates into the same session. Typed chat streams through the authenticated CopilotKit AG-UI route.
- `progress`, `artifact.show`, `approval.result`, and `runtime.error` update their corresponding visible state.

UI to host messages are `ready`, `run.cancel`, `artifact.changed`, `artifact.save`, `approval.request`, and `desktop.action`. Every artifact carries `kind`, `id`, `title`, optional `followupId`, and bounded kind-specific fields. The host validates untrusted JSON with `VisualArtifactStore.Parse`, persists with `Save(sessionId, conversationId, element)`, and reopens records from `Load(sessionId)`. `VisualArtifact` exposes `Id`, `Kind`, `Title`, `FollowupId`, `SessionId`, `ConversationId`, exact `SourceJson`, and `UpdatedAtUtc`.

The runtime exposes CopilotKit 1.78 multi-route endpoints at `/api/copilotkit/info` and `/api/copilotkit/agent/:id/run`. The focused fixture starts the bundled server against a synthetic AG-UI endpoint and proves a `TEXT_MESSAGE_CONTENT` SSE delta traverses both layers. Runtime routes require the ephemeral bearer token, an exact same-origin `Origin`, and a loopback `Host`.

Generated visuals use the strict schemas in `src/contracts.ts`. They select known React components. The 3D preview uses a fixed `srcdoc` inside `sandbox="allow-scripts"` with a deny-by-default CSP and receives no host token, file access, provider credentials, or network permission. Desktop actions always return to the native approval bridge.

The interaction and sandbox approach was informed by CopilotKit's Open Intelligent UI project. No upstream component source was copied. Its MIT license is retained in `OPEN-INTELLIGENT-UI-LICENSE.txt` for attribution.
