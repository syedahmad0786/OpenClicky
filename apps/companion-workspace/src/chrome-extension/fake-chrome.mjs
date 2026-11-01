// In-memory Chrome for the extension tests: windows, per-window tab order (indexes derive from it), and tab
// groups that vanish when empty. Shared by tabs.test.mjs and core.test.mjs.
export function fakeChrome() {
  const f = { windows: new Map(), tabs: new Map(), order: new Map(), groups: new Map(), nextTab: 100, nextGroup: 50, nextWindow: 2, focusFails: false };
  const view = (t) => ({ ...t, index: f.order.get(t.windowId).indexOf(t.id) });
  const gc = () => { for (const g of [...f.groups.keys()]) if (![...f.tabs.values()].some((t) => t.groupId === g)) f.groups.delete(g); };
  const need = (id) => { if (!f.tabs.has(id)) throw new Error(`No tab with id: ${id}`); return f.tabs.get(id); };
  const ids = (x) => (Array.isArray(x) ? x : [x]);
  const place = (t, windowId, index) => {
    f.order.get(t.windowId)?.splice(f.order.get(t.windowId).indexOf(t.id), 1);
    t.windowId = windowId;
    const list = f.order.get(windowId);
    list.splice(index === undefined ? list.length : Math.min(index, list.length), 0, t.id);
  };

  f.addWindow = (id, props = {}) => { f.windows.set(id, { id, type: "normal", incognito: false, focused: false, ...props }); f.order.set(id, []); };
  f.add = (id, windowId, props = {}) => {
    const t = { id, windowId, groupId: -1, active: false, incognito: false, url: `https://site${id}.test/`, title: `Tab ${id}`, ...props };
    f.tabs.set(id, t);
    f.order.get(windowId).push(id);
    return t;
  };
  f.activate = (id) => { const t = f.tabs.get(id); for (const o of f.tabs.values()) if (o.windowId === t.windowId) o.active = o.id === id; };
  f.addGroup = (id, windowId, title = "User group") => f.groups.set(id, { id, windowId, title, color: "grey", collapsed: false });
  f.get = (id) => view(f.tabs.get(id));
  f.indexes = (windowId) => f.order.get(windowId).slice();
  f.snapshot = () => JSON.stringify([[...f.tabs.values()].map((t) => view(t)), [...f.groups.values()], [...f.order.entries()]]);

  f.api = {
    tabs: {
      query: async (q = {}) => [...f.tabs.values()]
        .filter((t) => (q.active === undefined || t.active === q.active) && (q.windowId === undefined || t.windowId === q.windowId) && (q.groupId === undefined || t.groupId === q.groupId))
        .map(view),
      get: async (id) => view(need(id)),
      create: async ({ url, active, windowId, index }) => {
        const t = { id: f.nextTab++, windowId, groupId: -1, active: false, incognito: false, url, title: url };
        f.tabs.set(t.id, t);
        f.order.get(windowId).splice(index ?? f.order.get(windowId).length, 0, t.id);
        if (active) f.activate(t.id);
        return view(t);
      },
      update: async (id, p) => { need(id); if (p.active) f.activate(id); return view(f.tabs.get(id)); },
      move: async (tabIds, { windowId, index }) => {
        for (const id of ids(tabIds)) {
          const t = need(id);
          if (!f.windows.has(windowId)) throw new Error("No window");
          if (t.windowId !== windowId) t.groupId = -1; // changing window drops the group
          place(t, windowId, index);
        }
        gc();
      },
      remove: async (tabIds) => {
        for (const id of ids(tabIds)) { const t = need(id); f.order.get(t.windowId).splice(f.order.get(t.windowId).indexOf(id), 1); f.tabs.delete(id); }
        gc();
      },
      group: async ({ tabIds, groupId, createProperties }) => {
        let gid = groupId;
        if (gid === undefined) { gid = f.nextGroup++; f.addGroup(gid, createProperties.windowId, ""); }
        else if (!f.groups.has(gid)) throw new Error("No group");
        for (const id of ids(tabIds)) {
          const t = need(id);
          t.groupId = gid;
          if (groupId !== undefined) { // joining an existing group lands next to its last member
            const gw = f.groups.get(gid).windowId;
            place(t, gw); // out of the way (end of the window), then right after the group's last member
            const list = f.order.get(gw);
            const last = Math.max(...list.filter((x) => x !== id && f.tabs.get(x).groupId === gid).map((x) => list.indexOf(x)), -1);
            if (last !== -1) place(t, gw, last + 1);
          }
        }
        gc();
        return gid;
      },
      ungroup: async (tabIds) => { for (const id of ids(tabIds)) need(id).groupId = -1; gc(); },
    },
    tabGroups: {
      get: async (id) => { if (!f.groups.has(id)) throw new Error("No group"); return { ...f.groups.get(id) }; },
      update: async (id, p) => { if (!f.groups.has(id)) throw new Error("No group"); Object.assign(f.groups.get(id), p); },
    },
    windows: {
      getLastFocused: async () => {
        if (f.focusFails) throw new Error("No window");
        const w = [...f.windows.values()].find((x) => x.focused);
        if (!w) throw new Error("No window");
        return { ...w };
      },
      create: async () => {
        const id = f.nextWindow++;
        f.addWindow(id, { focused: true });
        f.add(f.nextTab++, id, { active: true, url: "chrome://newtab/" });
        return { ...f.windows.get(id) };
      },
    },
  };
  return f;
}
