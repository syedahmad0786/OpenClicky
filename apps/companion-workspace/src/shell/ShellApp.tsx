import { useEffect, useState } from "react";
import { listenToHost, postPage } from "./bridge";
import type { ShellState } from "./contract";

export default function ShellApp() {
  const [state, setState] = useState<ShellState | null>(null);
  useEffect(() => {
    const stop = listenToHost((message) => {
      if (message.kind === "session.bootstrap") postPage({ kind: "ready" });
      else if (message.kind === "shell.state") setState(message.state);
    });
    // Navigation can finish before React subscribes to native messages.
    postPage({ kind: "bootstrap.request" });
    return stop;
  }, []);
  if (!state) return null;
  // Placeholder: the real notch replaces this in later tasks.
  return <div className="notch notch--collapsed" data-region aria-label="OpenClicky" />;
}
