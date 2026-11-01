import { parseHostMessage, type HostMessage, type PageMessage } from "./contract";

declare global {
  interface Window { chrome?: { webview?: { postMessage(value: unknown): void; addEventListener(type: "message", listener: (event: MessageEvent) => void): void; removeEventListener(type: "message", listener: (event: MessageEvent) => void): void } } }
}

export function postPage(message: PageMessage): void {
  window.chrome?.webview?.postMessage(message);
}

export function listenToHost(listener: (message: HostMessage) => void): () => void {
  const receive = (event: MessageEvent) => {
    const parsed = parseHostMessage(event.data);
    if (parsed) listener(parsed);
  };
  const webview = window.chrome?.webview;
  if (webview) {
    webview.addEventListener("message", receive);
    return () => webview.removeEventListener("message", receive);
  }
  if (import.meta.env.DEV) {
    const receiveBrowserFixture = (event: MessageEvent) => {
      if (event.source === window && event.origin === window.location.origin) receive(event);
    };
    window.addEventListener("message", receiveBrowserFixture);
    return () => window.removeEventListener("message", receiveBrowserFixture);
  }
  return () => {};
}
