import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import { postPage } from "../bridge";
import { talkGesture, type ChatRow, type DrawingAction, type ShellState } from "../contract";
import { PillButton } from "../ui";
import { MicIcon } from "./icons";

const MAX_TEXT = 8_000; // host limit for text.submit

/**
 * The host's streaming reply row (ShellController.StreamAssistant): chat.delta appends to it, and when the reply is
 * complete the host removes it and pushes the finished reply as an ordinary row. While it is there the log is
 * aria-busy, so a screen reader reads the finished reply once instead of every chunk.
 */
export const isStreamingRow = (row: ChatRow) => row.role === "assistant" && row.id.startsWith("live-stream-");

const REPLAY_MODES: { action: DrawingAction; label: string; name: string }[] = [
  { action: "replayWindow", label: "Window", name: "Replay in a window" },
  { action: "replayFullScreen", label: "Full screen", name: "Replay full screen" },
  { action: "replayBoard", label: "Board", name: "Replay on a board" },
];

/** A saved drawing in the conversation: its caption, and Replay (in a window, full screen or on a board), Print and Remove. */
function DrawingRow({ row }: { row: ChatRow }) {
  const [choosing, setChoosing] = useState(false);
  const replay = useRef<HTMLButtonElement>(null);
  const act = (action: DrawingAction) => postPage({ kind: "drawing.action", id: row.drawingId!, action });
  // Esc closes the mode choice and puts focus back on Replay.
  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key !== "Escape" || !choosing) return;
    event.preventDefault();
    setChoosing(false);
    replay.current?.focus();
  };
  return (
    <div className="notch-chat__drawing" onKeyDown={onKeyDown}>
      <div className="notch-chat__row" data-role={row.role}>{row.text}</div>
      <div className="notch-chat__drawing-actions">
        <PillButton size="sm" variant="outline" ref={replay} aria-expanded={choosing} aria-haspopup="true" onClick={() => setChoosing((open) => !open)}>Replay</PillButton>
        <PillButton size="sm" variant="outline" onClick={() => act("print")}>Print</PillButton>
        <PillButton size="sm" variant="outline" title="Remove from chat" onClick={() => act("remove")}>Remove</PillButton>
      </div>
      {choosing && (
        <div className="notch-chat__drawing-actions" role="group" aria-label="Replay mode">
          {REPLAY_MODES.map(({ action, label, name }) => (
            <PillButton key={action} size="sm" variant="secondary" aria-label={name} onClick={() => { setChoosing(false); act(action); }}>{label}</PillButton>
          ))}
        </div>
      )}
    </div>
  );
}

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
      <div className="notch-body notch-chat__list" ref={list} role="log" aria-live="polite" aria-label="Conversation"
        aria-busy={rows.some(isStreamingRow) || undefined}>
        {rows.length === 0 ? (
          <div className="notch-chat__empty">
            <strong>No messages yet</strong>
            <span>Say something, or type below.</span>
          </div>
        ) : rows.map((row) => row.drawingId
          ? <DrawingRow key={row.id} row={row} />
          : <div key={row.id} className="notch-chat__row" data-role={row.role}>{row.text}</div>)}
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
