import { useEffect, useState } from "react";
import { useCopilotAction, useCopilotReadable } from "@copilotkit/react-core";
import { CopilotChat, CopilotKit, type Message, useAgent } from "@copilotkit/react-core/v2";
import "@copilotkit/react-core/v2/styles.css";
import { ArtifactView } from "./artifacts";
import { artifactSchema, type Artifact } from "./contracts";
import { listenToHost, postHost } from "./host-bridge";
import { runtimeHeaders, setRuntimeToken } from "./runtime-auth";
import "./styles.css";

const sampleArtifacts: Artifact[] = [
  { kind: "chart", id: "capacity", title: "Weekly capacity", series: [{ label: "Plan", value: 18 }, { label: "Build", value: 32 }, { label: "Verify", value: 24 }] },
  { kind: "diagram", id: "flow", title: "Connected work", nodes: [{ id: "ask", label: "Ask", detail: "Your request starts here." }, { id: "approve", label: "Approve", detail: "Desktop actions wait for your access policy." }, { id: "prove", label: "Prove", detail: "Inspect the actual destination result." }], edges: [{ from: "ask", to: "approve" }, { from: "approve", to: "prove" }] },
  { kind: "calculator", id: "estimate", title: "Effort sketch", fields: [{ id: "research", label: "Research", value: 3, min: 0, max: 20, step: 1 }, { id: "build", label: "Build", value: 8, min: 0, max: 20, step: 1 }] },
  { kind: "scene3d", id: "model", title: "Spatial preview", shape: "cube", color: "#25664c", rotation: 24 },
];

function ConnectedWorkspace() {
  const [artifacts, setArtifacts] = useState<Artifact[]>([]);
  const [activeId, setActiveId] = useState("");
  const [progress, setProgress] = useState("Ready");
  const [streamingText, setStreamingText] = useState("");
  const { agent, isReady } = useAgent({ agentId: "default" });
  const active = artifacts.find((artifact) => artifact.id === activeId) ?? artifacts[0];

  useCopilotReadable({ description: "Current OpenClicky workspace artifacts and progress", value: { artifacts, progress } }, [artifacts, progress]);
  useCopilotAction({
    name: "requestDesktopAction",
    description: "Ask the OpenClicky host to perform a desktop action under the run's access snapshot.",
    parameters: [{ name: "action", type: "string", required: true }, { name: "detail", type: "string", required: true }],
    renderAndWaitForResponse: ({ args, respond, status }) => <div className="approval" role="group" aria-label="Desktop action approval">
      <strong>{status === "executing" ? "Approval needed" : "Approval resolved"}</strong><p>{args.detail}</p>
      {status === "executing" && <div><button onClick={() => { postHost({ kind: "approval.request", requestId: crypto.randomUUID(), action: args.action, detail: args.detail }); respond?.("Requested from the native approval gate."); }}>Review in OpenClicky</button><button onClick={() => respond?.("Declined by the user.")}>Decline</button></div>}
    </div>,
  }, []);

  useEffect(() => {
    if (!isReady) return;
    const stop = listenToHost((message) => {
      if (message.kind === "session.snapshot") { setArtifacts(message.artifacts); setActiveId(message.artifacts[0]?.id ?? ""); setProgress(message.progress ?? "Ready"); }
      if (message.kind === "artifact.show") {
        setArtifacts((current) => [...current.filter((item) => item.id !== message.artifact.id), message.artifact]); setActiveId(message.artifact.id);
      }
      if (message.kind === "progress") setProgress(message.text);
      if (message.kind === "message.delta") setStreamingText(message.text);
      if (message.kind === "message.complete") {
        setStreamingText("");
        const next = { id: message.messageId, role: message.role, content: message.text } as Message;
        const index = agent.messages.findIndex((item) => item.id === message.messageId);
        agent.setMessages(index < 0 ? [...agent.messages, next] : agent.messages.map((item, itemIndex) => itemIndex === index ? next : item));
      }
      if (message.kind === "message.clear") { setStreamingText(""); agent.setMessages([]); }
      if (message.kind === "composer.prefill") {
        const textarea = document.querySelector<HTMLTextAreaElement>(".conversation textarea");
        if (textarea) {
          const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")?.set;
          setter?.call(textarea, message.text);
          textarea.dispatchEvent(new Event("input", { bubbles: true }));
          textarea.focus();
        }
      }
      if (message.kind === "runtime.error") setProgress(message.message);
    });
    postHost({ kind: "ready" });
    return stop;
  }, [agent, isReady]);

  const update = (next: Artifact) => {
    const parsed = artifactSchema.safeParse(next);
    if (!parsed.success) return;
    setArtifacts((current) => current.map((item) => item.id === parsed.data.id ? parsed.data : item));
    postHost({ kind: "artifact.changed", artifact: parsed.data });
  };

  return <main className="shell"><aside className="conversation"><header><img src="./openclicky.png" alt="" /><div><h1>OpenClicky</h1><span className="conversation-status" title={progress} aria-label={`Status: ${progress}`}>{progress}</span></div></header>
      <CopilotChat agentId="default" labels={{ welcomeMessageText: "What are we working through?", chatInputPlaceholder: "Ask OpenClicky…" }} />
      {streamingText && <p className="sr-status" role="status">{streamingText}</p>}
    </aside><section className="workbench"><nav aria-label="Artifacts">{artifacts.map((artifact) => <button className={artifact.id === activeId ? "active" : ""} key={artifact.id} onClick={() => setActiveId(artifact.id)}>{artifact.title}</button>)}</nav>
      <div className="canvas">{active ? <ArtifactView artifact={active} onChange={update} /> : <div className="empty"><h2>Turn an answer into something you can use.</h2><p>Charts, connected plans, calculators, and spatial previews stay editable here.</p><button onClick={() => { setArtifacts(sampleArtifacts); setActiveId(sampleArtifacts[0].id); }}>Load interactive demo</button></div>}</div>
      <footer><span>Interactive workspace</span><button disabled={!active} onClick={() => active && postHost({ kind: "artifact.save", artifact: active })}>Save artifact</button><button onClick={() => postHost({ kind: "run.cancel" })}>Stop</button></footer>
    </section></main>;
}

export default function Workspace() {
  const [token, setToken] = useState("");
  useEffect(() => {
    const stop = listenToHost((message) => {
      if (message.kind === "session.bootstrap") { setRuntimeToken(message.token); setToken(message.token); }
    });
    // Navigation can finish before React subscribes to native messages.
    postHost({ kind: "bootstrap.request" });
    return stop;
  }, []);
  if (!token) return <main className="connecting" role="status">Connecting to the local workspace…</main>;
  return <CopilotKit runtimeUrl="/api/copilotkit" headers={runtimeHeaders} agentId="default" enableInspector={false}><ConnectedWorkspace /></CopilotKit>;
}
