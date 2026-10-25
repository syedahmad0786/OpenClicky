// Real-Chrome acceptance harness for the extension's page layer (sub-project G, task G10).
//
//   node apps/chrome-extension/test/cdp-smoke.mjs [chromePath]
//
// Starts its own headless Chrome in a throwaway profile under D:\tmp-build, serves test/fixture.html from
// 127.0.0.1 and drives a PageSession through a WebSocket debuggee. It never touches the user's own Chrome or
// profile: it kills only the PID it started (and that tree) and deletes only the profile folder it made.
// Prints one `PASS <name>` per check, `INFO` lines for recorded facts, and exits non-zero on any failure.
import { spawn, spawnSync } from "node:child_process";
import fs from "node:fs";
import http from "node:http";
import net from "node:net";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { PageSession, PASSWORD_REFUSAL } from "../page.mjs";

const CHROME = process.argv[2] ?? "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";
const BUILD_ROOT = "D:\\tmp-build";
const SECRET = "hunter2-secret";
const COOKIE = "sid=topsecret-cookie";
const here = path.dirname(fileURLToPath(import.meta.url));

const failures = [];
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function check(name, fn) {
  try {
    await fn();
    console.log(`PASS ${name}`);
  } catch (e) {
    failures.push(name);
    console.log(`FAIL ${name}: ${String(e?.message ?? e).split("\n")[0]}`);
  }
}
const info = (text) => console.log(`INFO ${text}`);
const assert = (cond, why) => { if (!cond) throw new Error(why); };
async function rejects(promise, pattern) {
  let error = null;
  try { await promise; } catch (e) { error = e; }
  assert(error, "expected a refusal but the call succeeded");
  const text = String(error.message);
  assert(typeof pattern === "string" ? text === pattern : pattern.test(text), `unexpected message: ${text.slice(0, 200)}`);
}

// ---- Chrome ----------------------------------------------------------------------------------------------

const launched = []; // every Chrome this run started: { proc, profile }

async function launchChrome(tag, url, extra = []) {
  const profile = path.join(BUILD_ROOT, `openclicky-g-${process.pid}${tag}`);
  assert(profile.startsWith(`${BUILD_ROOT}\\openclicky-g-`), "refusing a profile outside the build folder");
  fs.rmSync(profile, { recursive: true, force: true });
  fs.mkdirSync(profile, { recursive: true });
  const proc = spawn(CHROME, [
    "--headless=new", `--user-data-dir=${profile}`, "--remote-debugging-port=0", "--no-first-run", "--no-default-browser-check", ...extra, url,
  ], { stdio: "ignore", windowsHide: true });
  const entry = { proc, profile, exited: false };
  proc.once("exit", () => { entry.exited = true; });
  proc.once("error", () => { entry.exited = true; });
  launched.push(entry);
  const portFile = path.join(profile, "DevToolsActivePort");
  for (let i = 0; i < 200 && !fs.existsSync(portFile); i++) {
    assert(!entry.exited, "Chrome exited before opening the debugging port");
    await sleep(100);
  }
  assert(fs.existsSync(portFile), "Chrome did not write DevToolsActivePort");
  const port = Number(fs.readFileSync(portFile, "utf8").split(/\r?\n/)[0]);
  assert(port > 0, "bad DevToolsActivePort");
  entry.port = port;
  entry.pid = proc.pid;
  return entry;
}

async function getJson(url) {
  const r = await fetch(url);
  return r.json();
}

async function stopChrome(entry) {
  if (!entry.exited && entry.pid) spawnSync("taskkill", ["/PID", String(entry.pid), "/T", "/F"], { stdio: "ignore" });
  for (let i = 0; i < 50 && !entry.exited; i++) await sleep(100);
  for (let i = 0; i < 20; i++) {
    try { fs.rmSync(entry.profile, { recursive: true, force: true }); return; } catch { await sleep(500); }
  }
}

// Synchronous cleanup of only what this run started: our Chrome PIDs (with their trees) and our profile folders.
function cleanupSync() {
  for (const e of launched) if (!e.exited && e.pid) { spawnSync("taskkill", ["/PID", String(e.pid), "/T", "/F"], { stdio: "ignore" }); e.exited = true; }
  for (const e of launched) {
    if (!e.profile.startsWith(`${BUILD_ROOT}\\openclicky-g-`)) continue;
    let gone = false;
    for (let i = 0; i < 10 && !gone; i++) {
      try { fs.rmSync(e.profile, { recursive: true, force: true }); gone = !fs.existsSync(e.profile); } catch { /* locked: Chrome is still closing */ }
      if (!gone) Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 300);
    }
    if (!gone) info(`could not remove ${e.profile}; delete it by hand`);
  }
  launched.length = 0;
}
// A crash, an unhandled rejection, Ctrl+C or a terminate must not leave Chrome or a profile folder behind.
process.on("exit", cleanupSync);
for (const [event, why] of [["uncaughtException", "uncaught exception"], ["unhandledRejection", "unhandled rejection"], ["SIGINT", "interrupted"], ["SIGTERM", "terminated"]]) {
  process.on(event, (e) => {
    console.log(`FAIL harness: ${why}${e instanceof Error ? `: ${e.stack}` : ""}`);
    cleanupSync();
    process.exit(1);
  });
}

// ---- Who is listening where ----------------------------------------------------------------------------------

/** The PID plus all its descendants (Chrome's DevTools socket belongs to the browser process, but be safe). */
function ownedPids(root) {
  const r = spawnSync("powershell", ["-NoProfile", "-Command", "Get-CimInstance Win32_Process | ForEach-Object { \"$($_.ProcessId) $($_.ParentProcessId)\" }"], { encoding: "utf8" });
  const parent = new Map();
  for (const line of String(r.stdout).split(/\r?\n/)) { const m = /^(\d+) (\d+)$/.exec(line.trim()); if (m) parent.set(Number(m[1]), Number(m[2])); }
  const set = new Set([root]);
  for (let grew = true; grew;) { grew = false; for (const [pid, ppid] of parent) if (set.has(ppid) && !set.has(pid)) { set.add(pid); grew = true; } }
  return set;
}

/** LISTENING rows for a port from both `netstat -p tcp` (IPv4) and `-p tcpv6`. */
function listeningRows(port) {
  const rows = [];
  for (const proto of ["tcp", "tcpv6"]) {
    const out = spawnSync("netstat", ["-ano", "-p", proto], { encoding: "utf8" }).stdout ?? "";
    for (const line of out.split(/\r?\n/)) {
      const m = /^\s*TCP\s+(\S+):(\d+)\s+\S+\s+LISTENING\s+(\d+)\s*$/.exec(line);
      if (m && Number(m[2]) === port) rows.push({ address: m[1], pid: Number(m[3]) });
    }
  }
  return rows;
}

function tryConnect(host, port) {
  return new Promise((resolve) => {
    const socket = net.connect({ host, port });
    const done = (result) => { socket.destroy(); resolve(result); };
    socket.setTimeout(2000, () => done("timed out"));
    socket.once("connect", () => done("connected"));
    socket.once("error", (e) => done(`refused (${e.code})`));
  });
}

/** Passes when the script is blocked by the guard's own message, or returns without the cookie value. */
async function cookieClosed(js, code, via) {
  let text;
  try { text = (await js(code)).text; } catch (e) {
    assert(/Not available to agents/.test(String(e.message)), `${via} failed for another reason: ${String(e.message).slice(0, 160)}`);
    info(`${via} guard: ${String(e.message).slice(0, 120)}`);
    return;
  }
  assert(!text.includes(COOKIE), `${via}.document.cookie returned ${text}`);
  info(`${via} returned without the cookie value: ${text}`);
}

// ---- Debuggee over a page WebSocket ------------------------------------------------------------------------

class Page {
  constructor(ws) {
    this.ws = ws;
    this.nextId = 1;
    this.pending = new Map();
    this.events = [];
    this.listener = null;
    ws.onmessage = (m) => {
      const msg = JSON.parse(String(m.data));
      if (msg.id !== undefined) {
        const p = this.pending.get(msg.id);
        this.pending.delete(msg.id);
        if (!p) return;
        if (msg.error) p.reject(new Error(`${msg.error.message} (${msg.error.code})`));
        else p.resolve(msg.result ?? {});
      } else if (msg.method) {
        this.events.push({ method: msg.method, params: msg.params ?? {} });
        this.listener?.(msg.method, msg.params ?? {});
      }
    };
  }

  static async connect(url) {
    assert(new URL(url).hostname === "127.0.0.1", `page socket is not on loopback: ${url}`);
    const ws = new WebSocket(url);
    await new Promise((resolve, reject) => { ws.onopen = resolve; ws.onerror = () => reject(new Error("page WebSocket failed")); });
    return new Page(ws);
  }

  send(method, params = {}) {
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.ws.send(JSON.stringify({ id, method, params }));
    });
  }

  async eval(expression) {
    const r = await this.send("Runtime.evaluate", { expression, returnByValue: true, awaitPromise: true });
    if (r.exceptionDetails) throw new Error(`eval failed: ${r.exceptionDetails.text}`);
    return r.result?.value;
  }
}

// ---- Fixture server (loopback only) ------------------------------------------------------------------------

function startServer() {
  const fixture = fs.readFileSync(path.join(here, "fixture.html"), "utf8");
  const pages = {
    "/": fixture,
    "/page2": `<!doctype html><meta charset="utf-8"><title>Second page</title><h1>Second page</h1>
      <label for="q">Search</label> <input id="q" type="text"><p><button type="button">Go</button></p>`,
    "/frame": `<!doctype html><meta charset="utf-8"><title>Child</title><p>Child frame</p><input id="cpw" type="password" aria-label="Child password">`,
  };
  const server = http.createServer((req, res) => {
    const body = pages[new URL(req.url, "http://127.0.0.1").pathname];
    if (body === undefined) { res.writeHead(404).end("not found"); return; }
    res.writeHead(200, { "content-type": "text/html; charset=utf-8", "set-cookie": `${COOKIE}; Path=/`, "cache-control": "no-store" });
    res.end(body);
  });
  return new Promise((resolve) => server.listen(0, "127.0.0.1", () => resolve(server)));
}

// ---- Small helpers -----------------------------------------------------------------------------------------

/** Every `role "name" [eN]` line as { name -> ref }. */
function refsIn(text) {
  const map = new Map();
  for (const line of text.split("\n")) {
    const m = /^\s*\S+ "(.*?)" \[(e\d+)\]/.exec(line);
    if (m) map.set(m[1], m[2]);
  }
  return map;
}

function jpegSize(b) {
  for (let i = 2; i + 8 < b.length;) {
    if (b[i] !== 0xff) { i++; continue; }
    const marker = b[i + 1];
    if (marker >= 0xc0 && marker <= 0xc3) return { h: b.readUInt16BE(i + 5), w: b.readUInt16BE(i + 7) };
    i += 2 + b.readUInt16BE(i + 2);
  }
  throw new Error("not a JPEG");
}

// ---- Run ---------------------------------------------------------------------------------------------------

async function main() {
  assert(fs.existsSync(CHROME), `Chrome not found at ${CHROME}`);
  const server = await startServer();
  const addr = server.address();
  const base = `http://127.0.0.1:${addr.port}`;
  let main = null;
  try {
    await check("fixture server binds 127.0.0.1", async () => assert(addr.address === "127.0.0.1", `bound to ${addr.address}`));

    main = await launchChrome("", "about:blank");
    let version;
    await check("DevTools endpoint is on 127.0.0.1", async () => {
      version = await getJson(`http://127.0.0.1:${main.port}/json/version`);
      const host = new URL(version.webSocketDebuggerUrl).hostname;
      assert(host === "127.0.0.1", `webSocketDebuggerUrl host is ${host}`);
      info(`${version.Browser}, pid ${main.pid}, profile ${main.profile}`);
    });
    if (!version) throw new Error("no DevTools endpoint");

    // The URL above only echoes our Host header: prove who is really listening on the port, and where.
    await check("DevTools port listens only on loopback (netstat, owned by our Chrome)", async () => {
      const mine = ownedPids(main.pid);
      const rows = listeningRows(main.port);
      assert(rows.length > 0, `netstat shows nothing listening on port ${main.port}`);
      for (const r of rows) {
        assert(mine.has(r.pid), `port ${main.port} is held by pid ${r.pid}, not our Chrome tree`);
        assert(r.address === "127.0.0.1" || r.address === "[::1]", `DevTools listens on ${r.address}:${main.port}`);
      }
      info(`listening on ${rows.map((r) => `${r.address}:${main.port} (pid ${r.pid})`).join(", ")}`);
    });
    await check("DevTools port refuses a non-loopback local address", async () => {
      const external = Object.values(os.networkInterfaces()).flat().filter((i) => i && i.family === "IPv4" && !i.internal).map((i) => i.address);
      if (!external.length) { info("no non-loopback IPv4 address on this machine; skipping the non-loopback connect"); return; }
      const outcomes = [];
      for (const address of external) {
        const outcome = await tryConnect(address, main.port);
        info(`connect to ${address}:${main.port} -> ${outcome}`);
        assert(outcome !== "connected", `the DevTools port accepted a connection on ${address}`);
        outcomes.push(outcome);
      }
      // Windows Firewall may silently drop instead of refusing; either way nothing accepted. The netstat check above is the proof of the bind.
      if (!outcomes.some((o) => o.startsWith("refused"))) info("no address answered with an explicit refusal (the firewall drops them); none accepted the connection");
    });

    let targets = [];
    for (let i = 0; i < 50 && !targets.some((t) => t.type === "page"); i++) {
      targets = await getJson(`http://127.0.0.1:${main.port}/json/list`);
      await sleep(100);
    }
    const target = targets.find((t) => t.type === "page");
    assert(target, "no page target");
    const page = await Page.connect(target.webSocketDebuggerUrl);
    const session = new PageSession((method, params) => page.send(method, params));
    page.listener = (method, params) => session.onEvent(method, params);
    await session.prepare();

    const mark = () => page.events.length;
    const since = (i, method) => page.events.slice(i).filter((e) => e.method === method);

    const loadMark = mark();
    await check("navigate opens the fixture; settle waits for the load", async () => {
      await session.navigate(`${base}/`);
      assert((await page.eval("location.pathname")) === "/", "not on the fixture");
      assert((await page.eval("document.readyState")) === "complete", "page still loading after settle");
    });
    await check("Page.frameStartedLoading and frameStoppedLoading fire for navigation", async () => {
      const started = since(loadMark, "Page.frameStartedLoading");
      const stopped = since(loadMark, "Page.frameStoppedLoading");
      assert(started.some((e) => e.params.frameId === session.mainFrameId), "no frameStartedLoading for the main frame");
      assert(stopped.some((e) => e.params.frameId === session.mainFrameId), "no frameStoppedLoading for the main frame");
      assert(since(loadMark, "Page.frameNavigated").length > 0, "no frameNavigated");
    });

    // -- snapshot and refs
    let first = new Map();
    await check("snapshot lists the fixture controls with refs", async () => {
      const text = (await session.snapshot({})).text;
      first = refsIn(text);
      for (const name of ["Name", "Password", "Colour", "Notes", "Add line", "Show banner", "Under banner", "Second page", "Alert", "Corner"]) {
        assert(first.has(name), `no ref for "${name}"`);
      }
      assert(/Line|Stock|Apples/.test(text) || text.includes("OpenClicky Fixture"), "headings and text are missing");
    });
    await check("refs are the same on a second snapshot", async () => {
      const again = refsIn((await session.snapshot({})).text);
      for (const [name, ref] of first) assert(again.get(name) === ref, `"${name}" went from ${ref} to ${again.get(name)}`);
    });
    const ref = (name) => { const r = first.get(name); assert(r, `no ref for "${name}"`); return r; };

    await check("click on the add button returns a diff with the new line", async () => {
      const before = session.lines;
      const r = await session.click({ ref: ref("Add line") });
      assert(/Clicked/.test(r.text), r.text);
      await session.snapshot({});
      const diff = session.diffSince(before);
      assert(diff.startsWith("Changes since your last look") && /\+ .*Line 1/.test(diff), `diff was: ${diff.slice(0, 200)}`);
    });

    await check("fill and read-back on the text field", async () => {
      await session.fill({ ref: ref("Name"), text: "Ada Lovelace" });
      assert((await page.eval("document.getElementById('name').value")) === "Ada Lovelace", "value not kept");
    });
    await check("fill on the select and the contenteditable", async () => {
      await session.fill({ ref: ref("Colour"), text: "Green" });
      assert((await page.eval("document.getElementById('color').value")) === "g", "select not changed");
      await session.fill({ ref: ref("Notes"), text: "Remember the milk" });
      assert((await page.eval("document.getElementById('notes').innerText")) === "Remember the milk", "contenteditable not filled");
    });

    // -- passwords
    await check("password refused: fill returns the exact refusal and the field stays empty", async () => {
      await rejects(session.fill({ ref: ref("Password"), text: SECRET }), PASSWORD_REFUSAL);
      assert((await page.eval("document.getElementById('pw').value")) === "", "password field was changed");
    });
    await check("password refused: type into the focused password field", async () => {
      await session.click({ ref: ref("Password") });
      assert((await page.eval("document.activeElement.id")) === "pw", "password field did not take focus");
      await rejects(session.type({ text: SECRET }), PASSWORD_REFUSAL);
      assert((await page.eval("document.getElementById('pw').value")) === "", "password field was changed");
    });
    await check("password value never appears in a snapshot, extract or the AX StaticText under it", async () => {
      await page.eval(`document.getElementById('pw').value = ${JSON.stringify(SECRET)}`); // test setup, main world
      const ax = JSON.stringify((await page.send("Accessibility.getFullAXTree")).nodes);
      info(`raw AX tree contains the password text: ${ax.includes(SECRET)}`);
      const snap = (await session.snapshot({})).text;
      assert(!snap.includes(SECRET), "the snapshot contains the password text");
      const query = (await session.snapshot({ query: "Password" })).text;
      assert(!query.includes(SECRET), "a queried snapshot contains the password text");
      const extract = (await session.extract()).text;
      assert(!extract.includes(SECRET), "extract contains the password text");
      const line = snap.split("\n").find((l) => /"Password" \[e\d+\]/.test(l)) ?? "";
      info(`password line in snapshot: ${line.trim()}`);
    });

    // -- covered click
    await check("click under the banner fails, then succeeds with dismiss:true", async () => {
      await session.click({ ref: ref("Show banner") });
      assert((await page.eval("!document.getElementById('banner').hidden")) === true, "banner did not appear");
      await rejects(session.click({ ref: ref("Under banner") }), /covering/);
      assert((await page.eval("document.getElementById('ucount').textContent")) === "0", "the covered click went through");
      await session.click({ ref: ref("Under banner"), dismiss: true });
      assert((await page.eval("document.getElementById('banner').hidden")) === true, "the banner was not closed");
      assert((await page.eval("document.getElementById('ucount').textContent")) === "1", "the button was not clicked after dismissing");
    });

    await check("wait_for gone: true finishes once the text is absent and waits while it is there", async () => {
      const done = await session.waitFor({ text: "We use cookies on this page.", gone: true, timeout: 5 }); // the banner was closed
      assert(done.text === "The page is ready.", done.text);
      await rejects(session.waitFor({ text: "Second page", gone: true, timeout: 1 }), "Still waiting after 1 s.");
    });

    await check("extract has the table CSV and no field values", async () => {
      const text = (await session.extract()).text;
      assert(text.includes("Item,Qty") && text.includes("Apples,3") && text.includes("Pears,5"), `table CSV missing: ${text.slice(0, 200)}`);
      assert(!text.includes("Ada Lovelace"), "a text field value leaked into extract");
      info(`contenteditable text in extract (page text, not an input value): ${text.includes("Remember the milk")}`);
    });

    // -- javascript guard
    const js = (code) => session.javascript({ code });
    await check("javascript returning document.title works", async () => {
      assert((await js("return document.title")).text === '"OpenClicky Fixture"', "title not returned");
    });
    await check("javascript reading document.cookie fails", async () => {
      assert((await page.eval("document.cookie")).includes(COOKIE), "setup: the page has no cookie to protect");
      await rejects(js("return document.cookie"), /Not available to agents/);
    });
    await check("javascript cannot read a same-origin child iframe's cookie via frames[0]", async () => {
      assert((await page.eval("frames[0].document.cookie")).includes(COOKIE), "setup: the child frame has no cookie to protect");
      await cookieClosed(js, "return frames[0].document.cookie", "frames[0]");
    });
    await check("javascript cannot read a same-origin child iframe's cookie via window[0]", async () => {
      await cookieClosed(js, "return window[0].document.cookie", "window[0]");
    });
    await check("javascript: storage blocked and password value reads blank", async () => {
      await rejects(js("return localStorage.length"), /Not available to agents/);
      assert((await js("return document.getElementById('pw').value")).text === '""', "password value was readable");
      await rejects(js("document.getElementById('pw').value = 'x'"), /password field/);
      assert((await page.eval("document.getElementById('pw').value")) === SECRET, "the page's password was changed by a script");
    });

    // -- dialogs
    await check("the alert blocks a click until answerDialog", async () => {
      await session.click({ ref: ref("Alert") });
      assert(session.dialog?.message === "Heads up", "the dialog was not seen");
      await rejects(session.click({ ref: ref("Add line") }), /dialog is open/);
      await session.answerDialog({ accept: true });
      assert(session.dialog === null, "dialog still open");
      assert((await page.eval("document.getElementById('alerted').textContent")) === "answered", "the page never saw the dialog close");
      await session.click({ ref: ref("Add line") });
    });

    // -- screenshot and coordinates
    await check("screenshot is CSS-pixel sized and enables click_at", async () => {
      await page.send("Emulation.setDeviceMetricsOverride", { width: 800, height: 600, deviceScaleFactor: 2, mobile: false });
      await sleep(300);
      try {
        await rejects(session.clickAt({ x: 68, y: 28 }), /screenshot/);
        const shot = await session.screenshot();
        // The visual viewport excludes the 15 px scrollbar, so compare with the document's client size, not innerWidth.
        const css = await page.eval("[document.documentElement.clientWidth, document.documentElement.clientHeight, devicePixelRatio]");
        assert(css[2] === 2, `device scale factor was ${css[2]}`);
        const size = jpegSize(Buffer.from(shot.image.data, "base64"));
        assert(size.w === css[0] && size.h === css[1], `image ${size.w}x${size.h} vs CSS viewport ${css[0]}x${css[1]}`);
        assert(shot.text.includes(`${css[0]}×${css[1]} CSS pixels`), shot.text);
        await session.clickAt({ x: 68, y: 28 }); // middle of the fixed Corner button
        assert((await page.eval("document.getElementById('cornercount').textContent")) === "Corner clicks: 1", "click_at missed the button");
      } finally {
        await page.send("Emulation.clearDeviceMetricsOverride");
      }
    });

    // -- navigation
    const navMark = mark();
    await check("navigation retires refs", async () => {
      await session.click({ ref: ref("Second page") });
      assert((await page.eval("location.pathname")) === "/page2", "did not reach the second page");
      await rejects(session.click({ ref: ref("Add line") }), /earlier page/);
      const started = since(navMark, "Page.frameStartedLoading").some((e) => e.params.frameId === session.mainFrameId);
      const stopped = since(navMark, "Page.frameStoppedLoading").some((e) => e.params.frameId === session.mainFrameId);
      assert(started && stopped, "navigation did not fire the frame loading events");
    });
    await check("press Enter before a snapshot is refused in a new epoch", async () => {
      await rejects(session.press({ key: "Enter" }), /snapshot/);
      await session.press({ key: "Tab" }); // keys that don't submit need no snapshot
      const second = refsIn((await session.snapshot({})).text);
      assert(second.has("Search"), "second page not snapshotted");
      for (const r of second.values()) assert(![...first.values()].includes(r), `ref ${r} was reused after navigation`);
      await session.press({ key: "Enter" });
    });
    await check("back returns to the fixture", async () => {
      await session.back();
      assert((await page.eval("location.pathname")) === "/", "still on the second page");
    });

    page.ws.close();

    // -- the install-help launch: chrome://extensions/ on the command line (plain, then with the product's --profile-directory)
    for (const [tag, extra] of [["-ext", []], ["-ext2", ["--profile-directory=Default"]]]) {
      // A record, not a check: INFO only (a launch problem is reported but is not a failure of the extension).
      try {
        const ext = await launchChrome(tag, "chrome://extensions/", extra);
        let urls = [];
        for (let i = 0; i < 50; i++) {
          try { urls = (await getJson(`http://127.0.0.1:${ext.port}/json/list`)).filter((t) => t.type === "page").map((t) => t.url); } catch { /* starting */ }
          if (urls.includes("chrome://extensions/")) break;
          await sleep(200);
        }
        info(`headless command line ${extra.join(" ")} chrome://extensions/ -> page targets ${JSON.stringify(urls)}; target URL is chrome://extensions/: ${urls.includes("chrome://extensions/")}`);
        await stopChrome(ext);
      } catch (e) {
        info(`could not record the chrome://extensions/ launch ${extra.join(" ")}: ${String(e?.message ?? e).split("\n")[0]}`);
      }
    }
  } finally {
    for (const entry of launched) await stopChrome(entry);
    await new Promise((r) => server.close(r));
  }
}

try {
  await main();
} catch (e) {
  failures.push("harness");
  console.log(`FAIL harness: ${e?.stack ?? e}`);
}
console.log(failures.length ? `FAILED ${failures.length}: ${failures.join(", ")}` : "ALL PASS");
process.exit(failures.length ? 1 : 0);
