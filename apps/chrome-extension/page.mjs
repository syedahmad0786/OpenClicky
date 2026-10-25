// One tab's page operations over the Chrome debugging protocol. `send(method, params)` is the only channel
// to the page and events arrive through onEvent(); nothing here touches chrome.* so tests can script it.
import { RefTable, renderSnapshot, diffSnapshot } from "./snapshot.mjs";
import {
  extractMain, guardMain, fieldKind, focusField, chooseOption, readField, scrollToCenter, isCovered, dismissCover,
  activeField, pageMatches,
} from "./inpage.mjs";

export const PASSWORD_REFUSAL = "Refused: that is a password field. Ask the user to type it themselves.";
const GONE = "That element is gone. Take a new snapshot.";
const COVERED = "Something is covering that element. Try dismiss: true or close it first.";
const NO_FOCUS = "Nothing editable has focus. Click a field first.";
const NEED_SNAPSHOT = "Take a snapshot of this page first.";
const NEED_SHOT = "Take a screenshot of this page first.";
const NOT_KEPT = "The page did not keep that text.";

export const DEFAULT_LIMITS = { snapshotChars: 12000, extractChars: 40000, tables: 20, rows: 200, textChars: 20000, codeChars: 20000, resultChars: 20000 };

const KEYS = {
  Enter: 13, Tab: 9, Escape: 27, ArrowLeft: 37, ArrowUp: 38, ArrowRight: 39, ArrowDown: 40,
  Backspace: 8, Delete: 46, PageUp: 33, PageDown: 34, Home: 36, End: 35,
};
const NEEDS_SNAPSHOT = new Set(["Enter", "Backspace", "Delete"]);

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const norm = (s) => String(s ?? "").replace(/\s+/g, " ").trim();
const attr = (node, name) => {
  const a = node?.attributes ?? [];
  for (let i = 0; i + 1 < a.length; i += 2) if (String(a[i]).toLowerCase() === name) return a[i + 1];
  return undefined;
};
const isInput = (node, type) => String(node?.nodeName ?? "").toUpperCase() === "INPUT"
  && String(attr(node, "type") ?? "").trim().toLowerCase() === type;

/** Absolute http(s) URL, no user-info, at most 2,048 characters; returns the reason it is not, or null. */
export function badUrl(url) {
  if (typeof url !== "string" || !url || url.length > 2048) return "Use an absolute http or https address of at most 2,048 characters.";
  let u;
  try { u = new URL(url); } catch { return "That is not an absolute web address."; }
  if (u.protocol !== "http:" && u.protocol !== "https:") return "Only http and https addresses can be opened.";
  if (u.username || u.password) return "Addresses with a user name or password are not allowed.";
  return null;
}

export class PageSession {
  constructor(send, limits = {}) {
    this.send = send;
    this.limits = { ...DEFAULT_LIMITS, ...limits };
    this.refs = new RefTable();
    this.dialog = null;
    this.lines = null;
    this.mainFrameId = null;
    this._snapEpoch = -1;
    this._shotEpoch = -1;
    this._navPending = false;
    this._inFlight = new Set();
    this._chooser = null;
  }

  get epoch() { return this.refs.epoch; }

  onEvent(method, p = {}) {
    switch (method) {
      case "Page.frameNavigated":
        if (p.frame && !p.frame.parentId) {
          this.mainFrameId = p.frame.id;
          this.refs.retire();
          this._inFlight.clear();
        }
        break;
      case "Page.frameStartedLoading":
        if (p.frameId === this.mainFrameId) this._navPending = true;
        break;
      case "Page.frameStoppedLoading":
        if (p.frameId === this.mainFrameId) this._navPending = false;
        break;
      case "Page.javascriptDialogOpening":
        this.dialog = { type: p.type, message: p.message ?? "" };
        break;
      case "Page.javascriptDialogClosed":
        this.dialog = null;
        break;
      case "Page.fileChooserOpened":
        this._chooser?.(p);
        break;
      case "Network.requestWillBeSent":
        if (p.request?.method && p.request.method !== "GET") this._inFlight.add(p.requestId);
        break;
      case "Network.loadingFinished":
      case "Network.loadingFailed":
        this._inFlight.delete(p.requestId);
        break;
    }
  }

  async prepare() {
    for (const domain of ["Page", "DOM", "Runtime", "Network", "Accessibility"]) await this.send(`${domain}.enable`, {});
    await this.send("Emulation.setFocusEmulationEnabled", { enabled: true });
    await this.send("Page.setWebLifecycleState", { state: "active" });
    const tree = await this.send("Page.getFrameTree", {});
    this.mainFrameId = tree?.frameTree?.frame?.id ?? this.mainFrameId;
  }

  /** 300 ms, then until no main-frame navigation or non-GET request is pending; at most 8 s in all. */
  async settle() {
    const end = Date.now() + 8000;
    await sleep(300);
    while ((this._navPending || this._inFlight.size) && !this.dialog && Date.now() < end) await sleep(100);
    return { text: "The page is settled." };
  }

  diffSince(previous) { return diffSnapshot(previous, this.lines ?? []); }

  _noDialog() {
    if (this.dialog) {
      const message = String(this.dialog.message).slice(0, 500);
      throw new Error(`A page dialog is open (${this.dialog.type}): "${message}". Answer it with dialog first.`);
    }
  }

  async _done(text) { await this.settle(); return { text }; }

  async _isPassword(backendNodeId) {
    const { node } = await this.send("DOM.describeNode", { backendNodeId });
    return isInput(node, "password");
  }

  async _node(ref) {
    const backendNodeId = this.refs.resolve(ref);
    // Frees the handles the previous operation resolved, so a long-lived page doesn't pin old nodes.
    await Promise.resolve(this.send("Runtime.releaseObjectGroup", { objectGroup: "openclicky" })).catch(() => {});
    try {
      const { object } = await this.send("DOM.resolveNode", { backendNodeId, objectGroup: "openclicky" });
      if (!object?.objectId) throw new Error(GONE);
      return { backendNodeId, objectId: object.objectId };
    } catch {
      throw new Error(GONE);
    }
  }

  async _call(objectId, fn, args = []) {
    let r;
    try {
      r = await this.send("Runtime.callFunctionOn", {
        objectId, functionDeclaration: String(fn), arguments: args.map((value) => ({ value })), returnByValue: true, awaitPromise: true,
      });
    } catch {
      throw new Error(GONE);
    }
    if (r?.exceptionDetails) throw new Error("The page rejected that action. Take a new snapshot.");
    return r?.result?.value;
  }

  async _eval(fn, arg, extra = {}) {
    const expression = `(${fn})(${arg === undefined ? "" : JSON.stringify(arg)})`;
    const r = await this.send("Runtime.evaluate", { expression, returnByValue: true, ...extra });
    return r?.result?.value;
  }

  async _point(n) {
    await this._call(n.objectId, scrollToCenter);
    let quads;
    try { ({ quads } = await this.send("DOM.getContentQuads", { backendNodeId: n.backendNodeId })); } catch { throw new Error(GONE); }
    const q = quads?.[0];
    if (!q) throw new Error("That element is not visible. Take a new snapshot.");
    return { x: (q[0] + q[2] + q[4] + q[6]) / 4, y: (q[1] + q[3] + q[5] + q[7]) / 4 };
  }

  async _mouse(type, x, y, extra = {}) {
    await this.send("Input.dispatchMouseEvent", { type, x, y, ...extra });
  }

  async _clickXY(x, y) {
    await this._mouse("mouseMoved", x, y);
    await this._mouse("mousePressed", x, y, { button: "left", clickCount: 1 });
    await this._mouse("mouseReleased", x, y, { button: "left", clickCount: 1 });
  }

  async _key(key) {
    const base = { key, code: key, windowsVirtualKeyCode: KEYS[key] };
    await this.send("Input.dispatchKeyEvent", { type: "keyDown", ...base, ...(key === "Enter" ? { text: "\r" } : {}) });
    await this.send("Input.dispatchKeyEvent", { type: "keyUp", ...base });
  }

  async _clickRef(ref, dismiss) {
    const n = await this._node(ref);
    let p = await this._point(n);
    if (await this._call(n.objectId, isCovered, [p.x, p.y])) {
      if (!dismiss) throw new Error(COVERED);
      await this._key("Escape");
      await sleep(200);
      p = await this._point(n);
      if (await this._call(n.objectId, isCovered, [p.x, p.y])) {
        await this._call(n.objectId, dismissCover, [p.x, p.y]);
        await sleep(300);
        p = await this._point(n);
        if (await this._call(n.objectId, isCovered, [p.x, p.y])) throw new Error(COVERED);
      }
    }
    await this._clickXY(p.x, p.y);
  }

  async snapshot({ query, interactive } = {}) {
    const { nodes = [] } = (await this.send("Accessibility.getFullAXTree", {})) ?? {};
    // The only thing keeping password values out of snapshots: renderSnapshot prints any non-empty AX value.
    const passwordIds = new Set();
    await Promise.all(nodes.map(async (n) => {
      const id = n.backendDOMNodeId;
      const v = n.value?.value;
      const role = n.role?.value;
      if (id === undefined || !(role === "textbox" || role === "searchbox" || (v !== undefined && v !== null && v !== ""))) return;
      try { if (await this._isPassword(id)) passwordIds.add(id); } catch { passwordIds.add(id); } // fail closed
    }));
    const r = renderSnapshot(nodes, this.refs, { query, interactive, budget: this.limits.snapshotChars, passwordIds });
    this.lines = r.lines;
    this._snapEpoch = this.epoch;
    return { text: r.text };
  }

  async extract() {
    this._noDialog();
    const L = this.limits;
    const r = (await this._eval(extractMain, { chars: L.extractChars, tables: L.tables, rows: L.rows })) ?? {};
    const parts = [r.markdown ?? "", ...(r.tables ?? []).map((t, i) => `Table ${i + 1} (CSV):\n${t}`)];
    return { text: parts.filter(Boolean).join("\n\n").slice(0, L.extractChars) };
  }

  async click({ ref, dismiss } = {}) {
    this._noDialog();
    await this._clickRef(ref, dismiss);
    return this._done(`Clicked ${ref}.`);
  }

  _checkText(text) {
    if (typeof text !== "string") throw new Error("Give the text to enter.");
    if (text.length > this.limits.textChars) throw new Error(`That text is too long (at most ${this.limits.textChars} characters).`);
  }

  async fill({ ref, text } = {}) {
    this._noDialog();
    this._checkText(text);
    const n = await this._node(ref);
    let password;
    try { password = await this._isPassword(n.backendNodeId); } catch { throw new Error(GONE); }
    if (password) throw new Error(PASSWORD_REFUSAL);
    const kind = await this._call(n.objectId, fieldKind); // also catches a label whose control is a password
    if (kind === "password") throw new Error(PASSWORD_REFUSAL);
    if (kind === "select") {
      if (!(await this._call(n.objectId, chooseOption, [text]))) throw new Error("No option in that list matches the text.");
    } else if (kind === "text" || kind === "editable") {
      await this._call(n.objectId, focusField);
      // insertText goes wherever focus is now, and a focus handler may have moved it: check again.
      const focus = await this._eval(activeField);
      if (focus === "password") throw new Error(PASSWORD_REFUSAL);
      if (focus !== "editable") throw new Error("That field did not keep the focus. Take a new snapshot.");
      await this.send("Input.insertText", { text });
    } else if (kind === "readonly") {
      throw new Error("That field is read-only or disabled.");
    } else {
      throw new Error("That element can't be filled. Use a text field, text area or list.");
    }
    await this.settle();
    let kept = false;
    try { kept = ((await this._call(n.objectId, readField)) ?? []).some((v) => norm(v) === norm(text)); } catch { /* gone */ }
    if (!kept) throw new Error(NOT_KEPT);
    return { text: `Filled ${ref}.` };
  }

  async type({ text } = {}) {
    this._noDialog();
    this._checkText(text);
    const focus = await this._eval(activeField);
    if (focus === "password") throw new Error(PASSWORD_REFUSAL);
    if (focus !== "editable") throw new Error(NO_FOCUS);
    await this.send("Input.insertText", { text });
    return this._done(`Typed ${text.length} characters.`);
  }

  async press({ key } = {}) {
    this._noDialog();
    if (!Object.hasOwn(KEYS, key)) throw new Error(`Unknown key. Use one of: ${Object.keys(KEYS).join(", ")}.`);
    if (NEEDS_SNAPSHOT.has(key) && this._snapEpoch !== this.epoch) throw new Error(NEED_SNAPSHOT);
    await this._key(key);
    return this._done(`Pressed ${key}.`);
  }

  async _viewport() {
    const m = await this.send("Page.getLayoutMetrics", {});
    return m?.cssVisualViewport ?? m?.cssLayoutViewport ?? { pageX: 0, pageY: 0, clientWidth: 0, clientHeight: 0 };
  }

  async scroll({ direction } = {}) {
    this._noDialog();
    if (direction !== "up" && direction !== "down") throw new Error("Scroll up or down.");
    const v = await this._viewport();
    const deltaY = (direction === "down" ? 1 : -1) * 0.8 * v.clientHeight;
    await this._mouse("mouseWheel", v.clientWidth / 2, v.clientHeight / 2, { deltaX: 0, deltaY });
    return this._done(`Scrolled ${direction}.`);
  }

  async waitFor({ text, url, gone, timeout } = {}) {
    this._noDialog();
    if (!text && !url) throw new Error("Say what to wait for: text or url.");
    const seconds = Math.min(20, Math.max(0, Number(timeout ?? 10) || 0));
    const end = Date.now() + seconds * 1000;
    for (;;) {
      let ok = false;
      try { ok = (await this._eval(pageMatches, { text, url, gone })) === true; } catch { /* page is changing */ }
      if (ok) return { text: "The page is ready." };
      this._noDialog();
      if (Date.now() >= end) throw new Error(`Still waiting after ${seconds} s.`);
      await sleep(200);
    }
  }

  async back() {
    this._noDialog();
    const { currentIndex, entries } = (await this.send("Page.getNavigationHistory", {})) ?? {};
    const prev = entries?.[currentIndex - 1];
    if (!prev) throw new Error("There is no earlier page in this tab.");
    await this.send("Page.navigateToHistoryEntry", { entryId: prev.id });
    return this._done("Went back.");
  }

  async navigate(url) {
    this._noDialog();
    const bad = badUrl(url);
    if (bad) throw new Error(bad);
    const r = await this.send("Page.navigate", { url });
    if (r?.errorText) throw new Error(`That page did not open (${r.errorText}).`);
    return this._done(`Opened ${url}.`);
  }

  async screenshot() {
    this._noDialog();
    const v = await this._viewport();
    const dpr = Number(await this._eval(function () { return window.devicePixelRatio; })) || 1;
    const clip = { x: v.pageX, y: v.pageY, width: v.clientWidth, height: v.clientHeight, scale: 1 / dpr };
    const { data } = await this.send("Page.captureScreenshot", { format: "jpeg", quality: 70, clip });
    this._shotEpoch = this.epoch;
    return {
      text: `Screenshot of the visible page, ${Math.round(v.clientWidth)}×${Math.round(v.clientHeight)} CSS pixels.`,
      image: { data, mimeType: "image/jpeg" },
    };
  }

  _needShot(...nums) {
    this._noDialog();
    if (this._shotEpoch !== this.epoch) throw new Error(NEED_SHOT);
    if (!nums.every((n) => Number.isFinite(n) && n >= 0)) throw new Error("Give x and y in screenshot pixels.");
  }

  async clickAt({ x, y } = {}) {
    this._needShot(x, y);
    await this._clickXY(x, y);
    return this._done(`Clicked at ${Math.round(x)}, ${Math.round(y)}.`);
  }

  async hoverAt({ x, y } = {}) {
    this._needShot(x, y);
    await this._mouse("mouseMoved", x, y);
    return this._done(`Moved the pointer to ${Math.round(x)}, ${Math.round(y)}.`);
  }

  async dragAt({ x, y, toX, toY } = {}) {
    this._needShot(x, y, toX, toY);
    await this._mouse("mousePressed", x, y, { button: "left", clickCount: 1 });
    for (let i = 1; i <= 12; i++) {
      await this._mouse("mouseMoved", x + ((toX - x) * i) / 12, y + ((toY - y) * i) / 12, { button: "left", buttons: 1 });
    }
    await this._mouse("mouseReleased", toX, toY, { button: "left", clickCount: 1 });
    return this._done(`Dragged from ${Math.round(x)}, ${Math.round(y)} to ${Math.round(toX)}, ${Math.round(toY)}.`);
  }

  /** Runs in a fresh isolated world (Ruling R3) after guardMain; page globals are invisible to the script. */
  async javascript({ code } = {}) {
    this._noDialog();
    if (typeof code !== "string" || !code.trim()) throw new Error("Give the script to run.");
    if (code.length > this.limits.codeChars) throw new Error(`That script is too long (at most ${this.limits.codeChars} characters).`);
    const { executionContextId: contextId } = await this.send("Page.createIsolatedWorld", { frameId: this.mainFrameId, worldName: "openclicky" });
    const guard = await this.send("Runtime.evaluate", { expression: `(${guardMain})()`, contextId });
    if (guard?.exceptionDetails) throw new Error("The script could not be prepared safely, so it did not run.");
    // Best effort: guard the same-named world in every child frame, so window[n] from the script lands in a
    // guarded realm. Frames this target can't reach (out of process) are skipped.
    let tree = null;
    try { tree = await this.send("Page.getFrameTree", {}); } catch { /* no tree */ }
    const children = [];
    const collect = (t) => { for (const c of t?.childFrames ?? []) { if (c.frame?.id) children.push(c.frame.id); collect(c); } };
    collect(tree?.frameTree);
    for (const frameId of children) {
      try {
        const world = await this.send("Page.createIsolatedWorld", { frameId, worldName: "openclicky" });
        await this.send("Runtime.evaluate", { expression: `(${guardMain})()`, contextId: world.executionContextId });
      } catch { /* frame went away or is out of process */ }
    }
    const r = await this.send("Runtime.evaluate", {
      expression: `(async () => { ${code} })()`, contextId, awaitPromise: true, returnByValue: true, timeout: 15000,
    });
    if (r?.exceptionDetails) {
      const why = r.exceptionDetails.exception?.description ?? r.exceptionDetails.text ?? "error";
      throw new Error(`The script failed: ${String(why).split("\n")[0].slice(0, 500)}`);
    }
    const value = r?.result?.value;
    const text = value === undefined ? "undefined" : JSON.stringify(value);
    return { text: text.slice(0, this.limits.resultChars) };
  }

  async upload({ ref, paths } = {}) {
    this._noDialog();
    if (!Array.isArray(paths) || paths.length < 1 || paths.length > 10) throw new Error("Give 1 to 10 file paths.");
    if (!paths.every((p) => typeof p === "string" && /^([a-zA-Z]:[\\/]|\\\\|\/)/.test(p))) throw new Error("File paths must be absolute.");
    const n = await this._node(ref);
    const { node } = await this.send("DOM.describeNode", { backendNodeId: n.backendNodeId });
    let target = n.backendNodeId;
    if (isInput(node, "file")) {
      if (paths.length > 1 && attr(node, "multiple") === undefined) throw new Error("That file field takes one file.");
    } else {
      await this.send("Page.setInterceptFileChooserDialog", { enabled: true });
      let timer;
      try {
        const opened = new Promise((resolve, reject) => {
          this._chooser = resolve;
          timer = setTimeout(() => reject(new Error("No file picker opened. Use the file field's ref instead.")), 5000);
        });
        await this._clickRef(ref, false);
        const chosen = await opened;
        if (paths.length > 1 && chosen.mode === "selectSingle") throw new Error("That file field takes one file.");
        target = chosen.backendNodeId;
      } finally {
        clearTimeout(timer);
        this._chooser = null;
        await this.send("Page.setInterceptFileChooserDialog", { enabled: false }).catch(() => {});
      }
    }
    await this.send("DOM.setFileInputFiles", { files: paths, backendNodeId: target });
    return this._done(`Attached ${paths.length} file${paths.length === 1 ? "" : "s"}.`);
  }

  async answerDialog({ accept, text } = {}) {
    if (!this.dialog) throw new Error("No page dialog is open.");
    const params = { accept: !!accept };
    if (typeof text === "string" && this.dialog.type === "prompt") params.promptText = text;
    await this.send("Page.handleJavaScriptDialog", params);
    this.dialog = null;
    return this._done(accept ? "Accepted the dialog." : "Dismissed the dialog.");
  }
}
