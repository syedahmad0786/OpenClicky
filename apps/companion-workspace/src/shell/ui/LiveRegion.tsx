import { useEffect, useState } from "react";

type Message = { text: string; n: number };
let post: ((text: string) => void) | null = null;

/** Say something once through the page's single polite live region (status changes only, never streamed text). */
export function announce(text: string): void {
  const clean = text.trim();
  if (clean) post?.(clean);
}

/**
 * The one polite live region: job status changes, new cards and finished captions. Each message is a new node, so
 * the same words twice are read twice. Mounted once, by ShellApp.
 */
export function LiveRegion() {
  const [message, setMessage] = useState<Message>({ text: "", n: 0 });
  useEffect(() => {
    post = (text) => setMessage((m) => ({ text, n: m.n + 1 }));
    return () => { post = null; };
  }, []);
  return <div role="status" aria-live="polite" className="oc-sr-only"><span key={message.n}>{message.text}</span></div>;
}
