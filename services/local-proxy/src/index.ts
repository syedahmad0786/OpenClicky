import http from "node:http";
import { URL } from "node:url";

/**
 * OpenClicky local proxy — client-only sidecar.
 * Inspired by farzaa/clicky worker routes, rewritten for OpenAI-compatible OpenRouter.
 *
 * Routes:
 *   GET  /health
 *   POST /v1/chat/completions      → OpenRouter chat
 *   POST /chat                     → alias
 *   POST /v1/audio/transcriptions  → OpenRouter STT
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

async function readBody(req: http.IncomingMessage): Promise<Buffer> {
  const chunks: Buffer[] = [];
  for await (const chunk of req) {
    chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
  }
  return Buffer.concat(chunks);
}

async function proxyJson(
  req: http.IncomingMessage,
  res: http.ServerResponse,
  upstreamPath: string,
): Promise<void> {
  const apiKey = readApiKey(req);
  if (!apiKey) {
    res.writeHead(401, { "content-type": "application/json" });
    res.end(JSON.stringify({ error: "Missing API key (Authorization Bearer or OPENROUTER_API_KEY)" }));
    return;
  }

  const body = await readBody(req);
  const upstream = await fetch(`${upstreamBase}${upstreamPath}`, {
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

async function proxyMultipart(
  req: http.IncomingMessage,
  res: http.ServerResponse,
  upstreamPath: string,
): Promise<void> {
  const apiKey = readApiKey(req);
  if (!apiKey) {
    res.writeHead(401, { "content-type": "application/json" });
    res.end(JSON.stringify({ error: "Missing API key (Authorization Bearer or OPENROUTER_API_KEY)" }));
    return;
  }

  const body = await readBody(req);
  const contentType = req.headers["content-type"];
  if (!contentType) {
    res.writeHead(400, { "content-type": "application/json" });
    res.end(JSON.stringify({ error: "Missing content-type" }));
    return;
  }

  const upstream = await fetch(`${upstreamBase}${upstreamPath}`, {
    method: "POST",
    headers: {
      authorization: `Bearer ${apiKey}`,
      "content-type": contentType,
      "http-referer": "https://github.com/openclicky/OpenClicky",
      "x-title": "OpenClicky",
    },
    body,
  });

  res.writeHead(upstream.status, {
    "content-type": upstream.headers.get("content-type") ?? "application/json",
  });
  const buf = Buffer.from(await upstream.arrayBuffer());
  res.end(buf);
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
        await proxyJson(req, res, "/chat/completions");
        return;
      }

      if (req.method === "POST" && url.pathname === "/v1/audio/transcriptions") {
        await proxyMultipart(req, res, "/audio/transcriptions");
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
