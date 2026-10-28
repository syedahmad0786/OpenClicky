import { createServer, type IncomingMessage, type ServerResponse } from "node:http";
import { existsSync } from "node:fs";
import { readFile, stat } from "node:fs/promises";
import { dirname, extname, join, normalize, relative, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { timingSafeEqual } from "node:crypto";
import { HttpAgent } from "@ag-ui/client";
import { CopilotRuntime, createCopilotRuntimeHandler } from "@copilotkit/runtime/v2";
import { createCopilotNodeHandler } from "@copilotkit/runtime/v2/node";

const bind = process.env.OPENCLICKY_WORKSPACE_BIND ?? "127.0.0.1";
const port = Number(process.env.OPENCLICKY_WORKSPACE_PORT ?? "0");
const token = process.env.OPENCLICKY_WORKSPACE_TOKEN ?? "";
const agentUrl = process.env.OPENCLICKY_HOST_AGENT_URL ?? "";
const agentToken = process.env.OPENCLICKY_HOST_AGENT_TOKEN ?? "";
if (bind !== "127.0.0.1" && bind !== "::1") throw new Error("Workspace runtime only binds to loopback.");
if (!Number.isInteger(port) || port < 0 || port > 65535) throw new Error("OPENCLICKY_WORKSPACE_PORT is invalid.");
if (token.length < 32) throw new Error("OPENCLICKY_WORKSPACE_TOKEN must be an ephemeral token of at least 32 characters.");
if (!/^https?:\/\/(127\.0\.0\.1|\[::1\]|localhost)(:\d+)?\//.test(agentUrl)) throw new Error("OPENCLICKY_HOST_AGENT_URL must be a loopback URL.");

const runtime = new CopilotRuntime({ agents: { default: new HttpAgent({ url: agentUrl, headers: agentToken ? { Authorization: `Bearer ${agentToken}` } : {} }) } });
const runtimeHandler = createCopilotNodeHandler(createCopilotRuntimeHandler({ runtime, basePath: "/api/copilotkit", activateChannels: false }));
const moduleDirectory = dirname(fileURLToPath(import.meta.url));
// The bundle is shipped beside index.html, while tsx runs this source from server/ during development.
const dist = normalize(existsSync(join(moduleDirectory, "index.html"))
  ? moduleDirectory
  : join(moduleDirectory, "..", "dist"));
const mime: Record<string, string> = { ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".css": "text/css; charset=utf-8", ".svg": "image/svg+xml", ".ico": "image/x-icon", ".png": "image/png" };

function authorized(request: IncomingMessage): boolean {
  const expected = Buffer.from(`Bearer ${token}`); const actual = Buffer.from(request.headers.authorization ?? "");
  return expected.length === actual.length && timingSafeEqual(expected, actual);
}

async function staticFile(request: IncomingMessage, response: ServerResponse) {
  try {
    const host = request.headers.host!;
    const url = new URL(request.url ?? "/", `http://${host}`);
    const requested = url.pathname === "/" ? "index.html" : decodeURIComponent(url.pathname.slice(1));
    const path = normalize(join(dist, requested));
    const fromDist = relative(dist, path);
    if (fromDist === ".." || fromDist.startsWith(`..${sep}`) || requested.includes("\0")) throw new Error();
    if (!(await stat(path)).isFile()) throw new Error();
    response.writeHead(200, { "Content-Type": mime[extname(path)] ?? "application/octet-stream", "Cache-Control": "no-store", "Content-Security-Policy": "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-src 'self' blob:; connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'none'", "Referrer-Policy": "no-referrer", "X-Content-Type-Options": "nosniff" });
    response.end(await readFile(path));
  } catch { response.writeHead(404).end("Not found"); }
}

const server = createServer(async (request, response) => {
  const host = request.headers.host ?? "";
  const address = server.address();
  if (!address || typeof address === "string" || host !== `${bind === "::1" ? "[::1]" : bind}:${address.port}`) { response.writeHead(400).end("Invalid host"); return; }
  if ((request.url ?? "").startsWith("/api/copilotkit")) {
    // Same-origin browser GET requests omit Origin; every request still requires the private bearer token.
    const origin = request.headers.origin;
    const isRead = request.method === "GET" || request.method === "HEAD";
    if ((origin !== undefined || !isRead) && origin !== `http://${host}`) { response.writeHead(403, { "Cache-Control": "no-store" }).end("Invalid origin"); return; }
    if (!authorized(request)) { response.writeHead(401, { "Cache-Control": "no-store" }).end("Unauthorized"); return; }
    try { await runtimeHandler(request, response); }
    catch { if (!response.headersSent) response.writeHead(502, { "Cache-Control": "no-store" }); response.end("Runtime unavailable"); }
    return;
  }
  if (request.url === "/shutdown" && request.method === "POST") {
    if (!authorized(request)) { response.writeHead(401).end(); return; }
    response.writeHead(202).end(); server.close(); return;
  }
  if (request.method !== "GET" && request.method !== "HEAD") { response.writeHead(405).end(); return; }
  await staticFile(request, response);
});
server.listen(port, bind, () => {
  const address = server.address();
  if (!address || typeof address === "string") throw new Error("Workspace runtime did not open a TCP listener.");
  process.stdout.write(JSON.stringify({ kind: "ready", origin: `http://127.0.0.1:${address.port}` }) + "\n");
});
