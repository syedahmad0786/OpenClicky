// Accessibility snapshot lines, stable refs and diffs. Plain ES module: no imports, no Chrome APIs.
// Input is the CDP Accessibility.getFullAXTree node shape.

export const INTERACTIVE_ROLES = new Set([
  "button", "link", "textbox", "searchbox", "combobox", "checkbox", "radio", "switch", "slider",
  "spinbutton", "menuitem", "menuitemcheckbox", "menuitemradio", "tab", "option", "listbox", "treeitem",
]);

const EARLIER_PAGE = "That ref is from an earlier page. Take a new snapshot.";
const FLAGS = ["focused", "disabled", "checked", "selected", "expanded", "required", "invalid"];
const WRAPPERS = new Set(["generic", "none", "group"]);

/** Refs `e<number>`: stable per backend node within an epoch, never reused after retire(). */
export class RefTable {
  constructor() {
    this.epoch = 0;
    this._counter = 0;
    this._byNode = new Map(); // backendNodeId -> ref
    this._byRef = new Map(); // ref -> backendNodeId
    this._retired = new Set();
  }

  refFor(backendNodeId) {
    let ref = this._byNode.get(backendNodeId);
    if (!ref) {
      ref = `e${++this._counter}`;
      this._byNode.set(backendNodeId, ref);
      this._byRef.set(ref, backendNodeId);
    }
    return ref;
  }

  resolve(ref) {
    if (this._byRef.has(ref)) return this._byRef.get(ref);
    if (this._retired.has(ref)) throw new Error(EARLIER_PAGE);
    throw new Error(`Unknown ref ${ref}. Take a new snapshot.`);
  }

  retire() {
    for (const ref of this._byRef.keys()) this._retired.add(ref);
    this._byNode.clear();
    this._byRef.clear();
    this.epoch++;
  }
}

const clip = (s, n) => (s.length > n ? s.slice(0, n) : s);
const clean = (v) => String(v ?? "").replace(/\s+/g, " ").trim();
const truthy = (v) => v === true || (typeof v === "string" && v !== "" && v !== "false");

export function renderSnapshot(nodes, refs, opts = {}) {
  const { query, interactive, budget = 12000, passwordIds } = opts;
  const byId = new Map(nodes.map((n) => [n.nodeId, n]));
  const isChild = new Set();
  for (const n of nodes) for (const c of n.childIds ?? []) isChild.add(c);
  const roots = nodes.filter((n) => !isChild.has(n.nodeId));
  if (!roots.length && nodes.length) roots.push(nodes[0]);

  const entries = []; // { line, name, ref }
  const seen = new Set();

  const walk = (node, depth, parentName) => {
    if (!node || seen.has(node.nodeId)) return;
    seen.add(node.nodeId);
    const role = node.role?.value ?? "";
    const name = clean(node.name?.value);
    const props = new Map((node.properties ?? []).map((p) => [p.name, p.value?.value]));
    let childDepth = depth;
    let childParentName = name;
    if (!node.ignored && role !== "InlineTextBox") {
      if (role === "StaticText") {
        if (name && name !== parentName) {
          entries.push({ line: `${"  ".repeat(depth)}text "${clip(name, 120)}"`, name, ref: false });
        }
      } else if (!(WRAPPERS.has(role) && !name)) {
        const id = node.backendDOMNodeId;
        const wantsRef = id !== undefined && (INTERACTIVE_ROLES.has(role) || props.has("focusable"));
        let line = `${"  ".repeat(depth)}${role} "${clip(name, 120)}"`;
        if (wantsRef) line += ` [${refs.refFor(id)}]`;
        const v = node.value?.value;
        if (v !== undefined && v !== null && v !== "" && !passwordIds?.has(id)) line += ` value="${clip(clean(v), 200)}"`;
        for (const f of FLAGS) if (truthy(props.get(f))) line += ` ${f}`;
        if (role === "heading" && props.has("level")) line += ` level=${props.get("level")}`;
        entries.push({ line, name, ref: wantsRef });
        childDepth = depth + 1;
      } else {
        childParentName = parentName; // lifted wrapper
      }
    } else {
      childParentName = parentName;
    }
    for (const cid of node.childIds ?? []) walk(byId.get(cid), childDepth, childParentName);
  };
  for (const r of roots) walk(r, 0, "");

  const q = query ? query.toLowerCase() : "";
  const kept = entries.filter((e) => (!q || e.name.toLowerCase().includes(q)) && (!interactive || e.ref));
  const lines = [];
  let used = 0;
  let cut = -1;
  // ponytail: cuts at the budget; put on-screen nodes first if agents keep missing content
  for (let i = 0; i < kept.length; i++) {
    used += kept[i].line.length + 1;
    if (used > budget) { cut = i; break; }
    lines.push(kept[i].line);
  }
  const truncated = cut >= 0;
  if (truncated) lines.push(`… ${kept.length - cut} more lines. Use query or interactive to narrow it.`);
  return { lines, text: lines.join("\n"), truncated };
}

export function diffSnapshot(previous, next) {
  const full = () => "Page:\n" + next.join("\n");
  if (!previous) return full();
  const count = (arr) => arr.reduce((m, l) => m.set(l, (m.get(l) ?? 0) + 1), new Map());
  const minus = (a, b) => {
    const left = count(b);
    return a.filter((l) => {
      const c = left.get(l) ?? 0;
      if (c > 0) { left.set(l, c - 1); return false; }
      return true;
    });
  };
  const added = minus(next, previous);
  const removed = minus(previous, next);
  if (!added.length && !removed.length) return "No change since your last look.";
  // ponytail: tiny pages (< 5 lines) always diff; the 60 % rewrite rule only makes sense on bigger pages
  if (next.length >= 5 && added.length + removed.length > next.length * 0.6) return full();
  return ["Changes since your last look:", ...added.map((l) => `+ ${l}`), ...removed.map((l) => `- ${l}`)].join("\n");
}
