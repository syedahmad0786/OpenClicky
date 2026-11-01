import { afterEach, describe, expect, it } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { CopilotChat, CopilotKit } from "@copilotkit/react-core/v2";
import { createServer } from "node:http";
import { spawn, type ChildProcess } from "node:child_process";
import { once } from "node:events";
import { join } from "node:path";
import { createInterface } from "node:readline";

const token = "0123456789abcdef0123456789abcdef";
let child: ChildProcess | undefined;
afterEach(() => { child?.kill(); child = undefined; });

describe("CopilotChat transport", () => {
  it("submits through the packaged v2 runtime and renders an AG-UI delta", async () => {
    let received = "";
    const agent = createServer((request, response) => {
      request.on("data", (chunk) => { received += chunk.toString(); });
      request.on("end", () => {
        const input = JSON.parse(received) as { threadId: string; runId: string };
        response.writeHead(200, { "content-type": "text/event-stream" });
        for (const event of [
          { type: "RUN_STARTED", threadId: input.threadId, runId: input.runId },
          { type: "TEXT_MESSAGE_START", messageId: "answer-ui", role: "assistant" },
          { type: "TEXT_MESSAGE_CONTENT", messageId: "answer-ui", delta: "Visible fixture answer" },
          { type: "TEXT_MESSAGE_END", messageId: "answer-ui" },
          { type: "RUN_FINISHED", threadId: input.threadId, runId: input.runId },
        ]) response.write(`data: ${JSON.stringify(event)}\n\n`);
        response.end();
      });
    }).listen(0, "127.0.0.1");
    await once(agent, "listening");
    const address = agent.address();
    if (!address || typeof address === "string") throw new Error("agent did not listen");
    const origin = await startRuntime(`http://127.0.0.1:${address.port}/ag-ui`);
    const nativeFetch = globalThis.fetch;
    const relayed: string[] = [];
    globalThis.fetch = async (input, init) => {
      const headers = new Headers(input instanceof Request ? input.headers : init?.headers);
      if (init?.headers) new Headers(init.headers).forEach((value, name) => headers.set(name, value));
      const url = input instanceof Request ? input.url : String(input);
      if (url.startsWith(origin) && (init?.method ?? (input instanceof Request ? input.method : "GET")) !== "GET") headers.set("Origin", origin);
      const response = await nativeFetch(input, { ...init, headers, signal: undefined });
      if (url.includes("/run")) relayed.push(await response.clone().text());
      return response;
    };
    try {
      render(<CopilotKit runtimeUrl={`${origin}/api/copilotkit`} headers={{ Authorization: `Bearer ${token}` }} agentId="default"><CopilotChat labels={{ chatInputPlaceholder: "Fixture prompt" }} /></CopilotKit>);
      const input = await screen.findByPlaceholderText("Fixture prompt", {}, { timeout: 10_000 });
      fireEvent.input(input, { target: { value: "Hello from component" } });
      const send = screen.getByTestId("copilot-send-button");
      await waitFor(() => expect(send).toBeEnabled());
      fireEvent.click(send);
      await waitFor(() => expect(received).toContain("Hello from component"));
      await waitFor(() => expect(relayed.length).toBe(1));
      expect(await screen.findByText("Visible fixture answer", {}, { timeout: 10_000 })).toBeVisible();
    } finally {
      globalThis.fetch = nativeFetch;
      agent.close();
    }
  }, 20_000);
});

async function startRuntime(agentUrl: string): Promise<string> {
  child = spawn(process.execPath, [join(process.cwd(), "dist", "server.mjs")], { cwd: process.cwd(), stdio: ["ignore", "pipe", "pipe"], env: { SystemRoot: process.env.SystemRoot, TEMP: process.env.TEMP, NODE_ENV: "test", OPENCLICKY_WORKSPACE_TOKEN: token, OPENCLICKY_WORKSPACE_PORT: "0", OPENCLICKY_WORKSPACE_BIND: "127.0.0.1", OPENCLICKY_HOST_AGENT_URL: agentUrl, OPENCLICKY_HOST_AGENT_TOKEN: "host-fixture-token" } });
  const lines = createInterface({ input: child.stdout! });
  for await (const value of lines) if (String(value).startsWith('{"kind":"ready"')) return JSON.parse(String(value)).origin;
  throw new Error("runtime closed before ready");
}
