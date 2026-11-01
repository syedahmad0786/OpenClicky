# Model routing research — 2026-10-12

## Decision

Do not add Jev or SubQ now. Neither product moves a Windows pointer, inspects native controls, executes actions, or renders OpenClicky's UI. They are hosted model APIs that would add another provider, key, billing relationship, disclosure surface, and failure path.

If action selection is a measured latency bottleneck, run one small experiment with OpenAI's **Decisions API** using the existing `gpt-6-luna` model: give it the already-inspected screen state and a bounded list of candidate actions, then let the existing native executor validate and perform the selected action under the current Preview/Confirm-each/Auto/Full-access policy. This preserves the current trust boundary. First confirm that OpenClicky's existing ChatGPT sign-in authorization can call `POST /v1/decisions`; the public API documentation describes the endpoint, but does not establish that this particular sign-in integration is entitled to it.

## What each product actually is

| Product | Verified role | Input/output | What it could improve | What it does not provide | Access boundary |
|---|---|---|---|---|---|
| TypeSafe AI **Jev** | Hosted “System One” decision model | Text/JSON state plus Choice, Score, or Noul questions; typed answers and probabilities | Fast classification, routing, scoring, or choosing among options already supplied by OpenClicky | No generated reply, Windows grounding, native control inspection, mouse/keyboard execution, or UI renderer. TypeSafe says Jev is currently text-only, with no image/audio/video input. | Separate TypeSafe account and API key at `api.typesafe.ai`; early-access product. This is independent of the user's ChatGPT subscription/sign-in. |
| OpenAI **Decisions API** | Real OpenAI public-beta endpoint, not a generic name for Responses | Shared text and/or image evidence plus predicate, choice, or score questions; typed answers and probabilities | Officially described as about 10× faster than Responses for these bounded judgments. It could cheaply select a next action from candidates produced by OpenClicky's Windows inspection layer. | No free-form answer, function/tool loop, screen-control discovery, pointer movement, click execution, or rich UI rendering | Dedicated `POST /v1/decisions`, currently `gpt-6-luna` only. Standard OpenAI API usage is billed separately from ChatGPT subscriptions. OpenAI also documents plan-backed use for participating “Sign in with ChatGPT” apps, but the Decisions guide does not say every such integration receives this endpoint; OpenClicky's entitlement needs a non-mutating capability check. |
| Subquadratic **SubQ** | Hosted preview model platform with two distinct models | `subq-snap-preview`: typed decisions over text/images. `subq-preview`: text-only long-context chat/Responses with files and function calling. | Snap could fill the same bounded-decision niche as Jev/OpenAI Decisions. Preview could help only if unusually large files or repository-scale context becomes a real requirement. | No documented computer-use model, Windows perception/executor, pointer movement, or renderer. Function calling merely asks OpenClicky's code to run a declared function. | Separate SubQ account, credits, base URL, and API key. Preview capabilities may change. Default prompt/completion retention is indefinite unless the organization changes its retention policy; per-request `store: false` is available for Chat/Responses, while Decisions is listed as unaffected by zero-data-retention mode. |

## Where these fit in OpenClicky

The current system already has four separate jobs. Keeping them separate avoids treating a quick classifier as a security boundary:

1. **Grounding:** OpenClicky's Windows layer inspects the actual destination controls and produces current, bounded candidates.
2. **Planning/selection:** Responses handles open-ended screen understanding and replies. A Decisions-style call could handle only a narrow selection such as “which of these inspected controls best matches the user's requested target?”
3. **Execution:** the native action executor revalidates the target, applies the running task's fixed access snapshot, requests confirmation when required, and performs the input.
4. **Rendering:** the existing native `openclicky.ui.document` path renders bounded cards, flows, and follow-up prompts. None of the three researched products replaces it.

A decision result is advisory evidence. Confidence is not permission, and a high score must not bypass target validation or widen access.

## Smallest useful evaluation

### Measured conversation path, 12 October

The existing ChatGPT `gpt-6-luna` Responses path was measured before adding a decision provider. Generated chart, diagram and screen cases all passed semantic validation at default, low and no reasoning. First-text medians were **3.679 s default**, **2.707 s low**, and **3.813 s none**; total-response medians were **4.725 s**, **4.463 s**, and **4.812 s**. One sample per case/effort is directional only. The raw timing receipt is `artifacts/chatgpt-latency-20261012.json`; no user screen, history or audio was submitted.

These measurements concern conversation generation, not action selection. They support first tuning the existing conversation request; they do not justify adding Decisions, Jev or SubQ. Lower reasoning effort is documented for routine requests in the [OpenAI deployment checklist](https://developers.openai.com/api/docs/guides/deployment-checklist#set-up-reasoningeffort). Native/physical end-to-end targets remain open.

The production conversation client now selects low effort only for streamed requests using exact `gpt-6-luna`; guides, other models and OpenWork planning are unchanged. A second three-case live production sample passed all content checks, with median first text **2.723 s** and total **3.437 s**. The diagram case still took **3.513 s** to first text. This is a bounded validation, not a reliability percentile or a physical voice result. Receipt: `artifacts/chatgpt-latency-production-20261012.json`.

### Decision selection experiment, only if still needed

Only proceed if telemetry shows model decision time is material in Windows action loops. Reuse a small set of recorded, non-sensitive screen-state fixtures and compare the current Responses selection with OpenAI Decisions on:

- correct candidate selected, including “none/ask the user” cases;
- end-to-end selection latency, not provider-reported model latency alone;
- confidence calibration at the threshold chosen for asking rather than acting;
- no change to executor validation, confirmation behavior, or action receipts.

Do not test three providers at once. OpenAI Decisions has the smallest architectural and operational delta because OpenClicky already uses `gpt-6-luna`; Jev and SubQ become sensible only if that experiment fails a measured latency/cost requirement and the privacy/provider cost is explicitly accepted.

## Primary sources

- OpenAI Decisions guide (public beta, `gpt-6-luna`, typed questions, claimed 10× speedup): https://developers.openai.com/api/docs/guides/decisions
- OpenAI Decisions endpoint reference (accepted inputs and returned shapes): https://developers.openai.com/api/reference/resources/decisions/methods/create
- OpenAI billing separation: https://help.openai.com/en/articles/9039756-managing-billing-for-chatgpt-and-the-api-platform
- OpenAI plan use in participating third-party apps: https://help.openai.com/en/articles/20001542-using-your-chatgpt-plan-in-other-apps-and-sites
- TypeSafe Jev introduction and limitations: https://docs.typesafe.ai/introduction and https://docs.typesafe.ai/concepts/system-one
- TypeSafe Jev API/key quick start: https://docs.typesafe.ai/introduction/quickstart
- TypeSafe launch announcement (vendor performance claims): https://typesafe.ai/blog/introducing-system-one-models-and-jev
- SubQ product announcement: https://subq.ai/introducing-subq
- SubQ API overview and model matrix: https://docs.subq.ai/using-the-api/ and https://docs.subq.ai/using-the-api/models/
- SubQ authentication, function calling, and retention: https://docs.subq.ai/using-the-api/authentication/ , https://docs.subq.ai/using-the-api/function-calling/ , and https://docs.subq.ai/using-the-api/data-retention/

## Uncertainty

The “10× faster” Decisions figure and Jev's “two orders of magnitude” claim are vendor claims, not OpenClicky measurements. Jev is early access; both OpenAI Decisions and SubQ are preview/beta surfaces. No official source reviewed documents a Jev, Decisions, or SubQ facility that directly operates Windows controls. OpenClicky's exact ChatGPT sign-in scopes and Decisions entitlement were not tested because this research did not access credentials or make provider calls.
