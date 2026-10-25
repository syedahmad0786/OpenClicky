import { describe, expect, it } from "vitest";
import { PageSession, PASSWORD_REFUSAL } from "../../../chrome-extension/page.mjs";

// Scripted CDP: responders by method (value or function of params); every call is recorded.
function fake(responders = {}) {
  const calls = [];
  const send = async (method, params = {}) => {
    calls.push({ method, params });
    const r = responders[method];
    return typeof r === "function" ? r(params) : (r ?? {});
  };
  return { send, calls, of: (m) => calls.filter((c) => c.method === m) };
}

const ax = (nodeId, role, name, backend, extra = {}) => ({
  nodeId, ignored: false, role: { type: "role", value: role }, name: { type: "computedString", value: name },
  backendDOMNodeId: backend, childIds: [], ...extra,
});
const page = (...kids) => [ax("1", "main", "Page", 1, { childIds: kids.map((k) => k.nodeId) }), ...kids];
const buttonPage = { nodes: page(ax("2", "button", "Save", 5)) };
const quad = { quads: [[10, 20, 30, 20, 30, 40, 10, 40]] };
const passwordNode = { node: { nodeName: "INPUT", attributes: ["id", "p", "type", "Password"] } };

describe("PageSession", () => {
  it("prepare enables domains and focus emulation", async () => {
    const f = fake({ "Page.getFrameTree": { frameTree: { frame: { id: "M" } } } });
    await new PageSession(f.send).prepare();
    const methods = f.calls.map((c) => c.method);
    for (const m of ["Page.enable", "DOM.enable", "Runtime.enable", "Network.enable", "Accessibility.enable"]) expect(methods).toContain(m);
    expect(f.of("Emulation.setFocusEmulationEnabled")[0].params).toEqual({ enabled: true });
    expect(f.of("Page.setWebLifecycleState")[0].params).toEqual({ state: "active" });
  });

  it("click dispatches press and release at the quad centre", async () => {
    const f = fake({ "Accessibility.getFullAXTree": buttonPage, "DOM.resolveNode": { object: { objectId: "o5" } }, "DOM.getContentQuads": quad });
    const s = new PageSession(f.send);
    await s.snapshot({});
    expect((await s.click({ ref: "e1" })).text).toBe("Clicked e1.");
    expect(f.of("DOM.resolveNode")[0].params.backendNodeId).toBe(5);
    const mouse = f.of("Input.dispatchMouseEvent").map((c) => c.params);
    expect(mouse.map((m) => m.type)).toEqual(["mouseMoved", "mousePressed", "mouseReleased"]);
    for (const m of mouse) expect([m.x, m.y]).toEqual([20, 30]);
    expect(mouse[1]).toMatchObject({ button: "left", clickCount: 1 });
  });

  it("click on a removed node asks for a new snapshot", async () => {
    const f = fake({
      "Accessibility.getFullAXTree": buttonPage,
      "DOM.resolveNode": () => { throw new Error("No node with given id found"); },
    });
    const s = new PageSession(f.send);
    await s.snapshot({});
    await expect(s.click({ ref: "e1" })).rejects.toThrow("That element is gone. Take a new snapshot.");
    expect(f.of("Input.dispatchMouseEvent")).toHaveLength(0);
  });

  it("click under a cover is refused without dismiss", async () => {
    const f = fake({
      "Accessibility.getFullAXTree": buttonPage, "DOM.resolveNode": { object: { objectId: "o5" } }, "DOM.getContentQuads": quad,
      "Runtime.callFunctionOn": (p) => ({ result: { value: p.functionDeclaration.includes("elementFromPoint") ? true : undefined } }),
    });
    const s = new PageSession(f.send);
    await s.snapshot({});
    await expect(s.click({ ref: "e1" })).rejects.toThrow("Something is covering that element. Try dismiss: true or close it first.");
    expect(f.of("Input.dispatchMouseEvent")).toHaveLength(0);
  });

  it("fill refuses a password input", async () => {
    const f = fake({
      "Accessibility.getFullAXTree": { nodes: page(ax("2", "textbox", "Password", 7)) },
      "DOM.resolveNode": { object: { objectId: "o7" } },
      "DOM.describeNode": passwordNode,
    });
    const s = new PageSession(f.send);
    await s.snapshot({});
    await expect(s.fill({ ref: "e1", text: "hunter2" })).rejects.toThrow(PASSWORD_REFUSAL);
    expect(PASSWORD_REFUSAL).toBe("Refused: that is a password field. Ask the user to type it themselves.");
    expect(f.of("Input.insertText")).toHaveLength(0);
  });

  it("fill re-checks focus before inserting text", async () => {
    const f = fake({
      "Accessibility.getFullAXTree": { nodes: page(ax("2", "textbox", "Name", 7)) },
      "DOM.resolveNode": { object: { objectId: "o7" } },
      "DOM.describeNode": { node: { nodeName: "INPUT", attributes: ["type", "text"] } },
      "Runtime.callFunctionOn": (p) => ({ result: { value: p.functionDeclaration.startsWith("function fieldKind") ? "text" : undefined } }),
      "Runtime.evaluate": { result: { value: "password" } }, // a focus handler moved focus to a password field
    });
    const s = new PageSession(f.send);
    await s.snapshot({});
    await expect(s.fill({ ref: "e1", text: "hunter2" })).rejects.toThrow(PASSWORD_REFUSAL);
    expect(f.of("Input.insertText")).toHaveLength(0);
  });

  it("type refuses a focused password field", async () => {
    const f = fake({ "Runtime.evaluate": { result: { value: "password" } } });
    const s = new PageSession(f.send);
    await expect(s.type({ text: "hunter2" })).rejects.toThrow(PASSWORD_REFUSAL);
    expect(f.of("Input.insertText")).toHaveLength(0);
    const g = fake({ "Runtime.evaluate": { result: { value: "none" } } });
    await expect(new PageSession(g.send).type({ text: "x" })).rejects.toThrow("Nothing editable has focus. Click a field first.");
  });

  it("press Enter needs a snapshot first", async () => {
    const f = fake({ "Accessibility.getFullAXTree": buttonPage });
    const s = new PageSession(f.send);
    await expect(s.press({ key: "Enter" })).rejects.toThrow("Take a snapshot of this page first.");
    expect(f.of("Input.dispatchKeyEvent")).toHaveLength(0);
    await s.snapshot({});
    await s.press({ key: "Enter" });
    const keys = f.of("Input.dispatchKeyEvent").map((c) => c.params);
    expect(keys.map((k) => k.type)).toEqual(["keyDown", "keyUp"]);
    expect(keys[0]).toMatchObject({ key: "Enter", code: "Enter", windowsVirtualKeyCode: 13, text: "\r" });
    await s.press({ key: "Tab" }); // Tab needs no snapshot
    s.onEvent("Page.frameNavigated", { frame: { id: "F" } });
    await expect(s.press({ key: "Backspace" })).rejects.toThrow("Take a snapshot of this page first.");
  });

  it("main-frame navigation retires refs", async () => {
    const f = fake({ "Accessibility.getFullAXTree": buttonPage, "DOM.resolveNode": { object: { objectId: "o5" } }, "DOM.getContentQuads": quad });
    const s = new PageSession(f.send);
    await s.snapshot({});
    s.onEvent("Page.frameNavigated", { frame: { id: "child", parentId: "F" } });
    expect(s.epoch).toBe(0);
    s.onEvent("Page.frameNavigated", { frame: { id: "F" } });
    expect(s.epoch).toBe(1);
    await expect(s.click({ ref: "e1" })).rejects.toThrow("That ref is from an earlier page. Take a new snapshot.");
  });

  it("dialog blocks other actions", async () => {
    const f = fake({ "Accessibility.getFullAXTree": buttonPage });
    const s = new PageSession(f.send);
    s.onEvent("Page.javascriptDialogOpening", { type: "alert", message: "Hi" });
    expect(s.dialog).toEqual({ type: "alert", message: "Hi" });
    await expect(s.click({ ref: "e1" })).rejects.toThrow('A page dialog is open (alert): "Hi". Answer it with dialog first.');
    await expect(s.extract()).rejects.toThrow("A page dialog is open");
    await s.snapshot({});
    await s.answerDialog({ accept: true });
    expect(f.of("Page.handleJavaScriptDialog")[0].params).toMatchObject({ accept: true });
    s.onEvent("Page.javascriptDialogClosed", {});
    expect(s.dialog).toBe(null);
  });

  it("click_at needs a screenshot in this epoch", async () => {
    const f = fake({
      "Page.getLayoutMetrics": { cssVisualViewport: { pageX: 0, pageY: 100, clientWidth: 800, clientHeight: 600 } },
      "Runtime.evaluate": { result: { value: 2 } },
      "Page.captureScreenshot": { data: "SU1H" },
    });
    const s = new PageSession(f.send);
    await expect(s.clickAt({ x: 5, y: 6 })).rejects.toThrow("Take a screenshot of this page first.");
    const shot = await s.screenshot();
    expect(shot.image).toEqual({ data: "SU1H", mimeType: "image/jpeg" });
    expect(f.of("Page.captureScreenshot")[0].params).toEqual({
      format: "jpeg", quality: 70, clip: { x: 0, y: 100, width: 800, height: 600, scale: 0.5 },
    });
    await s.clickAt({ x: 5, y: 6 });
    expect(f.of("Input.dispatchMouseEvent").map((c) => c.params.type)).toEqual(["mouseMoved", "mousePressed", "mouseReleased"]);
    s.onEvent("Page.frameNavigated", { frame: { id: "F" } });
    await expect(s.hoverAt({ x: 5, y: 6 })).rejects.toThrow("Take a screenshot of this page first.");
  });

  it("snapshot hides password field values", async () => {
    const f = fake({
      "Accessibility.getFullAXTree": { nodes: page(
        ax("2", "textbox", "Password", 7, { value: { type: "string", value: "hunter2" } }),
        ax("3", "textbox", "User", 8, { value: { type: "string", value: "alice" } }),
      ) },
      "DOM.describeNode": (p) => (p.backendNodeId === 7 ? passwordNode : { node: { nodeName: "INPUT", attributes: ["type", "text"] } }),
    });
    const s = new PageSession(f.send);
    const { text } = await s.snapshot({});
    expect(text).not.toContain("hunter2");
    expect(text).toContain('textbox "Password" [e1]');
    expect(text).not.toMatch(/Password" \[e1\] value=/);
    expect(text).toContain('textbox "User" [e2] value="alice"');
    expect(s.lines).toEqual(text.split("\n"));
    expect(s.diffSince(s.lines)).toBe("No change since your last look.");
  });

  it("javascript runs guarded in an isolated world and cuts the result", async () => {
    const f = fake({
      "Page.createIsolatedWorld": (p) => ({ executionContextId: p.frameId === "F" ? 42 : 43 }),
      "Page.getFrameTree": { frameTree: { frame: { id: "F" }, childFrames: [{ frame: { id: "C", parentId: "F" } }] } },
      "Runtime.evaluate": (p) => ({ result: { value: p.expression.startsWith("(async") ? "y".repeat(30000) : undefined } }),
    });
    const s = new PageSession(f.send);
    s.onEvent("Page.frameNavigated", { frame: { id: "F" } });
    const { text } = await s.javascript({ code: "return document.title" });
    expect(f.of("Page.createIsolatedWorld").map((c) => c.params)).toEqual([
      { frameId: "F", worldName: "openclicky" }, { frameId: "C", worldName: "openclicky" },
    ]);
    const [guard, childGuard, run] = f.of("Runtime.evaluate").map((c) => c.params);
    expect(guard).toMatchObject({ contextId: 42 });
    expect(guard.expression).toContain("Not available to agents.");
    expect(childGuard).toMatchObject({ contextId: 43, expression: guard.expression });
    expect(run).toMatchObject({ contextId: 42, awaitPromise: true, returnByValue: true, timeout: 15000 });
    expect(run.expression).toBe("(async () => { return document.title })()");
    expect(text.length).toBe(20000);
  });
});
