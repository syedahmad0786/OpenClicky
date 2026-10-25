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
 * Locks down the current JS world before an agent script runs (used in the isolated world only).
 * A convenience guard, not a security boundary: it covers the direct routes to cookies, storage, saved
 * credentials and password values. Known gaps: same-origin child frames reached as window[n], Attr nodes,
 * and a script submitting a form that already holds a password.
 */
export function guardMain() {
  const NO = "Not available to agents.";
  const REFUSE = "Refused: that is a password field. Ask the user to type it themselves.";
  const apply = Reflect.apply;
  const define = Object.defineProperty;
  const getAttr = Element.prototype.getAttribute;
  const trim = String.prototype.trim;
  const lower = String.prototype.toLowerCase;
  const isPw = (el) => {
    if (!(el instanceof HTMLInputElement)) return false;
    const t = apply(getAttr, el, ["type"]);
    return typeof t === "string" && apply(lower, apply(trim, t, []), []) === "password";
  };
  const deny = () => { throw new Error(NO); };
  const lock = (obj, name, desc) => { try { define(obj, name, { configurable: false, ...desc }); } catch { /* not present here */ } };

  lock(Document.prototype, "cookie", { get: deny, set: deny });
  for (const name of ["cookieStore", "localStorage", "sessionStorage", "indexedDB"]) lock(window, name, { get: deny });
  if (typeof Navigator !== "undefined") lock(Navigator.prototype, "credentials", { get: deny });
  for (const C of [window.HTMLIFrameElement, window.HTMLFrameElement, window.HTMLObjectElement]) {
    if (C) for (const name of ["contentDocument", "contentWindow"]) lock(C.prototype, name, { get: deny });
  }
  lock(window, "open", { value: deny, writable: false });

  const input = HTMLInputElement.prototype;
  const accessor = (name, onGet, onSet) => {
    const d = Object.getOwnPropertyDescriptor(input, name);
    if (!d) return;
    lock(input, name, {
      get() { return onGet && isPw(this) ? onGet() : apply(d.get, this, []); },
      set(v) { if (isPw(this)) throw new Error(REFUSE); apply(d.set, this, [v]); },
    });
  };
  accessor("value", () => "");
  accessor("type");
  accessor("defaultValue", () => "");
  const wrap = (proto, name, blocked) => {
    const fn = proto[name];
    if (typeof fn !== "function") return;
    lock(proto, name, { writable: false, value: function (...args) { if (blocked(this, args)) throw new Error(REFUSE); return apply(fn, this, args); } });
  };
  const attrName = (n) => apply(lower, String(n), []);
  const touchesPw = (el, n) => isPw(el) && /^(type|value)$/.test(attrName(n));
  wrap(Element.prototype, "setAttribute", (el, a) => touchesPw(el, a[0]));
  wrap(Element.prototype, "setAttributeNS", (el, a) => touchesPw(el, a[1]));
  wrap(Element.prototype, "removeAttribute", (el, a) => touchesPw(el, a[0]));
  wrap(Element.prototype, "removeAttributeNS", (el, a) => touchesPw(el, a[1]));
  wrap(Element.prototype, "toggleAttribute", (el, a) => touchesPw(el, a[0]));
  wrap(input, "setRangeText", (el) => isPw(el));
  wrap(Document.prototype, "execCommand", () => isPw(document.activeElement));

  const FD = window.FormData;
  if (FD) {
    const del = FD.prototype.delete;
    const Safe = function FormData(...args) {
      const fd = new FD(...args);
      const form = args[0];
      if (form && form.elements) for (const el of form.elements) if (isPw(el) && el.name) apply(del, fd, [el.name]);
      return fd;
    };
    Safe.prototype = FD.prototype;
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

/** this = covered element: click a visible close-style button inside whatever covers (x, y). */
export function dismissCover(x, y) {
  const doc = this.ownerDocument;
  const words = /\b(close|dismiss|no thanks|decline|reject)\b|×/i;
  for (let n = doc.elementFromPoint(x, y); n && n !== doc.body && n !== doc.documentElement; n = n.parentElement) {
    for (const b of n.querySelectorAll("button, [role=button], a, input[type=button], input[type=submit]")) {
      const label = `${b.innerText || b.value || ""} ${b.getAttribute("aria-label") || ""} ${b.getAttribute("title") || ""}`;
      if (words.test(label) && b.getClientRects().length && getComputedStyle(b).visibility !== "hidden") { b.click(); return true; }
    }
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
