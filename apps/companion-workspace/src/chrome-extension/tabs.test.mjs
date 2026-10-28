import { describe, expect, it } from "vitest";
import { TabManager } from "../../../chrome-extension/tabs.mjs";
import { fakeChrome as chrome } from "./fake-chrome.mjs";

// Window 1 is focused: [A 11, U 12 (active), C 13].
function setup() {
  const f = chrome();
  f.addWindow(1, { focused: true });
  f.add(11, 1);
  f.add(12, 1, { active: true });
  f.add(13, 1);
  let saved;
  const store = { load: async () => saved, save: async (s) => { saved = JSON.parse(JSON.stringify(s)); } };
  const mgr = new TabManager(f.api, store);
  const begin = (task, extra = {}) => mgr.begin({ task, owner: `job-${task}`, groupTitle: "Research", color: "blue", say: "working", ...extra });
  return { f, store, mgr, begin };
}

describe("TabManager", () => {
  it("open groups the tab next to the active tab", async () => {
    const { f, mgr, begin } = setup();
    const title = "A very long job title that goes past thirty-two characters";
    await begin("t1", { groupTitle: title, color: "green" });
    const id = await mgr.open("t1", "https://example.test/");
    const tab = f.get(id);
    expect(tab.active).toBe(false);
    expect(tab.index).toBe(f.get(12).index + 1);
    expect(f.groups.get(tab.groupId)).toMatchObject({ title: title.slice(0, 32), color: "green", collapsed: false });
    expect(mgr.ownerOf(id)).toBe("t1");
    expect(mgr.current("t1")).toBe(id);
    await mgr.open("t1", "https://example.test/two"); // second tab joins the same group
    expect(f.groups.size).toBe(1);
  });

  it("an unknown colour falls back to blue", async () => {
    const { f, mgr, begin } = setup();
    await begin("t1", { color: "purple" });
    const id = await mgr.open("t1", "https://example.test/");
    expect(f.groups.get(f.get(id).groupId).color).toBe("blue");
  });

  it("creates a window only when there is no usable one", async () => {
    const { f, mgr, begin } = setup();
    f.focusFails = true;
    await begin("t1");
    const id = await mgr.open("t1", "https://example.test/");
    expect(f.get(id).windowId).toBe(2);
    expect(f.windows.size).toBe(2);
  });

  it("an incognito last-focused window is not used", async () => {
    const { f, mgr, begin } = setup();
    f.windows.get(1).incognito = true;
    await begin("t1");
    const id = await mgr.open("t1", "https://example.test/");
    expect(f.get(id).windowId).toBe(2);
  });

  it("read leaves a user tab in place", async () => {
    const { f, mgr, begin } = setup();
    await begin("t1");
    const before = f.snapshot();
    await mgr.access("t1", 13, false);
    expect(f.snapshot()).toBe(before);
    expect(mgr.ownerOf(13)).toBeUndefined();
  });

  it("claimed tab returns to its group and index", async () => {
    const { f, mgr, begin } = setup();
    f.add(14, 1);
    f.addGroup(7, 1);
    f.tabs.get(12).groupId = 7; // A, [U C](7), D: U sits at index 1 inside the user's group
    f.tabs.get(13).groupId = 7;
    const ix = f.get(12).index;
    expect(ix).toBe(1);
    await begin("t1");
    await mgr.access("t1", 12, true);
    expect(f.get(12).groupId).not.toBe(7);
    expect(mgr.ownerOf(12)).toBe("t1");
    await mgr.end("t1", true);
    expect(f.get(12)).toMatchObject({ groupId: 7, index: ix });
    expect(f.tabs.has(12)).toBe(true);
  });

  it("claimed tab with no group is ungrouped on return, and a gone group counts as none", async () => {
    const { f, mgr, begin } = setup();
    await begin("t1");
    await mgr.access("t1", 13, true);
    expect(f.get(13).groupId).not.toBe(-1);
    await mgr.end("t1", true);
    expect(f.get(13)).toMatchObject({ groupId: -1, index: 2 });

    f.addGroup(8, 1);
    f.tabs.get(11).groupId = 8;
    await begin("t2");
    await mgr.access("t2", 11, true);
    expect(f.groups.has(8)).toBe(false); // claiming the only member emptied the user's group
    await mgr.end("t2", true);
    expect(f.get(11).groupId).toBe(-1);
  });

  it("other job's tab is refused", async () => {
    const { mgr, begin } = setup();
    await begin("a");
    await begin("b");
    const id = await mgr.open("a", "https://example.test/");
    await expect(mgr.access("b", id, true)).rejects.toThrow("That tab belongs to another job.");
    await expect(mgr.access("b", id, false)).rejects.toThrow("That tab belongs to another job.");
    await mgr.access("a", id, true); // its own tab is fine
  });

  it("tidy end closes unkept tabs", async () => {
    const { f, mgr, begin } = setup();
    await begin("t1");
    const a = await mgr.open("t1", "https://example.test/a");
    const b = await mgr.open("t1", "https://example.test/b");
    await mgr.end("t1", true);
    expect(f.tabs.has(a)).toBe(false);
    expect(f.tabs.has(b)).toBe(false);
    expect(f.groups.size).toBe(0);
    expect(mgr.owners()).toEqual([]);
  });

  it("tidy end keeps kept tabs and folds the group", async () => {
    const { f, mgr, begin } = setup();
    await begin("t1");
    const a = await mgr.open("t1", "https://example.test/a");
    const b = await mgr.open("t1", "https://example.test/b");
    mgr.keep("t1", a, "result");
    await mgr.end("t1", true);
    expect(f.tabs.has(a)).toBe(true);
    expect(f.tabs.has(b)).toBe(false);
    expect(f.groups.get(f.get(a).groupId).collapsed).toBe(true);
    expect(mgr.ownerOf(a)).toBeUndefined();
  });

  it("tidy end keeps the tab the user is viewing", async () => {
    const { f, mgr, begin } = setup();
    await begin("t1");
    const a = await mgr.open("t1", "https://example.test/a");
    const b = await mgr.open("t1", "https://example.test/b");
    f.activate(a);
    await mgr.end("t1", true);
    expect(f.tabs.has(a)).toBe(true);
    expect(f.tabs.has(b)).toBe(false);
  });

  it("tidy end leaves a tab dragged out of the group", async () => {
    const { f, mgr, begin } = setup();
    await begin("t1");
    const a = await mgr.open("t1", "https://example.test/a");
    const b = await mgr.open("t1", "https://example.test/b");
    await f.api.tabs.ungroup([a]);
    await mgr.end("t1", true);
    expect(f.tabs.has(a)).toBe(true);
    expect(f.tabs.has(b)).toBe(false);
  });

  it("tidy end skips a tab the user already closed", async () => {
    const { f, mgr, begin } = setup();
    await begin("t1");
    const a = await mgr.open("t1", "https://example.test/a");
    const b = await mgr.open("t1", "https://example.test/b");
    await f.api.tabs.remove(a);
    await mgr.end("t1", true);
    expect(f.tabs.has(b)).toBe(false);
    expect(mgr.owners()).toEqual([]);
  });

  it("hand over closes nothing", async () => {
    const { f, mgr, begin } = setup();
    await begin("t1");
    const a = await mgr.open("t1", "https://example.test/a");
    await mgr.access("t1", 13, true);
    const before = f.snapshot();
    await mgr.end("t1", false);
    expect(f.snapshot()).toBe(before);
    expect(mgr.ownerOf(a)).toBeUndefined();
    expect(mgr.ownerOf(13)).toBeUndefined();
  });

  it("sweep ends owners that are not running", async () => {
    const { f, mgr, begin } = setup();
    await begin("a");
    await begin("b");
    const ta = await mgr.open("a", "https://example.test/a");
    const tb = await mgr.open("b", "https://example.test/b");
    await mgr.sweep(["a"]);
    expect(mgr.owners().map((o) => o.task)).toEqual(["a"]);
    expect(mgr.ownerOf(tb)).toBeUndefined();
    expect(f.tabs.has(ta)).toBe(true);
    expect(f.tabs.has(tb)).toBe(true); // handed over, not closed
  });

  it("close only closes tabs the job opened", async () => {
    const { f, mgr, begin } = setup();
    await begin("t1");
    const a = await mgr.open("t1", "https://example.test/a");
    await expect(mgr.close("t1", 13)).rejects.toThrow("Only tabs this job opened can be closed.");
    await mgr.access("t1", 13, true);
    await expect(mgr.close("t1", 13)).rejects.toThrow("Only tabs this job opened can be closed.");
    await mgr.close("t1", a);
    expect(f.tabs.has(a)).toBe(false);
    expect(mgr.current("t1")).toBe(13);
  });

  it("state survives restore", async () => {
    const { f, store, mgr, begin } = setup();
    await begin("t1", { say: "hello" });
    const a = await mgr.open("t1", "https://example.test/a");
    await mgr.access("t1", 13, true);
    mgr.keep("t1", a, "waiting");
    await new Promise((r) => setTimeout(r, 0));
    const again = new TabManager(f.api, store);
    await again.restore();
    expect(again.owners()).toEqual([{ task: "t1", title: "Research", say: "hello", tabs: [a, 13] }]);
    expect(again.ownerOf(a)).toBe("t1");
    expect(again.current("t1")).toBe(13);
    await again.end("t1", true); // the restored kept mark and claim are honoured
    expect(f.tabs.has(a)).toBe(true);
    expect(f.get(13).groupId).toBe(-1);
  });

  it("incognito tabs are hidden and refused", async () => {
    const { f, mgr, begin } = setup();
    f.add(20, 1, { incognito: true });
    await begin("t1");
    expect(await mgr.list("t1")).not.toContain("[20]");
    await expect(mgr.access("t1", 20, false)).rejects.toThrow("Incognito tabs are off limits.");
    await expect(mgr.access("t1", 20, true)).rejects.toThrow("Incognito tabs are off limits.");
    expect(mgr.ownerOf(20)).toBeUndefined();
  });

  it("list marks the viewed tab and ownership, and hides non-web tabs", async () => {
    const { f, mgr, begin } = setup();
    f.add(21, 1, { url: "chrome://settings/" });
    await begin("a");
    await begin("b");
    const mine = await mgr.open("a", "https://example.test/a");
    await mgr.open("b", "https://example.test/b");
    const lines = (await mgr.list("a")).split("\n");
    expect(lines).toContain("[12] Tab 12 — https://site12.test/ (you are here)");
    expect(lines).toContain("[11] Tab 11 — https://site11.test/");
    expect(lines).toContain(`[${mine}] https://example.test/a — https://example.test/a (this job)`);
    expect(lines.some((l) => l.includes("example.test/b") && l.endsWith(" (another job)"))).toBe(true);
    expect(lines.some((l) => l.startsWith("[21]"))).toBe(false);
  });

  it("list stops at 500 tabs and 650,000 characters", async () => {
    const { f, mgr } = setup();
    for (let i = 0; i < 600; i++) f.add(1000 + i, 1);
    expect((await mgr.list("t")).split("\n")).toHaveLength(500);
    for (const t of f.tabs.values()) t.title = "x".repeat(5000);
    const out = await mgr.list("t");
    expect(out.length).toBeLessThanOrEqual(650000);
    expect(out.split("\n").length).toBeLessThan(500);
  });

  it("open for an unknown job is refused", async () => {
    const { mgr } = setup();
    await expect(mgr.open("nobody", "https://example.test/")).rejects.toThrow("Unknown job");
  });
});
