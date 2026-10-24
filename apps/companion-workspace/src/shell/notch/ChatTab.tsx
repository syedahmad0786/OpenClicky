import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import { postPage } from "../bridge";
import type { ShellState } from "../contract";
import { PillButton } from "../ui";
import { MicIcon } from "./icons";

const MAX_TEXT = 8_000; // host limit for text.submit

export function ChatTab({ state }: { state: ShellState }) {
  const { rows } = state.chat;
  const talk = state.hotkeys.talk;
  const [draft, setDraft] = useState("");
  const list = useRef<HTMLDivElement>(null);
  const tail = rows.at(-1);

  useEffect(() => { if (list.current) list.current.scrollTop = list.current.scrollHeight; }, [rows.length, tail?.text]);

  const submit = () => {
    const text = draft.trim();
    if (!text) return;
    postPage({ kind: "text.submit", text });
    setDraft("");
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
          {talk.length ? `Hold ${talk.join(" + ")} to talk` : "Talk"}
        </PillButton>
        <textarea
          className="notch-chat__input" rows={1} maxLength={MAX_TEXT} placeholder="Ask OpenClicky…" aria-label="Message OpenClicky"
          value={draft} onChange={(e) => setDraft(e.target.value)} onKeyDown={onKeyDown}
          onPointerDown={() => postPage({ kind: "surface.focus", wanted: true })}
          onFocus={() => postPage({ kind: "surface.focus", wanted: true })}
          onBlur={() => postPage({ kind: "surface.focus", wanted: false })}
        />
      </div>
    </>
  );
}
