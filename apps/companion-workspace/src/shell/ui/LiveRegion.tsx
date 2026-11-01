import { useEffect, useState } from "react";

type Message = { text: string; n: number };
let post: ((text: string) => void) | null = null;
let pending: string[] = [];

/**
 * Say something once through the page's single polite live region (status changes only, never streamed text).
 * Everything said in one tick (one host state can change a job and add its card) is read as one message, in order,
 * without repeats; a later setState in the same batch cannot drop an earlier line.
 */
export function announce(text: string): void {
  const clean = text.trim();
  if (!clean || !post) return;
  if (pending.length === 0) queueMicrotask(flush);
  if (!pending.includes(clean)) pending.push(clean);
}

function flush() {
  const text = pending.join(". ");
  pending = [];
  post?.(text);
}

/**
 * The one polite live region: job status changes, new cards and finished captions. Each message is a new node, so
 * the same words twice are read twice. Mounted once, by ShellApp.
 */
export function LiveRegion() {
  const [message, setMessage] = useState<Message>({ text: "", n: 0 });
  useEffect(() => {
    post = (text) => setMessage((m) => ({ text, n: m.n + 1 }));
    return () => { post = null; pending = []; };
  }, []);
  return <div role="status" aria-live="polite" className="oc-sr-only"><span key={message.n}>{message.text}</span></div>;
}
