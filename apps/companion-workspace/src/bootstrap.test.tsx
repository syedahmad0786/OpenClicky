import { afterEach, expect, it, vi } from "vitest";
import { render, screen, waitFor, cleanup } from "@testing-library/react";
import Workspace from "./App";

vi.mock("@copilotkit/react-core", () => ({ useCopilotAction: () => {}, useCopilotReadable: () => {} }));
vi.mock("@copilotkit/react-core/v2", () => ({
  CopilotKit: ({ children }: { children: React.ReactNode }) => children,
  CopilotChat: () => <div>Conversation ready</div>,
  useAgent: () => ({ agent: { messages: [] }, isReady: false }),
}));
afterEach(() => { cleanup(); delete window.chrome; });

it("requests bootstrap after subscribing even when the earlier navigation message was lost", async () => {
  const listeners = new Set<(event: MessageEvent) => void>();
  const postMessage = vi.fn((value: unknown) => {
    if ((value as { kind: string }).kind !== "bootstrap.request") return;
    expect(listeners.size).toBe(1);
    for (const listener of listeners) listener(new MessageEvent("message", {
      data: { kind: "session.bootstrap", token: "0123456789abcdef0123456789abcdef" },
    }));
  });
  window.chrome = { webview: { postMessage,
    addEventListener: (_, listener) => { listeners.add(listener); },
    removeEventListener: (_, listener) => { listeners.delete(listener); },
  } };
  const view = render(<Workspace />);
  await waitFor(() => expect(screen.getByText("Conversation ready")).toBeVisible());
  expect(screen.queryByText("Connecting to the local workspace…")).toBeNull();
  expect(postMessage).toHaveBeenCalledWith({ kind: "bootstrap.request" });
  view.unmount();
  expect(listeners.size).toBe(0);
});
