import { beforeEach, describe, expect, it, vi } from "vitest";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

type BridgeReply = { ok: boolean; error?: string; observation?: { revision: number; controls: Array<Record<string, unknown>> }; value?: string; truncated?: boolean };
type BridgeListener = (message: Record<string, unknown>, sender: unknown, reply: (response: BridgeReply) => void) => boolean | void;

const script = readFileSync(resolve(process.cwd(), "../chrome-extension/content-script.js"), "utf8");
let listener: BridgeListener;
let pageWorldPushState: History["pushState"];

function install() {
  const addListener = vi.fn((next: BridgeListener) => { listener = next; });
  Object.defineProperty(window, "chrome", { configurable: true, value: { runtime: { onMessage: { addListener } } } });
  window.eval(script);
  expect(addListener).toHaveBeenCalledOnce();
}

function send(message: Record<string, unknown>) {
  let response: BridgeReply | undefined;
  listener(message, null, value => { response = value; });
  expect(response).toBeDefined();
  return response!;
}

function observe() {
  const response = send({ name: "observe" });
  expect(response.ok).toBe(true);
  return response.observation!;
}

beforeEach(() => {
  document.body.innerHTML = "";
  vi.restoreAllMocks();
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockReturnValue({
    x: 0, y: 0, top: 0, left: 0, right: 120, bottom: 30, width: 120, height: 30, toJSON: () => ({})
  } as DOMRect);
  pageWorldPushState = history.pushState;
  install();
});

describe("Chrome bridge content text entry", () => {
  it.each(["input", "textarea"])("uses the native %s setter and emits a bubbling input event", tag => {
    const control = document.createElement(tag) as HTMLInputElement | HTMLTextAreaElement;
    document.body.append(control);
    const prototype = tag === "input" ? HTMLInputElement.prototype : HTMLTextAreaElement.prototype;
    const native = Object.getOwnPropertyDescriptor(prototype, "value")!;
    let shadowSetterCalls = 0;
    Object.defineProperty(control, "value", {
      configurable: true,
      get: () => native.get!.call(control),
      set: () => { shadowSetterCalls++; },
    });
    const events: InputEvent[] = [];
    control.addEventListener("input", event => events.push(event as InputEvent));
    const observation = observe();

    const result = send({ name: "typeText", revision: observation.revision, elementId: "e1", text: "hello" });

    expect(result).toMatchObject({ ok: true, value: "hello", truncated: false });
    expect(control.value).toBe("hello");
    expect(shadowSetterCalls).toBe(0);
    expect(events).toHaveLength(1);
    expect(events[0]).toMatchObject({ bubbles: true, inputType: "insertText", data: "hello" });
  });

  it("refuses read-only, disabled, password, and unsupported inputs", () => {
    document.body.innerHTML = `<input readonly><textarea disabled></textarea><input type="password"><input type="checkbox">`;
    const observation = observe();

    expect(send({ name: "typeText", revision: observation.revision, elementId: "e1", text: "x" }).error).toContain("read-only");
    expect(send({ name: "typeText", revision: observation.revision, elementId: "e2", text: "x" }).error).toContain("disabled");
    expect(send({ name: "typeText", revision: observation.revision, elementId: "e3", text: "x" }).error).toContain("Password");
    expect(send({ name: "typeText", revision: observation.revision, elementId: "e4", text: "x" }).error).toContain("not supported");
  });

  it("rejects semantic target changes and allows an unchanged target", () => {
    const link = document.createElement("a");
    link.href = "/first";
    link.textContent = "First destination";
    document.body.append(link);
    let clicks = 0;
    link.addEventListener("click", event => { event.preventDefault(); clicks++; });
    let observation = observe();
    link.href = "/second";
    expect(send({ name: "followLink", revision: observation.revision, linkId: "e1" }).error).toContain("element changed");
    expect(clicks).toBe(0);

    link.href = "/first";
    observation = observe();
    link.textContent = "Reused for another destination";
    expect(send({ name: "click", revision: observation.revision, elementId: "e1" }).error).toContain("element changed");
    expect(clicks).toBe(0);

    link.textContent = "First destination";
    observation = observe();
    expect(send({ name: "click", revision: observation.revision, elementId: "e1" })).toEqual({ ok: true });
    expect(clicks).toBe(1);
  });

  it("rejects changed input values and focus-time destination changes", () => {
    const input = document.createElement("input");
    input.name = "message";
    input.value = "draft";
    document.body.append(input);
    let observation = observe();
    input.value = "page changed this";
    expect(send({ name: "typeText", revision: observation.revision, elementId: "e1", text: "send" }).error).toContain("element changed");

    input.value = "a".repeat(2100);
    observation = observe();
    input.value = `${"a".repeat(2099)}b`;
    expect(send({ name: "typeText", revision: observation.revision, elementId: "e1", text: "send" }).error).toContain("element changed");

    input.value = "draft";
    observation = observe();
    input.addEventListener("focus", () => input.setAttribute("aria-label", "Different destination"), { once: true });
    expect(send({ name: "typeText", revision: observation.revision, elementId: "e1", text: "send" }).error).toContain("changed while receiving focus");
    expect(input.value).toBe("draft");
  });

  it("rechecks destination and navigation around focus and input mutation", () => {
    const detached = document.createElement("input");
    detached.name = "message";
    detached.value = "draft";
    document.body.replaceChildren(detached);
    let observation = observe();
    detached.addEventListener("focus", () => {
      const replacement = document.createElement("input");
      replacement.name = "message";
      replacement.value = "draft";
      detached.replaceWith(replacement);
    }, { once: true });
    expect(send({ name: "typeText", revision: observation.revision, elementId: "e1", text: "requested" }).error).toContain("changed while receiving focus");
    expect(detached.value).toBe("draft");

    const focusNavigated = document.createElement("input");
    focusNavigated.value = "draft";
    document.body.replaceChildren(focusNavigated);
    observation = observe();
    focusNavigated.addEventListener("focus", () => pageWorldPushState.call(history, {}, "", "#focus-navigation"), { once: true });
    expect(send({ name: "typeText", revision: observation.revision, elementId: "e1", text: "requested" }).error).toContain("Navigation changed while");
    expect(focusNavigated.value).toBe("draft");

    const inputNavigated = document.createElement("input");
    document.body.replaceChildren(inputNavigated);
    observation = observe();
    inputNavigated.addEventListener("input", () => pageWorldPushState.call(history, {}, "", "#input-navigation"), { once: true });
    expect(send({ name: "typeText", revision: observation.revision, elementId: "e1", text: "requested" }).error).toContain("Navigation changed during text entry");
    expect(inputNavigated.value).toBe("requested");
  });

  it("rejects removed, hidden, and post-mutation stale controls", () => {
    const input = document.createElement("input");
    document.body.append(input);
    let observation = observe();
    input.remove();
    expect(send({ name: "typeText", revision: observation.revision, elementId: "e1", text: "x" }).error).toContain("stale");

    document.body.append(input);
    observation = observe();
    input.style.display = "none";
    expect(send({ name: "typeText", revision: observation.revision, elementId: "e1", text: "x" }).error).toContain("no longer visible");

    input.style.display = "block";
    input.addEventListener("input", () => input.remove(), { once: true });
    observation = observe();
    expect(send({ name: "typeText", revision: observation.revision, elementId: "e1", text: "x" }).error).toContain("changed during text entry");

    const rejectingInput = document.createElement("input");
    document.body.append(rejectingInput);
    rejectingInput.addEventListener("input", () => { rejectingInput.value = "page value"; });
    observation = observe();
    expect(send({ name: "typeText", revision: observation.revision, elementId: "e1", text: "requested" }).error).toContain("did not retain");
  });

  it("invalidates observed elements after browser and same-document navigation", () => {
    document.body.innerHTML = "<input>";
    let observation = observe();
    window.dispatchEvent(new PopStateEvent("popstate"));
    expect(send({ name: "typeText", revision: observation.revision, elementId: "e1", text: "x" }).error).toContain("Navigation changed");

    observation = observe();
    history.pushState({}, "", "#next");
    expect(send({ name: "typeText", revision: observation.revision, elementId: "e1", text: "x" }).error).toContain("Navigation changed");

    observation = observe();
    pageWorldPushState.call(history, {}, "", "#page-world");
    expect(send({ name: "typeText", revision: observation.revision, elementId: "e1", text: "x" }).error).toContain("Navigation changed");
  });

  it("exposes only bounded values from supported non-password text controls", () => {
    const long = "a".repeat(2100);
    document.body.innerHTML = `<input value="${long}"><textarea>draft</textarea><input type="password" value="secret"><input type="checkbox" value="private">`;

    const controls = observe().controls;

    expect(controls[0].value).toBe(long.slice(0, 2000));
    expect(controls[1].value).toBe("draft");
    expect(controls[2]).not.toHaveProperty("value");
    expect(controls[3]).not.toHaveProperty("value");
  });

  it("does not read a password value while building its internal target signature", () => {
    const password = document.createElement("input");
    password.type = "password";
    Object.defineProperty(password, "value", { configurable: true, get: () => { throw new Error("password value read"); } });
    document.body.append(password);

    const control = observe().controls[0];

    expect(control).not.toHaveProperty("value");
  });
});
