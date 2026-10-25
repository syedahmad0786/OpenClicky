import { useEffect, useState, type ReactNode } from "react";
import { postPage } from "../bridge";
import { talkGesture, type OnboardingAction, type SettingsSection, type ShellState } from "../contract";
import { act, patch } from "../settings/common";
import { GlowingCursor } from "../settings/CursorPage";
import { useMicLevel } from "../settings/MicrophonePage";
import { ACCENTS, CheckIcon, KeyCaps, Meter, PillButton, Tile } from "../ui";
import { cx } from "../ui/cx";

export type Tour = NonNullable<ShellState["onboarding"]>;
export interface StepView { badge?: string; title: string; body: ReactNode; extra?: ReactNode }

export const tourAction = (action: OnboardingAction) => postPage({ kind: "onboarding", action });
/** The pointer is on the tour card, so the notch is collapsed: open it too (Settings is exempt from the idle close). */
const openSettings = (section: SettingsSection) => {
  postPage({ kind: "notch.tab", tab: "settings" });
  postPage({ kind: "settings.section", section });
  postPage({ kind: "notch.open", open: true });
};
const gesture = (trigger: "hold" | "doubleTap") => (trigger === "doubleTap" ? "Double-tap" : "Hold");

const BARS = 16;
const HEARD_LEVEL = 0.15;

const MIC_ACCESS = {
  allowed: "Windows lets OpenClicky use the microphone.",
  denied: "Windows is blocking desktop apps from the microphone. Turn on “Let desktop apps access your microphone”, then check again.",
  unknown: "I couldn't read the Windows setting. If talking doesn't work, check it there.",
} as const;
const PHASE: Partial<Record<ShellState["status"]["phase"], string>> = { listening: "Listening…", thinking: "Thinking…", speaking: "Speaking…" };
const DRIVE_STEPS = ["Click the green button", "Click into the text box", "Type hello"] as const;

/** Always on the page (an empty live region until something happens), so the tick is announced when it appears. */
function Status({ done, pending, children }: { done: boolean; pending?: string; children: string }) {
  return <p role="status" className={cx("onboarding__status", done && "is-done")}>{done ? <><CheckIcon /> {children}</> : pending}</p>;
}

function Note({ text, children }: { text: string; children?: ReactNode }) {
  return <div className="onboarding__note"><p>{text}</p>{children}</div>;
}

const Privacy = ({ children }: { children: string }) => <p className="onboarding__privacy">{children}</p>;

function MicCheck({ state }: { state: ShellState }) {
  const m = state.settings.microphone;
  const level = useMicLevel();
  const [heard, setHeard] = useState(false); // page-side: resets whenever the step is left
  useEffect(() => { if (level >= HEARD_LEVEL) setHeard(true); }, [level]);
  const device = m.devices.find((d) => d.id === m.deviceId)?.name ?? "the Windows default microphone";
  return (
    <>
      <p className="onboarding__line">{`Listening with ${device}`}</p>
      <div className="onboarding__meter"><Meter segments value={Math.min(BARS, Math.ceil(level * BARS))} max={BARS} label="Microphone level" /></div>
      <div className="onboarding__actions">
        <PillButton size="sm" onClick={() => act("testMic")}>Test microphone</PillButton>
        <PillButton size="sm" variant="outline" onClick={() => openSettings("microphone")}>Change microphone</PillButton>
      </div>
      <Status done={heard}>I can hear you.</Status>
    </>
  );
}

/** Copy and controls of the current step; the card around it (progress, title, footer) is in Onboarding.tsx. */
export function stepView(state: ShellState, tour: Tour): StepView {
  const s = state.settings;
  const { stepDone: done, busy } = tour;
  const keys = (k: keyof ShellState["hotkeys"]) => <KeyCaps keys={state.hotkeys[k]} />;
  const verb = (k: "text" | "dictate" | "handsFree") => gesture(s.shortcuts[k].trigger);
  switch (tour.step) {
    case "welcome":
      return {
        title: "Hi, I'm OpenClicky.",
        body: "I live at the top of your screen and help with whatever you're looking at. Talk to me, type to me, or let me show you things right on your screen. The tour takes about three minutes, and you can skip it at any point.",
      };
    case "colour":
      return {
        title: "Pick my colour",
        body: "It's the colour of my cursor and of everything I draw. You can change it later in Settings → Cursor.",
        extra: (
          <div role="group" aria-label="Colour" className="onboarding__tiles">
            {ACCENTS.map((a) => (
              <Tile key={a.hex} icon={<GlowingCursor colour={a.hex} />} name={a.name}
                selected={a.hex.toLowerCase() === s.cursor.colour.toLowerCase()} onClick={() => patch("cursor.colour", a.hex)} />
            ))}
          </div>
        ),
      };
    case "microphone":
      return {
        badge: "Permission 1 of 2",
        title: "Let me hear you",
        body: "I only listen while you hold the talk keys or dictate. Windows decides whether desktop apps may use the microphone, and you choose there.",
        extra: (
          <>
            <p role="status" className="onboarding__status">{MIC_ACCESS[s.agents.reach.find((r) => r.id === "microphone")?.state ?? "unknown"]}</p>
            <Privacy>Dictation always turns speech into text on this PC. So does Talk with ChatGPT or Windows speech; with an OpenRouter key and Windows speech off, the recording goes to OpenRouter.</Privacy>
            <div className="onboarding__actions">
              <PillButton size="sm" onClick={() => act("openPrivacySetting", "microphone")}>Open microphone settings</PillButton>
              <PillButton size="sm" variant="outline" onClick={() => tourAction("recheck")}>Check again</PillButton>
            </div>
          </>
        ),
      };
    case "screen":
      return {
        badge: "Permission 2 of 2",
        title: "Let me see what you see",
        body: "Windows doesn't ask before desktop apps take screenshots, so there is nothing to switch on. I take them only when you talk to me or a task runs: up to four displays, with my own windows left out. They go with your words to your model provider (ChatGPT or OpenRouter) and nowhere else.",
        extra: (
          <div className="onboarding__actions">
            <PillButton size="sm" onClick={() => tourAction("openScreenSettings")}>Open Windows screen settings</PillButton>
          </div>
        ),
      };
    case "micCheck":
      return {
        title: "First, can I hear you?",
        body: "Press Test and say something. The bars move when I can hear you. The test stays on this PC.",
        extra: <MicCheck state={state} />,
      };
    case "voiceHello":
      return {
        title: "This is how you talk to me",
        body: <>{talkGesture(state)} {keys("talk")}, say {"“"}Hi, I'm …{"”"} with your name, then let go. I'll answer out loud.</>,
        extra: (
          <>
            <Status done={done} pending={PHASE[state.status.phase] ?? "Waiting for you…"}>Nice to meet you!</Status>
            {!s.account.signedInAs && !s.account.openRouterKeySet ? (
              <Note text="Connect ChatGPT or add an OpenRouter key first, so I can answer.">
                <PillButton size="sm" onClick={() => openSettings("account")}>Open Account settings</PillButton>
              </Note>
            ) : null}
            <Privacy>Your words and screenshots go to your model provider.</Privacy>
          </>
        ),
      };
    case "drawDemo":
      return {
        title: "I can draw on your screen",
        body: "When it helps, I sketch right over your apps: circles, arrows and notes that never change the app underneath. This sample runs on this PC and sends nothing.",
        extra: (
          <>
            <div className="onboarding__actions">
              <PillButton size="sm" variant="primary" disabled={busy} onClick={() => tourAction("drawDemo")}>Show me</PillButton>
            </div>
            <Status done={done}>Press Esc to wipe it.</Status>
          </>
        ),
      };
    case "circleToAsk":
      return {
        title: "Circle anything, then ask",
        body: <>{talkGesture(state)} {keys("talk")} and, while you talk, circle or underline something with your mouse. Ask {"“"}What's this?{"”"} and I'll know what you mean.</>,
        extra: (
          <>
            {!s.cursor.screenHighlight ? (
              <Note text="Screen highlight is off.">
                <PillButton size="sm" onClick={() => patch("cursor.screenHighlight", true)}>Turn it on</PillButton>
              </Note>
            ) : null}
            <Privacy>Your marks, the text under them and the screenshot go with the question to your model provider.</Privacy>
            <Status done={done}>Got it.</Status>
          </>
        ),
      };
    case "driveDemo":
      return {
        title: "I can use your mouse and keyboard",
        body: "For bigger jobs I can click and type for you, one step at a time. Practise on a test window: nothing else on your PC is touched and no model is called.",
        extra: (
          <>
            <ul className="onboarding__checklist">
              {DRIVE_STEPS.map((t) => (
                <li key={t}><span className={cx("onboarding__check", done && "is-done")} aria-hidden="true">{done ? <CheckIcon /> : null}</span>{t}</li>
              ))}
            </ul>
            <Note text="I ask before I start and before every step, whatever your access setting. Move the mouse or press a key to take over; Esc stops." />
            <div className="onboarding__actions">
              <PillButton size="sm" variant="primary" disabled={busy} onClick={() => tourAction("driveDemo")}>Start the practice run</PillButton>
            </div>
            <Status done={done}>All three done, checked in the test window.</Status>
          </>
        ),
      };
    case "textMode":
      return {
        title: "Rather type?",
        body: <>{verb("text")} {keys("text")} and a text box opens up here. Type your question and press Enter.</>,
        extra: <Status done={done}>That's the one.</Status>,
      };
    case "dictation":
      return {
        title: "Dictate into any app",
        body: <>Click into a text box in another app, such as Notepad. {verb("dictate")} {keys("dictate")}, speak, then let go: your words appear at the cursor. {verb("handsFree")} {keys("handsFree")} to keep dictating hands-free.</>,
        extra: (
          <>
            <Privacy>Your voice becomes text on this PC. With Tidy up with AI on, the words and the text around the cursor go to your model provider.</Privacy>
            {s.microphone.whisper !== "ready" ? (
              <Note text="Dictation needs the local voice. Download it in Settings → Microphone.">
                <PillButton size="sm" onClick={() => openSettings("microphone")}>Open Microphone settings</PillButton>
              </Note>
            ) : null}
            <Status done={done}>Nicely done.</Status>
          </>
        ),
      };
    case "finale":
      return {
        title: "You're all set",
        body: "Hover the bar at the top of your screen for Home, Chat and Settings. You can take this tour again from Settings → General.",
      };
  }
}
