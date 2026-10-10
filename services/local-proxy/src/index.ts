import http from "node:http";
import { URL } from "node:url";

/**
 * OpenClicky local proxy — client-only sidecar.
 * Inspired by farzaa/clicky worker routes, rewritten for OpenAI-compatible OpenRouter.
 *
 * Routes:
 *   GET  /health
 *   POST /v1/chat/completions  → OpenRouter (or OPENCLICKY_UPSTREAM)
 *   POST /chat                 → alias of /v1/chat/completions (Clicky-shaped)
 */

const port = Number(process.env.PORT ?? 8787);
const upstreamBase = (process.env.OPENCLICKY_UPSTREAM ?? "https://openrouter.ai/api/v1").replace(
  /\/$/,
  "",
);

function readApiKey(req: http.IncomingMessage): string | undefined {
  const header = req.headers.authorization;
  if (header?.toLowerCase().startsWith("bearer ")) {
    return header.slice(7).trim();
  }
  return process.env.OPENROUTER_API_KEY ?? process.env.OPENCLICKY_API_KEY;
}

async function proxyChat(req: http.IncomingMessage, res: http.ServerResponse): Promise<void> {
  const apiKey = readApiKey(req);
  if (!apiKey) {
    res.writeHead(401, { "content-type": "application/json" });
    res.end(JSON.stringify({ error: "Missing API key (Authorization Bearer or OPENROUTER_API_KEY)" }));
    return;
  }

  const chunks: Buffer[] = [];
  for await (const chunk of req) {
    chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
  }
  const body = Buffer.concat(chunks);

  const upstream = await fetch(`${upstreamBase}/chat/completions`, {
    method: "POST",
    headers: {
      authorization: `Bearer ${apiKey}`,
      "content-type": req.headers["content-type"] ?? "application/json",
      "http-referer": "https://github.com/openclicky/OpenClicky",
      "x-title": "OpenClicky",
    },
    body,
  });

  const responseHeaders: Record<string, string> = {
    "content-type": upstream.headers.get("content-type") ?? "application/json",
  };
  const cacheControl = upstream.headers.get("cache-control");
  if (cacheControl) {
    responseHeaders["cache-control"] = cacheControl;
  }

  res.writeHead(upstream.status, responseHeaders);
  if (!upstream.body) {
    res.end();
    return;
  }

  const reader = upstream.body.getReader();
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) {
        break;
      }
      res.write(Buffer.from(value));
    }
  } finally {
    res.end();
  }
}

const server = http.createServer((req, res) => {
  const url = new URL(req.url ?? "/", `http://127.0.0.1:${port}`);

  void (async () => {
    try {
      if (req.method === "GET" && url.pathname === "/health") {
        res.writeHead(200, { "content-type": "application/json" });
        res.end(
          JSON.stringify({
            ok: true,
            service: "openclicky-local-proxy",
            upstream: upstreamBase,
          }),
        );
        return;
      }

      if (
        req.method === "POST" &&
        (url.pathname === "/v1/chat/completions" || url.pathname === "/chat")
      ) {
        await proxyChat(req, res);
        return;
      }

      res.writeHead(404, { "content-type": "application/json" });
      res.end(JSON.stringify({ error: "not found" }));
    } catch (error) {
      console.error(error);
      if (!res.headersSent) {
        res.writeHead(500, { "content-type": "application/json" });
      }
      res.end(JSON.stringify({ error: String(error) }));
    }
  })();
});

server.listen(port, "127.0.0.1", () => {
  console.log(`openclicky-local-proxy listening on http://127.0.0.1:${port}`);
  console.log(`upstream=${upstreamBase}`);
});
