import { useEffect, useState } from "react";
import { listenToHost, postPage } from "./bridge";
import { CaptionsBar } from "./captions/CaptionsBar";
import type { ShellState } from "./contract";
import { Notch } from "./notch/Notch";
import { useRegions } from "./notch/useRegions";

const MAX_ROW_TEXT = 32_000; // contract limit for a chat row

/** Streamed assistant text: append to the row if the host still lists it. */
function withDelta(state: ShellState, rowId: string, text: string): ShellState {
  if (!state.chat.rows.some((row) => row.id === rowId)) return state;
  const rows = state.chat.rows.map((row) => (row.id === rowId ? { ...row, text: (row.text + text).slice(0, MAX_ROW_TEXT) } : row));
  return { ...state, chat: { rows } };
}

export default function ShellApp() {
  const [state, setState] = useState<ShellState | null>(null);
  useRegions();
  useEffect(() => {
    const stop = listenToHost((message) => {
      if (message.kind === "session.bootstrap") postPage({ kind: "ready" });
      else if (message.kind === "shell.state") setState(message.state);
      else if (message.kind === "chat.delta") setState((current) => (current ? withDelta(current, message.rowId, message.text) : current));
    });
    // Navigation can finish before React subscribes to native messages.
    postPage({ kind: "bootstrap.request" });
    return stop;
  }, []);
  const theme = state?.theme, accent = state?.accent, reduced = state?.reducedMotion;
  useEffect(() => {
    if (theme === undefined) return;
    const root = document.documentElement;
    root.dataset.theme = theme;
    root.dataset.reducedMotion = String(reduced);
    root.style.setProperty("--accent", accent!);
  }, [theme, accent, reduced]);
  return (
    <>
      {state ? <Notch state={state} /> : null}
      <CaptionsBar />
    </>
  );
}
