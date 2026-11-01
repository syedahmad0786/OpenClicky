# Intelligent UI integration review — 2026-10-12

## Decision

OpenClicky now has a small native controlled-generative-UI path for its existing chat: a completed assistant answer can include one bounded AG-UI `CUSTOM` event named `openclicky.ui.document`. The ordinary text remains the spoken and saved answer; the event value renders as native Windows cards, selectable flow steps, connections, and follow-up buttons.

This is not an embedded copy of OpenDots or Open Intelligent UI, and it does not claim the current Responses API call is an AG-UI transport. It uses AG-UI's documented custom-event envelope as the interchange shape for one application-defined payload. The renderer and payload semantics are OpenClicky code.

## Why this is the useful first slice

OpenClicky is a .NET 8 WinForms tray companion with one existing chat and voice pipeline. OpenDots is a web/mobile application template using a React UI, a server runtime, CopilotKit Intelligence for conversations, TanStack AI, Channels SDK, and separate persistent workspace services. Open Intelligent UI is a Next.js + Python agent application; its richer route streams model-produced HTML, CSS, and JavaScript into a sandboxed iframe and uses a separate visualization router. Importing either application would create another product surface and runtime rather than enrich the Windows conversation.

The fixed native renderer brings the most useful part of their interaction model into the current companion:

- the model can choose plain text or a known rich component;
- rich output stays attached to the same assistant turn;
- selecting a flow node reveals detail;
- action buttons put a declared prompt into the shared composer for review before Send;
- model output cannot introduce code, controls, URLs, tools, or direct desktop actions.

## Wire contract

Native acceptance on 12 October: a real GPT-6-Luna reply rendered a status card and a three-step clipboard flow in Chat. Selecting Review links displayed its detail; the follow-up button populated the composer and did not send automatically. The default-size test found cramped prose and a narrow card; responsive panel sizing, minimum card width and scroll extent were then corrected. Final layout acceptance is recorded separately in the 0.5.0 test report.

The assistant supplies readable text followed by one fenced `ag-ui` JSON object:

```json
{
  "type": "CUSTOM",
  "name": "openclicky.ui.document",
  "value": {
    "version": 1,
    "title": "Release plan",
    "summary": "Two steps remain.",
    "cards": [
      { "title": "Coverage", "value": "82%", "detail": "Focused tests pass", "tone": "good" }
    ],
    "nodes": [
      { "id": "build", "title": "Build", "detail": "Compile Release", "status": "done" },
      { "id": "verify", "title": "Verify", "detail": "Run focused checks", "status": "current" }
    ],
    "edges": [
      { "from": "build", "to": "verify", "label": "then" }
    ],
    "actions": [
      { "label": "Show risks", "prompt": "Explain the remaining release risks" }
    ]
  }
}
```

`IntelligentUiParser.Parse(raw)` removes the fenced object from `PlainText` even when the event is malformed, so JSON is not read aloud or saved as conversation prose. It accepts only version 1 of the named `CUSTOM` event and enforces payload, collection, string, enum, unique-node, and edge-reference limits before the UI sees data.

## Upstream compatibility and limits

AG-UI defines the `CUSTOM` event as an application-specific event with `name` and `value`. That makes the envelope a real documented AG-UI event. OpenClicky's `openclicky.ui.document` value is a local extension and is not A2UI, Open Generative UI, or an upstream CopilotKit component schema.

OpenClicky already exports local task history with documented AG-UI lifecycle, step, custom, and terminal event names. The new rich answer path currently arrives inside a completed text response rather than over AG-UI SSE/WebSocket framing. A full AG-UI client would also need run input, lifecycle ordering, streaming message/activity handling, errors, cancellation, and capability negotiation.

No CopilotKit, OpenDots, Open Intelligent UI, or AG-UI SDK package is included by this change. No upstream source or assets were copied. The reviewed OpenDots, Open Intelligent UI, CopilotKit, and AG-UI repositories are MIT licensed, but CopilotKit Intelligence service deployment and Open Intelligent UI's model/visualization providers have separate setup and service requirements.

## Full integration path

If OpenClicky later needs upstream Open Intelligent UI features such as arbitrary charts, maps, calculators, or 3D scenes, treat that as a separate WebView/runtime project. It needs a packaged renderer, a strict sandbox and host bridge, network and provider disclosure, lifecycle integration, accessibility checks, and a real AG-UI stream. Reusing the upstream renderer is more honest and safer than accepting arbitrary model HTML in WinForms.

## Sources reviewed

- OpenDots repository and architecture: https://github.com/CopilotKit/OpenDots
- OpenDots setup and Intelligence requirement: https://github.com/CopilotKit/OpenDots/blob/main/docs/SETUP.md
- Open Intelligent UI repository and runtime outline: https://github.com/CopilotKit/OpenIntelligentUI
- Open Intelligent UI documentation index: https://github.com/CopilotKit/OpenIntelligentUI/blob/main/docs/README.md
- CopilotKit architecture and package layers: https://github.com/CopilotKit/CopilotKit/blob/main/dev-docs/architecture/ARCHITECTURE.md
- AG-UI overview: https://github.com/ag-ui-protocol/ag-ui/blob/main/docs/introduction.mdx
- AG-UI event definitions, including `CUSTOM`: https://github.com/ag-ui-protocol/ag-ui/blob/main/docs/concepts/events.mdx
- AG-UI 1.0 schema project: https://github.com/ag-ui-protocol/ag-ui/tree/main/spec
