import { useId, useMemo, useState } from "react";
import type { Artifact } from "./contracts";

export function ArtifactView({ artifact, onChange }: { artifact: Artifact; onChange: (next: Artifact) => void }) {
  switch (artifact.kind) {
    case "chart": return <Chart artifact={artifact} onChange={onChange} />;
    case "diagram": return <Diagram artifact={artifact} />;
    case "calculator": return <Calculator artifact={artifact} onChange={onChange} />;
    case "scene3d": return <Scene artifact={artifact} onChange={onChange} />;
  }
}

function Chart({ artifact, onChange }: { artifact: Extract<Artifact, { kind: "chart" }>; onChange: (next: Artifact) => void }) {
  const maximum = Math.max(...artifact.series.map((item) => item.value), 1);
  return <section className="artifact" aria-label={artifact.title}>
    <h2>{artifact.title}</h2><div className="bars">
      {artifact.series.map((item, index) => <label key={item.label} className="bar-row">
        <span>{item.label}</span><i style={{ width: `${Math.max(2, item.value / maximum * 100)}%` }} />
        <input aria-label={`${item.label} value`} type="number" value={item.value} onChange={(event) => {
          const series = artifact.series.map((other, at) => at === index ? { ...other, value: Number(event.target.value) } : other);
          onChange({ ...artifact, series });
        }} />
      </label>)}
    </div>
  </section>;
}

function Diagram({ artifact }: { artifact: Extract<Artifact, { kind: "diagram" }> }) {
  const [selected, setSelected] = useState(artifact.nodes[0]?.id);
  const arrowId = useId().replaceAll(":", "");
  const node = artifact.nodes.find((item) => item.id === selected);
  const columns = Math.min(4, Math.ceil(Math.sqrt(artifact.nodes.length)));
  const rows = Math.ceil(artifact.nodes.length / columns);
  const positions = new Map(artifact.nodes.map((item, index) => [item.id, {
    x: (index % columns + 0.5) * 100 / columns,
    y: (Math.floor(index / columns) + 0.5) * 100 / rows,
  }]));
  return <section className="artifact" aria-label={artifact.title}>
    <h2>{artifact.title}</h2><div className="diagram" role="group" aria-label={`${artifact.title} nodes and connections`}>
      <svg aria-label={`${artifact.edges.length} connections`} role="img" viewBox="0 0 100 100" preserveAspectRatio="none">
        <defs><marker id={arrowId} markerWidth="5" markerHeight="5" refX="4" refY="2.5" orient="auto"><path d="M0,0 L5,2.5 L0,5 Z" /></marker></defs>
        {artifact.edges.map((edge, index) => {
          const from = positions.get(edge.from)!; const to = positions.get(edge.to)!;
          return <line key={`${edge.from}-${edge.to}-${index}`} data-edge={`${edge.from}-${edge.to}`} x1={from.x} y1={from.y} x2={to.x} y2={to.y} markerEnd={`url(#${arrowId})`} />;
        })}
      </svg>
      {artifact.nodes.map((item) => {
        const position = positions.get(item.id)!;
        return <button type="button" key={item.id} style={{ left: `${position.x}%`, top: `${position.y}%` }} className={selected === item.id ? "node selected" : "node"} onClick={() => setSelected(item.id)}>{item.label}</button>;
      })}
    </div><p className="selection" aria-live="polite">{node?.detail}</p>
  </section>;
}

function Calculator({ artifact, onChange }: { artifact: Extract<Artifact, { kind: "calculator" }>; onChange: (next: Artifact) => void }) {
  const total = useMemo(() => artifact.fields.reduce((sum, field) => sum + field.value, 0), [artifact.fields]);
  return <section className="artifact" aria-label={artifact.title}>
    <h2>{artifact.title}</h2><output className="total">{new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 }).format(total)}</output>
    {artifact.fields.map((field, index) => <label className="slider" key={field.id}>{field.label}<strong>{field.value}</strong>
      <input type="range" min={field.min} max={field.max} step={field.step} value={field.value} onChange={(event) => {
        const fields = artifact.fields.map((other, at) => at === index ? { ...other, value: Number(event.target.value) } : other);
        onChange({ ...artifact, fields });
      }} />
    </label>)}
  </section>;
}

function Scene({ artifact, onChange }: { artifact: Extract<Artifact, { kind: "scene3d" }>; onChange: (next: Artifact) => void }) {
  const faces = artifact.shape === "cube"
    ? ["front", "back", "right", "left", "top", "bottom"].map((face) => `<i class="cube-face ${face}"></i>`).join("")
    : ["front", "right", "back", "left"].map((face) => `<i class="pyramid-face ${face}"></i>`).join("") + '<i class="pyramid-base"></i>';
  const frame = `<!doctype html><meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">
  <style>html,body{height:100%;margin:0;display:grid;place-items:center;background:#f7f8f4;perspective:700px}.shape{position:relative;width:108px;height:108px;transform-style:preserve-3d;transform:rotateX(-22deg) rotateY(${artifact.rotation}deg);transition:transform .12s linear}.shape i{position:absolute;display:block;background:${artifact.color};border:1px solid rgba(0,0,0,.12)}.cube-face{inset:0}.cube-face.front{transform:translateZ(54px)}.cube-face.back{transform:rotateY(180deg) translateZ(54px)}.cube-face.right{transform:rotateY(90deg) translateZ(54px)}.cube-face.left{transform:rotateY(-90deg) translateZ(54px)}.cube-face.top{transform:rotateX(90deg) translateZ(54px)}.cube-face.bottom{transform:rotateX(-90deg) translateZ(54px)}.pyramid-face{width:0;height:0;border-left:54px solid transparent;border-right:54px solid transparent;border-bottom:108px solid ${artifact.color};background:transparent;transform-origin:50% 100%}.pyramid-face.front{transform:rotateX(30deg) translateZ(31px)}.pyramid-face.back{transform:rotateY(180deg) rotateX(30deg) translateZ(31px)}.pyramid-face.right{transform:rotateY(90deg) rotateX(30deg) translateZ(31px)}.pyramid-face.left{transform:rotateY(-90deg) rotateX(30deg) translateZ(31px)}.pyramid-base{inset:0;transform:rotateX(90deg) translateZ(-54px)}</style><div class="shape ${artifact.shape}" role="img" aria-label="${artifact.shape}">${faces}</div>`;
  return <section className="artifact scene" aria-label={artifact.title}><h2>{artifact.title}</h2>
    <iframe title={`${artifact.shape} preview`} sandbox="" srcDoc={frame} />
    <label className="slider">Rotate<strong>{artifact.rotation}°</strong><input type="range" min="-180" max="180" value={artifact.rotation} onChange={(event) => onChange({ ...artifact, rotation: Number(event.target.value) })} /></label>
  </section>;
}
