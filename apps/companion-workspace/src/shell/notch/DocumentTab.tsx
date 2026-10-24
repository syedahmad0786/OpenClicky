import { useMemo, useState, type ReactNode } from "react";
import { postPage } from "../bridge";
import type { DocumentView } from "../contract";
import { CloseIcon, IconButton, PillButton } from "../ui";
import { columnStats, formatNumber, isNumericColumn, parseCsv, parseInline, parseMarkdown } from "./documentParse";
import "./document.css";

const MAX_CSV_ROWS = 200; // rows drawn; the statistics always cover every row

function Inline({ text }: { text: string }) {
  return (
    <>
      {parseInline(text).map((part, i): ReactNode =>
        part.t === "code" ? <code key={i}>{part.text}</code>
        : part.t === "bold" ? <strong key={i}>{part.text}</strong>
        : part.t === "link" ? <span key={i} className="notch-md__link" title={part.url}>{part.text}</span>
        : part.text)}
    </>
  );
}

function Scroll({ children }: { children: ReactNode }) {
  return <div className="notch-body notch-doc__body" role="region" aria-label="Document content" tabIndex={0}>{children}</div>;
}

function Markdown({ source }: { source: string }) {
  const blocks = useMemo(() => parseMarkdown(source), [source]);
  return (
    <div className="notch-md">
      {blocks.map((block, i) => {
        switch (block.t) {
          case "h": { const H = (["h3", "h4", "h5"] as const)[block.level - 1]; return <H key={i} className={`notch-md__h${block.level}`}><Inline text={block.text} /></H>; }
          case "p": return <p key={i}><Inline text={block.text} /></p>;
          case "ul": return <ul key={i}>{block.items.map((item, j) => <li key={j}><Inline text={item} /></li>)}</ul>;
          case "ol": return <ol key={i}>{block.items.map((item, j) => <li key={j}><Inline text={item} /></li>)}</ol>;
          default: return <pre key={i} className="notch-doc__pre"><code>{block.text}</code></pre>;
        }
      })}
    </div>
  );
}

/** Table with the first row as header; clicking a numeric column's header shows its sum, average and count below. */
function Csv({ source }: { source: string }) {
  const [selected, setSelected] = useState<number | null>(null);
  const { head, body, numeric } = useMemo(() => {
    const [head = [], ...body] = parseCsv(source);
    const width = body.reduce((w, r) => Math.max(w, r.length), head.length); // no spread: 100k rows would overflow the stack
    return { head: Array.from({ length: width }, (_, c) => head[c] ?? ""), body, numeric: Array.from({ length: width }, (_, c) => isNumericColumn(body, c)) };
  }, [source]);
  const stats = selected !== null && numeric[selected] ? columnStats(body, selected) : null;
  return (
    <>
      <Scroll>
        <table className="notch-csv">
          <thead>
            <tr>
              {head.map((name, c) => (
                <th key={c} scope="col" className={numeric[c] ? "is-numeric" : undefined}>
                  {numeric[c]
                    ? <button type="button" aria-pressed={selected === c} onClick={() => setSelected(selected === c ? null : c)}>{name}</button>
                    : name}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {body.slice(0, MAX_CSV_ROWS).map((row, r) => (
              <tr key={r}>{head.map((_, c) => <td key={c} className={numeric[c] ? "is-numeric" : undefined}>{row[c] ?? ""}</td>)}</tr>
            ))}
          </tbody>
        </table>
      </Scroll>
      {stats ? (
        <div className="notch-doc__stats" role="group" aria-label="Column statistics">
          <span>Sum</span><b>{formatNumber(stats.sum)}</b>
          <span>Average</span><b>{formatNumber(stats.average)}</b>
          <span>Count</span><b>{formatNumber(stats.count)}</b>
        </div>
      ) : null}
      {body.length > MAX_CSV_ROWS ? <p className="notch-doc__note">{`Showing the first ${MAX_CSV_ROWS} rows.`}</p> : null}
    </>
  );
}

/** The Document tab: file header (open / reveal / close) over a read-only view of what the host sent. */
export function DocumentTab({ doc, onClose }: { doc: DocumentView | null; onClose: () => void }) {
  return (
    <>
      <div className="notch-doc__bar">
        <h2 className="notch-doc__name" title={doc?.name}>{doc?.name ?? "Document"}</h2>
        {doc ? (
          <>
            <PillButton size="sm" variant="secondary" onClick={() => postPage({ kind: "file.open", path: doc.path })}>Open in its app</PillButton>
            {doc.kind !== "pdf" ? <PillButton size="sm" variant="outline" onClick={() => postPage({ kind: "file.reveal", path: doc.path })}>Show in folder</PillButton> : null}
          </>
        ) : null}
        <IconButton label="Close" icon={<CloseIcon />} onClick={onClose} />
      </div>
      {doc?.kind === "csv" ? <Csv key={doc.path} source={doc.content} />
        : doc?.kind === "markdown" ? <Scroll><Markdown source={doc.content} /></Scroll>
        : doc?.kind === "text" ? <Scroll><pre className="notch-doc__pre">{doc.content}</pre></Scroll>
        : <div className="notch-body"><p className="notch-doc__empty">{doc ? "Preview isn’t available for PDFs yet." : "No document is open."}</p></div>}
      {doc && doc.kind !== "pdf" && doc.truncated ? <p className="notch-doc__note">Showing the first part of this file.</p> : null}
    </>
  );
}
