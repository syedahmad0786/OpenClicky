import { describe, expect, it } from "vitest";
import { RefTable, renderSnapshot, diffSnapshot, INTERACTIVE_ROLES } from "../../../chrome-extension/snapshot.mjs";

const prop = (name, value, type = "boolean") => ({ name, value: { type, value } });
let nextId = 1;
const ax = (role, name, backend, extra = {}) => ({
  nodeId: String(nextId++),
  ignored: false,
  role: { type: "role", value: role },
  name: { type: "computedString", value: name },
  backendDOMNodeId: backend,
  childIds: [],
  ...extra,
});
const tree = (root, ...kids) => {
  root.childIds = kids.map((k) => k.nodeId);
  return [root, ...kids];
};

describe("snapshot", () => {
  it("renders roles names refs and flags", () => {
    const nodes = tree(
      ax("main", "Sign up", 1),
      ax("button", "Save", 2, { properties: [prop("disabled", true), prop("focusable", true)] }),
      ax("textbox", "Email", 3, { value: { type: "string", value: "a@b.c" }, properties: [prop("required", true)] }),
    );
    const { lines, text, truncated } = renderSnapshot(nodes, new RefTable());
    expect(lines).toEqual(['main "Sign up"', '  button "Save" [e1] disabled', '  textbox "Email" [e2] value="a@b.c" required']);
    expect(text).toBe(lines.join("\n"));
    expect(truncated).toBe(false);
    expect(INTERACTIVE_ROLES.has("treeitem")).toBe(true);
  });

  it("skips ignored and unnamed wrappers, drops echoed text, flags headings", () => {
    const echo = ax("StaticText", "Go", 6);
    const link = ax("link", "Go", 5, { childIds: [echo.nodeId] });
    const other = ax("StaticText", "Hello", 7);
    const wrap = ax("generic", "", 4, { childIds: [link.nodeId, other.nodeId] });
    const hid = ax("button", "Hidden", 8, { ignored: true });
    const h = ax("heading", "Title", 9, { properties: [prop("level", 2, "integer")] });
    const root = ax("RootWebArea", "Doc", 1, { childIds: [wrap.nodeId, hid.nodeId, h.nodeId] });
    const { lines } = renderSnapshot([root, wrap, link, echo, other, hid, h], new RefTable());
    expect(lines).toEqual(['RootWebArea "Doc"', '  link "Go" [e1]', '  text "Hello"', '  heading "Title" level=2']);
  });

  it("refs survive re-snapshot of the same document", () => {
    const refs = new RefTable();
    const nodes = tree(ax("main", "m", 1), ax("button", "A", 10), ax("button", "B", 11));
    const a = renderSnapshot(nodes, refs).lines;
    const b = renderSnapshot(nodes, refs).lines;
    expect(b).toEqual(a);
    expect(refs.resolve("e2")).toBe(11);
  });

  it("retired refs never resolve", () => {
    const refs = new RefTable();
    expect(refs.refFor(10)).toBe("e1");
    refs.retire();
    expect(() => refs.resolve("e1")).toThrow("That ref is from an earlier page. Take a new snapshot.");
    expect(() => refs.resolve("e99")).toThrow("Unknown ref e99. Take a new snapshot.");
    expect(refs.refFor(10)).toBe("e2");
    expect(refs.refFor(11)).toBe("e3");
    expect(refs.epoch).toBe(1);
  });

  it("hides password values", () => {
    const nodes = tree(ax("main", "m", 1), ax("textbox", "Password", 5, { value: { type: "string", value: "hunter2" } }));
    const { text } = renderSnapshot(nodes, new RefTable(), { passwordIds: new Set([5]) });
    expect(text).not.toContain("value=");
    expect(text).not.toContain("hunter2");
  });

  it("query and interactive filter lines", () => {
    const nodes = tree(ax("main", "m", 1), ax("button", "Save", 2), ax("button", "Cancel", 3), ax("StaticText", "Saved earlier", 4));
    expect(renderSnapshot(nodes, new RefTable(), { query: "SAVE" }).lines).toEqual(['  button "Save" [e1]', '  text "Saved earlier"']);
    expect(renderSnapshot(nodes, new RefTable(), { interactive: true }).lines).toEqual(['  button "Save" [e1]', '  button "Cancel" [e2]']);
  });

  it("cuts at the budget with a hint", () => {
    const buttons = Array.from({ length: 2000 }, (_, i) => ax("button", `B${i}`, 100 + i));
    const r = renderSnapshot(tree(ax("main", "m", 1), ...buttons), new RefTable(), { budget: 1000 });
    expect(r.truncated).toBe(true);
    const last = r.lines[r.lines.length - 1];
    const cut = 2001 - (r.lines.length - 1);
    expect(last).toBe(`… ${cut} more lines. Use query or interactive to narrow it.`);
    expect(r.text.length).toBeLessThan(1200);
  });

  it("diff reports added and removed lines", () => {
    expect(diffSnapshot(["a", "b"], ["a", "c"])).toBe("Changes since your last look:\n+ c\n- b");
    expect(diffSnapshot(["a", "b"], ["a", "b"])).toBe("No change since your last look.");
    expect(diffSnapshot(null, ["a", "b"])).toBe("Page:\na\nb");
    const prev = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "10"];
    const big = ["1", "2", "3", "4", "x1", "x2", "x3", "x4", "9", "10"];
    expect(diffSnapshot(prev, big)).toBe("Page:\n" + big.join("\n"));
    const small = ["1", "2", "3", "4", "5", "6", "7", "8", "9", "y"];
    expect(diffSnapshot(prev, small)).toBe("Changes since your last look:\n+ y\n- 10");
  });
});
