// Wires the dispatcher to Chrome: the native host port to OpenClicky, chrome.debugger, tab groups, the popup.
// Production uses chrome.debugger only, never --remote-debugging-port.
import { createBridge } from "./core.mjs";
import { TabManager } from "./tabs.mjs";

const HOST = "com.openclicky.chrome_bridge";
const ALARM = "openclicky-connect";
const STORE_KEY = "openclickyTabs";
const REFS_KEY = "openclickyRefs";

const tabs = new TabManager(
  { tabs: chrome.tabs, tabGroups: chrome.tabGroups, windows: chrome.windows },
  {
    load: async () => (await chrome.storage.session.get(STORE_KEY))[STORE_KEY],
    save: (state) => chrome.storage.session.set({ [STORE_KEY]: state }),
  },
);
const restored = tabs.restore().catch(() => {});

const bridge = createBridge({
  debuggee: {
    attach: (tabId) => chrome.debugger.attach({ tabId }, "1.3"),
    detach: (tabId) => chrome.debugger.detach({ tabId }),
    send: (tabId, method, params) => chrome.debugger.sendCommand({ tabId }, method, params),
    onEvent: (cb) => chrome.debugger.onEvent.addListener((source, method, params) => {
      if (source.tabId !== undefined) cb(source.tabId, method, params ?? {});
    }),
  },
  tabs,
  downloads: chrome.downloads,
  refStore: {
    load: async () => (await chrome.storage.session.get(REFS_KEY))[REFS_KEY],
    save: (counters) => chrome.storage.session.set({ [REFS_KEY]: counters }),
  },
});

let port = null;
let link = "checking"; // checking | setup | offline | profile | connected

const state = () => (link === "connected" && tabs.owners().length ? "working" : link);

function post(message) {
  try { port?.postMessage(message); } catch { /* the port just closed; onDisconnect follows */ }
}

function refresh() {
  const s = state();
  const on = s === "connected" || s === "working";
  chrome.action.setBadgeText({ text: s === "working" ? "•" : "" }).catch(() => {});
  chrome.action.setIcon({ path: on ? "icons/on-32.png" : "icons/off-32.png" }).catch(() => {});
}

// Memoised: install and the top-level wake-up both connect at once, and must not mint two ids.
let profileIdPromise = null;
function profileId() {
  profileIdPromise ??= (async () => {
    const { profileId: saved } = await chrome.storage.local.get("profileId");
    if (saved) return saved;
    const id = crypto.randomUUID();
    await chrome.storage.local.set({ profileId: id });
    return id;
  })().catch((e) => { profileIdPromise = null; throw e; });
  return profileIdPromise;
}

function brand() {
  const names = (navigator.userAgentData?.brands ?? []).map((b) => b.brand);
  return names.find((n) => n !== "Chromium" && !/not.?a.?brand/i.test(n)) ?? (names.includes("Chromium") ? "Chromium" : "Chrome");
}

async function onMessage(message) {
  if (message?.type === "ready") {
    link = !message.app ? "offline" : message.selected ? "connected" : "profile";
  } else if (message?.type === "command") {
    await restored;
    const r = await bridge.execute(message);
    post({ type: "result", id: message.id, ...r });
  }
  refresh();
}

// One attempt at a time: callers during an attempt share it.
let connecting = null;
function connect() {
  connecting ??= open().catch(() => {}).finally(() => { connecting = null; });
  return connecting;
}

async function open() {
  if (port) return;
  const id = await profileId();
  if (port) return;
  link = "checking";
  refresh();
  port = chrome.runtime.connectNative(HOST);
  port.onMessage.addListener((m) => { onMessage(m).catch(() => {}); });
  port.onDisconnect.addListener(() => {
    const why = chrome.runtime.lastError?.message ?? "";
    port = null;
    bridge.handOverAll();
    link = /not found|forbidden/i.test(why) ? "setup" : "offline";
    refresh();
  });
  post({ type: "hello", protocol: 2, profileId: id, brand: brand() });
}

chrome.runtime.onInstalled.addListener(() => { connect(); });
chrome.runtime.onStartup.addListener(() => { connect(); });
chrome.alarms.create(ALARM, { periodInMinutes: 0.5 });
chrome.alarms.onAlarm.addListener((a) => { if (a.name === ALARM) connect(); });
connect(); // also on any other wake-up (popup open), so the popup never waits for the alarm

chrome.debugger.onDetach.addListener((source, reason) => {
  if (source.tabId === undefined) return;
  for (const task of bridge.onDetach(source.tabId, reason)) post({ type: "stop", task });
  refresh();
});

async function status() {
  await restored;
  const jobs = [];
  for (const o of tabs.owners().slice(0, 3)) {
    const list = [];
    for (const id of o.tabs.slice(0, 4)) {
      try { list.push({ id, title: (await chrome.tabs.get(id)).title || "Untitled tab" }); } catch { /* closed */ }
    }
    jobs.push({ title: o.title, say: o.say, tabs: list });
  }
  return { state: state(), jobs };
}

async function onPopup(message) {
  switch (message?.type) {
    case "status":
      return status();
    case "stop":
      await bridge.handOverAll();
      post({ type: "stop" });
      break;
    case "select":
      post({ type: "select" });
      break;
    case "openApp":
      if (!port) await connect();
      post({ type: "openApp" });
      break;
    case "focusTab": {
      const id = message.tabId;
      if (!tabs.owners().some((o) => o.tabs.includes(id))) break; // only this extension's job tabs
      const tab = await chrome.tabs.update(id, { active: true });
      await chrome.windows.update(tab.windowId, { focused: true });
      break;
    }
  }
  refresh();
  return status();
}

chrome.runtime.onMessage.addListener((message, sender, reply) => {
  if (sender.id !== chrome.runtime.id || sender.tab) return false; // only our own popup
  onPopup(message).then(reply, () => reply(null));
  return true;
});
