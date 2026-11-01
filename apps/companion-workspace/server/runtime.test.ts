// @vitest-environment node
import { afterEach, describe, expect, it } from "vitest";
import { createServer, request as httpRequest } from "node:http";
import { spawn, type ChildProcess } from "node:child_process";
import { once } from "node:events";
import { join } from "node:path";
import { createInterface } from "node:readline";
import { copyFile, mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";

const token = "0123456789abcdef0123456789abcdef";
let child: ChildProcess | undefined;
afterEach(() => { if (child && !child.killed) child.kill(); child = undefined; });

describe("packaged CopilotKit runtime", () => {
  it("serves side-by-side bundled assets from an arbitrary package directory", async () => {
    const packaged = await mkdtemp(join(tmpdir(), "openclicky-workspace-package-"));
    try {
      await mkdir(join(packaged, "assets"));
      await copyFile(join(process.cwd(), "dist", "server.mjs"), join(packaged, "server.mjs"));
      await writeFile(join(packaged, "index.html"), "<!doctype html><script src=\"/assets/fixture.js\"></script>");
      await writeFile(join(packaged, "assets", "fixture.js"), "globalThis.fixtureLoaded=true;");

      const origin = await startRuntime("http://127.0.0.1:9/ag-ui", join(packaged, "server.mjs"), packaged);
      const root = await fetch(`${origin}/`);
      expect(root.status).toBe(200);
      expect(await root.text()).toContain("/assets/fixture.js");
      const asset = await fetch(`${origin}/assets/fixture.js`);
      expect(asset.status).toBe(200);
      expect(await asset.text()).toContain("fixtureLoaded");
      expect((await fetch(`${origin}/api/copilotkit/info`)).status).toBe(401);
    } finally {
      const running = child;
      if (running?.exitCode === null) {
        running.kill();
        await once(running, "exit");
      }
      child = undefined;
      await rm(packaged, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 });
    }
  }, 20_000);

  it("authenticates the real info/run routes and relays AG-UI SSE", async () => {
    const agent = createServer((request, response) => {
      expect(request.headers.authorization).toBe("Bearer host-fixture-token");
      response.writeHead(200, { "content-type": "text/event-stream" });
      for (const event of [
        { type: "RUN_STARTED", threadId: "thread-1", runId: "run-1" },
        { type: "TEXT_MESSAGE_START", messageId: "answer-1", role: "assistant" },
        { type: "TEXT_MESSAGE_CONTENT", messageId: "answer-1", delta: "fixture delta" },
        { type: "TEXT_MESSAGE_END", messageId: "answer-1" },
        { type: "RUN_FINISHED", threadId: "thread-1", runId: "run-1" },
      ]) response.write(`data: ${JSON.stringify(event)}\n\n`);
      response.end();
    }).listen(0, "127.0.0.1");
    await once(agent, "listening");
    const agentAddress = agent.address();
    if (!agentAddress || typeof agentAddress === "string") throw new Error("fixture agent did not listen");
    try {
      const origin = await startRuntime(`http://127.0.0.1:${agentAddress.port}/ag-ui`);
      const headers = { Authorization: `Bearer ${token}`, Origin: origin };
      const info = await fetch(`${origin}/api/copilotkit/info`, { headers });
      expect(info.status).toBe(200);
      const runtimeInfo = await info.json() as { agents: Record<string, { name: string }> };
      expect(runtimeInfo.agents.default.name).toBe("default");
      expect((await fetch(`${origin}/api/copilotkit/info`, { headers: { Authorization: `Bearer ${token}` } })).status).toBe(200);
      expect((await fetch(`${origin}/api/copilotkit/info`)).status).toBe(401);
      expect((await fetch(`${origin}/api/copilotkit/agent/default/run`, { method: "POST", headers: { Authorization: `Bearer ${token}` } })).status).toBe(403);

      const run = await fetch(`${origin}/api/copilotkit/agent/default/run`, {
        method: "POST", headers: { ...headers, "content-type": "application/json" },
        body: JSON.stringify({ threadId: "thread-1", runId: "run-1", state: {}, messages: [{ id: "question-1", role: "user", content: "hello" }], tools: [], context: [], forwardedProps: {} }),
      });
      expect(run.status).toBe(200);
      const stream = await run.text();
      expect(stream).toContain("TEXT_MESSAGE_CONTENT");
      expect(stream).toContain("fixture delta");

      expect((await fetch(`${origin}/api/copilotkit/info`, { headers: { Origin: origin } })).status).toBe(401);
      expect((await fetch(`${origin}/api/copilotkit/info`, { headers: { Authorization: `Bearer ${token}`, Origin: "http://example.test" } })).status).toBe(403);
      expect((await fetch(`${origin}/%2e%2e/package.json`)).status).toBe(404);
      expect((await fetch(`${origin}/%E0%A4%A`)).status).toBe(404);
      expect(await requestStatus(origin, { Host: "example.test" })).toBe(400);
      expect(await requestStatus(origin, { Host: "127.0.0.1:1" })).toBe(400);
    } finally { agent.close(); }
  }, 20_000);
});

async function startRuntime(agentUrl: string, serverPath = join(process.cwd(), "dist", "server.mjs"), cwd = process.cwd()): Promise<string> {
  child = spawn(process.execPath, [serverPath], {
    cwd, stdio: ["ignore", "pipe", "pipe"],
    env: { SystemRoot: process.env.SystemRoot, TEMP: process.env.TEMP, NODE_ENV: "test", OPENCLICKY_WORKSPACE_TOKEN: token, OPENCLICKY_WORKSPACE_PORT: "0", OPENCLICKY_WORKSPACE_BIND: "127.0.0.1", OPENCLICKY_HOST_AGENT_URL: agentUrl, OPENCLICKY_HOST_AGENT_TOKEN: "host-fixture-token" },
  });
  const lines = createInterface({ input: child.stdout! });
  let errors = ""; child.stderr!.on("data", (chunk) => { errors += chunk.toString(); });
  const line = await Promise.race([
    (async () => { for await (const value of lines) if (String(value).startsWith('{"kind":"ready"')) return String(value); throw new Error(`runtime closed before ready: ${errors}`); })(),
    once(child, "exit").then(([code]) => { throw new Error(`runtime exited ${code}: ${errors}`); }),
    new Promise<string>((_, reject) => setTimeout(() => reject(new Error(`runtime readiness timed out: ${errors}`)), 10_000)),
  ]);
  const ready = JSON.parse(line);
  expect(ready.kind).toBe("ready");
  return ready.origin;
}

async function requestStatus(origin: string, headers: Record<string, string>): Promise<number> {
  return await new Promise((resolve, reject) => {
    const request = httpRequest(origin, { headers }, (response) => { response.resume(); resolve(response.statusCode ?? 0); });
    request.on("error", reject); request.end();
  });
}
