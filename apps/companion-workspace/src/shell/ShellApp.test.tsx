import { afterEach, expect, it, vi } from "vitest";
import { act, cleanup, render, screen } from "@testing-library/react";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import ShellApp from "./ShellApp";

const fixture = JSON.parse(readFileSync(resolve(__dirname, "../../../../tests/fixtures/shell-state.v1.json"), "utf8"));
afterEach(() => { cleanup(); delete window.chrome; });

function mockHost() {
  const listeners = new Set<(event: MessageEvent) => void>();
  const postMessage = vi.fn();
  window.chrome = { webview: { postMessage,
    addEventListener: (_, listener) => { listeners.add(listener); },
    removeEventListener: (_, listener) => { listeners.delete(listener); },
  } };
  const send = (data: unknown) => act(() => { for (const l of listeners) l(new MessageEvent("message", { data })); });
  return { listeners, postMessage, send };
}

it("ShellApp renders nothing until shell.state arrives, then the collapsed notch", () => {
  const host = mockHost();
  const view = render(<ShellApp />);
  expect(host.postMessage).toHaveBeenCalledWith({ kind: "bootstrap.request" });
  expect(view.container).toBeEmptyDOMElement();

  host.send({ kind: "session.bootstrap", token: "0123456789abcdef0123456789abcdef" });
  expect(host.postMessage).toHaveBeenCalledWith({ kind: "ready" });
  expect(view.container).toBeEmptyDOMElement();

  host.send({ kind: "shell.state", state: { ...fixture, version: 2 } });
  expect(view.container).toBeEmptyDOMElement();

  host.send({ kind: "shell.state", state: { ...fixture, notch: { tab: "home", open: false } } });
  const notch = screen.getByLabelText("OpenClicky");
  expect(notch).toHaveClass("notch", "notch--collapsed");
  expect(notch).toHaveAttribute("data-region");

  view.unmount();
  expect(host.listeners.size).toBe(0);
});
