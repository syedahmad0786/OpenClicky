import { useEffect, useState } from "react";
import { listenToHost } from "../bridge";
import "./captions.css";

export const CLEAR_DELAY_MS = 1_500; // caption.clear → fade starts
export const FADE_MS = 250;
/** A word that is never followed by caption.clear (a stray word after Stop) still goes away. */
export const IDLE_CLEAR_MS = 10_000;
const KEEP_CHARS = 400; // far more than two lines; older words scroll out above

/**
 * Spoken words, bottom centre, while the host speaks with captions on. Debounced: a word cancels a pending clear.
 * Not announced: the words are the speech the user is already hearing.
 */
export function CaptionsBar() {
  const [text, setText] = useState("");
  const [fading, setFading] = useState(false);

  useEffect(() => {
    let clearTimer: number | undefined, fadeTimer: number | undefined, idleTimer: number | undefined;
    const cancel = () => { window.clearTimeout(clearTimer); window.clearTimeout(fadeTimer); clearTimer = fadeTimer = undefined; };
    const clear = () => {
      if (clearTimer !== undefined || fadeTimer !== undefined) return; // already clearing
      clearTimer = window.setTimeout(() => {
        clearTimer = undefined;
        setFading(true);
        fadeTimer = window.setTimeout(() => { fadeTimer = undefined; setText(""); setFading(false); }, FADE_MS);
      }, CLEAR_DELAY_MS);
    };
    const stop = listenToHost((message) => {
      if (message.kind === "caption.word") {
        const word = message.text.trim();
        if (!word) return;
        cancel();
        setFading(false);
        setText((current) => {
          const next = current ? `${current} ${word}` : word;
          return next.length <= KEEP_CHARS ? next : next.slice(next.indexOf(" ", next.length - KEEP_CHARS) + 1);
        });
        window.clearTimeout(idleTimer);
        idleTimer = window.setTimeout(clear, IDLE_CLEAR_MS);
      } else if (message.kind === "caption.clear") clear();
    });
    return () => { stop(); cancel(); window.clearTimeout(idleTimer); };
  }, []);

  if (!text) return null;
  return (
    <div className={fading ? "captions captions--fading" : "captions"} data-region>
      <p className="captions__text">{text}</p>
    </div>
  );
}
