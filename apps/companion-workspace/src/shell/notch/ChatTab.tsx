import { useEffect, useRef, type KeyboardEvent } from "react";
import { postPage } from "../bridge";
import { talkGesture, type ShellState } from "../contract";
import { PillButton } from "../ui";
import { MicIcon } from "./icons";

const MAX_TEXT = 8_000; // host limit for text.submit

interface ChatTabProps {
  state: ShellState; draft: string; onDraft: (text: string) => void;
  /** The host asked for the field (composer.focus, text mode); call onComposerFocused once it has focus. */
  focusComposer?: boolean; onComposerFocused?: () => void;
}

export function ChatTab({ state, draft, onDraft, focusComposer, onComposerFocused }: ChatTabProps) {
  const { rows } = state.chat;
  const talk = state.hotkeys.talk;
  const list = useRef<HTMLDivElement>(null);
  const focused = useRef(false);
  const input = useRef<HTMLTextAreaElement>(null);
  const tail = rows.at(-1);

  // A field that unmounts while focused (tab switch, collapse) fires no blur: hand the keyboard focus back here.
  useEffect(() => () => { if (focused.current) postPage({ kind: "surface.focus", wanted: false }); }, []);
  useEffect(() => { if (list.current) list.current.scrollTop = list.current.scrollHeight; }, [rows.length, tail?.text]);
  useEffect(() => {
    if (!focusComposer) return;
    input.current?.focus();
    onComposerFocused?.();
  }, [focusComposer, onComposerFocused]);

  const submit = () => {
    const text = draft.trim();
    if (!text) return;
    postPage({ kind: "text.submit", text });
    onDraft("");
  };
  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key === "Enter" && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      submit();
    } else if (event.key === "Escape") {
      event.currentTarget.blur(); // onBlur gives the focus back
    }
  };

  return (
    <>
      <div className="notch-body notch-chat__list" ref={list} role="log" aria-live="polite" aria-label="Conversation">
        {rows.length === 0 ? (
          <div className="notch-chat__empty">
            <strong>No messages yet</strong>
            <span>Say something, or type below.</span>
          </div>
        ) : rows.map((row) => <div key={row.id} className="notch-chat__row" data-role={row.role}>{row.text}</div>)}
      </div>
      <div className="notch-chat__bar">
        <PillButton variant="primary" className="notch-chat__talk" icon={<MicIcon />} onClick={() => postPage({ kind: "talk.toggle" })}>
          {talk.length ? `${talkGesture(state)} ${talk.join(" + ")} to talk` : "Talk"}
        </PillButton>
        <textarea
          ref={input} className="notch-chat__input" rows={1} maxLength={MAX_TEXT} placeholder="Ask OpenClicky…" aria-label="Message OpenClicky"
          value={draft} onChange={(e) => onDraft(e.target.value)} onKeyDown={onKeyDown}
          onPointerDown={() => postPage({ kind: "surface.focus", wanted: true })}
          onFocus={() => { focused.current = true; postPage({ kind: "surface.focus", wanted: true }); }}
          onBlur={() => { focused.current = false; postPage({ kind: "surface.focus", wanted: false }); }}
        />
      </div>
    </>
  );
}
