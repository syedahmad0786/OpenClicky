// Tab groups and ownership: one Chrome tab group per job, plus the rules for touching the user's own tabs.
// Only the injected `api` ({tabs, tabGroups, windows}, chrome.* promise signatures) and `store`
// ({load, save}) are used, so tests drive it with an in-memory fake.
//
// Rules in one place:
//  - a job gets one group, created on its first open or claim;
//  - reading a user tab leaves it where it is; changing it claims it (remembering group, index and window);
//  - another live owner's tab is always refused; incognito tabs are never listed or touched;
//  - a tidy end closes what the job opened (unless kept, being viewed, or dragged out of the group) and gives
//    claimed tabs back; a hand-over only forgets the owner.
// Operations on one manager are expected to arrive one at a time (the dispatcher awaits each call).

const COLORS = new Set(["red", "blue", "yellow", "green"]);
const MAX_TITLE = 32;
const MAX_LIST_TABS = 500;
const MAX_LIST_CHARS = 650000;
const NONE = -1;

const isWeb = (url) => /^https?:/i.test(String(url ?? ""));
const oneLine = (s) => String(s ?? "").replace(/\s+/g, " ").trim();

export class TabManager {
  constructor(api, store) {
    this.api = api;
    this.store = store;
    this.owners_ = new Map(); // task -> owner record (plain, JSON-safe)
  }

  async restore() {
    const state = await this.store.load();
    this.owners_ = new Map();
    for (const o of state?.owners ?? []) {
      this.owners_.set(o.task, {
        task: o.task, owner: o.owner, title: o.title ?? "", say: o.say ?? "", color: COLORS.has(o.color) ? o.color : "blue",
        groupId: typeof o.groupId === "number" ? o.groupId : NONE,
        opened: [...(o.opened ?? [])], claimed: { ...(o.claimed ?? {}) }, kept: { ...(o.kept ?? {}) },
        current: o.current,
      });
    }
  }

  async begin({ task, owner, groupTitle, color, say }) {
    // Every method is keyed by `task`, so that is the owner identity here; `owner` (the job id) is kept as information.
    const o = this.owners_.get(task) ?? {
      task, owner, title: "", say: "", color: "blue", groupId: NONE, opened: [], claimed: {}, kept: {}, current: undefined,
    };
    o.owner = owner;
    o.title = oneLine(groupTitle).slice(0, MAX_TITLE);
    o.color = COLORS.has(color) ? color : "blue";
    o.say = say ?? "";
    this.owners_.set(task, o);
    await this.#save();
  }

  owners() {
    return [...this.owners_.values()].map((o) => ({
      task: o.task, title: o.title, say: o.say, tabs: [...o.opened, ...Object.keys(o.claimed).map(Number)],
    }));
  }

  ownerOf(tabId) {
    for (const o of this.owners_.values()) if (this.#owns(o, tabId)) return o.task;
    return undefined;
  }

  async list(task) {
    const lines = [];
    let chars = 0;
    const here = await this.#viewedTabId();
    for (const t of await this.api.tabs.query({})) {
      if (t.incognito || !isWeb(t.url)) continue;
      if (lines.length >= MAX_LIST_TABS) break;
      const owner = this.ownerOf(t.id);
      const line = `[${t.id}] ${oneLine(t.title)} — ${t.url}`
        + (t.id === here ? " (you are here)" : "")
        + (owner === undefined ? "" : owner === task ? " (this job)" : " (another job)");
      if (chars + line.length + 1 > MAX_LIST_CHARS) break;
      chars += line.length + 1;
      lines.push(line);
    }
    return lines.join("\n");
  }

  async open(task, url) {
    const o = this.#owner(task);
    const win = await this.#workWindow();
    const [active] = await this.api.tabs.query({ active: true, windowId: win.id });
    const props = { url, active: false, windowId: win.id };
    if (active) props.index = active.index + 1;
    const tab = await this.api.tabs.create(props);
    o.opened.push(tab.id);
    o.current = tab.id;
    await this.#save();
    await this.#join(o, tab.id, tab.windowId ?? win.id);
    await this.#save();
    return tab.id;
  }

  async access(task, tabId, mutating) {
    const o = this.#owner(task);
    const tab = await this.api.tabs.get(tabId);
    const other = this.ownerOf(tabId);
    if (other !== undefined && other !== task) throw new Error("That tab belongs to another job.");
    if (tab.incognito) throw new Error("Incognito tabs are off limits.");
    if (!this.#owns(o, tabId) && mutating) {
      o.claimed[tabId] = { groupId: tab.groupId ?? NONE, index: tab.index, windowId: tab.windowId };
      await this.#save();
      await this.#join(o, tabId, tab.windowId);
    }
    o.current = tabId;
    await this.#save();
  }

  current(task) {
    return this.owners_.get(task)?.current;
  }

  async close(task, tabId) {
    const o = this.#owner(task);
    if (!o.opened.includes(tabId)) throw new Error("Only tabs this job opened can be closed.");
    try { await this.api.tabs.remove(tabId); } catch { /* already closed by the user */ }
    this.#forgetTab(o, tabId);
    await this.#save();
  }

  keep(task, tabId, why) {
    const o = this.#owner(task);
    if (!this.#owns(o, tabId)) throw new Error("Only tabs this job opened or claimed can be kept.");
    o.kept[tabId] = why === "waiting" ? "waiting" : "result";
    return this.#save();
  }

  async end(task, tidy) {
    const o = this.owners_.get(task);
    if (!o) return;
    if (tidy) await this.#tidy(o);
    this.owners_.delete(task);
    await this.#save();
  }

  async sweep(running) {
    const live = new Set(running);
    for (const task of [...this.owners_.keys()]) if (!live.has(task)) await this.end(task, false);
  }

  // ponytail: tidy is best effort per tab (a tab the user closed or a window that is gone is skipped);
  // upgrade to reporting skipped tabs if the card ever needs to show them.
  async #tidy(o) {
    const viewed = await this.#viewedTabId();
    for (const id of [...o.opened]) {
      if (id === viewed || o.kept[id]) continue;
      let tab;
      try { tab = await this.api.tabs.get(id); } catch { continue; } // the user closed it already
      if (o.groupId === NONE || tab.groupId !== o.groupId) continue; // dragged out of the group: theirs now
      try { await this.api.tabs.remove(id); } catch { /* gone */ }
    }
    for (const [key, saved] of Object.entries(o.claimed)) {
      const id = Number(key);
      try {
        const tab = await this.api.tabs.get(id);
        if (o.groupId === NONE || tab.groupId !== o.groupId) continue; // the user moved it themselves
        if (saved.groupId !== NONE && await this.#groupExists(saved.groupId)) {
          await this.api.tabs.group({ tabIds: [id], groupId: saved.groupId });
        } else {
          await this.api.tabs.ungroup([id]);
        }
        await this.api.tabs.move([id], { windowId: saved.windowId, index: saved.index });
      } catch { /* closed, or its window is gone */ }
    }
    if (o.groupId !== NONE) {
      try {
        if ((await this.api.tabs.query({ groupId: o.groupId })).length > 0) {
          await this.api.tabGroups.update(o.groupId, { collapsed: true });
        }
      } catch { /* group already gone */ }
    }
  }

  #owner(task) {
    const o = this.owners_.get(task);
    if (!o) throw new Error("Unknown job. Start it before using tabs.");
    return o;
  }

  #owns(o, tabId) {
    return o.opened.includes(tabId) || Object.hasOwn(o.claimed, tabId);
  }

  #forgetTab(o, tabId) {
    o.opened = o.opened.filter((id) => id !== tabId);
    delete o.claimed[tabId];
    delete o.kept[tabId];
    if (o.current === tabId) o.current = undefined;
  }

  async #groupExists(groupId) {
    try { await this.api.tabGroups.get(groupId); return true; } catch { return false; }
  }

  async #join(o, tabId, windowId) {
    if (o.groupId !== NONE && await this.#groupExists(o.groupId)) {
      await this.api.tabs.group({ tabIds: [tabId], groupId: o.groupId });
      return;
    }
    o.groupId = await this.api.tabs.group({ tabIds: [tabId], createProperties: { windowId } });
    await this.api.tabGroups.update(o.groupId, { title: o.title, color: o.color, collapsed: false });
  }

  async #workWindow() {
    try {
      const w = await this.api.windows.getLastFocused({ windowTypes: ["normal"] });
      if (w && !w.incognito) return w;
    } catch { /* fall through to a new window */ }
    return this.api.windows.create({ type: "normal", focused: false });
  }

  async #viewedTabId() {
    try {
      const w = await this.api.windows.getLastFocused({ windowTypes: ["normal"] });
      const [t] = await this.api.tabs.query({ active: true, windowId: w.id });
      return t?.id;
    } catch { return undefined; }
  }

  #save() {
    return this.store.save({ owners: [...this.owners_.values()].map((o) => structuredClone(o)) });
  }
}
