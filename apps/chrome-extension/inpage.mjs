// Functions that run inside the page. Each is serialised with fn.toString() and evaluated there, so every
// one must be self-contained: no imports, no references to anything else in this module.
// Helpers that take `this` run through Runtime.callFunctionOn on a resolved element.

/** Page text as markdown plus tables as CSV. Never reads form field values. */
export function extractMain(limits) {
  const L = Object.assign({ chars: 40000, tables: 20, rows: 200 }, limits || {});
  const SKIP = new Set(["script", "style", "noscript", "template", "input", "textarea", "select", "option", "svg", "canvas", "iframe", "object", "embed"]);
  const BLOCK = /^(address|article|aside|blockquote|body|dd|details|dialog|div|dl|dt|fieldset|figcaption|figure|footer|form|header|hr|main|nav|ol|p|pre|section|summary|ul|tr)$/;
  const skip = (el) => {
    if (SKIP.has(el.localName) || el.hidden || el.getAttribute("aria-hidden") === "true") return true;
    if ((el.getAttribute("type") || "").trim().toLowerCase() === "password") return true;
    const cs = getComputedStyle(el);
    return cs.display === "none" || cs.visibility === "hidden";
  };
  const norm = (s) => s.replace(/\s+/g, " ").trim();
  const inline = (node) => {
    if (node.nodeType === 3) return node.nodeValue;
    if (node.nodeType !== 1 || skip(node)) return "";
    if (node.localName === "br") return " ";
    let s = "";
    for (const c of node.childNodes) s += inline(c);
    if (node.localName === "a" && /^https?:/i.test(node.href || "")) {
      const t = norm(s);
      return t ? ` [${t}](${node.href}) ` : "";
    }
    return BLOCK.test(node.localName) ? ` ${s} ` : s;
  };
  const csvCell = (s) => (/[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s);

  const out = [];
  const tables = [];
  let size = 0;
  let para = "";
  const emit = (line) => { if (line && size < L.chars) { out.push(line); size += line.length + 2; } };
  const flush = () => { emit(norm(para)); para = ""; };
  const walk = (node) => {
    if (size >= L.chars) return;
    if (node.nodeType === 3) { para += node.nodeValue; return; }
    if (node.nodeType !== 1 || skip(node)) return;
    const tag = node.localName;
    if (/^h[1-6]$/.test(tag)) { flush(); const t = norm(inline(node)); if (t) emit(`${"#".repeat(+tag[1])} ${t}`); return; }
    if (tag === "li") { flush(); const t = norm(inline(node)); if (t) emit(`- ${t}`); return; }
    if (tag === "a" || tag === "br") { para += inline(node); return; }
    if (tag === "table") {
      flush();
      if (tables.length < L.tables) {
        const rows = [];
        for (const tr of node.rows) {
          if (rows.length >= L.rows) break;
          if (skip(tr)) continue;
          rows.push([...tr.cells].map((td) => csvCell(norm(inline(td)))).join(","));
        }
        tables.push(rows.join("\n"));
        emit(`[table ${tables.length}]`);
      }
      return;
    }
    const block = BLOCK.test(tag);
    if (block) flush();
    for (const c of node.childNodes) walk(c);
    if (block) flush();
  };
  if (document.body) walk(document.body);
  flush();
  return { markdown: out.join("\n\n").slice(0, L.chars), tables };
}

/**
 * Locks down the current JS world before an agent script runs (used in isolated worlds only).
 * A convenience guard, not a security boundary: it covers the direct routes to cookies, storage, saved
 * credentials and password values, and blocks creating new frames (a fresh realm would be unguarded).
 * Checks use originals captured here, so a script can't swap them out first. Known gaps: indexed
 * window[n] access to child frames that already exist (page.mjs guards every frame's world first, best
 * effort), frames created by cloning an existing frame element or adopting one from an XHR document,
 * Attr nodes, and a script submitting a form that already holds a password.
 */
export function guardMain() {
  const NO = "Not available to agents.";
  const REFUSE = "Refused: that is a password field. Ask the user to type it themselves.";
  const apply = Reflect.apply;
  const construct = Reflect.construct;
  const define = Object.defineProperty;
  const describe = Object.getOwnPropertyDescriptor;
  const Str = String;
  const exec = RegExp.prototype.exec;
  const matches = (re, s) => apply(exec, re, [s]) !== null;
  const input = HTMLInputElement.prototype;
  const typeGet = describe(input, "type").get;
  // The original type getter brand-checks the element and reports how it renders, whatever its prototype.
  const isPw = (el) => { try { return apply(typeGet, el, []) === "password"; } catch { return false; } };
  const deny = () => { throw new Error(NO); };
  const lock = (obj, name, desc) => { try { if (obj) define(obj, name, { configurable: false, ...desc }); } catch { /* not present here */ } };

  lock(Document.prototype, "cookie", { get: deny, set: deny });
  for (const name of ["cookieStore", "localStorage", "sessionStorage", "indexedDB", "frames"]) lock(window, name, { get: deny });
  if (typeof Navigator !== "undefined") lock(Navigator.prototype, "credentials", { get: deny });
  for (const C of [window.HTMLIFrameElement, window.HTMLFrameElement, window.HTMLObjectElement]) {
    if (C) for (const name of ["contentDocument", "contentWindow"]) lock(C.prototype, name, { get: deny });
  }
  lock(window, "open", { value: deny, writable: false });

  const accessor = (proto, name, onGet, onSet) => {
    const d = proto && describe(proto, name);
    if (!d || !d.set) return;
    lock(proto, name, {
      get() { return onGet ? onGet(this, d.get) : apply(d.get, this, []); },
      set(v) { apply(d.set, this, [onSet(this, v)]); },
    });
  };
  const noPw = (el, v) => { if (isPw(el)) throw new Error(REFUSE); return v; };
  const blankPw = (el, get) => (isPw(el) ? "" : apply(get, el, []));
  accessor(input, "value", blankPw, noPw);
  accessor(input, "type", null, noPw);
  accessor(input, "defaultValue", blankPw, noPw);

  const wrap = (proto, name, blocked, message = REFUSE) => {
    const fn = proto && proto[name];
    if (typeof fn !== "function") return;
    lock(proto, name, { writable: false, value: function (...args) { if (blocked(this, args)) throw new Error(message); return apply(fn, this, args); } });
  };
  const PW_ATTR = /^(type|value)$/i;
  const touchesPw = (el, n) => isPw(el) && matches(PW_ATTR, Str(n));
  wrap(Element.prototype, "setAttribute", (el, a) => touchesPw(el, a[0]));
  wrap(Element.prototype, "setAttributeNS", (el, a) => touchesPw(el, a[1]));
  wrap(Element.prototype, "removeAttribute", (el, a) => touchesPw(el, a[0]));
  wrap(Element.prototype, "removeAttributeNS", (el, a) => touchesPw(el, a[1]));
  wrap(Element.prototype, "toggleAttribute", (el, a) => touchesPw(el, a[0]));
  wrap(input, "setRangeText", (el) => isPw(el));
  const activeGet = describe(Document.prototype, "activeElement")?.get;
  wrap(Document.prototype, "execCommand", () => isPw(activeGet ? apply(activeGet, document, []) : document.activeElement));

  // No new frames: markup is coerced to a string once, checked, and that same string is used.
  const FRAME_TAG = /<(iframe|frame|object|embed)/i;
  const FRAME_NAME = /^(?:[^:]*:)?(iframe|frame|object|embed)$/i;
  const markup = (v) => (v === null ? "" : Str(v));
  const html = (i) => (self, a) => { a[i] = markup(a[i]); return matches(FRAME_TAG, a[i]); };
  wrap(Document.prototype, "createElement", (d, a) => { a[0] = Str(a[0]); return matches(FRAME_NAME, a[0]); }, NO);
  wrap(Document.prototype, "createElementNS", (d, a) => { a[1] = Str(a[1]); return matches(FRAME_NAME, a[1]); }, NO);
  wrap(Element.prototype, "insertAdjacentHTML", html(1), NO);
  wrap(Element.prototype, "setHTMLUnsafe", html(0), NO);
  wrap(window.ShadowRoot && ShadowRoot.prototype, "setHTMLUnsafe", html(0), NO);
  wrap(window.DOMParser && DOMParser.prototype, "parseFromString", html(0), NO);
  wrap(Range.prototype, "createContextualFragment", html(0), NO);
  wrap(Document, "parseHTMLUnsafe", html(0), NO);
  // ponytail: write/writeln are refused outright; markup split across calls can't be checked per call
  wrap(Document.prototype, "write", () => true, NO);
  wrap(Document.prototype, "writeln", () => true, NO);
  const frameFree = (self, v) => { const s = markup(v); if (matches(FRAME_TAG, s)) throw new Error(NO); return s; };
  accessor(Element.prototype, "innerHTML", null, frameFree);
  accessor(Element.prototype, "outerHTML", null, frameFree);
  accessor(window.ShadowRoot && ShadowRoot.prototype, "innerHTML", null, frameFree);

  const FD = window.FormData;
  if (FD) {
    const del = FD.prototype.delete;
    const elementsGet = describe(HTMLFormElement.prototype, "elements").get;
    const lengthGet = describe(HTMLCollection.prototype, "length").get;
    const item = HTMLCollection.prototype.item;
    const nameGet = describe(input, "name").get;
    const Safe = function FormData(...args) {
      const fd = construct(FD, args);
      let els = null;
      try { els = apply(elementsGet, args[0], []); } catch { /* no form */ }
      const count = els ? apply(lengthGet, els, []) : 0;
      for (let i = 0; i < count; i++) {
        const el = apply(item, els, [i]);
        const name = isPw(el) ? apply(nameGet, el, []) : "";
        if (name) apply(del, fd, [name]);
      }
      return fd;
    };
    Safe.prototype = FD.prototype;
    lock(FD.prototype, "constructor", { value: Safe, writable: false });
    lock(window, "FormData", { value: Safe, writable: false });
  }
}

/** this = element (or a label): text | select | editable | password | readonly | other. */
export function fieldKind() {
  const el = this.localName === "label" && this.control ? this.control : this;
  const type = ((el.getAttribute && el.getAttribute("type")) || "").trim().toLowerCase();
  if (el.localName === "input") {
    if (type === "password") return "password";
    if (el.disabled || el.readOnly) return "readonly";
    return ["", "text", "search", "email", "url", "tel", "number"].includes(type) ? "text" : "other";
  }
  if (el.localName === "textarea") return el.disabled || el.readOnly ? "readonly" : "text";
  if (el.localName === "select") return el.disabled ? "readonly" : "select";
  return el.isContentEditable ? "editable" : "other";
}

/** this = field: focus it and select its contents so inserted text replaces them. */
export function focusField() {
  const el = this.localName === "label" && this.control ? this.control : this;
  el.focus();
  if (el.isContentEditable) {
    const range = document.createRange();
    range.selectNodeContents(el);
    const sel = getSelection();
    sel.removeAllRanges();
    sel.addRange(range);
  } else {
    try { el.select(); } catch { /* not selectable */ }
  }
}

/** this = select (or its label): choose the option whose value or text matches. */
export function chooseOption(text) {
  const el = this.localName === "label" && this.control ? this.control : this;
  const want = String(text).trim().toLowerCase();
  const opts = [...el.options];
  const opt = opts.find((o) => o.value === text) || opts.find((o) => o.text.trim().toLowerCase() === want);
  if (!opt) return false;
  opt.selected = true;
  el.dispatchEvent(new Event("input", { bubbles: true }));
  el.dispatchEvent(new Event("change", { bubbles: true }));
  return true;
}

/** this = field: what it holds now, for the read-back check. */
export function readField() {
  const el = this.localName === "label" && this.control ? this.control : this;
  if (el.localName === "select") { const o = el.options[el.selectedIndex]; return o ? [o.value, o.text] : []; }
  if (el.localName === "input" || el.localName === "textarea") return [el.value];
  return [el.innerText];
}

/** this = element: scroll it to the middle of the viewport. */
export function scrollToCenter() {
  if (this.scrollIntoView) this.scrollIntoView({ block: "center" });
}

/** this = element: true when something else is on top of it at (x, y). */
export function isCovered(x, y) {
  const doc = this.ownerDocument;
  const win = doc && doc.defaultView;
  if (!win || win !== win.top) return false; // ponytail: no hit test inside frames; add frame offsets if needed
  let hit = doc.elementFromPoint(x, y);
  while (hit && hit.shadowRoot) {
    const inner = hit.shadowRoot.elementFromPoint(x, y);
    if (!inner || inner === hit) break;
    hit = inner;
  }
  if (!hit) return false;
  const within = (a, b) => { for (let n = a; n; n = n.parentNode || n.host) if (n === b) return true; return false; };
  return !(within(hit, this) || within(this, hit));
}

/** this = covered element: click a visible close-style button inside whatever covers (x, y), never outside it. */
export function dismissCover(x, y) {
  const target = this;
  const doc = this.ownerDocument;
  const words = /\b(close|dismiss|no thanks|decline|reject)\b|×/i;
  const sel = "button, [role=button], a, input[type=button], input[type=submit]";
  const holds = (a, el) => { for (let n = el; n; n = n.parentNode || n.host) if (n === a) return true; return false; };
  const usable = (b) => {
    const label = `${b.innerText ?? b.textContent ?? ""} ${b.value || ""} ${b.getAttribute("aria-label") || ""} ${b.getAttribute("title") || ""}`;
    return b.matches(sel) && words.test(label) && b.getClientRects().length > 0 && getComputedStyle(b).visibility !== "hidden";
  };
  let hit = doc.elementFromPoint(x, y);
  while (hit && hit.shadowRoot) {
    const inner = hit.shadowRoot.elementFromPoint(x, y);
    if (!inner || inner === hit) break;
    hit = inner;
  }
  // Climb from the cover only while the subtree does not also hold the target (shadow-aware).
  for (let n = hit; n && n.nodeType === 1 && !holds(n, target); n = n.parentElement || (n.parentNode && n.parentNode.host) || null) {
    const inShadow = n.shadowRoot ? [...n.shadowRoot.querySelectorAll(sel)] : [];
    for (const b of [n, ...n.querySelectorAll(sel), ...inShadow]) if (usable(b)) { b.click(); return true; }
  }
  return false;
}

/** What has keyboard focus, looking into open shadow roots and same-origin frames: editable | password | none. */
export function activeField() {
  let a = document.activeElement;
  for (;;) {
    while (a && a.shadowRoot && a.shadowRoot.activeElement) a = a.shadowRoot.activeElement;
    if (a && (a.localName === "iframe" || a.localName === "frame")) {
      let d = null;
      try { d = a.contentDocument; } catch { /* cross-origin */ }
      if (!d) return "none";
      a = d.activeElement;
      continue;
    }
    break;
  }
  if (!a) return "none";
  const type = ((a.getAttribute && a.getAttribute("type")) || "").trim().toLowerCase();
  if (a.localName === "input") {
    if (type === "password") return "password";
    const noText = ["button", "submit", "reset", "checkbox", "radio", "file", "image", "hidden", "range", "color"];
    return a.disabled || a.readOnly || noText.includes(type) ? "none" : "editable";
  }
  if (a.localName === "textarea") return a.disabled || a.readOnly ? "none" : "editable";
  return a.isContentEditable ? "editable" : "none";
}

/** wait_for check: text present, gone text absent, URL contains a fragment. */
export function pageMatches(want) {
  const body = document.body ? document.body.innerText : "";
  if (want.text && !body.includes(want.text)) return false;
  if (want.gone && body.includes(want.gone)) return false;
  if (want.url && !location.href.includes(want.url)) return false;
  return true;
}
